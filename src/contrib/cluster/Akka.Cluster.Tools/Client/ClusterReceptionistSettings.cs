//-----------------------------------------------------------------------
// <copyright file="ClusterReceptionistSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Tools.Client
{
    /// <summary>
    /// Settings that control receptionist placement, contact-point selection, response tunnels, and client failure detection.
    /// </summary>
    public sealed class ClusterReceptionistSettings : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Create settings from the default configuration "akka.cluster.client.receptionist".
        /// </summary>
        /// <param name="system">Actor system whose configuration supplies receptionist settings.</param>
        /// <exception cref="ConfigurationException">Thrown when the actor system has no receptionist configuration.</exception>
        /// <returns>Settings loaded from <c>akka.cluster.client.receptionist</c>.</returns>
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
        /// <param name="config">Configuration with the layout of <c>akka.cluster.client.receptionist</c>.</param>
        /// <returns>Settings parsed from the supplied configuration.</returns>
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
        /// Creates receptionist settings with the supplied role, contact count, tunnel timeout, and heartbeat timing.
        /// </summary>
        /// <param name="role">Cluster role on which to start the receptionist; null or empty allows all members.</param>
        /// <param name="numberOfContacts">Maximum number of receptionist contact points returned to a client.</param>
        /// <param name="responseTunnelReceiveTimeout">Idle timeout for a client response tunnel.</param>
        /// <param name="heartbeatInterval">Expected interval between client heartbeat messages.</param>
        /// <param name="acceptableHeartbeatPause">Additional heartbeat delay tolerated before a client is considered unreachable.</param>
        /// <param name="failureDetectionInterval">Interval at which the receptionist checks client heartbeat deadlines.</param>
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
        /// Returns a copy with the receptionist role changed.
        /// </summary>
        /// <param name="role">Role on which the receptionist should run; null or empty allows all members.</param>
        /// <returns>A copy of these settings with the supplied role.</returns>
        public ClusterReceptionistSettings WithRole(string role)
        {
            return Copy(role: role);
        }

        /// <summary>
        /// Returns a copy with no role restriction for the receptionist.
        /// </summary>
        /// <returns>A copy of these settings that allows the receptionist on all cluster members.</returns>
        public ClusterReceptionistSettings WithoutRole()
        {
            return Copy(role: "");
        }

        /// <summary>
        /// Returns a copy with the maximum number of contact points changed.
        /// </summary>
        /// <param name="numberOfContacts">Maximum contact points to return to each client.</param>
        /// <returns>A copy of these settings with the supplied contact count.</returns>
        public ClusterReceptionistSettings WithNumberOfContacts(int numberOfContacts)
        {
            return Copy(numberOfContacts: numberOfContacts);
        }

        /// <summary>
        /// Returns a copy with the response tunnel's idle timeout changed.
        /// </summary>
        /// <param name="responseTunnelReceiveTimeout">New idle timeout for response tunnels.</param>
        /// <returns>A copy of these settings with the supplied timeout.</returns>
        public ClusterReceptionistSettings WithResponseTunnelReceiveTimeout(TimeSpan responseTunnelReceiveTimeout)
        {
            return Copy(responseTunnelReceiveTimeout: responseTunnelReceiveTimeout);
        }

        /// <summary>
        /// Returns a copy with client heartbeat and deadline-check intervals changed.
        /// </summary>
        /// <param name="heartbeatInterval">Expected interval between client heartbeat messages.</param>
        /// <param name="acceptableHeartbeatPause">Additional heartbeat delay tolerated before a client is considered unreachable.</param>
        /// <param name="failureDetectionInterval">Interval at which the receptionist checks client heartbeat deadlines.</param>
        /// <returns>A copy of these settings with the supplied timing values.</returns>
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
