// -----------------------------------------------------------------------
//  <copyright file="ClusterShardingSettings.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Coordination;
using Akka.Util;

namespace Akka.Cluster.Sharding;

/// <summary>
///     TBD
/// </summary>
[Serializable]
public class TuningParameters
{
    /// <summary>
    ///     TBD
    /// </summary>
    public readonly int BufferSize;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly TimeSpan CoordinatorFailureBackoff;

    public readonly int CoordinatorStateReadMajorityPlus;

    public readonly int CoordinatorStateWriteMajorityPlus;
    public readonly TimeSpan EntityRecoveryConstantRateStrategyFrequency;
    public readonly int EntityRecoveryConstantRateStrategyNumberOfEntities;

    public readonly string EntityRecoveryStrategy;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly TimeSpan EntityRestartBackoff;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly TimeSpan HandOffTimeout;

    /// <summary>
    ///     The shard deletes persistent events (messages and snapshots) after doing snapshot
    ///     keeping this number of old persistent batches.
    ///     Batch is of size <see cref="SnapshotAfter" />.
    ///     When set to 0 after snapshot is successfully done all messages with equal or lower sequence number will be deleted.
    ///     Default value of 2 leaves last maximum 2*<see cref="SnapshotAfter" /> messages and 3 snapshots (2 old ones + fresh
    ///     snapshot)
    /// </summary>
    public readonly int KeepNrOfBatches;

    public readonly int LeastShardAllocationAbsoluteLimit;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly int LeastShardAllocationMaxSimultaneousRebalance;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly int LeastShardAllocationRebalanceThreshold;

    public readonly double LeastShardAllocationRelativeLimit;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly TimeSpan RebalanceInterval;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly TimeSpan RetryInterval;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly TimeSpan ShardFailureBackoff;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly TimeSpan ShardStartTimeout;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly int SnapshotAfter;

    public readonly TimeSpan UpdatingStateTimeout;

    public readonly TimeSpan WaitingForStateTimeout;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="coordinatorFailureBackoff">TBD</param>
    /// <param name="retryInterval">TBD</param>
    /// <param name="bufferSize">TBD</param>
    /// <param name="handOffTimeout">TBD</param>
    /// <param name="shardStartTimeout">TBD</param>
    /// <param name="shardFailureBackoff">TBD</param>
    /// <param name="entityRestartBackoff">TBD</param>
    /// <param name="rebalanceInterval">TBD</param>
    /// <param name="snapshotAfter">TBD</param>
    /// <param name="keepNrOfBatches">Keep this number of old persistent batches</param>
    /// <param name="leastShardAllocationRebalanceThreshold">TBD</param>
    /// <param name="leastShardAllocationMaxSimultaneousRebalance">TBD</param>
    /// <param name="waitingForStateTimeout">TBD</param>
    /// <param name="updatingStateTimeout">TBD</param>
    /// <param name="entityRecoveryStrategy">TBD</param>
    /// <param name="entityRecoveryConstantRateStrategyFrequency">TBD</param>
    /// <param name="entityRecoveryConstantRateStrategyNumberOfEntities">TBD</param>
    /// <param name="coordinatorStateWriteMajorityPlus">TBD</param>
    /// <param name="coordinatorStateReadMajorityPlus">TBD</param>
    /// <param name="leastShardAllocationAbsoluteLimit">TBD</param>
    /// <param name="leastShardAllocationRelativeLimit">TBD</param>
    /// <exception cref="ArgumentException">
    ///     This exception is thrown when the specified <paramref name="entityRecoveryStrategy" /> is invalid.
    ///     Acceptable values include: all | constant
    /// </exception>
    public TuningParameters(
        TimeSpan coordinatorFailureBackoff,
        TimeSpan retryInterval,
        int bufferSize,
        TimeSpan handOffTimeout,
        TimeSpan shardStartTimeout,
        TimeSpan shardFailureBackoff,
        TimeSpan entityRestartBackoff,
        TimeSpan rebalanceInterval,
        int snapshotAfter,
        int keepNrOfBatches,
        int leastShardAllocationRebalanceThreshold,
        int leastShardAllocationMaxSimultaneousRebalance,
        TimeSpan waitingForStateTimeout,
        TimeSpan updatingStateTimeout,
        string entityRecoveryStrategy,
        TimeSpan entityRecoveryConstantRateStrategyFrequency,
        int entityRecoveryConstantRateStrategyNumberOfEntities,
        int coordinatorStateWriteMajorityPlus,
        int coordinatorStateReadMajorityPlus,
        int leastShardAllocationAbsoluteLimit,
        double leastShardAllocationRelativeLimit
    )
    {
        if (entityRecoveryStrategy != "all" && entityRecoveryStrategy != "constant")
            throw new ArgumentException(
                $"Unknown 'entity-recovery-strategy' [{entityRecoveryStrategy}], valid values are 'all' or 'constant'");

        CoordinatorFailureBackoff = coordinatorFailureBackoff;
        RetryInterval = retryInterval;
        BufferSize = bufferSize;
        HandOffTimeout = handOffTimeout;
        ShardStartTimeout = shardStartTimeout;
        ShardFailureBackoff = shardFailureBackoff;
        EntityRestartBackoff = entityRestartBackoff;
        RebalanceInterval = rebalanceInterval;
        SnapshotAfter = snapshotAfter;
        KeepNrOfBatches = keepNrOfBatches;
        LeastShardAllocationRebalanceThreshold = leastShardAllocationRebalanceThreshold;
        LeastShardAllocationMaxSimultaneousRebalance = leastShardAllocationMaxSimultaneousRebalance;
        WaitingForStateTimeout = waitingForStateTimeout;
        UpdatingStateTimeout = updatingStateTimeout;
        EntityRecoveryStrategy = entityRecoveryStrategy;
        EntityRecoveryConstantRateStrategyFrequency = entityRecoveryConstantRateStrategyFrequency;
        EntityRecoveryConstantRateStrategyNumberOfEntities = entityRecoveryConstantRateStrategyNumberOfEntities;
        CoordinatorStateWriteMajorityPlus = coordinatorStateWriteMajorityPlus;
        CoordinatorStateReadMajorityPlus = coordinatorStateReadMajorityPlus;
        LeastShardAllocationAbsoluteLimit = leastShardAllocationAbsoluteLimit;
        LeastShardAllocationRelativeLimit = leastShardAllocationRelativeLimit;
    }

    public TuningParameters WithCoordinatorFailureBackoff(TimeSpan coordinatorFailureBackoff)
    {
        return Copy(coordinatorFailureBackoff);
    }

    public TuningParameters WithRetryInterval(TimeSpan retryInterval)
    {
        return Copy(retryInterval: retryInterval);
    }

    public TuningParameters WithBufferSize(int bufferSize)
    {
        return Copy(bufferSize: bufferSize);
    }

    public TuningParameters WithHandOffTimeout(TimeSpan handOffTimeout)
    {
        return Copy(handOffTimeout: handOffTimeout);
    }

    public TuningParameters WithShardStartTimeout(TimeSpan shardStartTimeout)
    {
        return Copy(shardStartTimeout: shardStartTimeout);
    }

    public TuningParameters WithShardFailureBackoff(TimeSpan shardFailureBackoff)
    {
        return Copy(shardFailureBackoff: shardFailureBackoff);
    }

    public TuningParameters WithEntityRestartBackoff(TimeSpan entityRestartBackoff)
    {
        return Copy(entityRestartBackoff: entityRestartBackoff);
    }

    public TuningParameters WithRebalanceInterval(TimeSpan rebalanceInterval)
    {
        return Copy(rebalanceInterval: rebalanceInterval);
    }

    public TuningParameters WithSnapshotAfter(int snapshotAfter)
    {
        return Copy(snapshotAfter: snapshotAfter);
    }

    public TuningParameters WithKeepNrOfBatches(int keepNrOfBatches)
    {
        return Copy(keepNrOfBatches: keepNrOfBatches);
    }

    public TuningParameters WithLeastShardAllocationRebalanceThreshold(int leastShardAllocationRebalanceThreshold)
    {
        return Copy(leastShardAllocationRebalanceThreshold: leastShardAllocationRebalanceThreshold);
    }

    public TuningParameters WithLeastShardAllocationMaxSimultaneousRebalance(
        int leastShardAllocationMaxSimultaneousRebalance)
    {
        return Copy(leastShardAllocationMaxSimultaneousRebalance: leastShardAllocationMaxSimultaneousRebalance);
    }

    public TuningParameters WithWaitingForStateTimeout(TimeSpan waitingForStateTimeout)
    {
        return Copy(waitingForStateTimeout: waitingForStateTimeout);
    }

    public TuningParameters WithUpdatingStateTimeout(TimeSpan updatingStateTimeout)
    {
        return Copy(updatingStateTimeout: updatingStateTimeout);
    }

    public TuningParameters WithEntityRecoveryStrategy(string entityRecoveryStrategy)
    {
        return Copy(entityRecoveryStrategy: entityRecoveryStrategy);
    }

    public TuningParameters WithEntityRecoveryConstantRateStrategyFrequency(
        TimeSpan entityRecoveryConstantRateStrategyFrequency)
    {
        return Copy(entityRecoveryConstantRateStrategyFrequency: entityRecoveryConstantRateStrategyFrequency);
    }

    public TuningParameters WithEntityRecoveryConstantRateStrategyNumberOfEntities(
        int entityRecoveryConstantRateStrategyNumberOfEntities)
    {
        return Copy(
            entityRecoveryConstantRateStrategyNumberOfEntities: entityRecoveryConstantRateStrategyNumberOfEntities);
    }

    public TuningParameters WithCoordinatorStateWriteMajorityPlus(int coordinatorStateWriteMajorityPlus)
    {
        return Copy(coordinatorStateWriteMajorityPlus: coordinatorStateWriteMajorityPlus);
    }

    public TuningParameters WithCoordinatorStateReadMajorityPlus(int coordinatorStateReadMajorityPlus)
    {
        return Copy(coordinatorStateReadMajorityPlus: coordinatorStateReadMajorityPlus);
    }

    public TuningParameters WithLeastShardAllocationAbsoluteLimit(int leastShardAllocationAbsoluteLimit)
    {
        return Copy(leastShardAllocationAbsoluteLimit: leastShardAllocationAbsoluteLimit);
    }

    public TuningParameters WithLeastShardAllocationRelativeLimit(double leastShardAllocationRelativeLimit)
    {
        return Copy(leastShardAllocationRelativeLimit: leastShardAllocationRelativeLimit);
    }

    private TuningParameters Copy(
        TimeSpan? coordinatorFailureBackoff = null,
        TimeSpan? retryInterval = null,
        int? bufferSize = null,
        TimeSpan? handOffTimeout = null,
        TimeSpan? shardStartTimeout = null,
        TimeSpan? shardFailureBackoff = null,
        TimeSpan? entityRestartBackoff = null,
        TimeSpan? rebalanceInterval = null,
        int? snapshotAfter = null,
        int? keepNrOfBatches = null,
        int? leastShardAllocationRebalanceThreshold = null,
        int? leastShardAllocationMaxSimultaneousRebalance = null,
        TimeSpan? waitingForStateTimeout = null,
        TimeSpan? updatingStateTimeout = null,
        string entityRecoveryStrategy = null,
        TimeSpan? entityRecoveryConstantRateStrategyFrequency = null,
        int? entityRecoveryConstantRateStrategyNumberOfEntities = null,
        int? coordinatorStateWriteMajorityPlus = null,
        int? coordinatorStateReadMajorityPlus = null,
        int? leastShardAllocationAbsoluteLimit = null,
        double? leastShardAllocationRelativeLimit = null)
    {
        return new TuningParameters(
            coordinatorFailureBackoff ?? CoordinatorFailureBackoff,
            retryInterval ?? RetryInterval,
            bufferSize ?? BufferSize,
            handOffTimeout ?? HandOffTimeout,
            shardStartTimeout ?? ShardStartTimeout,
            shardFailureBackoff ?? ShardFailureBackoff,
            entityRestartBackoff ?? EntityRestartBackoff,
            rebalanceInterval ?? RebalanceInterval,
            snapshotAfter ?? SnapshotAfter,
            keepNrOfBatches ?? KeepNrOfBatches,
            leastShardAllocationRebalanceThreshold ?? LeastShardAllocationRebalanceThreshold,
            leastShardAllocationMaxSimultaneousRebalance ?? LeastShardAllocationMaxSimultaneousRebalance,
            waitingForStateTimeout ?? WaitingForStateTimeout,
            updatingStateTimeout ?? UpdatingStateTimeout,
            entityRecoveryStrategy ?? EntityRecoveryStrategy,
            entityRecoveryConstantRateStrategyFrequency ?? EntityRecoveryConstantRateStrategyFrequency,
            entityRecoveryConstantRateStrategyNumberOfEntities ?? EntityRecoveryConstantRateStrategyNumberOfEntities,
            coordinatorStateWriteMajorityPlus ?? CoordinatorStateWriteMajorityPlus,
            coordinatorStateReadMajorityPlus ?? CoordinatorStateReadMajorityPlus,
            leastShardAllocationAbsoluteLimit ?? LeastShardAllocationAbsoluteLimit,
            leastShardAllocationRelativeLimit ?? LeastShardAllocationRelativeLimit
        );
    }
}

public enum StateStoreMode
{
    Persistence,
    DData,

    /// <summary>
    ///     Only for testing
    /// </summary>
    Custom
}

public enum RememberEntitiesStore
{
    DData,
    Eventsourced,

    /// <summary>
    ///     Only for testing
    /// </summary>
    Custom
}

/// <summary>
///     TBD
/// </summary>
[Serializable]
public sealed class ClusterShardingSettings : INoSerializationVerificationNeeded
{
    /// <summary>
    ///     TBD
    /// </summary>
    public readonly ClusterSingletonManagerSettings CoordinatorSingletonSettings;

    /// <summary>
    ///     Absolute path to the journal plugin configuration entity that is to be used for the internal
    ///     persistence of ClusterSharding.If not defined the default journal plugin is used. Note that
    ///     this is not related to persistence used by the entity actors.
    /// </summary>
    public readonly string JournalPluginId;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly LeaseUsageSettings LeaseSettings;

    /// <summary>
    ///     Passivate entities that have not received any message in this interval.
    ///     Note that only messages sent through sharding are counted, so direct messages
    ///     to the <see cref="IActorRef" /> of the actor or messages that it sends to itself are not counted as activity.
    ///     Use 0 to disable automatic passivation. It is always disabled if `RememberEntities` is enabled.
    /// </summary>
    public readonly TimeSpan PassivateIdleEntityAfter;

    /// <summary>
    ///     True if active entity actors shall be automatically restarted upon <see cref="Shard" /> restart.i.e.
    ///     if the <see cref="Shard" /> is started on a different <see cref="ShardRegion" /> due to rebalance or crash.
    /// </summary>
    public readonly bool RememberEntities;

    public readonly RememberEntitiesStore RememberEntitiesStore;

    /// <summary>
    ///     Specifies that this entity type requires cluster nodes with a specific role.
    ///     If the role is not specified all nodes in the cluster are used.
    /// </summary>
    public readonly string Role;

    public readonly TimeSpan ShardRegionQueryTimeout;

    /// <summary>
    ///     Absolute path to the snapshot plugin configuration entity that is to be used for the internal persistence
    ///     of ClusterSharding. If not defined the default snapshot plugin is used.Note that this is not related
    ///     to persistence used by the entity actors.
    /// </summary>
    public readonly string SnapshotPluginId;

    public readonly StateStoreMode StateStoreMode;

    /// <summary>
    ///     Additional tuning parameters, see descriptions in reference.conf
    /// </summary>
    public readonly TuningParameters TuningParameters;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="role">TBD</param>
    /// <param name="rememberEntities">TBD</param>
    /// <param name="journalPluginId">TBD</param>
    /// <param name="snapshotPluginId">TBD</param>
    /// <param name="passivateIdleEntityAfter">TBD</param>
    /// <param name="stateStoreMode">TBD</param>
    /// <param name="tuningParameters">TBD</param>
    /// <param name="coordinatorSingletonSettings">TBD</param>
    public ClusterShardingSettings(
        string role,
        bool rememberEntities,
        string journalPluginId,
        string snapshotPluginId,
        TimeSpan passivateIdleEntityAfter,
        StateStoreMode stateStoreMode,
        TuningParameters tuningParameters,
        ClusterSingletonManagerSettings coordinatorSingletonSettings)
        : this(role, rememberEntities, journalPluginId, snapshotPluginId, passivateIdleEntityAfter, stateStoreMode,
            RememberEntitiesStore.DData, TimeSpan.FromSeconds(3), tuningParameters, coordinatorSingletonSettings, null)
    {
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="role">TBD</param>
    /// <param name="rememberEntities">TBD</param>
    /// <param name="journalPluginId">TBD</param>
    /// <param name="snapshotPluginId">TBD</param>
    /// <param name="passivateIdleEntityAfter">TBD</param>
    /// <param name="stateStoreMode">TBD</param>
    /// <param name="tuningParameters">TBD</param>
    /// <param name="coordinatorSingletonSettings">TBD</param>
    /// <param name="leaseSettings">TBD</param>
    public ClusterShardingSettings(
        string role,
        bool rememberEntities,
        string journalPluginId,
        string snapshotPluginId,
        TimeSpan passivateIdleEntityAfter,
        StateStoreMode stateStoreMode,
        TuningParameters tuningParameters,
        ClusterSingletonManagerSettings coordinatorSingletonSettings,
        LeaseUsageSettings leaseSettings)
        : this(role, rememberEntities, journalPluginId, snapshotPluginId, passivateIdleEntityAfter, stateStoreMode,
            RememberEntitiesStore.DData, TimeSpan.FromSeconds(3), tuningParameters, coordinatorSingletonSettings,
            leaseSettings)
    {
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="role">TBD</param>
    /// <param name="rememberEntities">TBD</param>
    /// <param name="journalPluginId">TBD</param>
    /// <param name="snapshotPluginId">TBD</param>
    /// <param name="passivateIdleEntityAfter">TBD</param>
    /// <param name="stateStoreMode">TBD</param>
    /// <param name="rememberEntitiesStore">TBD</param>
    /// <param name="shardRegionQueryTimeout">TBD</param>
    /// <param name="tuningParameters">TBD</param>
    /// <param name="coordinatorSingletonSettings">TBD</param>
    /// <param name="leaseSettings">TBD</param>
    public ClusterShardingSettings(
        string role,
        bool rememberEntities,
        string journalPluginId,
        string snapshotPluginId,
        TimeSpan passivateIdleEntityAfter,
        StateStoreMode stateStoreMode,
        RememberEntitiesStore rememberEntitiesStore,
        TimeSpan shardRegionQueryTimeout,
        TuningParameters tuningParameters,
        ClusterSingletonManagerSettings coordinatorSingletonSettings,
        LeaseUsageSettings leaseSettings)
    {
        Role = role;
        RememberEntities = rememberEntities;
        JournalPluginId = journalPluginId;
        SnapshotPluginId = snapshotPluginId;
        PassivateIdleEntityAfter = passivateIdleEntityAfter;
        StateStoreMode = stateStoreMode;
        RememberEntitiesStore = rememberEntitiesStore;
        ShardRegionQueryTimeout = shardRegionQueryTimeout;
        TuningParameters = tuningParameters;
        CoordinatorSingletonSettings = coordinatorSingletonSettings;
        LeaseSettings = leaseSettings;
    }

    /// <summary>
    ///     If true, idle entities should be passivated if they have not received any message by this interval, otherwise it is
    ///     not enabled.
    /// </summary>
    internal bool ShouldPassivateIdleEntities => PassivateIdleEntityAfter > TimeSpan.Zero && !RememberEntities;

    /// <summary>
    ///     Create settings from the default configuration `akka.cluster.sharding`.
    /// </summary>
    /// <param name="system">TBD</param>
    /// <returns>TBD</returns>
    public static ClusterShardingSettings Create(ActorSystem system)
    {
        var config = system.Settings.Config.GetConfig("akka.cluster.sharding");
        if (config.IsNullOrEmpty())
            throw ConfigurationException.NullOrEmptyConfig<ClusterShardingSettings>("akka.cluster.sharding");

        var coordinatorSingletonPath = config.GetString("coordinator-singleton");

        return Create(config, system.Settings.Config.GetConfig(coordinatorSingletonPath));
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="config">TBD</param>
    /// <param name="singletonConfig">TBD</param>
    /// <returns>TBD</returns>
    public static ClusterShardingSettings Create(Config config, Config singletonConfig)
    {
        if (config.IsNullOrEmpty())
            throw ConfigurationException.NullOrEmptyConfig<ClusterShardingSettings>();


        int ConfigMajorityPlus(string p)
        {
            if (config.GetString(p)?.ToLowerInvariant() == "all")
                return int.MaxValue;
            return config.GetInt(p);
        }

        var tuningParameters = new TuningParameters(
            config.GetTimeSpan("coordinator-failure-backoff"),
            config.GetTimeSpan("retry-interval"),
            config.GetInt("buffer-size"),
            config.GetTimeSpan("handoff-timeout"),
            config.GetTimeSpan("shard-start-timeout"),
            config.GetTimeSpan("shard-failure-backoff"),
            config.GetTimeSpan("entity-restart-backoff"),
            config.GetTimeSpan("rebalance-interval"),
            config.GetInt("snapshot-after"),
            config.GetInt("keep-nr-of-batches"),
            config.GetInt("least-shard-allocation-strategy.rebalance-threshold"),
            config.GetInt("least-shard-allocation-strategy.max-simultaneous-rebalance"),
            config.GetTimeSpan("waiting-for-state-timeout"),
            config.GetTimeSpan("updating-state-timeout"),
            config.GetString("entity-recovery-strategy"),
            config.GetTimeSpan("entity-recovery-constant-rate-strategy.frequency"),
            config.GetInt("entity-recovery-constant-rate-strategy.number-of-entities"),
            ConfigMajorityPlus("coordinator-state.write-majority-plus"),
            ConfigMajorityPlus("coordinator-state.read-majority-plus"),
            config.GetInt("least-shard-allocation-strategy.rebalance-absolute-limit"),
            config.GetDouble("least-shard-allocation-strategy.rebalance-relative-limit"));

        var coordinatorSingletonSettings = ClusterSingletonManagerSettings.Create(singletonConfig);
        var role = config.GetString("role");
        if (role == string.Empty) role = null;

        var usePassivateIdle = config.GetString("passivate-idle-entity-after").ToLowerInvariant();
        var passivateIdleAfter =
            usePassivateIdle.Equals("off") ||
            usePassivateIdle.Equals("false") ||
            usePassivateIdle.Equals("no")
                ? TimeSpan.Zero
                : config.GetTimeSpan("passivate-idle-entity-after");

        LeaseUsageSettings lease = null;
        var leaseConfigPath = config.GetString("use-lease");
        if (!string.IsNullOrEmpty(leaseConfigPath))
            lease = new LeaseUsageSettings(leaseConfigPath, config.GetTimeSpan("lease-retry-interval"));

        return new ClusterShardingSettings(
            role,
            config.GetBoolean("remember-entities"),
            config.GetString("journal-plugin-id"),
            config.GetString("snapshot-plugin-id"),
            passivateIdleAfter,
            (StateStoreMode)Enum.Parse(typeof(StateStoreMode), config.GetString("state-store-mode"), true),
            (RememberEntitiesStore)Enum.Parse(typeof(RememberEntitiesStore),
                config.GetString("remember-entities-store"), true),
            config.GetTimeSpan("shard-region-query-timeout"),
            tuningParameters,
            coordinatorSingletonSettings,
            lease);
    }

    /// <summary>
    ///     If true, this node should run the shard region, otherwise just a shard proxy should started on this node.
    /// </summary>
    /// <param name="cluster"></param>
    /// <returns></returns>
    internal bool ShouldHostShard(Cluster cluster)
    {
        return string.IsNullOrEmpty(Role) || cluster.SelfRoles.Contains(Role);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="role">TBD</param>
    /// <returns>TBD</returns>
    public ClusterShardingSettings WithRole(string role)
    {
        return Copy(role);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="rememberEntities">TBD</param>
    /// <returns>TBD</returns>
    public ClusterShardingSettings WithRememberEntities(bool rememberEntities)
    {
        return Copy(rememberEntities: rememberEntities);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="journalPluginId">TBD</param>
    /// <returns>TBD</returns>
    public ClusterShardingSettings WithJournalPluginId(string journalPluginId)
    {
        return Copy(journalPluginId: journalPluginId ?? string.Empty);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="snapshotPluginId">TBD</param>
    /// <returns>TBD</returns>
    public ClusterShardingSettings WithSnapshotPluginId(string snapshotPluginId)
    {
        return Copy(snapshotPluginId: snapshotPluginId ?? string.Empty);
    }

    public ClusterShardingSettings WithStateStoreMode(StateStoreMode mode)
    {
        return Copy(stateStoreMode: mode);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="tuningParameters">TBD</param>
    /// <exception cref="ArgumentNullException">
    ///     This exception is thrown when the specified <paramref name="tuningParameters" /> is undefined.
    /// </exception>
    /// <returns>TBD</returns>
    public ClusterShardingSettings WithTuningParameters(TuningParameters tuningParameters)
    {
        if (tuningParameters == null)
            throw new ArgumentNullException(nameof(tuningParameters),
                $"ClusterShardingSettings requires {nameof(tuningParameters)} to be provided");

        return Copy(tuningParameters: tuningParameters);
    }

    public ClusterShardingSettings WithPassivateIdleAfter(TimeSpan duration)
    {
        return Copy(passivateIdleAfter: duration);
    }

    public ClusterShardingSettings WithLeaseSettings(LeaseUsageSettings leaseSettings)
    {
        return Copy(leaseSettings: leaseSettings);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="coordinatorSingletonSettings">TBD</param>
    /// <exception cref="ArgumentNullException">
    ///     This exception is thrown when the specified <paramref name="coordinatorSingletonSettings" /> is undefined.
    /// </exception>
    /// <returns>TBD</returns>
    public ClusterShardingSettings WithCoordinatorSingletonSettings(
        ClusterSingletonManagerSettings coordinatorSingletonSettings)
    {
        if (coordinatorSingletonSettings == null)
            throw new ArgumentNullException(nameof(coordinatorSingletonSettings),
                $"ClusterShardingSettings requires {nameof(coordinatorSingletonSettings)} to be provided");

        return Copy(coordinatorSingletonSettings: coordinatorSingletonSettings);
    }

    private ClusterShardingSettings Copy(
        Option<string> role = default,
        bool? rememberEntities = null,
        string journalPluginId = null,
        string snapshotPluginId = null,
        TimeSpan? passivateIdleAfter = null,
        StateStoreMode? stateStoreMode = null,
        RememberEntitiesStore? rememberEntitiesStore = null,
        TimeSpan? shardRegionQueryTimeout = null,
        TuningParameters tuningParameters = null,
        ClusterSingletonManagerSettings coordinatorSingletonSettings = null,
        Option<LeaseUsageSettings> leaseSettings = default)
    {
        return new ClusterShardingSettings(
            role.HasValue ? role.Value : Role,
            rememberEntities ?? RememberEntities,
            journalPluginId ?? JournalPluginId,
            snapshotPluginId ?? SnapshotPluginId,
            passivateIdleAfter ?? PassivateIdleEntityAfter,
            stateStoreMode ?? StateStoreMode,
            rememberEntitiesStore ?? RememberEntitiesStore,
            shardRegionQueryTimeout ?? ShardRegionQueryTimeout,
            tuningParameters ?? TuningParameters,
            coordinatorSingletonSettings ?? CoordinatorSingletonSettings,
            leaseSettings.HasValue ? leaseSettings.Value : LeaseSettings);
    }
}