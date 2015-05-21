using System;
using Akka.Actor;
using Akka.Persistence.Cassandra.Journal;
using Akka.Persistence.Cassandra.SessionManagement;
using Akka.Persistence.Cassandra.Snapshot;

namespace Akka.Persistence.Cassandra
{
    /// <summary>
    /// An Akka.NET extension for Cassandra persistence.
    /// </summary>
    public class CassandraExtension : IExtension
    {
        /// <summary>
        /// The settings for the Cassandra journal.
        /// </summary>
        public CassandraJournalSettings JournalSettings { get; private set; }

        /// <summary>
        /// The settings for the Cassandra snapshot store.
        /// </summary>
        public CassandraSnapshotStoreSettings SnapshotStoreSettings { get; private set; }

        /// <summary>
        /// The session manager for resolving session instances.
        /// </summary>
        public IManageSessions SessionManager { get; private set; }
        
        public CassandraExtension(ExtendedActorSystem system)
        {
            if (system == null) throw new ArgumentNullException("system");
            
            // Initialize fallback configuration defaults
            system.Settings.InjectTopLevelFallback(CassandraPersistence.DefaultConfig());

            // Get or add the session manager
            SessionManager = CassandraSession.Instance.Apply(system);
            
            // Read config
            var journalConfig = system.Settings.Config.GetConfig("cassandra-journal");
            JournalSettings = new CassandraJournalSettings(journalConfig);

            var snapshotConfig = system.Settings.Config.GetConfig("cassandra-snapshot-store");
            SnapshotStoreSettings = new CassandraSnapshotStoreSettings(snapshotConfig);
        }
    }
}