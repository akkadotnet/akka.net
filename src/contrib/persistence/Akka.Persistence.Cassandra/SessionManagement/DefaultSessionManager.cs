using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Configuration;
using Cassandra;

namespace Akka.Persistence.Cassandra.SessionManagement
{
    /// <summary>
    /// A default session manager implementation that reads configuration from the system's "cassandra-cluster"
    /// section and builds ISession instances from that configuration. Caches session instances for reuse.
    /// </summary>
    public class DefaultSessionManager : IManageSessions
    {
        private readonly Config _sessionConfigs;
        private readonly ConcurrentDictionary<string, Lazy<ISession>> _sessionCache;
        
        public DefaultSessionManager(ExtendedActorSystem system)
        {
            if (system == null) throw new ArgumentNullException("system");

            // Read configuration sections
            _sessionConfigs = system.Settings.Config.GetConfig("cassandra-sessions");

            _sessionCache = new ConcurrentDictionary<string, Lazy<ISession>>();
        }

        /// <summary>
        /// Resolves the session with the key specified.
        /// </summary>
        public ISession ResolveSession(string key)
        {
            return _sessionCache.GetOrAdd(key, k => new Lazy<ISession>(() => CreateSession(k))).Value;
        }

        /// <summary>
        /// Releases the session instance.
        /// </summary>
        public void ReleaseSession(ISession session)
        {
            // No-op since we want session instance to live for actor system's duration 
            // (TODO: Dispose of session instance if hooks are added to listen for Actor system shutdown?)
        }
        
        private ISession CreateSession(string clusterName)
        {
            if (_sessionConfigs.HasPath(clusterName) == false)
                throw new ConfigurationException(string.Format("Cannot find cluster configuration named '{0}'", clusterName));

            // Get a cluster builder from the settings, build the cluster, and connect for a session
            var clusterSettings = new SessionSettings(_sessionConfigs.GetConfig(clusterName));
            return clusterSettings.Builder.Build().Connect();
        }
    }
}