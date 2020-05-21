//-----------------------------------------------------------------------
// <copyright file="ReplicatorSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using System;
using System.Collections.Immutable;
using System.Collections.Generic;

namespace Akka.DistributedData
{
    public sealed class ReplicatorSettings
    {
        /// <summary>
        /// Create settings from the default configuration `akka.cluster.distributed-data`.
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static ReplicatorSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.distributed-data");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ReplicatorSettings>("akka.cluster.distributed-data");

            return Create(config);
        }

        /// <summary>
        /// Create settings from a configuration with the same layout as
        /// the default configuration `akka.cluster.distributed-data`.
        /// </summary>
        /// <param name="config">TBD</param>
        /// <exception cref="ArgumentNullException">TBD</exception>
        /// <returns>TBD</returns>
        public static ReplicatorSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ReplicatorSettings>();

            var dispatcher = config.GetString("use-dispatcher", null);
            if (string.IsNullOrEmpty(dispatcher)) dispatcher = Dispatchers.DefaultDispatcherId;

            var durableConfig = config.GetConfig("durable");
            var durableKeys = durableConfig.GetStringList("keys");
            var durableStoreProps = Props.Empty;
            var durableStoreTypeName = durableConfig.GetString("store-actor-class", null);

            if (durableKeys.Count != 0)
            {
                Type durableStoreType;
                if (string.IsNullOrEmpty(durableStoreTypeName))
                {
                    throw new ArgumentException($"`akka.cluster.distributed-data.durable.store-actor-class` must be set when `akka.cluster.distributed-data.durable.keys` have been configured.");
                }

                durableStoreType = Type.GetType(durableStoreTypeName);
                if (durableStoreType is null)
                {
                    throw new ArgumentException($"`akka.cluster.distributed-data.durable.store-actor-class` is set to an invalid class {durableStoreType}.");
                }
                durableStoreProps = Props.Create(durableStoreType, durableConfig).WithDispatcher(durableConfig.GetString("use-dispatcher"));
            }

            // TODO: This constructor call fails when these fields are not populated inside the Config object:
            // TODO: `pruning-marker-time-to-live` key depends on Config.GetTimeSpan() to return a TimeSpan.Zero default.
            return new ReplicatorSettings(
                role: config.GetString("role"),
                gossipInterval: config.GetTimeSpan("gossip-interval"),
                notifySubscribersInterval: config.GetTimeSpan("notify-subscribers-interval"),
                maxDeltaElements: config.GetInt("max-delta-elements"),
                dispatcher: dispatcher,
                pruningInterval: config.GetTimeSpan("pruning-interval"),
                maxPruningDissemination: config.GetTimeSpan("max-pruning-dissemination"),
                durableKeys: durableKeys.ToImmutableHashSet(),
                durableStoreProps: durableStoreProps,
                pruningMarkerTimeToLive: config.GetTimeSpan("pruning-marker-time-to-live", null),
                durablePruningMarkerTimeToLive: durableConfig.GetTimeSpan("pruning-marker-time-to-live"),
                maxDeltaSize: config.GetInt("delta-crdt.max-delta-size"));
        }

        /// <summary>
        /// Determines if a durable store has been configured and is used. If configuration has defined some 
        /// durable keys, this field must be true.
        /// </summary>
        public bool IsDurable => !Equals(DurableStoreProps, Props.Empty);

        /// <summary>
        /// Replicas are running on members tagged with this role. All members are used if undefined.
        /// </summary>
        public string Role { get; }

        /// <summary>
        /// How often the Replicator should send out gossip information.
        /// </summary>
        public TimeSpan GossipInterval { get; }

        /// <summary>
        /// How often the subscribers will be notified of changes, if any.
        /// </summary>
        public TimeSpan NotifySubscribersInterval { get; }

        /// <summary>
        /// Maximum number of entries to transfer in one gossip message when synchronizing 
        /// the replicas.Next chunk will be transferred in next round of gossip.
        /// </summary>
        public int MaxDeltaElements { get; }

        /// <summary>
        /// Id of the dispatcher to use for Replicator actors. 
        /// If not specified the default dispatcher is used.
        /// </summary>
        public string Dispatcher { get; }

        /// <summary>
        /// How often the Replicator checks for pruning of data associated with removed cluster nodes.
        /// </summary>
        public TimeSpan PruningInterval { get; }

        /// <summary>
        /// How long time it takes (worst case) to spread the data to all other replica nodes.
        /// This is used when initiating and completing the pruning process of data associated 
        /// with removed cluster nodes. The time measurement is stopped when any replica is 
        /// unreachable, so it should be configured to worst case in a healthy cluster.
        /// </summary>
        public TimeSpan MaxPruningDissemination { get; }

        /// <summary>
        /// Keys that are durable. Prefix matching is supported by using `*` at the end of a key.
        /// All entries can be made durable by including "*" in the `Set`.
        /// </summary>
        public IImmutableSet<string> DurableKeys { get; }

        /// <summary>
        /// How long the tombstones of a removed node are kept on their CRDTs.
        /// </summary>
        public TimeSpan PruningMarkerTimeToLive { get; }

        /// <summary>
        /// 
        /// </summary>
        public TimeSpan DurablePruningMarkerTimeToLive { get; }

        /// <summary>
        /// Props for the durable store actor, when taken from actor class type name, it requires
        /// its constructor to take <see cref="Config"/> as constructor parameter.
        /// </summary>
        public Props DurableStoreProps { get; }

        public int MaxDeltaSize { get; }

        public ReplicatorSettings(string role,
                                  TimeSpan gossipInterval,
                                  TimeSpan notifySubscribersInterval,
                                  int maxDeltaElements,
                                  string dispatcher,
                                  TimeSpan pruningInterval,
                                  TimeSpan maxPruningDissemination, 
                                  IImmutableSet<string> durableKeys, 
                                  Props durableStoreProps, 
                                  TimeSpan pruningMarkerTimeToLive, 
                                  TimeSpan durablePruningMarkerTimeToLive,
                                  int maxDeltaSize)
        {
            Role = role;
            GossipInterval = gossipInterval;
            NotifySubscribersInterval = notifySubscribersInterval;
            MaxDeltaElements = maxDeltaElements;
            Dispatcher = dispatcher;
            PruningInterval = pruningInterval;
            MaxPruningDissemination = maxPruningDissemination;
            DurableKeys = durableKeys;
            DurableStoreProps = durableStoreProps;
            PruningMarkerTimeToLive = pruningMarkerTimeToLive;
            DurablePruningMarkerTimeToLive = durablePruningMarkerTimeToLive;
            MaxDeltaSize = maxDeltaSize;
        }

        private ReplicatorSettings Copy(string role = null,
            TimeSpan? gossipInterval = null,
            TimeSpan? notifySubscribersInterval = null,
            int? maxDeltaElements = null,
            string dispatcher = null,
            TimeSpan? pruningInterval = null,
            TimeSpan? maxPruningDissemination = null,
            IImmutableSet<string> durableKeys = null,
            Props durableStoreProps = null,
            TimeSpan? pruningMarkerTimeToLive = null,
            TimeSpan? durablePruningMarkerTimeToLive = null,
            int? maxDeltaSize = null)
        {
            return new ReplicatorSettings(
                role: role ?? this.Role,
                gossipInterval: gossipInterval ?? this.GossipInterval,
                notifySubscribersInterval: notifySubscribersInterval ?? this.NotifySubscribersInterval,
                maxDeltaElements: maxDeltaElements ?? this.MaxDeltaElements,
                dispatcher: dispatcher ?? this.Dispatcher,
                pruningInterval: pruningInterval ?? this.PruningInterval,
                maxPruningDissemination: maxPruningDissemination ?? this.MaxPruningDissemination,
                durableKeys: durableKeys ?? this.DurableKeys,
                durableStoreProps: durableStoreProps ?? this.DurableStoreProps,
                pruningMarkerTimeToLive: pruningMarkerTimeToLive ?? this.PruningMarkerTimeToLive,
                durablePruningMarkerTimeToLive: durablePruningMarkerTimeToLive ?? this.DurablePruningMarkerTimeToLive,
                maxDeltaSize: maxDeltaSize ?? this.MaxDeltaSize);
        }

        public ReplicatorSettings WithRole(string role) => Copy(role: role);
        public ReplicatorSettings WithGossipInterval(TimeSpan gossipInterval) => Copy(gossipInterval: gossipInterval);
        public ReplicatorSettings WithNotifySubscribersInterval(TimeSpan notifySubscribersInterval) => Copy(notifySubscribersInterval: notifySubscribersInterval);
        public ReplicatorSettings WithMaxDeltaElements(int maxDeltaElements) => Copy(maxDeltaElements: maxDeltaElements);
        public ReplicatorSettings WithDispatcher(string dispatcher) => Copy(dispatcher: string.IsNullOrEmpty(dispatcher) ? Dispatchers.DefaultDispatcherId : dispatcher);
        public ReplicatorSettings WithPruning(TimeSpan pruningInterval, TimeSpan maxPruningDissemination) => 
            Copy(pruningInterval: pruningInterval, maxPruningDissemination: maxPruningDissemination);
        public ReplicatorSettings WithDurableKeys(IImmutableSet<string> durableKeys) => Copy(durableKeys: durableKeys);
        public ReplicatorSettings WithDurableStoreProps(Props durableStoreProps) => Copy(durableStoreProps: durableStoreProps);
        public ReplicatorSettings WithPruningMarkerTimeToLive(TimeSpan pruningMarkerTtl, TimeSpan durablePruningMarkerTtl) =>
            Copy(pruningMarkerTimeToLive: pruningMarkerTtl, durablePruningMarkerTimeToLive: durablePruningMarkerTtl);
        public ReplicatorSettings WithMaxDeltaSize(int maxDeltaSize) => Copy(maxDeltaSize: maxDeltaSize);
    }
}
