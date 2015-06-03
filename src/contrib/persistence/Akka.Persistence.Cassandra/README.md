Akka.Persistence.Cassandra
==========================
A replicated journal and snapshot store implementation for Akka.Persistence backed by 
[Apache Cassandra](http://planetcassandra.org/).

**WARNING: The Akka.Persistence.Cassandra plugin is still in beta and the mechanics described below are subject to
change.**

Quick Start
-----------
To activate the journal plugin, add the following line to the actor system configuration file:
```
akka.persistence.journal.plugin = "cassandra-journal"
```
To activate the snapshot store plugin, add the following line to the actor system configuration file:
```
akka.persistence.snasphot-store.plugin = "cassandra-snapshot-store"
```
The default configuration will try to connect to a Cassandra cluster running on `127.0.0.1` for persisting messages
and snapshots. More information on the available configuration options is in the sections below.

Connecting to the Cluster
-------------------------
Both the journal and the snapshot store plugins use the [DataStax .NET Driver](https://github.com/datastax/csharp-driver)
for Cassandra to communicate with the cluster. The driver has an `ISession` object which is used to execute statements
against the cluster (very similar to a `DbConnection` object in ADO.NET). You can control the creation and
configuration of these session instance(s) by modifying the configuration under `cassandra-sessions`. Out of the
box, both the journal and the snapshot store plugin will try to use a session called `default`. You can override 
the settings for that session with the following configuration keys:

- `cassandra-sessions.default.contact-points`: A comma-seperated list of contact points in the cluster in the format 
of either `host` or `host:port`. Default value is *`[ "127.0.0.1" ]`*.
- `cassandra-sessions.default.port`: Default port for contact points in the cluster, used if a contact point is not 
in [host:port] format. Default value is *`9042`*.
- `cassandra-sessions.default.credentials.username`: The username to login to Cassandra hosts. No authentication is
used by default.
- `cassandra-sessions.default.credentials.password`: The password corresponding to the username. No authentication
is used by default.
- `cassandra-sessions.default.ssl`: Boolean value indicating whether to use SSL when connecting to the cluster. No
default value is set and so SSL is not used by default.
- `cassandra-sessions.default.compression`: The [type of compression](https://github.com/datastax/csharp-driver/blob/master/src/Cassandra/CompressionType.cs) 
to use when communicating with the cluster. No default value is set and so compression is not used by default. 

If you require more advanced configuration of the `ISession` object than the options provided here (for example, to
use a different session for the journal and snapshot store plugins or to configure the session via code or manage
it with an IoC container), see the [Advanced Session Management](#advanced-session-management) section below.

Journal
-------
### Features
- All operations of the journal plugin API are fully supported
- Uses Cassandra in a log-oriented way (i.e. data is only ever inserted but never updated)
- Uses marker records for permanent deletes to try and avoid the problem of [reading many tombstones](http://www.datastax.com/dev/blog/cassandra-anti-patterns-queues-and-queue-like-datasets)
  when replaying messages.
- Messages for a single persistence Id are partitioned across the cluster to avoid unbounded partition
  growth and support scalability by adding more nodes to the cluster.

### Configuration
As mentioned in the Quick Start section, you can activate the journal plugin by adding the following line to your
actor system configuration file:
```
akka.persistence.journal.plugin = "cassandra-journal"
```
You can also override the journal's default settings with the following configuration keys:
- `cassandra-journal.class`: The Type name of the Cassandra journal plugin. Default value is *`Akka.Persistence.Cassandra.Journal.CassandraJournal, Akka.Persistence.Cassandra`*.
- `cassandra-journal.session-key`: The name (key) of the session to use when resolving an `ISession` instance. When
using default session management, this points at a configuration section under `cassandra-sessions` where the
session's configuration is found. Default value is *`default`*.
- `cassandra-journal.use-quoted-identifiers`: Whether or not to quote the table and keyspace names when executing
statements against Cassandra. Default value is *`false`*.
- `cassandra-journal.keyspace`: The keyspace to be created/used by the journal. Default value is *`akkanet`*.
- `cassandra-journal.keyspace-creation-options`: A string to be appended to the `CREATE KEYSPACE` statement after
the `WITH` clause when the keyspace is automatically created. Use this to define options like the replication
strategy. Default value is *`REPLICATION = { 'class' : 'SimpleStrategy', 'replication_factor' : 1 }`*.
- `cassandra-journal.keyspace-autocreate`: When true, the journal will automatically try to create the keyspace if
it doesn't already exist on startup. Default value is *`true`*.
- `cassandra-journal.table`: The name of the table to be created/used by the journal. Default value is *`messages`*.
- `cassandra-journal.table-creation-properties`: A string to be appended to the `CREATE TABLE` statement after the 
`WITH` clause. Use this to define advanced table options like `gc_grace_seconds` or one of the other many table 
options. Default value is *an empty string*.
- `cassandra-journal.partition-size`: The approximate number of message rows to store in a single partition. Cannot 
be changed after table creation. Default value is *`5000000`*.
- `cassandra-journal.max-result-size`: The maximum number of messages to retrieve in a single request to Cassandra 
when replaying messages. Default value is *`50001`*.
- `cassandra-journal.read-consistency`: The consistency level to use for read operations. Default value is *`Quorum`*.
- `cassandra-journal.write-consistency`: The consistency level to use for write operations. Default value is 
*`Quorum`*.

The default value for read and write consistency levels ensure that persistent actors can read their own writes.
Consider using `LocalQuorum` for both reads and writes if using a Cassandra cluster with multiple datacenters.

Snapshot Store
--------------
### Features
- Snapshot IO is done in a fully asynchronous fashion, including deletes (the snapshot store plugin API only
directly specifies synchronous methods for doing deletes)

### Configuration
As mentioned in the Quick Start section, you can activate the snapshot store plugin by adding the following line 
to your actor system configuration file:
```
akka.persistence.snapshot-store.plugin = "cassandra-snapshot-store"
```
You can also override the snapshot store's default settings with the following configuration keys:
- `cassandra-snapshot-store.class`: The Type name of the Cassandra snapshot store plugin. Default value is 
*`Akka.Persistence.Cassandra.Snapshot.CassandraSnapshotStore, Akka.Persistence.Cassandra`*.
- `cassandra-snapshot-store.session-key`: The name (key) of the session to use when resolving an `ISession` 
instance. When using default session management, this points at a configuration section under `cassandra-sessions` 
where the session's configuration is found. Default value is *`default`*.
- `cassandra-snapshot-store.use-quoted-identifiers`: Whether or not to quote the table and keyspace names when
executing statements against Cassandra. Default value is *`false`*.
- `cassandra-snapshot-store.keyspace`: The keyspace to be created/used by the snapshot store. Default value is 
*`akkanet`*.
- `cassandra-snapshot-store.keyspace-creation-options`: A string to be appended to the `CREATE KEYSPACE` statement 
after the `WITH` clause when the keyspace is automatically created. Use this to define options like the replication 
strategy. Default value is *`REPLICATION = { 'class' : 'SimpleStrategy', 'replication_factor' : 1 }`*.
- `cassandra-snapshot-store.keyspace-autocreate`: When true, the snapshot store will automatically try to create
the keyspace if it doesn't already exist on startup. Default value is *`true`*.
- `cassandra-snapshot-store.table`: The name of the table to be created/used by the snapshot store. Default value 
is *`snapshots`*.
- `cassandra-snapshot-store.table-creation-properties`: A string to be appended to the `CREATE TABLE` statement 
after the `WITH` clause. Use this to define advanced table options like `gc_grace_seconds` or one of the other 
many table options. Default value is *an empty string*.
- `cassandra-snapshot-store.max-metadata-result-size`: The maximum number of snapshot metadata instances to 
retrieve in a single request when trying to find a snapshot that matches criteria. Default value is *`10`*.
- `cassandra-snapshot-store.read-consistency`: The consistency level to use for read operations. Default value 
is *`One`*.
- `cassandra-snapshot-store.write-consistency`: The consistency level to use for write operations. Default value 
is *`One`*.

Consider using `LocalOne` consistency level for both reads and writes if using a Cassandra cluster with multiple
datacenters.

Advanced Session Management
---------------------------
In some advanced scenarios, you may want to have more control over how `ISession` instances are created. Some
example scenarios might include:
- to use a different session instance for the journal and snapshot store plugins (i.e. maybe you have more than one
Cassandra cluster and are storing journal messages and snapshots in different clusters)
- to access more advanced configuration options for building the session instance in code using the DataStax 
driver's cluster builder API directly
- to use session instances that have already been registered with an IoC container and are being managed there

If you want more control over how session instances are created or managed, you have two options depending on how
much control you need.

### Defining multiple session instances in the `cassandra-sessions` section
It is possible to define configuration for more than one session instance under the `cassandra-sessions` section of
your actor system's configuration file. To do this, just create your own section with a unique name/key for the
sub-section. All of the same options listed above in the [Connecting to the Cluster](#connecting-to-the-cluster)
can then be used to configure that session.  For example, I might define seperate configurations for my journal and
snapshot store plugins like this:
```
cassandra-sessions {
  my-journal-session {
    contact-points = [ "10.1.1.1", "10.1.1.2" ]
    port = 9042
    credentials { 
      username = "myusername"
      password = "mypassword"
    }
  }
  
  my-snapshot-session {
    contact-points = [ "10.2.1.1:9142", "10.2.1.2:9142" ]
  }
}
```
I can then tell the journal and snapshot store plugins to use those sessions by overriding each plugin's `session-key`
configuration like this:
```
cassandra-journal.session-key = "my-journal-session"
cassandra-snapshot-store.session-key = "my-snapshot-session"
```

### Controlling session configuration and management with code
You can also override how sessions are created, managed and resolved with your own code. Session management is
done as its own plugin for Akka.NET and a default implementation that uses the `cassandra-sessions` section is
provided out of the box. If you want to provide your own implementation for doing this (for example, to manage
sessions with an IoC container or use the DataStax driver's cluster builder API to do more advanced configuration),
here are the steps you'll need to follow:

1. Create a class that implements the `IManageSessions` interface from `Akka.Persistence.Cassandra.SessionManagement`.
  This interface is simple and just requires that you provide a way for resolving and releasing session instances. For
  example:

  ```cs
  public class MySessionManager : IManageSessions
  {
      public override ISession ResolveSession(string key)
      {
          // Do something here to get the ISession instance (pull from IoC container, etc)
      }
    
      public override ISession ReleaseSession(ISession session)
      {
          // Do something here to release the session instance if necessary
      }
  }
  ```
1. Next, you'll need to create an extension id provider class by inheriting from
  `ExtensionIdProvider<IManageSessions>`.  This class is responsible for actually providing a copy of your
  `IManageSessions` implementation. For example:

  ```cs
  public class MySessionExtension : ExtensionIdProvider<IManageSessions>
  {
      public override IManageSessions CreateExtension(ExtendedActorSystem system)
      {
          // Return a copy of your implementation of IManageSessions
          return new MySessionManager();
      }
  }
  ```
1. Lastly, you'll need to register your extension with the actor system when creating it in your application. For
  example:

  ```cs
  var actorSystem = ActorSystem.Create("MyApplicationActorSystem");
  var extensionId = new MySessionExtension();
  actorSystem.RegisterExtension(extensionId);
  ```

The journal and snapshot store plugins will now call your code when resolving or releasing sessions.
