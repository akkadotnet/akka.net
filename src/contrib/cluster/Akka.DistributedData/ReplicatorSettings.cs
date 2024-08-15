// -----------------------------------------------------------------------
//  <copyright file="ReplicatorSettings.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.DistributedData;

public sealed class ReplicatorSettings
{
    [Obsolete("Use constructor with `verboseDebugLogging` argument. Obsolete since v1.5.0-alpha2")]
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
        int maxDeltaSize,
        bool restartReplicatorOnFailure,
        bool preferOldest) : this(
        role,
        gossipInterval,
        notifySubscribersInterval,
        maxDeltaElements,
        dispatcher,
        pruningInterval,
        maxPruningDissemination,
        durableKeys,
        durableStoreProps,
        pruningMarkerTimeToLive,
        durablePruningMarkerTimeToLive,
        maxDeltaSize,
        restartReplicatorOnFailure,
        preferOldest,
        false
    )
    {
    }

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
        int maxDeltaSize,
        bool restartReplicatorOnFailure,
        bool preferOldest,
        bool verboseDebugLogging)
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
        RestartReplicatorOnFailure = restartReplicatorOnFailure;
        PreferOldest = preferOldest;
        VerboseDebugLogging = verboseDebugLogging;
    }

    /// <summary>
    ///     Determines if a durable store has been configured and is used. If configuration has defined some
    ///     durable keys, this field must be true.
    /// </summary>
    public bool IsDurable => !Equals(DurableStoreProps, Props.Empty);

    /// <summary>
    ///     Replicas are running on members tagged with this role. All members are used if undefined.
    /// </summary>
    public string Role { get; }

    /// <summary>
    ///     How often the Replicator should send out gossip information.
    /// </summary>
    public TimeSpan GossipInterval { get; }

    /// <summary>
    ///     How often the subscribers will be notified of changes, if any.
    /// </summary>
    public TimeSpan NotifySubscribersInterval { get; }

    /// <summary>
    ///     Maximum number of entries to transfer in one gossip message when synchronizing
    ///     the replicas.Next chunk will be transferred in next round of gossip.
    /// </summary>
    public int MaxDeltaElements { get; }

    /// <summary>
    ///     Id of the dispatcher to use for Replicator actors.
    ///     If not specified the default dispatcher is used.
    /// </summary>
    public string Dispatcher { get; }

    /// <summary>
    ///     How often the Replicator checks for pruning of data associated with removed cluster nodes.
    /// </summary>
    public TimeSpan PruningInterval { get; }

    /// <summary>
    ///     How long time it takes (worst case) to spread the data to all other replica nodes.
    ///     This is used when initiating and completing the pruning process of data associated
    ///     with removed cluster nodes. The time measurement is stopped when any replica is
    ///     unreachable, so it should be configured to worst case in a healthy cluster.
    /// </summary>
    public TimeSpan MaxPruningDissemination { get; }

    /// <summary>
    ///     Keys that are durable. Prefix matching is supported by using `*` at the end of a key.
    ///     All entries can be made durable by including "*" in the `Set`.
    /// </summary>
    public IImmutableSet<string> DurableKeys { get; }

    /// <summary>
    ///     How long the tombstones of a removed node are kept on their CRDTs.
    /// </summary>
    public TimeSpan PruningMarkerTimeToLive { get; }

    /// <summary>
    /// </summary>
    public TimeSpan DurablePruningMarkerTimeToLive { get; }

    /// <summary>
    ///     Props for the durable store actor, when taken from actor class type name, it requires
    ///     its constructor to take <see cref="Config" /> as constructor parameter.
    /// </summary>
    public Props DurableStoreProps { get; }

    public int MaxDeltaSize { get; }

    public bool RestartReplicatorOnFailure { get; }

    /// <summary>
    ///     Update and Get operations are sent to oldest nodes first.
    /// </summary>
    public bool PreferOldest { get; }

    /// <summary>
    ///     Whether verbose debug logging is enabled.
    /// </summary>
    public bool VerboseDebugLogging { get; }

    /// <summary>
    ///     Create settings from the default configuration `akka.cluster.distributed-data`.
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
    ///     Create settings from a configuration with the same layout as
    ///     the default configuration `akka.cluster.distributed-data`.
    /// </summary>
    /// <param name="config">TBD</param>
    /// <exception cref="ArgumentNullException">TBD</exception>
    /// <returns>TBD</returns>
    public static ReplicatorSettings Create(Config config)
    {
        if (config.IsNullOrEmpty())
            throw ConfigurationException.NullOrEmptyConfig<ReplicatorSettings>();

        var dispatcher = config.GetString("use-dispatcher");
        if (string.IsNullOrEmpty(dispatcher)) dispatcher = Dispatchers.InternalDispatcherId;

        var durableConfig = config.GetConfig("durable");
        var durableKeys = durableConfig.GetStringList("keys");
        var durableStoreProps = Props.Empty;
        var durableStoreTypeName = durableConfig.GetString("store-actor-class");

        if (durableKeys.Count != 0)
        {
            if (string.IsNullOrEmpty(durableStoreTypeName))
                throw new ArgumentException(
                    "`akka.cluster.distributed-data.durable.store-actor-class` must be set when `akka.cluster.distributed-data.durable.keys` have been configured.");

            var durableStoreType = Type.GetType(durableStoreTypeName);
            if (durableStoreType is null)
                throw new ArgumentException(
                    $"`akka.cluster.distributed-data.durable.store-actor-class` is set to an invalid class {durableStoreTypeName}.");
            durableStoreProps = Props.Create(durableStoreType, durableConfig).WithDispatcher(dispatcher);
        }

        return new ReplicatorSettings(
            config.GetString("role", string.Empty),
            config.GetTimeSpan("gossip-interval", TimeSpan.FromSeconds(2)),
            config.GetTimeSpan("notify-subscribers-interval", TimeSpan.FromMilliseconds(500)),
            config.GetInt("max-delta-elements", 500),
            dispatcher,
            config.GetTimeSpan("pruning-interval", TimeSpan.FromSeconds(120)),
            config.GetTimeSpan("max-pruning-dissemination", TimeSpan.FromSeconds(300)),
            durableKeys.ToImmutableHashSet(),
            durableStoreProps,
            config.GetTimeSpan("pruning-marker-time-to-live", TimeSpan.FromHours(6)),
            durableConfig.GetTimeSpan("pruning-marker-time-to-live", TimeSpan.FromDays(10)),
            config.GetInt("delta-crdt.max-delta-size", 50),
            config.GetBoolean("recreate-on-failure"),
            config.GetBoolean("prefer-oldest"),
            config.GetBoolean("verbose-debug-logging"));
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
        int? maxDeltaSize = null,
        bool? restartReplicatorOnFailure = null,
        bool? preferOldest = null,
        bool? verboseDebugLogging = null)
    {
        return new ReplicatorSettings(
            role ?? Role,
            gossipInterval ?? GossipInterval,
            notifySubscribersInterval ?? NotifySubscribersInterval,
            maxDeltaElements ?? MaxDeltaElements,
            dispatcher ?? Dispatcher,
            pruningInterval ?? PruningInterval,
            maxPruningDissemination ?? MaxPruningDissemination,
            durableKeys ?? DurableKeys,
            durableStoreProps ?? DurableStoreProps,
            pruningMarkerTimeToLive ?? PruningMarkerTimeToLive,
            durablePruningMarkerTimeToLive ?? DurablePruningMarkerTimeToLive,
            maxDeltaSize ?? MaxDeltaSize,
            restartReplicatorOnFailure ?? RestartReplicatorOnFailure,
            preferOldest ?? PreferOldest,
            verboseDebugLogging ?? VerboseDebugLogging);
    }

    public ReplicatorSettings WithRole(string role)
    {
        return Copy(role);
    }

    public ReplicatorSettings WithGossipInterval(TimeSpan gossipInterval)
    {
        return Copy(gossipInterval: gossipInterval);
    }

    public ReplicatorSettings WithNotifySubscribersInterval(TimeSpan notifySubscribersInterval)
    {
        return Copy(notifySubscribersInterval: notifySubscribersInterval);
    }

    public ReplicatorSettings WithMaxDeltaElements(int maxDeltaElements)
    {
        return Copy(maxDeltaElements: maxDeltaElements);
    }

    public ReplicatorSettings WithDispatcher(string dispatcher)
    {
        return Copy(dispatcher: string.IsNullOrEmpty(dispatcher) ? Dispatchers.InternalDispatcherId : dispatcher);
    }

    public ReplicatorSettings WithPruning(TimeSpan pruningInterval, TimeSpan maxPruningDissemination)
    {
        return Copy(pruningInterval: pruningInterval, maxPruningDissemination: maxPruningDissemination);
    }

    public ReplicatorSettings WithDurableKeys(IImmutableSet<string> durableKeys)
    {
        return Copy(durableKeys: durableKeys);
    }

    public ReplicatorSettings WithDurableStoreProps(Props durableStoreProps)
    {
        return Copy(durableStoreProps: durableStoreProps);
    }

    public ReplicatorSettings WithPruningMarkerTimeToLive(TimeSpan pruningMarkerTtl, TimeSpan durablePruningMarkerTtl)
    {
        return Copy(pruningMarkerTimeToLive: pruningMarkerTtl, durablePruningMarkerTimeToLive: durablePruningMarkerTtl);
    }

    public ReplicatorSettings WithMaxDeltaSize(int maxDeltaSize)
    {
        return Copy(maxDeltaSize: maxDeltaSize);
    }

    public ReplicatorSettings WithRestartReplicatorOnFailure(bool restart)
    {
        return Copy(restartReplicatorOnFailure: restart);
    }

    public ReplicatorSettings WithPreferOldest(bool preferOldest)
    {
        return Copy(preferOldest: preferOldest);
    }

    public ReplicatorSettings WithVerboseDebugLogging(bool verboseDebugLogging)
    {
        return Copy(verboseDebugLogging: verboseDebugLogging);
    }
}