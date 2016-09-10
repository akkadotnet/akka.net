//-----------------------------------------------------------------------
// <copyright file="ClusterReceptionistSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Client
{
    public sealed class ClusterReceptionistSettings : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Create settings from the default configuration "akka.cluster.client.receptionist".
        /// </summary>
        public static ClusterReceptionistSettings Create(ActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(ClusterClientReceptionist.DefaultConfig());

            var config = system.Settings.Config.GetConfig("akka.cluster.client.receptionist");
            if (config == null)
                throw new ArgumentException(string.Format("Actor system [{0}] doesn't have `akka.cluster.client.receptionist` config set up", system.Name));

            return Create(config);
        }

        /// <summary>
        /// Create settings from a configuration with the same layout as the default configuration "akka.cluster.client.receptionist".
        /// </summary>
        public static ClusterReceptionistSettings Create(Config config)
        {
            var role = config.GetString("role");
            if (string.IsNullOrEmpty(role)) role = null;

            return new ClusterReceptionistSettings(
                role,
                config.GetInt("number-of-contacts"),
                config.GetTimeSpan("response-tunnel-receive-timeout"),
                config.GetTimeSpan("heartbeat-interval"),
                config.GetTimeSpan("acceptable-heartbeat-pause"),
                config.GetTimeSpan("failure-detection-interval"));
        }

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

        public ClusterReceptionistSettings WithRole(string role)
        {
            return Copy(role: role);
        }

        public ClusterReceptionistSettings WithoutRole()
        {
            return Copy(role: "");
        }

        public ClusterReceptionistSettings WithNumberOfContacts(int numberOfContacts)
        {
            return Copy(numberOfContacts: numberOfContacts);
        }

        public ClusterReceptionistSettings WithResponseTunnelReceiveTimeout(TimeSpan responseTunnelReceiveTimeout)
        {
            return Copy(responseTunnelReceiveTimeout: responseTunnelReceiveTimeout);
        }

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