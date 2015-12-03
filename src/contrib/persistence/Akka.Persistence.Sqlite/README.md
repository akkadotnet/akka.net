## Akka.Persistence.Sqlite

Akka Persistence journal and snapshot store backed by SQLite database.

**WARNING: Akka.Persistence.Sqlite plugin is still in beta and it's mechanics described bellow may be still subject to change**.

### Setup

To activate the journal plugin, add the following lines to actor system configuration file:

```
akka.persistence.journal.plugin = "akka.persistence.journal.sqlite"
akka.persistence.journal.sqlite.connection-string = "<database connection string>"
```

Similar configuration may be used to setup a SQLite snapshot store:

```
akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.sqlite"
akka.persistence.snapshot-store.sqlite.connection-string = "<database connection string>"
```

Remember that connection string must be provided separately to Journal and Snapshot Store. To finish setup simply initialize plugin using: `SqlitePersistence.Get(actorSystem);`

### Configuration

Both journal and snapshot store share the same configuration keys (however they resides in separate scopes, so they are definied distinctly for either journal or snapshot store):

- `class` (string with fully qualified type name) - determines class to be used as a persistent journal. Default: *Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite* (for journal) and *Akka.Persistence.Sqlite.Snapshot.SqliteSnapshotStore, Akka.Persistence.Sqlite* (for snapshot store).
- `plugin-dispatcher` (string with configuration path) - describes a message dispatcher for persistent journal. Default: *akka.actor.default-dispatcher*
- `connection-string` - connection string used to access SQLite database. Default: *none*.
- `connection-timeout` - timespan determining default connection timeouts on database-related operations. Default: *30s*
- `table-name` - name of the table used by either journal or snapshot store. Default: *event_journal* (for journal) or *snapshot_store* (for snapshot store)
- `auto-initialize` - flag determining if journal or snapshot store related tables should by automatically created when they have not been found in connected database. Default: *false*

In addition, journal configuration specifies additional field:

- `timestamp-provider` - fully qualified type name (with assembly) of the class responsible for generating timestamp values based on persisted message type. By default this points to *Akka.Persistence.Sql.Common.Journal.DefaultTimestampProvider, Akka.Persistence.Sql.Common*, which returns current UTC DateTime value.

### In-memory databases

Akka.Persistence.Sqlite plugin allows to use in-memory databases, however requires to use them in shared mode in order to work correctly. Example connection strings for such configurations are described below:

- `FullUri=file::memory:?cache=shared;` for anonymous in-memory database instances.
- `FullUri=file:<database-name>.db?mode=memory&cache=shared;` for named in-memory database instances. This way you can provide many separate databases residing in memory.

### Custom SQL data queries

SQLite persistence plugin defines a default table schema used for both journal and snapshot store.

**EventJournal table**:

    +----------------+-------------+------------+----------------+------------+---------+
    | persistence_id | sequence_nr | is_deleted |    manifest    | timestamp  | payload |
    +----------------+-------------+------------+----------------+------------+---------+
    |  varchar(255)  | integer(8)  | integer(1) |  varchar(255)  | integer(8) |   blob  |
    +----------------+-------------+------------+----------------+------------+---------+

**SnapshotStore table**:

    +----------------+-------------+------------+----------------+----------+
    | persistence_id | sequence_nr | created_at |    manifest    | snapshot |
    +----------------+-------------+------------+----------------+----------+
    |  varchar(255)  | integer(8)  | integer(8) |  varchar(255)  |   blob   |
    +----------------+-------------+------------+----------------+----------+

`created_at` column maps to `System.DateTime` value represented by it's ticks, to achieve 1 to 1 precision of dates between SQLite and .NET environment.

Underneath Akka.Persistence.Sqlite uses a raw ADO.NET commands. You may choose not to use a dedicated built in ones, but to create your own being better fit for your use case. To do so, you have to create your own versions of `IJournalQueryBuilder` and `IJournalQueryMapper` (for custom journals) or `ISnapshotQueryBuilder` and `ISnapshotQueryMapper` (for custom snapshot store).

### Tests

The SQLite tests are packaged and run as part of the default "All" build task. They use dedicated shared in memory instances of SQLite database and can be executed in parallel.
