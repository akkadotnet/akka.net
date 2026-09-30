# Akka.Hosting.AOT.App

A Native AOT canary for Akka.Hosting, the counterpart to `src/aot/Akka.AOT.App` for plain core. It
boots through `Host.CreateApplicationBuilder` + `AddAkka(...)`, the same shape a real Hosting app
uses, and exercises: `AddHocon` (plain settings merged ahead of `akka.conf`); a custom
`WithExtension<T>` (the same `akka.extensions` HOCON/`Type.GetType` load path a real cluster
extension takes); `WithActorSystemLivenessCheck()` wired into
`Microsoft.Extensions.Diagnostics.HealthChecks`; `ConfigureLoggers` routing Akka's log events
through `Microsoft.Extensions.Logging` (`AddLoggerFactory`, not Akka's own stdout logger);
`WithActors` registering both a plain actor and one built through Akka.DependencyInjection's
`resolver.Props<T>()`; a fire-and-forget `Tell` target with no reply; `Watch`/`Terminated`
(DeathWatch); and `AddStartup` running once after every actor above exists.

The app references `src/core/Akka`, `src/core/Akka.Streams` (transitively, through Akka.Hosting),
`src/contrib/hosting/Akka.Hosting`, and `src/contrib/dependencyinjection/Akka.DependencyInjection`,
and sets the `Akka.DynamicTypeLoading` feature switch to `false` with `Trim="true"`, exactly like
`Akka.AOT.App`. The switch is declared explicitly here for the same reason: this project consumes
core and Hosting as `ProjectReference`s rather than as NuGet packages, which is where
`buildTransitive/Akka.targets` would otherwise turn it off automatically.

## Publish and run

```bash
rm -rf src/aot/Akka.Hosting.AOT.App/bin src/aot/Akka.Hosting.AOT.App/obj
dotnet publish src/aot/Akka.Hosting.AOT.App -r linux-x64 -c Release -p:TrimmerSingleWarn=false
./src/aot/Akka.Hosting.AOT.App/bin/Release/net10.0/linux-x64/publish/Akka.Hosting.AOT.App
```

On a box with gcc but no clang, add `-p:CppCompilerAndLinker=gcc -p:LinkerFlavor=bfd`. On Windows
use `-r win-x64` and the matching `publish\Akka.Hosting.AOT.App.exe`.

Add `-p:RootAkka=true` to root `Akka`, `Akka.Hosting`, and `Akka.DependencyInjection`, analyzing
every path in all three instead of only the ones this canary's scenarios reach.

## Pass condition

Exit code `0` and `[canary-hosting] OK` on stdout. As with the plain-core canary, a silent boot is
not enough: `WatchdogLoggerProvider` is registered as the *only* `ILoggerProvider` on the host's
`ILoggerFactory`, so it sees every Warning/Error the host, the config system, or Akka itself (via
`LoggerFactoryLogger`) logs, and fails the run if anything was. On top of that, the app asserts:
HOCON merged from `AddHocon` resolves; the custom extension instance `AddStartup` marked is the
same one `WithExtension<T>` resolves back (proving the boot-time `akka.extensions` load actually
ran, not that a fallback silently created a second instance); the registry-registered and
DI-constructed actors both round-trip an `Ask`; the fire-and-forget actor's `Tell` is received; a
watched actor's `Terminated` arrives after `PoisonPill`; and the wired-up health check reports
`Healthy`.

**The app terminates itself.** Earlier revisions of this canary called
`host.WaitForShutdownAsync()` after printing `OK`, so the process could be stopped externally with
SIGINT/SIGTERM like a real long-running Hosting app. CI needs a deterministic exit instead of a
process it has to remember to kill, so `Program.cs` now returns `0` right after the assertions pass;
the `finally` block still calls `host.StopAsync()` either way.

## What to expect today

The unrooted publish is clean from Akka.Hosting and Akka.DependencyInjection: `0` `IL2xxx`/`IL3xxx`
warnings from `src/contrib/hosting/` or `src/contrib/dependencyinjection/`. It still emits the same
four `src/core/Akka/` warnings the plain-core canary's own baseline already tracks (extension/type
lookups on `akka.extensions`, `akka.actor.provider`, HOCON type names, and the generic name->Type
cache), plus three from `src/core/Akka.Streams/` that the core canary never sees, because it does
not reference Akka.Streams at all - Hosting does, and registers the stream-ref serializer
unconditionally at startup:

- `Implementation/StreamRef/SinkRefImpl.cs` and `SourceRefImpl.cs` (IL3050, `MakeGenericType`)
- `Serialization/SerializationTools.cs` (IL2057, `Type.GetType`)

All three are tracked in [#8667](https://github.com/akkadotnet/akka.net/issues/8667) and baselined
in `aot-warnings.baseline.txt` next to this file.

## In CI

The `HostingAotCanary` job in `build-system/pr-validation.yaml` runs on every PR: a single unrooted
publish, then run (proving `[canary-hosting] OK` and exit `0`), then the warning check reads that
same publish log. Unlike the plain-core canary, there is **no rooted publish** here: the three known
Streams warnings already surface on this canary's real, unrooted boot (the stream-ref serializer
registers itself at startup, not only when stream refs are actually used), so rooting `Akka.Hosting`
and `Akka.DependencyInjection` would not currently add coverage nothing else already gets - if a
future Hosting-only feature (cluster sharding, say) turns out to have a reflection path that only a
rooted analysis reaches, add the second publish then, the way the core canary does.

`scripts/CheckAotWarnings.cs` is shared with the plain-core canary; `--scope` is what makes it
reusable here instead of forking a second copy. This job passes three prefixes -
`src/contrib/hosting/`, `src/contrib/dependencyinjection/`, `src/core/Akka.Streams/` - so it fails
on any new warning under Hosting's or DI's own code, or under Streams (which nothing else watches),
while leaving `src/core/Akka/` alone: that surface is the plain-core canary's job, and gating on it
twice would just mean two baselines to update for the same warning.

Reproduce locally:

```bash
rm -rf src/aot/Akka.Hosting.AOT.App/bin src/aot/Akka.Hosting.AOT.App/obj
dotnet publish src/aot/Akka.Hosting.AOT.App -r linux-x64 -c Release \
    -p:TrimmerSingleWarn=false \
    -o /tmp/aot-hosting-publish 2>&1 | tee /tmp/aot-hosting-publish.log
dotnet run scripts/CheckAotWarnings.cs -- \
    --log /tmp/aot-hosting-publish.log \
    --baseline src/aot/Akka.Hosting.AOT.App/aot-warnings.baseline.txt \
    --scope "src/contrib/hosting/,src/contrib/dependencyinjection/,src/core/Akka.Streams/" \
    --repo-root .
```

Deleting `bin/` and `obj/` first is not optional: a warm intermediate directory makes MSBuild skip
native compilation, so the publish emits no IL warnings at all.

Running the same program on the JIT (`dotnet run --project src/aot/Akka.Hosting.AOT.App -c Release`)
reaches `[canary-hosting] OK` too, which is worth doing whenever the canary changes: it proves the
pass condition is actually satisfiable with the switch off (the `RuntimeHostConfigurationOption`
lands in `runtimeconfig.json` on a JIT run as well) before spending the time on a native publish.
