using Akka.Actor;

namespace Akka.Persistence.Cassandra.SessionManagement
{
    /// <summary>
    /// Extension Id provider for Cassandra Session management extension.
    /// </summary>
    public class CassandraSession : ExtensionIdProvider<IManageSessions>
    {
        public static CassandraSession Instance = new CassandraSession();

        public override IManageSessions CreateExtension(ExtendedActorSystem system)
        {
            return new DefaultSessionManager(system);
        }
    }
}