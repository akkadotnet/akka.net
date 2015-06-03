using System;
using Akka.Configuration;
using Cassandra;

namespace Akka.Persistence.Cassandra
{
    /// <summary>
    /// Abstract class for parsing common settings used by both the Journal and Snapshot store from HOCON configuration.
    /// </summary>
    public abstract class CassandraSettings
    {
        /// <summary>
        /// The name (key) of the session to use when resolving an ISession instance. When using default session management,
        /// this points at configuration under the "cassandra-sessions" section where the session's configuration is found.
        /// </summary>
        public string SessionKey { get; private set; }

        /// <summary>
        /// The keyspace to be created/used.
        /// </summary>
        public string Keyspace { get; private set; }

        /// <summary>
        /// A string to be appended to the CREATE KEYSPACE statement after the WITH clause when the keyspace is 
        /// automatically created. Use this to define options like replication strategy.
        /// </summary>
        public string KeyspaceCreationOptions { get; private set; }

        /// <summary>
        /// When true the plugin will automatically try to create the keyspace if it doesn't already exist on start.
        /// </summary>
        public bool KeyspaceAutocreate { get; private set; }

        /// <summary>
        /// Name of the table to be created/used.
        /// </summary>
        public string Table { get; private set; }

        /// <summary>
        /// A string to be appended to the CREATE TABLE statement after the WITH clause. Use this to define things
        /// like gc_grace_seconds or one of the many other table options.
        /// </summary>
        public string TableCreationProperties { get; private set; }

        /// <summary>
        /// Consistency level for reads.
        /// </summary>
        public ConsistencyLevel ReadConsistency { get; private set; }

        /// <summary>
        /// Consistency level for writes.
        /// </summary>
        public ConsistencyLevel WriteConsistency { get; private set; }

        protected CassandraSettings(Config config)
        {
            SessionKey = config.GetString("session-key");

            Keyspace = config.GetString("keyspace");
            KeyspaceCreationOptions = config.GetString("keyspace-creation-options");
            KeyspaceAutocreate = config.GetBoolean("keyspace-autocreate");

            Table = config.GetString("table");
            TableCreationProperties = config.GetString("table-creation-properties");

            // Quote keyspace and table if necessary
            if (config.GetBoolean("use-quoted-identifiers"))
            {
                Keyspace = string.Format("\"{0}\"", Keyspace);
                Table = string.Format("\"{0}\"", Keyspace);
            }

            ReadConsistency = (ConsistencyLevel) Enum.Parse(typeof(ConsistencyLevel), config.GetString("read-consistency"), true);
            WriteConsistency = (ConsistencyLevel) Enum.Parse(typeof(ConsistencyLevel), config.GetString("write-consistency"), true);
        }
    }
}
