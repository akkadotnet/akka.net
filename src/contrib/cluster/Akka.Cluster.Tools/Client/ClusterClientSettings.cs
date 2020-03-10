//-----------------------------------------------------------------------
// <copyright file="ClusterClientSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote;

namespace Akka.Cluster.Tools.Client
{
    /// <summary>
    /// TBD
    /// </summary>
    public sealed class ClusterClientSettings : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Create settings from the default configuration 'akka.cluster.client'.
        /// </summary>
        /// <param name="system">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public static ClusterClientSettings Create(ActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(ClusterClientReceptionist.DefaultConfig());

            var config = system.Settings.Config.GetConfig("akka.cluster.client");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterClientSettings>("akka.cluster.client");//($"Failed to create {nameof(ClusterClientSettings)}: Actor system [{system.Name}] doesn't have `akka.cluster.client` config set up");

            return Create(config);
        }

        /// <summary>
        /// Create settings from a configuration with the same layout as the default configuration 'akka.cluster.client'.
        /// </summary>
        /// <param name="config">TBD</param>
        /// <returns>TBD</returns>
        public static ClusterClientSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterClientSettings>();

            var initialContacts = config.GetStringList("initial-contacts", new string[] { }).Select(ActorPath.Parse).ToImmutableSortedSet();

            var useReconnect = config.GetString("reconnect-timeout", "").ToLowerInvariant();
            TimeSpan? reconnectTimeout = 
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
                reconnectTimeout);
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
        /// TBD
        /// </summary>
        /// <param name="initialContacts">TBD</param>
        /// <param name="establishingGetContactsInterval">TBD</param>
        /// <param name="refreshContactsInterval">TBD</param>
        /// <param name="heartbeatInterval">TBD</param>
        /// <param name="acceptableHeartbeatPause">TBD</param>
        /// <param name="bufferSize">TBD</param>
        /// <param name="reconnectTimeout">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        public ClusterClientSettings(
            IImmutableSet<ActorPath> initialContacts,
            TimeSpan establishingGetContactsInterval,
            TimeSpan refreshContactsInterval,
            TimeSpan heartbeatInterval,
            TimeSpan acceptableHeartbeatPause,
            int bufferSize,
            TimeSpan? reconnectTimeout = null)
        {
            if (bufferSize < 0 || bufferSize > 10000)
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
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialContacts">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public ClusterClientSettings WithInitialContacts(IImmutableSet<ActorPath> initialContacts)
        {
            if (initialContacts.Count == 0)
            {
                throw new ArgumentException("InitialContacts must be defined");
            }

            return Copy(initialContacts: initialContacts);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public ClusterClientSettings WithEstablishingGetContactsInterval(TimeSpan value)
        {
            return Copy(establishingGetContactsInterval: value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public ClusterClientSettings WithRefreshContactsInterval(TimeSpan value)
        {
            return Copy(refreshContactsInterval: value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public ClusterClientSettings WithHeartbeatInterval(TimeSpan value)
        {
            return Copy(heartbeatInterval: value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="bufferSize">TBD</param>
        /// <returns>TBD</returns>
        public ClusterClientSettings WithBufferSize(int bufferSize)
        {
            return Copy(bufferSize: bufferSize);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reconnectTimeout">TBD</param>
        /// <returns>TBD</returns>
        public ClusterClientSettings WithReconnectTimeout(TimeSpan? reconnectTimeout)
        {
            return Copy(reconnectTimeout: reconnectTimeout);
        }

        private ClusterClientSettings Copy(
            IImmutableSet<ActorPath> initialContacts = null,
            TimeSpan? establishingGetContactsInterval = null,
            TimeSpan? refreshContactsInterval = null,
            TimeSpan? heartbeatInterval = null,
            TimeSpan? acceptableHeartbeatPause = null,
            int? bufferSize = null,
            TimeSpan? reconnectTimeout = null)
        {
            return new ClusterClientSettings(
                initialContacts ?? InitialContacts,
                establishingGetContactsInterval ?? EstablishingGetContactsInterval,
                refreshContactsInterval ?? RefreshContactsInterval,
                heartbeatInterval ?? HeartbeatInterval,
                acceptableHeartbeatPause ?? AcceptableHeartbeatPause,
                bufferSize ?? BufferSize,
                reconnectTimeout ?? ReconnectTimeout);
        }
    }
}
