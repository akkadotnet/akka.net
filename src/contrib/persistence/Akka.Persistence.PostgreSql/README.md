## Akka.Persistence.PostgreSql

Akka Persistence journal and snapshot store backed by PostgreSql database.

**WARNING: Akka.Persistence.PostgreSql plugin is still in beta and it's mechanics described below may be still subject to change**.

### Setup

To activate the journal plugin, add the following lines to actor system configuration file:

```
akka.persistence.journal.plugin = "akka.persistence.journal.postgresql"
akka.persistence.journal.postgresql.connection-string = "<database connection string>"
```

Similar configuration may be used to setup a PostgreSql snapshot store:

```
akka.persistence.snasphot-store.plugin = "akka.persistence.snasphot-store.postgresql"
akka.persistence.snasphot-store.postgresql.connection-string = "<database connection string>"
```

Remember that connection string must be provided separately to Journal and Snapshot Store. To finish setup simply initialize plugin using: `PostgreSqlPersistence.Init(actorSystem);`

### Configuration

Both journal and snapshot store share the same configuration keys (however they resides in separate scopes, so they are definied distinctly for either journal or snapshot store):

- `class` (string with fully qualified type name) - determines class to be used as a persistent journal. Default: *Akka.Persistence.PostgreSql.Journal.PostgreSqlJournal, Akka.Persistence.PostgreSql* (for journal) and *Akka.Persistence.PostgreSql.Snapshot.PostgreSqlSnapshotStore, Akka.Persistence.PostgreSql* (for snapshot store).
- `plugin-dispatcher` (string with configuration path) - describes a message dispatcher for persistent journal. Default: *akka.actor.default-dispatcher*
- `connection-string` - connection string used to access PostgreSql database. Default: *none*.
- `connection-timeout` - timespan determining default connection timeouts on database-related operations. Default: *30s*
- `schema-name` - name of the database schema, where journal or snapshot store tables should be placed. Default: *public*
- `table-name` - name of the table used by either journal or snapshot store. Default: *event_journal* (for journal) or *snapshot_store* (for snapshot store)
- `auto-initialize` - flag determining if journal or snapshot store related tables should by automatically created when they have not been found in connected database. Default: *false*

### Custom SQL data queries

PostgreSql persistence plugin defines a default table schema used for both journal and snapshot store.

**EventJournal table**:

    +----------------+-------------+------------+---------------+---------+
    | persistence_id | sequence_nr | is_deleted | payload_type  | payload |
    +----------------+-------------+------------+---------------+---------+
    | varchar(200)   | bigint      | boolean    | varchar(500)  | bytea   |
    +----------------+-------------+------------+---------------+---------+
 
**SnapshotStore table**:
 
    +----------------+--------------+--------------------------+------------------+---------------+----------+
    | persistence_id | sequence_nr  | created_at               | created_at_ticks | snapshot_type | snapshot |
    +----------------+--------------+--------------------------+------------------+--------------------------+
    | varchar(200)   | bigint       | timestamp with time zone | smallint         | varchar(500)  | bytea    |
    +----------------+--------------+--------------------------+------------------+--------------------------+

**created_at and created_at_ticks - The max precision of a PostgreSQL timestamp is 6. The max precision of a .Net DateTime object is 7. Because of this differences, the additional ticks are saved in a separate column and combined during deserialization. There is also a check constraint restricting created_at_ticks to the range [0,10) to ensure that there are no precision differences in the opposite direction.**

Underneath Akka.Persistence.PostgreSql uses the Npgsql library to communicate with the database. You may choose not to use a dedicated built in ones, but to create your own being better fit for your use case. To do so, you have to create your own versions of `IJournalQueryBuilder` and `IJournalQueryMapper` (for custom journals) or `ISnapshotQueryBuilder` and `ISnapshotQueryMapper` (for custom snapshot store) and then attach inside journal, just like in the example below:

```csharp
class MyCustomPostgreSqlJournal: Akka.Persistence.PostgreSql.Journal.PostgreSqlJournal 
{
    public MyCustomPostgreSqlJournal() : base() 
    {
        QueryBuilder = new MyCustomJournalQueryBuilder();
        QueryMapper = new MyCustomJournalQueryMapper();
    }
}
```

The final step is to setup your custom journal using akka config:

```
akka.persistence.journal.postgresql.class = "MyModule.MyCustomPostgreSqlJournal, MyModule"
```

### Tests

The PostgreSql tests are packaged as a separate build task with a target of "RunPostgreSqlTests".

In order to run the tests, you must do the following things:

1. Download and install PostgreSql from: http://www.postgresql.org/download/
2. Install PostgreSql with the default settings.  The default connection string uses the following credentials:
  1. Username: postgres
  2. Password: postgres
3. A custom app.config file can be used and needs to be placed in the same folder as the dll