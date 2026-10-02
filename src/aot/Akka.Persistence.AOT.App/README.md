# Akka.Persistence.AOT.App

A Native AOT canary for Akka.NET persistence. It references `Akka.Persistence`, `Akka.Persistence.Query`
and `Akka.Persistence.Query.InMemory`, sets the `Akka.DynamicTypeLoading` feature switch to `false` with
`Trim="true"`, and runs two scenarios (`Program.cs`):

1. **A system with everything registered in code.** One fluent `PersistenceSetup` holds a journal, a
   snapshot store and a stash overflow configurator that Akka.Persistence does not ship (small subclasses
   of the in-memory ones, as a third-party plugin would be), the journal's event adapter with the event
   type it binds to, and the in-memory read journal. HOCON only names the default journal and snapshot
   store; no `class` string and no adapter section. The app also passes a `SerializationSetup` (a
   hand-written `SerializerWithStringManifest` for the event and the snapshot).
   A persistent actor persists `a` and `b` together (it stashes `b` while the write for `a` is in flight),
   saves a snapshot at sequence number 2, persists `c` and stops. A second incarnation recovers from the
   snapshot plus `c`. `CurrentEventsByPersistenceId` returns three envelopes; `CurrentEventsByTag("canary")`
   returns three too, which only happens when the registered event adapter ran. The app then checks that
   the registered journal and snapshot store were built once each, that the journal saw the default config
   of its record, and that the registered configurator is the default stash overflow strategy. A second
   actor persists and snapshots through the built-in `inmem` plugins in the same system. The canary does
   not drive a real stash overflow.
2. **A system whose journal `class` names a type nothing registers.** Starting that journal has to throw
   a `ConfigurationException` that names the HOCON setting and the `Akka.DynamicTypeLoading` switch.

## Publish and run

```bash
rm -rf src/aot/Akka.Persistence.AOT.App/bin src/aot/Akka.Persistence.AOT.App/obj
dotnet publish src/aot/Akka.Persistence.AOT.App -r linux-x64 -c Release -o /tmp/pcanary 2>&1 | tee /tmp/pcanary.log
/tmp/pcanary/Akka.Persistence.AOT.App
```

Deleting `bin/` and `obj/` first is not optional: a warm intermediate directory makes MSBuild skip
native compilation, so the publish emits no IL warnings at all.

`PublishAot` is gated on a `RuntimeIdentifier` for the same reason as in `Akka.AOT.App`: the project is
in `Akka.slnx`, and `-p:PublishAot=true` on the command line would reach every project in the graph and
turn on their trim analyzers.

## Pass condition

Exit code `0` and `[canary-persistence] OK` on stdout. A `LogFilterSetup` watchdog (`LogWatchdogFilter`)
fails the run on any `WARNING` or `ERROR` the logger sees, because core logs and continues when a
configured type name does not resolve.

## Warning baseline

`aot-warnings.baseline.txt` lists the `IL2xxx`/`IL3xxx` warnings the publish still emits from
`src/core/Akka.Persistence/`, `src/core/Akka.Persistence.Query/` and
`src/contrib/persistence/Akka.Persistence.Query.InMemory/`, each with the reason. Check it the same way
CI does:

```bash
dotnet run scripts/CheckAotWarnings.cs -- \
    --log /tmp/pcanary.log \
    --baseline src/aot/Akka.Persistence.AOT.App/aot-warnings.baseline.txt \
    --scope "src/core/Akka.Persistence/,src/core/Akka.Persistence.Query/,src/contrib/persistence/Akka.Persistence.Query.InMemory/" \
    --repo-root .
```

The persistence lookup sites (journals, snapshot stores, stash overflow, event adapters, read journals)
keep their reflection in `[RequiresUnreferencedCode]` methods behind `AkkaFeatures.IsDynamicTypeLoadingSupported`,
so none of them shows up here.

## `StreamsRoots.xml`

Akka.Streams builds the generic types on the boundary between two stream islands with `MakeGenericType`
and `Activator.CreateInstance`. The in-memory read journal uses `Source.ActorPublisher`, so each query
crosses such a boundary, and without a root the trimmer removes the constructor and the query fails with
`MissingMethodException`. `StreamsRoots.xml` roots those types for this app. It goes away when
Akka.Streams annotates or replaces that reflection.
