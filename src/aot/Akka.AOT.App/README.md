# Akka.AOT.App

A Native AOT canary for Akka.NET core. It boots a local `ActorSystem` twice - once from the bare
default config, once through an empty `BootstrapSetup` - creates one `UntypedActor` and one
`ReceiveActor`, round-trips a message through each with `Ask`, checks that what core resolves from
HOCON actually got built, then terminates.

The app references only `src/core/Akka` and sets the `Akka.DynamicTypeLoading` feature switch to
`false` with `Trim="true"`, so ILLink replaces every reflection fallback in core with dead code and
removes it. Anything core still needs to look up by name therefore fails at runtime, in the open,
instead of silently working because the JIT happened to have the type around.

`IlcTreatWarningsAsErrors` is off here on purpose: the repo turns warnings into errors everywhere,
and this project's job is to *print* the `IL2xxx`/`IL3xxx` list rather than fail the publish on it.
The app's own C# still builds with warnings as errors.

`PublishAot` is gated on a `RuntimeIdentifier` rather than set unconditionally: the project is
registered in `Akka.slnx`, and leaving AOT on would make every `dotnet build` of the solution
restore the RID-specific ILCompiler and emit a self-contained output. Supplying `-r` on the publish
command turns it on. Do **not** pass `-p:PublishAot=true` on the command line instead - a
command-line property is global, so MSBuild would hand it to `Akka.csproj` too, switching on the
trim/AOT Roslyn analyzers for core and turning their warnings into errors under the repo-wide
`TreatWarningsAsErrors`.

## Publish and run

```bash
rm -rf src/aot/Akka.AOT.App/bin src/aot/Akka.AOT.App/obj
dotnet publish src/aot/Akka.AOT.App -r linux-x64 -c Release -p:TrimmerSingleWarn=false
./src/aot/Akka.AOT.App/bin/Release/net10.0/linux-x64/publish/Akka.AOT.App
```

On a box with gcc but no clang, add `-p:CppCompilerAndLinker=gcc -p:LinkerFlavor=bfd`. On Windows
use `-r win-x64` and the matching `publish\Akka.AOT.App.exe`.

Add `-p:RootAkka=true` to root the whole `Akka` assembly. That makes ILC analyze every path in the
library instead of only the ones these two toy actors reach, which is the honest warning count for
core - at the cost of a much longer publish.

## Pass condition

Exit code `0` and `[canary] OK` on stdout. A silent boot is not enough, because core's
`Serialization` and `Mailboxes` log-and-continue when a configured type name does not resolve - an
`ActorSystem` will happily come up with no serializers registered. So the run also has to clear two
hurdles:

1. **No warnings.** Each run installs a `LogFilterSetup` whose filter records every `WARNING` and
   `ERROR` the stdout logger is asked to print. Anything collected during startup or the round-trip
   fails the run and the messages are printed. This is what catches
   `The type name for serializer 'json' did not resolve to an actual Type`,
   `Serialization binding to non existing serializer: 'bytes'` and
   `Mailbox Requirement mapping [...] is not an actual type`.
2. **Positive assertions.** After boot: `Serialization.FindSerializerFor` returns a serializer for a
   `byte[]`, `Scheduler` is a `HashedWheelTimerScheduler`, `Settings.LogFormatter` is a
   `SemanticLogMessageFormatter`, and `Mailboxes.Lookup("akka.actor.default-mailbox")` is an
   `UnboundedMailbox`. With the switch off a type that has no `serialization-bindings` entry of its
   own throws by design - core does not register the reflection-driven `json` serializer, nor the
   `System.Object` binding pointing at it - so the canary asserts that a `string` throws with a
   message naming the switch and `SerializationSetup`; a real AOT application registers a serializer
   for its own message types through a `SerializationSetup`.

Shutdown is bounded (`Terminate()` with a 30 s cap) and `AppDomain.UnhandledException` prints the
same failure block, so a crash on a pool thread cannot exit quietly.

Any other outcome prints `[canary] FAILED: <type>: <message>` plus the stack and the
inner-exception chain, which names the first site core still cannot resolve without reflection.

## What to expect today

**The AOT publish only goes green once the whole milestone-1 stack is applied.** With just the
feature switch and the log-formatter/stdout-logger/scheduler tables in place it dies inside
`ActorSystem.Create`, at `Mailboxes.LookupConfigurator` (`UnboundedMailbox` has no parameterless
constructor). The serializer and mailbox warnings above are collected on the way there, but the
exception is thrown before the first watchdog check runs, so it is the exception you see. That is
the expected result at this stage, not a regression.

Running the same program on the JIT (`dotnet run --project src/aot/Akka.AOT.App -c Release`) does
reach `[canary] OK`. That is worth doing whenever the canary changes: it proves the pass condition
is actually satisfiable - the watchdog does not fire on a healthy boot and all the positive
assertions hold. The switch is off there too (the `RuntimeHostConfigurationOption` lands in
`runtimeconfig.json`), so a green JIT run also confirms the three converted sites resolve from their
built-in tables rather than by reflection.
