//-----------------------------------------------------------------------
// <copyright file="ClusterClientSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Remote;

namespace Akka.Cluster.Tools.Client
{
    [Serializable]
    public sealed class ClusterClientSettings
    {
        public static ClusterClientSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.client");
            var initialContacts = config.GetStringList("initial-contacts").Select(ActorPath.Parse).ToImmutableSortedSet();

            return new ClusterClientSettings(initialContacts,
                config.GetTimeSpan("establishing-get-contacts-interval"),
                config.GetTimeSpan("refresh-contacts-interval"),
                config.GetTimeSpan("heartbeat-interval"),
                config.GetTimeSpan("acceptable-heartbeat-pause"),
                config.GetInt("buffer-size"));
        }

        /// <summary>
        /// Actor paths of the <see cref="ClusterReceptionist"/> actors on the servers (cluster nodes) that the client will try to contact initially.
        /// </summary>
        public readonly IImmutableSet<ActorPath> InitialContacts;

        /// <summary>
        /// Interval at which the client retries to establish contact with one of ClusterReceptionist on the servers (cluster nodes)
        /// </summary>
        public readonly TimeSpan EstablishingGetContactsInterval;

        /// <summary>
        /// Interval at which the client will ask the <see cref="ClusterReceptionist"/> for new contact points to be used for next reconnect.
        /// </summary>
        public readonly TimeSpan RefreshContactsInterval;

        /// <summary>
        /// How often failure detection heartbeat messages for detection of failed connections should be sent.
        /// </summary>
        public readonly TimeSpan HeartbeatInterval;

        /// <summary>
        /// Number of potentially lost/delayed heartbeats that will be accepted before considering it to be an anomaly. 
        /// The ClusterClient is using the <see cref="DeadlineFailureDetector"/>, which will trigger if there are 
        /// no heartbeats within the duration <see cref="HeartbeatInterval"/> + <see cref="AcceptableHeartbeatPause"/>.
        /// </summary>
        public readonly TimeSpan AcceptableHeartbeatPause;

        /// <summary>
        /// If connection to the receptionist is not established the client will buffer this number of messages and deliver 
        /// them the connection is established. When the buffer is full old messages will be dropped when new messages are sent via the client. 
        /// Use 0 to disable buffering, i.e. messages will be dropped immediately if the location of the receptionist is unavailable.
        /// </summary>
        public readonly int BufferSize;

        public ClusterClientSettings(
            IImmutableSet<ActorPath> initialContacts,
            TimeSpan establishingGetContactsInterval,
            TimeSpan refreshContactsInterval,
            TimeSpan heartbeatInterval,
            TimeSpan acceptableHeartbeatPause,
            int bufferSize)
        {
            InitialContacts = initialContacts;
            EstablishingGetContactsInterval = establishingGetContactsInterval;
            RefreshContactsInterval = refreshContactsInterval;
            HeartbeatInterval = heartbeatInterval;
            AcceptableHeartbeatPause = acceptableHeartbeatPause;
            BufferSize = bufferSize;
        }

        public ClusterClientSettings WithInitialContacts(IEnumerable<ActorPath> initialContacts)
        {
            return Copy(initialContacts: initialContacts);
        }

        public ClusterClientSettings WithEstablishingGetContactsInterval(TimeSpan value)
        {
            return Copy(establishingGetContactsInterval: value);
        }

        public ClusterClientSettings WithRefreshContactsInterval(TimeSpan value)
        {
            return Copy(refreshContactsInterval: value);
        }

        public ClusterClientSettings WithHeartbeatInterval(TimeSpan value)
        {
            return Copy(heartbeatInterval: value);
        }

        public ClusterClientSettings WithBufferSize(int bufferSize)
        {
            return Copy(bufferSize: bufferSize);
        }

        private ClusterClientSettings Copy(
            IEnumerable<ActorPath> initialContacts = null,
            TimeSpan? establishingGetContactsInterval = null,
            TimeSpan? refreshContactsInterval = null,
            TimeSpan? heartbeatInterval = null,
            TimeSpan? acceptableHeartbeatPause = null,
            int? bufferSize = null)
        {
            return new ClusterClientSettings(
                initialContacts == null ? InitialContacts : ImmutableHashSet.CreateRange(initialContacts),
                establishingGetContactsInterval ?? EstablishingGetContactsInterval,
                refreshContactsInterval ?? RefreshContactsInterval,
                heartbeatInterval ?? HeartbeatInterval,
                acceptableHeartbeatPause ?? AcceptableHeartbeatPause,
                bufferSize ?? BufferSize);
        }
    }
}