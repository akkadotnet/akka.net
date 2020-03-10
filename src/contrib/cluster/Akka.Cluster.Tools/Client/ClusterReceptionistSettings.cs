//-----------------------------------------------------------------------
// <copyright file="ClusterReceptionistSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Tools.Client
{
    /// <summary>
    /// TBD
    /// </summary>
    public sealed class ClusterReceptionistSettings : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Create settings from the default configuration "akka.cluster.client.receptionist".
        /// </summary>
        /// <param name="system">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public static ClusterReceptionistSettings Create(ActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(ClusterClientReceptionist.DefaultConfig());

            var config = system.Settings.Config.GetConfig("akka.cluster.client.receptionist");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterReceptionistSettings>("akka.cluster.client.receptionist");

            return Create(config);
        }

        /// <summary>
        /// Create settings from a configuration with the same layout as the default configuration "akka.cluster.client.receptionist".
        /// </summary>
        /// <param name="config">TBD</param>
        /// <returns>TBD</returns>
        public static ClusterReceptionistSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterReceptionistSettings>();

            var role = config.GetString("role", null);
            if (string.IsNullOrEmpty(role)) role = null;

            return new ClusterReceptionistSettings(
                role,
                config.GetInt("number-of-contacts"),
                config.GetTimeSpan("response-tunnel-receive-timeout"),
                config.GetTimeSpan("heartbeat-interval"),
                config.GetTimeSpan("acceptable-heartbeat-pause"),
                config.GetTimeSpan("failure-detection-interval"));
        }

        /// <summary>
        /// Start the receptionist on members tagged with this role. All members are used if undefined.
        /// </summary>
        public string Role { get; }

        /// <summary>
        /// The receptionist will send this number of contact points to the client.
        /// </summary>
        public int NumberOfContacts { get; }

        /// <summary>
        /// The actor that tunnel response messages to the client will be stopped after this time of inactivity.
        /// </summary>
        public TimeSpan ResponseTunnelReceiveTimeout { get; }

        /// <summary>
        /// How often failure detection heartbeat messages should be received for each ClusterClient
        /// </summary>
        public TimeSpan HeartbeatInterval { get; }

        /// <summary>
        /// Number of potentially lost/delayed heartbeats that will be
        /// accepted before considering it to be an anomaly.
        /// The ClusterReceptionist is using the akka.remote.DeadlineFailureDetector, which
        /// will trigger if there are no heartbeats within the duration
        /// heartbeat-interval + acceptable-heartbeat-pause, i.e. 15 seconds with
        /// the default settings.
        /// </summary>
        public TimeSpan AcceptableHeartbeatPause { get; }

        /// <summary>
        /// Failure detection checking interval for checking all ClusterClients
        /// </summary>
        public TimeSpan FailureDetectionInterval { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="role">TBD</param>
        /// <param name="numberOfContacts">TBD</param>
        /// <param name="responseTunnelReceiveTimeout">TBD</param>
        /// <param name="heartbeatInterval">TBD</param>
        /// <param name="acceptableHeartbeatPause">TBD</param>
        /// <param name="failureDetectionInterval">TBD</param>
        public ClusterReceptionistSettings(
            string role,
            int numberOfContacts,
            TimeSpan responseTunnelReceiveTimeout,
            TimeSpan heartbeatInterval,
            TimeSpan acceptableHeartbeatPause,
            TimeSpan failureDetectionInterval)
        {
            Role = !string.IsNullOrEmpty(role) ? role : null;
            NumberOfContacts = numberOfContacts;
            ResponseTunnelReceiveTimeout = responseTunnelReceiveTimeout;
            HeartbeatInterval = heartbeatInterval;
            AcceptableHeartbeatPause = acceptableHeartbeatPause;
            FailureDetectionInterval = failureDetectionInterval;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="role">TBD</param>
        /// <returns>TBD</returns>
        public ClusterReceptionistSettings WithRole(string role)
        {
            return Copy(role: role);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public ClusterReceptionistSettings WithoutRole()
        {
            return Copy(role: "");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="numberOfContacts">TBD</param>
        /// <returns>TBD</returns>
        public ClusterReceptionistSettings WithNumberOfContacts(int numberOfContacts)
        {
            return Copy(numberOfContacts: numberOfContacts);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="responseTunnelReceiveTimeout">TBD</param>
        /// <returns>TBD</returns>
        public ClusterReceptionistSettings WithResponseTunnelReceiveTimeout(TimeSpan responseTunnelReceiveTimeout)
        {
            return Copy(responseTunnelReceiveTimeout: responseTunnelReceiveTimeout);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="heartbeatInterval">TBD</param>
        /// <param name="acceptableHeartbeatPause">TBD</param>
        /// <param name="failureDetectionInterval">TBD</param>
        /// <returns>TBD</returns>
        public ClusterReceptionistSettings WithHeartbeat(TimeSpan heartbeatInterval, TimeSpan acceptableHeartbeatPause, TimeSpan failureDetectionInterval)
        {
            return Copy(
                heartbeatInterval: heartbeatInterval,
                acceptableHeartbeatPause: acceptableHeartbeatPause,
                failureDetectionInterval: failureDetectionInterval);
        }

        private ClusterReceptionistSettings Copy(
            string role = null,
            int? numberOfContacts = null,
            TimeSpan? responseTunnelReceiveTimeout = null,
            TimeSpan? heartbeatInterval = null,
            TimeSpan? acceptableHeartbeatPause = null,
            TimeSpan? failureDetectionInterval = null)
        {
            return new ClusterReceptionistSettings(
                role ?? Role,
                numberOfContacts ?? NumberOfContacts,
                responseTunnelReceiveTimeout ?? ResponseTunnelReceiveTimeout,
                heartbeatInterval ?? HeartbeatInterval,
                acceptableHeartbeatPause ?? AcceptableHeartbeatPause,
                failureDetectionInterval ?? FailureDetectionInterval);
        }
    }
}
