# Akka.Hosting.AOT.App

Native AOT canary for Akka.Hosting, the counterpart to `src/aot/Akka.AOT.App` for plain core. Boots
through `Host.CreateApplicationBuilder` + `AddAkka(...)` and proves, against a real unrooted
publish: a custom `WithExtension<T>` was registered by boot, not lazily created; a plain actor and a
DI-constructed one both round-trip through `Ask`; `WithActorSystemLivenessCheck()` reports through
`Microsoft.Extensions.Diagnostics.HealthChecks`; `ConfigureLoggers`'s `AddLoggerFactory` actually
routes Akka's logs through `Microsoft.Extensions.Logging`; and `Watch`/`Terminated` (DeathWatch)
works. A `WatchdogLoggerProvider` fails the run on any Warning/Error logged anywhere in the host.

The app terminates itself once every assertion passes (`OK`, exit 0) instead of waiting on
`host.WaitForShutdownAsync()`, so CI gets a deterministic exit.

## Run

```bash
rm -rf src/aot/Akka.Hosting.AOT.App/bin src/aot/Akka.Hosting.AOT.App/obj
dotnet publish src/aot/Akka.Hosting.AOT.App -r linux-x64 -c Release
./src/aot/Akka.Hosting.AOT.App/bin/Release/*/linux-x64/publish/Akka.Hosting.AOT.App
```

Pass condition: exit `0` and `[canary-hosting] OK` on stdout. On the JIT
(`dotnet run --project src/aot/Akka.Hosting.AOT.App -c Release`) the same pass condition must hold
with the AOT switch off.

## Warning baseline

`aot-warnings.baseline.txt` in this directory tracks known `IL2xxx`/`IL3xxx` warnings under
`src/contrib/hosting/`, `src/contrib/dependencyinjection/`, and `src/core/Akka.Streams/` (Hosting
registers the stream-ref serializer at startup; see #8667). `src/core/Akka/` is out of scope - the
plain-core canary's own baseline already covers it. Checked by the Hosting steps of the `AotCanary` job in
`build-system/pr-validation.yaml`, which also documents how to reproduce the check locally.
