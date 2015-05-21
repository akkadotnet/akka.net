using Akka.Actor;
using Cassandra;

namespace Akka.Persistence.Cassandra.SessionManagement
{
    /// <summary>
    /// Contract for extension responsible for resolving/releasing Cassandra ISession instances used by the
    /// Cassandra Persistence plugin.
    /// </summary>
    public interface IManageSessions : IExtension
    {
        /// <summary>
        /// Resolves the session with the key specified.
        /// </summary>
        ISession ResolveSession(string key);

        /// <summary>
        /// Releases the session instance.
        /// </summary>
        void ReleaseSession(ISession session);
    }
}