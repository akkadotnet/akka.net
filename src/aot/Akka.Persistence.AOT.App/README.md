# Akka.Persistence.AOT.App

A Native AOT canary for Akka.NET persistence, built the way a user builds it: with `Akka.Hosting` and
the `Akka.Persistence.Hosting` builders. It references `Akka.Persistence.Hosting`, sets the
`Akka.DynamicTypeLoading` feature switch to `false` with `Trim="true"`, and runs two scenarios (`Program.cs`):

1. **A Hosting app with every plugin supplied through the builders.** `CanaryJournalOptions` and
   `CanarySnapshotOptions` are what a plugin author writes: an identifier, a default config and a `CreatePluginActorFactory` override.
   They run a journal and a snapshot store that Akka.Persistence does not ship (small subclasses of the
   in-memory ones). `WithJournalAndSnapshot` adds the journal's write event adapter with
   `AddWriteEventAdapter` with a factory, `WithReadJournal` adds the in-memory read journal,
   `WithStashOverflowStrategy` adds a configurator, and `WithCustomSerializer` binds a hand-written
   `SerializerWithStringManifest`. No HOCON names a plugin class.
   A persistent actor persists `a` and `b` together (it stashes `b` while the write for `a` is in flight),
   saves a snapshot at sequence number 2, persists `c` and stops. A second incarnation recovers from the
   snapshot plus `c`. `CurrentEventsByPersistenceId` returns three envelopes; `CurrentEventsByTag("canary")`
   returns three too, which only happens when the event adapter ran. The app then checks that the journal and
   snapshot store were built once each, that the journal saw the default config of its options, and that the
   configurator is the default stash overflow strategy. A second actor persists and snapshots through the
   built-in `inmem` plugins in the same system (`WithInMemoryJournal`, `WithInMemorySnapshotStore`). The canary
   does not drive a real stash overflow.
2. **A Hosting app whose journal options supply no factory.** The options name their class in HOCON, so starting
   that journal has to throw a `ConfigurationException` that names the HOCON setting, the
   `Akka.DynamicTypeLoading` switch and Akka.Persistence.Hosting.

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
`src/core/Akka.Persistence/`, `src/core/Akka.Persistence.Query/`,
`src/contrib/persistence/Akka.Persistence.Query.InMemory/`, `src/contrib/hosting/Akka.Persistence.Hosting/` and `src/core/Akka.Streams/`, each with the reason. Check it the same way
CI does:

```bash
dotnet run scripts/CheckAotWarnings.cs -- \
    --log /tmp/pcanary.log \
    --baseline src/aot/Akka.Persistence.AOT.App/aot-warnings.baseline.txt \
    --scope "src/core/Akka.Persistence/,src/core/Akka.Persistence.Query/,src/contrib/persistence/Akka.Persistence.Query.InMemory/,src/contrib/hosting/Akka.Persistence.Hosting/,src/core/Akka.Streams/" \
    --repo-root .
```

The persistence lookup sites (journals, snapshot stores, stash overflow, event adapters, read journals)
keep their reflection in `[RequiresUnreferencedCode]` methods behind `AkkaFeatures.IsDynamicTypeLoadingSupported`,
so none of them shows up here.
