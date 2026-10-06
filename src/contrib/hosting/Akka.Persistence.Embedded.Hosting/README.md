# Akka.Persistence.Embedded.Hosting

Akka.Hosting support for `Akka.Persistence.Embedded`: a SQLite journal, snapshot store and read journal that read and
write the tables Akka.Persistence.Sql creates on SQLite by default, and run under Native AOT. This is how you use the plugin.

```csharp
builder.Services.AddAkka("my-system", akka => akka
    .WithEmbeddedPersistence("Data Source=app.db"));
```

That one call adds the journal, the snapshot store and the read journal as the default persistence plugins, creates the
tables on start, and needs no HOCON, no `class` setting and no `WithFallback`. It works with `Akka.DynamicTypeLoading`
turned off, which is what a Native AOT publish does.

`WithEmbeddedPersistence` has the same overloads and parameter names as `WithSqlPersistence` in
Akka.Persistence.Sql.Hosting (minus the linq2db ones: `providerName`, `schemaName`, `databaseMapping`, `DataOptions`).

## One layout

The plugin supports one table layout: the one Akka.Persistence.Sql 1.5.70 creates on SQLite by default. A database file
moves between the two plugins in both directions.

- Tags go to the `tags` table, one row per tag. The journal never writes a tags column (a table made from the
  Akka.Persistence.Sql docs DDL has a nullable one, which stays NULL).
- Every journal row has a `writer_uuid`.
- `DeleteMessagesTo` keeps the highest row at or below the target as a tombstone: `deleted = 1` and an empty `message`.
  Lower rows and their tag rows are deleted. The highest sequence number comes from `MAX(sequence_number)`, deleted rows
  included, so a persistence id goes on from where it was after you delete all its events. Queries and replay skip
  deleted rows, tag queries too.
- Column names are fixed. Table names are not (see below). There is no `journal_metadata` table, no CSV tags column and
  no schema name.

## Event adapters and health checks

```csharp
akka.WithEmbeddedPersistence(
    "Data Source=app.db",
    journalBuilder: journal => journal
        .AddWriteEventAdapter<MyTagger>("tagger", [typeof(MyEvent)])   // Native AOT safe
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
        JournalTableName = "events",
        QueryRefreshInterval = TimeSpan.FromMilliseconds(250)    // read journal
    },
    new EmbeddedSnapshotOptions { ConnectionString = "Data Source=app.db" });
```

or, as in Akka.Persistence.Sql.Hosting, with delegates:

```csharp
akka.WithEmbeddedPersistence(
    journal => journal.ConnectionString = "Data Source=app.db",
    snapshot => snapshot.ConnectionString = "Data Source=app.db");
```

A property left null keeps the plugin's reference setting. `EmbeddedJournalOptions` has `ConnectionString`,
`AutoInitialize`, `JournalTableName`, `TagTableName`, `BufferSize`, `BatchSize`, `ReplayBatchSize`, `ReadThreads` and,
for the read journal, `QueryRefreshInterval`, `QueryMaxBufferSize` and `QueryThreads`. `EmbeddedSnapshotOptions` has
`ConnectionString`, `AutoInitialize` and `TableName`. They match the plugin's configuration keys:

```hocon
akka.persistence {
  journal.embedded {
    connection-string = ""      # required
    auto-initialize = true
    table-name = "journal"
    tag-table-name = "tags"
    buffer-size = 5000          # write requests queued for the writer thread
    batch-size = 100            # rows per write transaction
    replay-batch-size = 1000    # rows per recovery round trip
    read-threads = 2
  }
  snapshot-store.embedded {
    connection-string = ""      # required
    auto-initialize = true
    table-name = "snapshot"
  }
  query.journal.embedded {
    write-plugin = "akka.persistence.journal.embedded"   # its connection string and table names are used
    max-buffer-size = 500       # rows per query round trip
    refresh-interval = 1s
    query-threads = 4
  }
}
```

Keys the plugin does not know, such as Akka.Persistence.Sql's `provider-name` or `tag-write-mode`, are not read. The
`Serializer` property that the Hosting base classes carry has no effect: serialization bindings pick the serializer.

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
`ReadJournalFor<SqliteReadJournal>("akka.persistence.query.journal.audit")`. The read journal id always follows the
journal's identifier, `akka.persistence.query.journal.{identifier}`.

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

## Writing your own plugin

See "Making Your Persistence Plugin Native AOT Ready" in `docs/articles/persistence/custom-persistence-provider.md`.
