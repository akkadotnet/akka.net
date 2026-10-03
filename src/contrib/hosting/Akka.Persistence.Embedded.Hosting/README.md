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

`WithEmbeddedPersistence` has the same overloads and parameter names as `WithSqlPersistence` in
Akka.Persistence.Sql.Hosting (minus the linq2db ones: `providerName`, `schemaName`, `databaseMapping`, `DataOptions`).

## Event adapters and health checks

```csharp
akka.WithEmbeddedPersistence(
    "Data Source=app.db",
    journalBuilder: journal => journal
        .AddWriteEventAdapter("tagger", static _ => new MyTagger(), typeof(MyEvent))   // the factory overload is Native AOT safe
        .WithHealthCheck(),
    snapshotBuilder: snapshot => snapshot.WithHealthCheck());
```

## Serializers

Native AOT has no JSON fallback, so bind a serializer for each event and snapshot type:

```csharp
akka.WithCustomSerializer("my-events", [typeof(MyEvent), typeof(MySnapshot)], system => new MyEventSerializer(system));
```

Prefer a `SerializerWithStringManifest`.

## Options

The read journal comes with the journal, and its settings are `Query*` properties of `EmbeddedJournalOptions`. You
register nothing twice:

```csharp
akka.WithEmbeddedPersistence(
    new EmbeddedJournalOptions
    {
        ConnectionString = "Data Source=app.db",
        TagStorageMode = TagWriteMode.Both,
        QueryRefreshInterval = TimeSpan.FromMilliseconds(250),   // read journal
        JournalSequenceRetrievalEnabled = true                   // read journal
    },
    new EmbeddedSnapshotOptions { ConnectionString = "Data Source=app.db" });
```

or, as in Akka.Persistence.Sql.Hosting, with delegates:

```csharp
akka.WithEmbeddedPersistence(
    journal => journal.ConnectionString = "Data Source=app.db",
    snapshot => snapshot.ConnectionString = "Data Source=app.db");
```

A property left null keeps the plugin's reference setting. `EmbeddedJournalOptions` also has `TagSeparator`,
`DeleteCompatibilityMode`, `UseWriterUuidColumn`, `JournalTableName`, `TagTableName`, `MetadataTableName`, `BufferSize`,
`BatchSize`, `ReplayBatchSize`, `ReadThreads`, `QueryMaxBufferSize`, `MaxConcurrentQueries`, `QueryThrottleTimeout` and
`QueryThreads`. They match the plugin's configuration keys.

If you register the journal with the generic `WithJournal(options)` instead, nothing registers the read journal in code,
and it needs `Akka.DynamicTypeLoading` on. Use `WithEmbeddedPersistence`.

## Two databases

Call it again with another `pluginIdentifier`. Each call registers its own journal, snapshot store and read journal:

```csharp
akka
    .WithEmbeddedPersistence("Data Source=orders.db", pluginIdentifier: "orders")
    .WithEmbeddedPersistence("Data Source=audit.db", pluginIdentifier: "audit", isDefaultPlugin: false);
```

A persistent actor picks the non-default plugin with `JournalPluginId = "akka.persistence.journal.audit"` and
`SnapshotPluginId = "akka.persistence.snapshot-store.audit"`. Read it with
`ReadJournalFor<SqliteReadJournal>("akka.persistence.query.journal.audit")`. A default plugin under another identifier is
also what the default read journal id, `akka.persistence.query.journal.embedded`, reads.

## Journal only or snapshot store only

`WithEmbeddedPersistence(connectionString, mode: PersistenceMode.Journal)` and `PersistenceMode.SnapshotStore`, or pass
only one of the two options objects.

## Connection pooling

The plugin keeps a few long-lived connections, so it opens them with `Pooling=False`: a pooled connection can outlive the
plugin and hold the database file open after the actor system stops. If your connection string sets `Pooling`
explicitly, the plugin keeps your value.

## Native library

`Microsoft.Data.Sqlite` ships the SQLite native library (`libe_sqlite3.so`, `libe_sqlite3.dylib`, `e_sqlite3.dll`). A Native AOT
publish copies it next to the executable; ship it with the app.
