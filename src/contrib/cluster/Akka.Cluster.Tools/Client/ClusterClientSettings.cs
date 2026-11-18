//-----------------------------------------------------------------------
// <copyright file="ClusterClientSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.Client.Serialization;
using Akka.Configuration;
using Akka.Remote;

#nullable enable
namespace Akka.Cluster.Tools.Client
{
    /// <summary>
    /// Immutable settings that control how a cluster client discovers receptionists, buffers messages, and monitors connections.
    /// </summary>
    public sealed class ClusterClientSettings : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Create settings from the default configuration 'akka.cluster.client'.
        /// </summary>
        /// <param name="system">Actor system whose configuration supplies the cluster client settings.</param>
        /// <exception cref="ConfigurationException">Thrown when the actor system has no cluster client configuration.</exception>
        /// <returns>Settings loaded from the actor system's <c>akka.cluster.client</c> configuration.</returns>
        public static ClusterClientSettings Create(ActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(ClusterClientReceptionist.DefaultConfig());

            var config = system.Settings.Config.GetConfig("akka.cluster.client");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterClientSettings>("akka.cluster.client");//($"Failed to create {nameof(ClusterClientSettings)}: Actor system [{system.Name}] doesn't have `akka.cluster.client` config set up");

            if (config.GetBoolean("use-legacy-serialization"))
            {
                system.Serialization.RemoveSerializationMap(typeof(IClusterClientProtocolMessage));
            }
            
            return Create(config);
        }

        /// <summary>
        /// Create settings from a configuration with the same layout as the default configuration 'akka.cluster.client'.
        /// </summary>
        /// <param name="config">Configuration with the layout of <c>akka.cluster.client</c>.</param>
        /// <returns>Settings parsed from the supplied configuration.</returns>
        public static ClusterClientSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterClientSettings>();

            var initialContacts = config.GetStringList("initial-contacts", new string[] { }).Select(ActorPath.Parse).ToImmutableSortedSet();

            var useReconnect = config.GetString("reconnect-timeout", "").ToLowerInvariant();
            var reconnectTimeout = 
                useReconnect.Equals("off") ||
                useReconnect.Equals("false") ||
                useReconnect.Equals("no") ? 
                    null : 
                    (TimeSpan?)config.GetTimeSpan("reconnect-timeout");

            return new ClusterClientSettings(initialContacts,
                config.GetTimeSpan("establishing-get-contacts-interval"),
                config.GetTimeSpan("refresh-contacts-interval"),
                config.GetTimeSpan("heartbeat-interval"),
                config.GetTimeSpan("acceptable-heartbeat-pause"),
                config.GetInt("buffer-size"),
                config.GetBoolean("use-legacy-serialization"),
                config.GetBoolean("use-initial-contacts-discovery"),
                ClusterClientDiscoverySettings.Create(config),
                reconnectTimeout,
                config.GetBoolean("verbose-logging"));
        }

        /// <summary>
        /// Actor paths of the <see cref="ClusterReceptionist"/> actors on the servers (cluster nodes) that the client will try to contact initially.
        /// </summary>
        public IImmutableSet<ActorPath> InitialContacts { get; }

        /// <summary>
        /// Interval at which the client retries to establish contact with one of ClusterReceptionist on the servers (cluster nodes)
        /// </summary>
        public TimeSpan EstablishingGetContactsInterval { get; }

        /// <summary>
        /// Interval at which the client will ask the <see cref="ClusterReceptionist"/> for new contact points to be used for next reconnect.
        /// </summary>
        public TimeSpan RefreshContactsInterval { get; }

        /// <summary>
        /// How often failure detection heartbeat messages for detection of failed connections should be sent.
        /// </summary>
        public TimeSpan HeartbeatInterval { get; }

        /// <summary>
        /// Number of potentially lost/delayed heartbeats that will be accepted before considering it to be an anomaly. 
        /// The ClusterClient is using the <see cref="DeadlineFailureDetector"/>, which will trigger if there are 
        /// no heartbeats within the duration <see cref="HeartbeatInterval"/> + <see cref="AcceptableHeartbeatPause"/>.
        /// </summary>
        public TimeSpan AcceptableHeartbeatPause { get; }

        /// <summary>
        /// If connection to the receptionist is not established the client will buffer this number of messages and deliver 
        /// them the connection is established. When the buffer is full old messages will be dropped when new messages are sent via the client. 
        /// Use 0 to disable buffering, i.e. messages will be dropped immediately if the location of the receptionist is unavailable.
        /// </summary>
        public int BufferSize { get; }

        /// <summary>
        /// If the connection to the receptionist is lost and cannot
        /// be re-established within this duration the cluster client will be stopped. This makes it possible
        /// to watch it from another actor and possibly acquire a new list of InitialContacts from some
        /// external service registry
        /// </summary>
        public TimeSpan? ReconnectTimeout { get; }

        /// <summary>
        /// If set to true, will cause all ClusterClient message to be serialized using the default <see cref="object"/>
        /// serializer.
        /// If set to false, will cause all ClusterClient message to be serialized using <see cref="ClusterClientMessageSerializer"/>
        /// </summary>
        public bool UseLegacySerialization { get; }
        
        public bool UseInitialContactDiscovery { get; }
        
        public ClusterClientDiscoverySettings DiscoverySettings { get; }
        
        public bool VerboseLogging { get; }
        
        /// <summary>
        /// Creates settings using the legacy serialization format.
        /// </summary>
        /// <param name="initialContacts">Receptionist actor paths contacted when establishing or reconnecting.</param>
        /// <param name="establishingGetContactsInterval">Interval between contact requests while establishing a connection.</param>
        /// <param name="refreshContactsInterval">Interval at which the connected client requests refreshed contact points.</param>
        /// <param name="heartbeatInterval">Interval between receptionist heartbeat checks.</param>
        /// <param name="acceptableHeartbeatPause">Additional delay tolerated beyond the heartbeat interval before declaring contact lost.</param>
        /// <param name="bufferSize">Maximum number of messages buffered while no receptionist connection is available; zero disables buffering.</param>
        /// <param name="reconnectTimeout">Optional time after which the client stops if it cannot reconnect.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="bufferSize"/> is outside the supported range.</exception>
        [Obsolete("Use constructor with useLegacySerialization argument instead. Since 1.5.15")]
        public ClusterClientSettings(
            IImmutableSet<ActorPath> initialContacts,
            TimeSpan establishingGetContactsInterval,
            TimeSpan refreshContactsInterval,
            TimeSpan heartbeatInterval,
            TimeSpan acceptableHeartbeatPause,
            int bufferSize,
            TimeSpan? reconnectTimeout = null)
            : this(
                initialContacts: initialContacts,
                establishingGetContactsInterval: establishingGetContactsInterval,
                refreshContactsInterval: refreshContactsInterval,
                heartbeatInterval: heartbeatInterval,
                acceptableHeartbeatPause: acceptableHeartbeatPause,
                bufferSize: bufferSize,
                useLegacySerialization: true,
                reconnectTimeout: reconnectTimeout)
        {
        }

        /// <summary>
        /// Creates settings with the supplied reconnect and serialization options.
        /// </summary>
        /// <param name="initialContacts">Receptionist actor paths contacted when establishing or reconnecting.</param>
        /// <param name="establishingGetContactsInterval">Interval between contact requests while establishing a connection.</param>
        /// <param name="refreshContactsInterval">Interval at which the connected client requests refreshed contact points.</param>
        /// <param name="heartbeatInterval">Interval between receptionist heartbeat checks.</param>
        /// <param name="acceptableHeartbeatPause">Additional delay tolerated beyond the heartbeat interval before declaring contact lost.</param>
        /// <param name="bufferSize">Maximum number of messages buffered while no receptionist connection is available; zero disables buffering.</param>
        /// <param name="reconnectTimeout">Optional time after which the client stops if it cannot reconnect.</param>
        /// <param name="useLegacySerialization">Whether cluster client protocol messages use the default object serializer.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="bufferSize"/> is outside the supported range.</exception>
        [Obsolete("Use constructor with useInitialContactsDiscovery and discoverySettings argument instead. Since 1.5.25")]
        public ClusterClientSettings(
            IImmutableSet<ActorPath> initialContacts,
            TimeSpan establishingGetContactsInterval,
            TimeSpan refreshContactsInterval,
            TimeSpan heartbeatInterval,
            TimeSpan acceptableHeartbeatPause,
            int bufferSize,
            bool useLegacySerialization,
            TimeSpan? reconnectTimeout = null)
            : this(
                initialContacts: initialContacts,
                establishingGetContactsInterval: establishingGetContactsInterval,
                refreshContactsInterval: refreshContactsInterval,
                heartbeatInterval: heartbeatInterval,
                acceptableHeartbeatPause: acceptableHeartbeatPause,
                bufferSize: bufferSize,
                useLegacySerialization: useLegacySerialization,
                useInitialContactsDiscovery: false,
                discoverySettings: null,
                reconnectTimeout: reconnectTimeout)
        {
        }

        /// <summary>
        /// Creates settings for contact discovery, connection timing, buffering, serialization, and logging.
        /// </summary>
        /// <param name="initialContacts">Receptionist actor paths contacted when establishing or reconnecting.</param>
        /// <param name="establishingGetContactsInterval">Interval between contact requests while establishing a connection.</param>
        /// <param name="refreshContactsInterval">Interval at which the connected client requests refreshed contact points.</param>
        /// <param name="heartbeatInterval">Interval between receptionist heartbeat checks.</param>
        /// <param name="acceptableHeartbeatPause">Additional delay tolerated beyond the heartbeat interval before declaring contact lost.</param>
        /// <param name="bufferSize">Maximum number of messages buffered while no receptionist connection is available; must be between 0 and 10000.</param>
        /// <param name="useInitialContactsDiscovery">Whether to discover initial contacts before connecting to a receptionist.</param>
        /// <param name="discoverySettings">Settings used when initial contact discovery is enabled; <see langword="null"/> selects the empty settings.</param>
        /// <param name="reconnectTimeout">Optional time after which the client stops if it cannot reconnect.</param>
        /// <param name="useLegacySerialization">Whether cluster client protocol messages use the default object serializer.</param>
        /// <param name="verboseLogging">Whether to enable verbose cluster client logging.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="bufferSize"/> is outside the supported range.</exception>
        public ClusterClientSettings(
            IImmutableSet<ActorPath> initialContacts,
            TimeSpan establishingGetContactsInterval,
            TimeSpan refreshContactsInterval,
            TimeSpan heartbeatInterval,
            TimeSpan acceptableHeartbeatPause,
            int bufferSize,
            bool useLegacySerialization,
            bool useInitialContactsDiscovery,
            ClusterClientDiscoverySettings? discoverySettings = null,
            TimeSpan? reconnectTimeout = null,
            bool verboseLogging = false)
        {
            if (bufferSize is < 0 or > 10000)
            {
                throw new ArgumentException("BufferSize must be >= 0 and <= 10000");
            }

            InitialContacts = initialContacts;
            EstablishingGetContactsInterval = establishingGetContactsInterval;
            RefreshContactsInterval = refreshContactsInterval;
            HeartbeatInterval = heartbeatInterval;
            AcceptableHeartbeatPause = acceptableHeartbeatPause;
            BufferSize = bufferSize;
            ReconnectTimeout = reconnectTimeout;
            UseLegacySerialization = useLegacySerialization;
            UseInitialContactDiscovery = useInitialContactsDiscovery;
            DiscoverySettings = discoverySettings ?? ClusterClientDiscoverySettings.Empty;
            VerboseLogging = verboseLogging;
        }
        
        /// <summary>
        /// Returns a copy using the specified initial receptionist contact paths.
        /// </summary>
        /// <param name="initialContacts">Non-empty set of receptionist actor paths to contact initially.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="initialContacts"/> is empty.</exception>
        /// <returns>A copy of these settings with the specified initial contacts.</returns>
        public ClusterClientSettings WithInitialContacts(IImmutableSet<ActorPath> initialContacts)
        {
            if (initialContacts.Count == 0)
            {
                throw new ArgumentException("InitialContacts must be defined");
            }

            return Copy(initialContacts: initialContacts);
        }

        /// <summary>
        /// Returns a copy with the interval between contact requests while establishing a connection changed.
        /// </summary>
        /// <param name="value">New interval between contact requests during connection establishment.</param>
        /// <returns>A copy of these settings with the supplied interval.</returns>
        public ClusterClientSettings WithEstablishingGetContactsInterval(TimeSpan value)
        {
            return Copy(establishingGetContactsInterval: value);
        }

        /// <summary>
        /// Returns a copy with the contact-point refresh interval changed.
        /// </summary>
        /// <param name="value">New interval at which the connected client requests updated contacts.</param>
        /// <returns>A copy of these settings with the supplied interval.</returns>
        public ClusterClientSettings WithRefreshContactsInterval(TimeSpan value)
        {
            return Copy(refreshContactsInterval: value);
        }

        /// <summary>
        /// Returns a copy with the receptionist heartbeat interval changed.
        /// </summary>
        /// <param name="value">New interval between heartbeat checks.</param>
        /// <returns>A copy of these settings with the supplied interval.</returns>
        public ClusterClientSettings WithHeartbeatInterval(TimeSpan value)
        {
            return Copy(heartbeatInterval: value);
        }

        /// <summary>
        /// Returns a copy with the maximum buffered message count changed.
        /// </summary>
        /// <param name="bufferSize">New maximum number of messages buffered while no receptionist is connected.</param>
        /// <returns>A copy of these settings with the supplied buffer size.</returns>
        public ClusterClientSettings WithBufferSize(int bufferSize)
        {
            return Copy(bufferSize: bufferSize);
        }

        /// <summary>
        /// Returns a copy with the reconnect timeout changed.
        /// </summary>
        /// <param name="reconnectTimeout">New timeout after which the client stops if reconnection has not succeeded; a null value retains the current timeout.</param>
        /// <returns>A copy of these settings with the supplied timeout when it is non-null.</returns>
        public ClusterClientSettings WithReconnectTimeout(TimeSpan? reconnectTimeout)
        {
            return Copy(reconnectTimeout: reconnectTimeout);
        }

        public ClusterClientSettings WithUseLegacySerialization(bool useLegacySerialization)
            => Copy(useLegacySerialization: useLegacySerialization);

        public ClusterClientSettings WithInitialContactsDiscovery(
            bool useInitialContactsDiscovery, 
            ClusterClientDiscoverySettings? discoverySettings = null)
            => Copy(useInitialContactsDiscovery: useInitialContactsDiscovery, discoverySettings: discoverySettings);
        
        private ClusterClientSettings Copy(
            IImmutableSet<ActorPath>? initialContacts = null,
            TimeSpan? establishingGetContactsInterval = null,
            TimeSpan? refreshContactsInterval = null,
            TimeSpan? heartbeatInterval = null,
            TimeSpan? acceptableHeartbeatPause = null,
            int? bufferSize = null,
            bool? useLegacySerialization = null,
            bool? useInitialContactsDiscovery = null,
            ClusterClientDiscoverySettings? discoverySettings = null,
            TimeSpan? reconnectTimeout = null,
            bool? verboseLogging = null)
        {
            return new ClusterClientSettings(
                initialContacts ?? InitialContacts,
                establishingGetContactsInterval ?? EstablishingGetContactsInterval,
                refreshContactsInterval ?? RefreshContactsInterval,
                heartbeatInterval ?? HeartbeatInterval,
                acceptableHeartbeatPause ?? AcceptableHeartbeatPause,
                bufferSize ?? BufferSize,
                useLegacySerialization ?? UseLegacySerialization,
                useInitialContactsDiscovery ?? UseInitialContactDiscovery,
                discoverySettings ?? DiscoverySettings,
                reconnectTimeout ?? ReconnectTimeout,
                verboseLogging ?? VerboseLogging);
        }
    }
}
