//-----------------------------------------------------------------------
// <copyright file="ReplicatorSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using System;
using System.Collections.Immutable;

namespace Akka.DistributedData
{
    public sealed class ReplicatorSettings : ICloneable
    {
        /// <summary>
        /// Create settings from the default configuration `akka.cluster.distributed-data`.
        /// </summary>
        public static ReplicatorSettings Create(ActorSystem system) =>
            Create(system.Settings.Config.GetConfig("akka.cluster.distributed-data"));

        /// <summary>
        /// Create settings from a configuration with the same layout as
        /// the default configuration `akka.cluster.distributed-data`.
        /// </summary>
        public static ReplicatorSettings Create(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config), "DistributedData HOCON config not provided.");

            var dispatcher = config.GetString("use-dispatcher");
            if (string.IsNullOrEmpty(dispatcher)) dispatcher = Dispatchers.DefaultDispatcherId;

            var durableConfig = config.GetConfig("durable");
            var durableKeys = durableConfig.GetStringList("keys");
            Props durableStoreProps = Props.Empty;
            if (durableKeys.Count != 0)
            {
                var durableStoreType = Type.GetType(durableConfig.GetString("store-actor-class"));
                if (durableStoreType == null)
                {
                    throw new ArgumentException($"`akka.cluster.distributed-data.durable.store-actor-class` must be set when `akka.cluster.distributed-data.durable.keys` have been configured.");
                }
                durableStoreProps = Props.Create(durableStoreType, durableConfig).WithDispatcher(durableConfig.GetString("use-dispatcher"));
            }

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
                pruningMarkerTimeToLive: config.GetTimeSpan("pruning-marker-time-to-live"),
                durablePruningMarkerTimeToLive: durableConfig.GetTimeSpan("pruning-marker-time-to-live"));
        }

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
        /// 
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
                                  TimeSpan durablePruningMarkerTimeToLive)
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
        }

        public object Clone()
        {
            return new ReplicatorSettings(Role, GossipInterval, NotifySubscribersInterval, MaxDeltaElements, Dispatcher, PruningInterval, MaxPruningDissemination, DurableKeys, DurableStoreProps, PruningMarkerTimeToLive, DurablePruningMarkerTimeToLive);
        }

        public ReplicatorSettings WithRole(string role)
        {
            return new ReplicatorSettings(role, GossipInterval, NotifySubscribersInterval, MaxDeltaElements, Dispatcher, PruningInterval, MaxPruningDissemination, DurableKeys, DurableStoreProps, PruningMarkerTimeToLive, DurablePruningMarkerTimeToLive);
        }

        public ReplicatorSettings WithGossipInterval(TimeSpan gossipInterval)
        {
            return new ReplicatorSettings(Role, gossipInterval, NotifySubscribersInterval, MaxDeltaElements, Dispatcher, PruningInterval, MaxPruningDissemination, DurableKeys, DurableStoreProps, PruningMarkerTimeToLive, DurablePruningMarkerTimeToLive);
        }

        public ReplicatorSettings WithNotifySubscribersInterval(TimeSpan notifySubscribersInterval)
        {
            return new ReplicatorSettings(Role, GossipInterval, notifySubscribersInterval, MaxDeltaElements, Dispatcher, PruningInterval, MaxPruningDissemination, DurableKeys, DurableStoreProps, PruningMarkerTimeToLive, DurablePruningMarkerTimeToLive);
        }

        public ReplicatorSettings WithMaxDeltaElements(int maxDeltaElements)
        {
            return new ReplicatorSettings(Role, GossipInterval, NotifySubscribersInterval, maxDeltaElements, Dispatcher, PruningInterval, MaxPruningDissemination, DurableKeys, DurableStoreProps, PruningMarkerTimeToLive, DurablePruningMarkerTimeToLive);
        }

        public ReplicatorSettings WithDispatcher(string dispatcher)
        {
            if(string.IsNullOrEmpty(dispatcher))
            {
                dispatcher = Dispatchers.DefaultDispatcherId;
            }
            return new ReplicatorSettings(Role, GossipInterval, NotifySubscribersInterval, MaxDeltaElements, dispatcher, PruningInterval, MaxPruningDissemination, DurableKeys, DurableStoreProps, PruningMarkerTimeToLive, DurablePruningMarkerTimeToLive);
        }

        public ReplicatorSettings WithPruning(TimeSpan pruningInterval, TimeSpan maxPruningDissemination)
        {
            return new ReplicatorSettings(Role, GossipInterval, NotifySubscribersInterval, MaxDeltaElements, Dispatcher, pruningInterval, maxPruningDissemination, DurableKeys, DurableStoreProps, PruningMarkerTimeToLive, DurablePruningMarkerTimeToLive);
        }

        public ReplicatorSettings WithDurableKeys(IImmutableSet<string> durableKeys)
        {
            return new ReplicatorSettings(Role, GossipInterval, NotifySubscribersInterval, MaxDeltaElements, Dispatcher, PruningInterval, MaxPruningDissemination, durableKeys, DurableStoreProps, PruningMarkerTimeToLive, DurablePruningMarkerTimeToLive);
        }

        public ReplicatorSettings WithDurableStoreProps(Props durableStoreProps)
        {
            return new ReplicatorSettings(Role, GossipInterval, NotifySubscribersInterval, MaxDeltaElements, Dispatcher, PruningInterval, MaxPruningDissemination, DurableKeys, durableStoreProps, PruningMarkerTimeToLive, DurablePruningMarkerTimeToLive);
        }

        public ReplicatorSettings WithPruningMarkerTimeToLive(TimeSpan pruningMarkerTtl, TimeSpan durablePruningMarkerTtl)
        {
            return new ReplicatorSettings(Role, GossipInterval, NotifySubscribersInterval, MaxDeltaElements, Dispatcher, PruningInterval, MaxPruningDissemination, DurableKeys, DurableStoreProps, pruningMarkerTtl, durablePruningMarkerTtl);
        }
    }
}
