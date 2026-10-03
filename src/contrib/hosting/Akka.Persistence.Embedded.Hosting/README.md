# Akka.Persistence.Embedded.Hosting

Akka.Hosting support for `Akka.Persistence.Embedded`: a SQLite journal, snapshot store and read journal that read and
write the same tables as Akka.Persistence.Sql on SQLite, and run under Native AOT. This is how you use the plugin.

```csharp
builder.Services.AddAkka("my-system", akka => akka
    .WithEmbeddedPersistence("Data Source=app.db"));
```

That one call adds the journal, the snapshot store and the read journal as the default persistence plugins, creates the
tables on start, and needs no HOCON, no `class` setting and no `WithFallback`. It works with `Akka.DynamicTypeLoading`
turned off, which is what a Native AOT publish does.

## Event adapters and health checks

```csharp
akka.WithEmbeddedPersistence(
    "Data Source=app.db",
    configureJournal: journal => journal
        .AddWriteEventAdapter("tagger", static _ => new MyTagger(), typeof(MyEvent))   // the factory overload is Native AOT safe
        .WithHealthCheck(),
    configureSnapshot: snapshot => snapshot.WithHealthCheck());
```

## Serializers

Native AOT has no JSON fallback, so bind a serializer for each event and snapshot type:

```csharp
akka.WithCustomSerializer("my-events", [typeof(MyEvent), typeof(MySnapshot)], system => new MyEventSerializer(system));
```

Prefer a `SerializerWithStringManifest`.

## Options

```csharp
akka.WithEmbeddedPersistence(
    new EmbeddedJournalOptions { ConnectionString = "Data Source=app.db", TagWriteMode = TagWriteMode.Both },
    new EmbeddedSnapshotOptions { ConnectionString = "Data Source=app.db" },
    new EmbeddedReadJournalOptions { RefreshInterval = TimeSpan.FromMilliseconds(250) });
```

`EmbeddedJournalOptions` also has `TagSeparator`, `DeleteCompatibilityMode`, `UseWriterUuidColumn`, the table names,
`BufferSize`, `BatchSize`, `ReplayBatchSize` and `ReadThreads`. Together they match the plugin's configuration keys.

## Two databases

Call it again with another `pluginIdentifier`. Each call registers its own journal, snapshot store and read journal:

```csharp
akka
    .WithEmbeddedPersistence("Data Source=orders.db", pluginIdentifier: "orders")
    .WithEmbeddedPersistence("Data Source=audit.db", pluginIdentifier: "audit", isDefaultPlugin: false);
```

A persistent actor picks the non-default plugin with `JournalPluginId = "akka.persistence.journal.audit"` and
`SnapshotPluginId = "akka.persistence.snapshot-store.audit"`. Read it with
`ReadJournalFor<SqliteReadJournal>("akka.persistence.query.journal.audit")`.

## Journal only or snapshot store only

`WithEmbeddedPersistence(connectionString, mode: PersistenceMode.Journal)` and `PersistenceMode.SnapshotStore`, or
`WithEmbeddedJournal(options)`, `WithEmbeddedSnapshotStore(options)` and `WithEmbeddedReadJournal(options)`.

## Native library

`Microsoft.Data.Sqlite` ships the SQLite native library (`libe_sqlite3.so`, `libe_sqlite3.dylib`, `e_sqlite3.dll`). A Native AOT
publish copies it next to the executable; ship it with the app.
