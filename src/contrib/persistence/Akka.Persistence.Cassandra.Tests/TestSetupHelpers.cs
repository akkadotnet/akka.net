using Akka.Actor;
using Cassandra;

namespace Akka.Persistence.Cassandra.Tests
{
    /// <summary>
    /// Some static helper methods for resetting Cassandra between tests or test contexts.
    /// </summary>
    public static class TestSetupHelpers
    {
        public static void ResetJournalData(ActorSystem sys)
        {
            // Get or add the extension
            var ext = CassandraPersistence.Instance.Apply(sys);

            // Use session to remove keyspace
            ISession session = ext.SessionManager.ResolveSession(ext.JournalSettings.SessionKey);
            session.DeleteKeyspaceIfExists(ext.JournalSettings.Keyspace);
            ext.SessionManager.ReleaseSession(session);
        }

        public static void ResetSnapshotStoreData(ActorSystem sys)
        {
            // Get or add the extension
            var ext = CassandraPersistence.Instance.Apply(sys);

            // Use session to remove the keyspace
            ISession session = ext.SessionManager.ResolveSession(ext.SnapshotStoreSettings.SessionKey);
            session.DeleteKeyspaceIfExists(ext.SnapshotStoreSettings.Keyspace);
            ext.SessionManager.ReleaseSession(session);
        }
    }
}
