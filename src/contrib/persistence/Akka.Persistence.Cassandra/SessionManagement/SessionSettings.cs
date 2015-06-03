using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Akka.Configuration;
using Cassandra;

namespace Akka.Persistence.Cassandra.SessionManagement
{
    /// <summary>
    /// Internal class for converting basic session settings in HOCON configuration to a Builder instance from the
    /// DataStax driver for Cassandra.
    /// </summary>
    internal class SessionSettings
    {
        /// <summary>
        /// A Builder instance with the appropriate configuration settings applied to it.
        /// </summary>
        public Builder Builder { get; private set; }

        public SessionSettings(Config config)
        {
            if (config == null) throw new ArgumentNullException("config");

            Builder = Cluster.Builder();
            
            // Get IP and port configuration
            int port = config.GetInt("port", 9042);
            IPEndPoint[] contactPoints = ParseContactPoints(config.GetStringList("contact-points"), port);
            Builder.AddContactPoints(contactPoints);

            // Support user/pass authentication
            if (config.HasPath("credentials"))
                Builder.WithCredentials(config.GetString("credentials.username"), config.GetString("credentials.password"));

            // Support SSL
            if (config.GetBoolean("ssl"))
                Builder.WithSSL();

            // Support compression
            string compressionTypeConfig = config.GetString("compression");
            if (compressionTypeConfig != null)
            {
                var compressionType = (CompressionType) Enum.Parse(typeof (CompressionType), compressionTypeConfig, true);
                Builder.WithCompression(compressionType);
            }
        }
        
        private static IPEndPoint[] ParseContactPoints(IList<string> contactPoints, int port)
        {
            if (contactPoints == null || contactPoints.Count == 0)
                throw new ConfigurationException("List of contact points cannot be empty.");

            return contactPoints.Select(cp =>
            {
                string[] ipAndPort = cp.Split(':');
                if (ipAndPort.Length == 1)
                    return new IPEndPoint(IPAddress.Parse(ipAndPort[0]), port);

                if (ipAndPort.Length == 2)
                    return new IPEndPoint(IPAddress.Parse(ipAndPort[0]), int.Parse(ipAndPort[1]));

                throw new ConfigurationException(string.Format("Contact points should have format [host:post] or [host] but found: {0}", cp));
            }).ToArray();
        }
    }
}