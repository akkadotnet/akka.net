# Akka.Persistence.Embedded.AOT.App

A Native AOT canary for the SQLite persistence plugin (`Akka.Persistence.Embedded`). It sets the
`Akka.DynamicTypeLoading` feature switch to `false` with `Trim="true"`, builds its systems with Akka.Hosting and
`WithEmbeddedPersistence(...)` (one call for the journal, snapshot store and read journal) and runs three scenarios (`Program.cs`):

1. **Main database.** A persistent actor persists five events (two tagged `red`), snapshots at
   sequence number 3, stops, and a new incarnation recovers with the snapshot. `DeleteMessages(2)` and a
   recovery from events only give `LastSequenceNr = 5` with events 3-5 replayed. Then every current query
   runs (`CurrentPersistenceIds`, `CurrentEventsByPersistenceId`, `CurrentEventsByTag`, `CurrentAllEvents`,
   `CurrentAllEvents(FromEnd(1))`), a live `EventsByTag` and a live `PersistenceIds` each see a new event,
   and `DeleteSnapshots(Latest)` leaves nothing to load.
   An event adapter registered with the journal tags one event, and `CurrentEventsByTag` finds it.
2. **Custom table names.** A second system and file with `JournalTableName`, `TagTableName` and the snapshot
   `TableName` set persists, snapshots and deletes. The events, tags and snapshot land in the named tables, and a tag
   query skips the tombstone.
3. **Unregistered.** A system without `WithEmbeddedPersistence()` must fail at start with a
   `ConfigurationException` that names `Akka.DynamicTypeLoading`.

It also opens a `SqliteConnection` first and prints `sqlite_version()`, which proves the native library
was found next to the executable.

## Publish and run

```bash
rm -rf src/aot/Akka.Persistence.Embedded.AOT.App/bin src/aot/Akka.Persistence.Embedded.AOT.App/obj
dotnet publish src/aot/Akka.Persistence.Embedded.AOT.App -r linux-x64 -c Release -o /tmp/scanary 2>&1 | tee /tmp/scanary.log
test -f /tmp/scanary/libe_sqlite3.so && /tmp/scanary/Akka.Persistence.Embedded.AOT.App
```

Deleting `bin/` and `obj/` first is not optional: a warm intermediate directory makes MSBuild skip
native compilation, so the publish emits no IL warnings at all.

`libe_sqlite3.so` must sit next to the executable. Ship the native library with the app.

## Pass condition

Exit code `0` and `[canary-sqlite] OK` on stdout. A `LogFilterSetup` watchdog (`LogWatchdogFilter`)
fails the run on any `WARNING` or `ERROR` the logger sees, because core logs and continues when a
configured type name does not resolve.

## Warning baseline

`aot-warnings.baseline.txt` holds only its header: the plugin, its Hosting package and Akka.Streams must publish with zero
`IL2xxx`/`IL3xxx` warnings. Check it the same way CI does:

```bash
dotnet run scripts/CheckAotWarnings.cs -- \
    --log /tmp/scanary.log \
    --baseline src/aot/Akka.Persistence.Embedded.AOT.App/aot-warnings.baseline.txt \
    --scope "src/contrib/persistence/Akka.Persistence.Embedded/,src/contrib/hosting/Akka.Persistence.Embedded.Hosting/,src/core/Akka.Streams/" \
    --repo-root .
```
