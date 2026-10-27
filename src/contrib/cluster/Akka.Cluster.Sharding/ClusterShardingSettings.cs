//-----------------------------------------------------------------------
// <copyright file="ClusterShardingSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Coordination;
using Akka.Util;

namespace Akka.Cluster.Sharding
{
    /// <summary>
    /// Timing, buffering, persistence, recovery, and shard-rebalance values used by cluster sharding.
    /// </summary>
    [Serializable]
    public class TuningParameters
    {
        /// <summary>
        /// The backoff duration used when the coordinator is restarted after failure.
        /// </summary>
        public readonly TimeSpan CoordinatorFailureBackoff;
        /// <summary>
        /// The interval at which a shard region retries unanswered coordinator requests.
        /// </summary>
        public readonly TimeSpan RetryInterval;
        /// <summary>
        /// The maximum number of messages buffered by a shard region while waiting for shard location or startup.
        /// </summary>
        public readonly int BufferSize;
        /// <summary>
        /// The timeout for a shard handoff to complete.
        /// </summary>
        public readonly TimeSpan HandOffTimeout;
        /// <summary>
        /// The time a coordinator waits for a region to acknowledge that it is hosting a shard.
        /// </summary>
        public readonly TimeSpan ShardStartTimeout;
        /// <summary>
        /// The backoff duration before restarting a shard after a failure while remembering entity state.
        /// </summary>
        public readonly TimeSpan ShardFailureBackoff;
        /// <summary>
        /// The delay before a remembered entity that stopped without passivating is restarted, unless a message for it arrives first.
        /// </summary>
        public readonly TimeSpan EntityRestartBackoff;
        /// <summary>
        /// The interval between coordinator checks for shards that may need rebalancing.
        /// </summary>
        public readonly TimeSpan RebalanceInterval;
        /// <summary>
        /// The number of persisted events after which a snapshot is attempted for persistence-backed sharding state.
        /// </summary>
        public readonly int SnapshotAfter;
        /// <summary>
        /// The shard deletes persistent events (messages and snapshots) after doing snapshot
        /// keeping this number of old persistent batches.
        /// Batch is of size <see cref="SnapshotAfter"/>.
        /// When set to 0 after snapshot is successfully done all messages with equal or lower sequence number will be deleted.
        /// Default value of 2 leaves last maximum 2*<see cref="SnapshotAfter"/> messages and 3 snapshots (2 old ones + fresh snapshot)
        /// </summary>
        public readonly int KeepNrOfBatches;
        /// <summary>
        /// The shard-count difference that the legacy least-shard strategy must exceed before it selects shards for rebalance.
        /// </summary>
        public readonly int LeastShardAllocationRebalanceThreshold;
        /// <summary>
        /// The maximum number of shards that the legacy least-shard strategy permits to rebalance concurrently.
        /// </summary>
        public readonly int LeastShardAllocationMaxSimultaneousRebalance;

        public readonly TimeSpan WaitingForStateTimeout;

        public readonly TimeSpan UpdatingStateTimeout;

        public readonly string EntityRecoveryStrategy;
        public readonly TimeSpan EntityRecoveryConstantRateStrategyFrequency;
        public readonly int EntityRecoveryConstantRateStrategyNumberOfEntities;

        public readonly int CoordinatorStateWriteMajorityPlus;
        public readonly int CoordinatorStateReadMajorityPlus;

        public readonly int LeastShardAllocationAbsoluteLimit;
        public readonly double LeastShardAllocationRelativeLimit;

        /// <summary>
        /// Creates tuning parameters for shard-region buffering, coordinator and shard timing, entity recovery, snapshots, and rebalance behavior.
        /// </summary>
        /// <param name="coordinatorFailureBackoff">The backoff duration used when restarting a failed coordinator.</param>
        /// <param name="retryInterval">The interval between retries of unanswered coordinator requests.</param>
        /// <param name="bufferSize">The maximum number of messages a shard region buffers while resolving shard locations.</param>
        /// <param name="handOffTimeout">The timeout for shard handoff.</param>
        /// <param name="shardStartTimeout">The time allowed for a region to acknowledge hosting a shard.</param>
        /// <param name="shardFailureBackoff">The delay before a shard is restarted after it terminates outside handoff when remembered entities are enabled.</param>
        /// <param name="entityRestartBackoff">The delay before restarting a remembered entity that stopped without passivating, if no message for it arrives first.</param>
        /// <param name="rebalanceInterval">The interval between checks for shards to rebalance.</param>
        /// <param name="snapshotAfter">The number of persisted events after which sharding state snapshotting is attempted.</param>
        /// <param name="keepNrOfBatches">Keep this number of old persistent batches</param>
        /// <param name="leastShardAllocationRebalanceThreshold">The eligible shard-count difference that the legacy strategy must exceed before selecting shards.</param>
        /// <param name="leastShardAllocationMaxSimultaneousRebalance">The maximum number of concurrent shard rebalances allowed by the legacy strategy.</param>
        /// <param name="waitingForStateTimeout">The timeout for reading initial distributed sharding state and shard state.</param>
        /// <param name="updatingStateTimeout">The timeout for updating distributed sharding state and writing remembered-entity state.</param>
        /// <param name="entityRecoveryStrategy">The recovery strategy for remembered entities: <c>all</c> starts them together, while <c>constant</c> starts them in batches.</param>
        /// <param name="entityRecoveryConstantRateStrategyFrequency">The interval between batches when <paramref name="entityRecoveryStrategy"/> is <c>constant</c>.</param>
        /// <param name="entityRecoveryConstantRateStrategyNumberOfEntities">The number of entities started in each batch when <paramref name="entityRecoveryStrategy"/> is <c>constant</c>.</param>
        /// <param name="coordinatorStateWriteMajorityPlus">The number of replicas beyond a majority required for a coordinator-state write; <see cref="int.MaxValue"/> selects all replicas.</param>
        /// <param name="coordinatorStateReadMajorityPlus">The number of replicas beyond a majority required for a coordinator-state read; <see cref="int.MaxValue"/> selects all replicas.</param>
        /// <param name="leastShardAllocationAbsoluteLimit">The absolute per-round shard-move limit used by the bounded least-shard strategy.</param>
        /// <param name="leastShardAllocationRelativeLimit">The fraction of known shards used to calculate the per-round limit for the bounded least-shard strategy.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="entityRecoveryStrategy"/> is invalid.
        /// Acceptable values include: all | constant
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
                throw new ArgumentException($"Unknown 'entity-recovery-strategy' [{entityRecoveryStrategy}], valid values are 'all' or 'constant'");

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
            => Copy(coordinatorFailureBackoff: coordinatorFailureBackoff); 
        public TuningParameters WithRetryInterval(TimeSpan retryInterval) 
            => Copy(retryInterval: retryInterval); 
        public TuningParameters WithBufferSize(int bufferSize) 
            => Copy(bufferSize: bufferSize); 
        public TuningParameters WithHandOffTimeout(TimeSpan handOffTimeout) 
            => Copy(handOffTimeout: handOffTimeout); 
        public TuningParameters WithShardStartTimeout(TimeSpan shardStartTimeout)
            => Copy(shardStartTimeout: shardStartTimeout); 
        public TuningParameters WithShardFailureBackoff(TimeSpan shardFailureBackoff) 
            => Copy(shardFailureBackoff: shardFailureBackoff); 
        public TuningParameters WithEntityRestartBackoff(TimeSpan entityRestartBackoff) 
            => Copy(entityRestartBackoff: entityRestartBackoff); 
        public TuningParameters WithRebalanceInterval(TimeSpan rebalanceInterval) 
            => Copy(rebalanceInterval: rebalanceInterval); 
        public TuningParameters WithSnapshotAfter(int snapshotAfter) 
            => Copy(snapshotAfter: snapshotAfter); 
        public TuningParameters WithKeepNrOfBatches(int keepNrOfBatches) 
            => Copy(keepNrOfBatches: keepNrOfBatches); 
        public TuningParameters WithLeastShardAllocationRebalanceThreshold(int leastShardAllocationRebalanceThreshold)
            => Copy(leastShardAllocationRebalanceThreshold: leastShardAllocationRebalanceThreshold); 
        public TuningParameters WithLeastShardAllocationMaxSimultaneousRebalance(int leastShardAllocationMaxSimultaneousRebalance) 
            => Copy(leastShardAllocationMaxSimultaneousRebalance: leastShardAllocationMaxSimultaneousRebalance); 
        public TuningParameters WithWaitingForStateTimeout(TimeSpan waitingForStateTimeout) 
            => Copy(waitingForStateTimeout: waitingForStateTimeout); 
        public TuningParameters WithUpdatingStateTimeout(TimeSpan updatingStateTimeout) 
            => Copy(updatingStateTimeout: updatingStateTimeout); 
        public TuningParameters WithEntityRecoveryStrategy(string entityRecoveryStrategy)
            => Copy(entityRecoveryStrategy: entityRecoveryStrategy); 
        public TuningParameters WithEntityRecoveryConstantRateStrategyFrequency(TimeSpan entityRecoveryConstantRateStrategyFrequency) 
            => Copy(entityRecoveryConstantRateStrategyFrequency: entityRecoveryConstantRateStrategyFrequency); 
        public TuningParameters WithEntityRecoveryConstantRateStrategyNumberOfEntities(int entityRecoveryConstantRateStrategyNumberOfEntities) 
            => Copy(entityRecoveryConstantRateStrategyNumberOfEntities: entityRecoveryConstantRateStrategyNumberOfEntities); 
        public TuningParameters WithCoordinatorStateWriteMajorityPlus(int coordinatorStateWriteMajorityPlus) 
            => Copy(coordinatorStateWriteMajorityPlus: coordinatorStateWriteMajorityPlus); 
        public TuningParameters WithCoordinatorStateReadMajorityPlus(int coordinatorStateReadMajorityPlus) 
            => Copy(coordinatorStateReadMajorityPlus: coordinatorStateReadMajorityPlus); 
        public TuningParameters WithLeastShardAllocationAbsoluteLimit(int leastShardAllocationAbsoluteLimit) 
            => Copy(leastShardAllocationAbsoluteLimit: leastShardAllocationAbsoluteLimit); 
        public TuningParameters WithLeastShardAllocationRelativeLimit(double leastShardAllocationRelativeLimit) 
            => Copy(leastShardAllocationRelativeLimit: leastShardAllocationRelativeLimit); 
        
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
            => new(
                coordinatorFailureBackoff: coordinatorFailureBackoff ?? CoordinatorFailureBackoff,
                retryInterval: retryInterval ?? RetryInterval,
                bufferSize: bufferSize ?? BufferSize,
                handOffTimeout: handOffTimeout ?? HandOffTimeout,
                shardStartTimeout: shardStartTimeout ?? ShardStartTimeout,
                shardFailureBackoff: shardFailureBackoff ?? ShardFailureBackoff,
                entityRestartBackoff: entityRestartBackoff ?? EntityRestartBackoff,
                rebalanceInterval: rebalanceInterval ?? RebalanceInterval,
                snapshotAfter: snapshotAfter ?? SnapshotAfter,
                keepNrOfBatches: keepNrOfBatches ?? KeepNrOfBatches,
                leastShardAllocationRebalanceThreshold: leastShardAllocationRebalanceThreshold ?? LeastShardAllocationRebalanceThreshold,
                leastShardAllocationMaxSimultaneousRebalance: leastShardAllocationMaxSimultaneousRebalance ?? LeastShardAllocationMaxSimultaneousRebalance,
                waitingForStateTimeout: waitingForStateTimeout ?? WaitingForStateTimeout,
                updatingStateTimeout: updatingStateTimeout ?? UpdatingStateTimeout,
                entityRecoveryStrategy: entityRecoveryStrategy ?? EntityRecoveryStrategy,
                entityRecoveryConstantRateStrategyFrequency: entityRecoveryConstantRateStrategyFrequency ?? EntityRecoveryConstantRateStrategyFrequency,
                entityRecoveryConstantRateStrategyNumberOfEntities:
                entityRecoveryConstantRateStrategyNumberOfEntities ?? EntityRecoveryConstantRateStrategyNumberOfEntities,
                coordinatorStateWriteMajorityPlus: coordinatorStateWriteMajorityPlus ?? CoordinatorStateWriteMajorityPlus,
                coordinatorStateReadMajorityPlus: coordinatorStateReadMajorityPlus ?? CoordinatorStateReadMajorityPlus,
                leastShardAllocationAbsoluteLimit: leastShardAllocationAbsoluteLimit ?? LeastShardAllocationAbsoluteLimit,
                leastShardAllocationRelativeLimit: leastShardAllocationRelativeLimit ?? LeastShardAllocationRelativeLimit
            );
    }

    public enum StateStoreMode
    {
        Persistence,
        DData,
        /// <summary>
        /// Only for testing
        /// </summary>
        Custom
    }

    public enum RememberEntitiesStore
    {
        DData,
        Eventsourced,

        /// <summary>
        /// Only for testing
        /// </summary>
        Custom
    }

    /// <summary>
    /// Settings that control shard placement, coordinator state storage, entity passivation, and shard-region behavior.
    /// </summary>
    [Serializable]
    public sealed class ClusterShardingSettings : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Specifies that this entity type requires cluster nodes with a specific role.
        /// If the role is not specified all nodes in the cluster are used.
        /// </summary>
        public readonly string Role;

        /// <summary>
        /// True if active entity actors shall be automatically restarted upon <see cref="Shard"/> restart.i.e.
        /// if the <see cref="Shard"/> is started on a different <see cref="ShardRegion"/> due to rebalance or crash.
        /// </summary>
        public readonly bool RememberEntities;

        /// <summary>
        /// Absolute path to the journal plugin configuration entity that is to be used for the internal
        /// persistence of ClusterSharding.If not defined the default journal plugin is used. Note that
        /// this is not related to persistence used by the entity actors.
        /// </summary>
        public readonly string JournalPluginId;

        /// <summary>
        /// Absolute path to the snapshot plugin configuration entity that is to be used for the internal persistence
        /// of ClusterSharding. If not defined the default snapshot plugin is used.Note that this is not related
        /// to persistence used by the entity actors.
        /// </summary>
        public readonly string SnapshotPluginId;

        public readonly StateStoreMode StateStoreMode;

        public readonly RememberEntitiesStore RememberEntitiesStore;

        public readonly TimeSpan ShardRegionQueryTimeout;

        /// <summary>
        /// Passivate entities that have not received any message in this interval.
        /// Note that only messages sent through sharding are counted, so direct messages
        /// to the <see cref="IActorRef"/> of the actor or messages that it sends to itself are not counted as activity.
        /// Use 0 to disable automatic passivation. It is always disabled if `RememberEntities` is enabled.
        /// </summary>
        public readonly TimeSpan PassivateIdleEntityAfter;

        /// <summary>
        /// Additional tuning parameters, see descriptions in reference.conf
        /// </summary>
        public readonly TuningParameters TuningParameters;

        /// <summary>
        /// The settings used to run the sharding coordinator as a cluster singleton.
        /// </summary>
        public readonly ClusterSingletonManagerSettings CoordinatorSingletonSettings;

        /// <summary>
        /// The optional lease configuration used by the coordinator singleton.
        /// </summary>
        public readonly LeaseUsageSettings LeaseSettings;

        public SupervisorStrategy? SupervisorStrategy { get; }
        
        /// <summary>
        /// Create settings from the default configuration `akka.cluster.sharding`.
        /// </summary>
        /// <param name="system">The actor system whose <c>akka.cluster.sharding</c> and singleton configuration are read.</param>
        /// <returns>Settings populated from the actor system configuration.</returns>
        public static ClusterShardingSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.sharding");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterShardingSettings>("akka.cluster.sharding");

            var coordinatorSingletonPath = config.GetString("coordinator-singleton");

            return Create(config, system.Settings.Config.GetConfig(coordinatorSingletonPath));
        }

        /// <summary>
        /// Creates settings from the supplied sharding and coordinator-singleton configuration objects.
        /// </summary>
        /// <param name="config">The <c>akka.cluster.sharding</c> configuration.</param>
        /// <param name="singletonConfig">The configuration for the coordinator singleton.</param>
        /// <returns>Settings populated from the supplied configurations.</returns>
        public static ClusterShardingSettings Create(Config config, Config singletonConfig)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterShardingSettings>();


            var tuningParameters = new TuningParameters(
                coordinatorFailureBackoff: config.GetTimeSpan("coordinator-failure-backoff"),
                retryInterval: config.GetTimeSpan("retry-interval"),
                bufferSize: config.GetInt("buffer-size"),
                handOffTimeout: config.GetTimeSpan("handoff-timeout"),
                shardStartTimeout: config.GetTimeSpan("shard-start-timeout"),
                shardFailureBackoff: config.GetTimeSpan("shard-failure-backoff"),
                entityRestartBackoff: config.GetTimeSpan("entity-restart-backoff"),
                rebalanceInterval: config.GetTimeSpan("rebalance-interval"),
                snapshotAfter: config.GetInt("snapshot-after"),
                keepNrOfBatches: config.GetInt("keep-nr-of-batches"),
                leastShardAllocationRebalanceThreshold: config.GetInt("least-shard-allocation-strategy.rebalance-threshold"),
                leastShardAllocationMaxSimultaneousRebalance: config.GetInt("least-shard-allocation-strategy.max-simultaneous-rebalance"),
                waitingForStateTimeout: config.GetTimeSpan("waiting-for-state-timeout"),
                updatingStateTimeout: config.GetTimeSpan("updating-state-timeout"),
                entityRecoveryStrategy: config.GetString("entity-recovery-strategy"),
                entityRecoveryConstantRateStrategyFrequency: config.GetTimeSpan("entity-recovery-constant-rate-strategy.frequency"),
                entityRecoveryConstantRateStrategyNumberOfEntities: config.GetInt("entity-recovery-constant-rate-strategy.number-of-entities"),
                coordinatorStateWriteMajorityPlus: ConfigMajorityPlus("coordinator-state.write-majority-plus"),
                coordinatorStateReadMajorityPlus: ConfigMajorityPlus("coordinator-state.read-majority-plus"),
                leastShardAllocationAbsoluteLimit: config.GetInt("least-shard-allocation-strategy.rebalance-absolute-limit"),
                leastShardAllocationRelativeLimit: config.GetDouble("least-shard-allocation-strategy.rebalance-relative-limit"));

            var coordinatorSingletonSettings = ClusterSingletonManagerSettings.Create(singletonConfig);
            var role = config.GetString("role", null);
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
                role: role,
                rememberEntities: config.GetBoolean("remember-entities"),
                journalPluginId: config.GetString("journal-plugin-id"),
                snapshotPluginId: config.GetString("snapshot-plugin-id"),
                passivateIdleEntityAfter: passivateIdleAfter,
                stateStoreMode: (StateStoreMode)Enum.Parse(typeof(StateStoreMode), config.GetString("state-store-mode"), ignoreCase: true),
                rememberEntitiesStore: (RememberEntitiesStore)Enum.Parse(typeof(RememberEntitiesStore), config.GetString("remember-entities-store"), ignoreCase: true),
                shardRegionQueryTimeout: config.GetTimeSpan("shard-region-query-timeout"),
                tuningParameters: tuningParameters,
                coordinatorSingletonSettings: coordinatorSingletonSettings,
                leaseSettings: lease);

            int ConfigMajorityPlus(string p)
            {
                if (config.GetString(p)?.ToLowerInvariant() == "all")
                    return int.MaxValue;
                return config.GetInt(p);
            }
        }

        /// <summary>
        /// Creates settings with the selected role, entity memory behavior, persistence plugins, state-store mode, tuning values, and singleton configuration.
        /// </summary>
        /// <param name="role">The cluster role required for nodes that host shard regions, or <see langword="null"/> to allow any role.</param>
        /// <param name="rememberEntities">Whether shards restore their previously active entities after restart or rebalance.</param>
        /// <param name="journalPluginId">The journal plugin id used for persistence-backed sharding state.</param>
        /// <param name="snapshotPluginId">The snapshot plugin id used for persistence-backed sharding state.</param>
        /// <param name="passivateIdleEntityAfter">The idle period before automatic entity passivation; a non-positive value disables it, and remembered entities are not passivated this way.</param>
        /// <param name="stateStoreMode">The storage mode used for coordinator and shard state.</param>
        /// <param name="tuningParameters">Timeouts, buffer limits, recovery options, snapshot settings, and rebalance limits.</param>
        /// <param name="coordinatorSingletonSettings">The settings for the coordinator singleton.</param>
        public ClusterShardingSettings(
            string role,
            bool rememberEntities,
            string journalPluginId,
            string snapshotPluginId,
            TimeSpan passivateIdleEntityAfter,
            StateStoreMode stateStoreMode,
            TuningParameters tuningParameters,
            ClusterSingletonManagerSettings coordinatorSingletonSettings)
            : this(role, rememberEntities, journalPluginId, snapshotPluginId, passivateIdleEntityAfter, stateStoreMode, RememberEntitiesStore.DData, TimeSpan.FromSeconds(3), tuningParameters, coordinatorSingletonSettings, null)
        {
        }

        /// <summary>
        /// Creates settings with an optional lease for the coordinator singleton.
        /// </summary>
        /// <param name="role">The cluster role required for nodes that host shard regions, or <see langword="null"/> to allow any role.</param>
        /// <param name="rememberEntities">Whether shards restore their previously active entities after restart or rebalance.</param>
        /// <param name="journalPluginId">The journal plugin id used for persistence-backed sharding state.</param>
        /// <param name="snapshotPluginId">The snapshot plugin id used for persistence-backed sharding state.</param>
        /// <param name="passivateIdleEntityAfter">The idle period before automatic entity passivation; a non-positive value disables it, and remembered entities are not passivated this way.</param>
        /// <param name="stateStoreMode">The storage mode used for coordinator and shard state.</param>
        /// <param name="tuningParameters">Timeouts, buffer limits, recovery options, snapshot settings, and rebalance limits.</param>
        /// <param name="coordinatorSingletonSettings">The settings for the coordinator singleton.</param>
        /// <param name="leaseSettings">Optional lease settings for the coordinator singleton.</param>
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
            : this(role, rememberEntities, journalPluginId, snapshotPluginId, passivateIdleEntityAfter, stateStoreMode, RememberEntitiesStore.DData, TimeSpan.FromSeconds(3), tuningParameters, coordinatorSingletonSettings, leaseSettings)
        {
        }

        /// <summary>
        /// Creates settings with explicit remembered-entity storage and shard-region query timeout values.
        /// </summary>
        /// <param name="role">The cluster role required for nodes that host shard regions, or <see langword="null"/> to allow any role.</param>
        /// <param name="rememberEntities">Whether shards restore their previously active entities after restart or rebalance.</param>
        /// <param name="journalPluginId">The journal plugin id used for persistence-backed sharding state.</param>
        /// <param name="snapshotPluginId">The snapshot plugin id used for persistence-backed sharding state.</param>
        /// <param name="passivateIdleEntityAfter">The idle period before automatic entity passivation; a non-positive value disables it, and remembered entities are not passivated this way.</param>
        /// <param name="stateStoreMode">The storage mode used for coordinator and shard state.</param>
        /// <param name="rememberEntitiesStore">The store used to remember entity ids when <paramref name="rememberEntities"/> is enabled.</param>
        /// <param name="shardRegionQueryTimeout">The timeout for queries that collect information from shard regions.</param>
        /// <param name="tuningParameters">Timeouts, buffer limits, recovery options, snapshot settings, and rebalance limits.</param>
        /// <param name="coordinatorSingletonSettings">The settings for the coordinator singleton.</param>
        /// <param name="leaseSettings">Optional lease settings for the coordinator singleton.</param>
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

        private ClusterShardingSettings(
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
            LeaseUsageSettings leaseSettings,
            SupervisorStrategy supervisorStrategy)
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
            SupervisorStrategy = supervisorStrategy;
        }

        /// <summary>
        /// If true, this node should run the shard region, otherwise just a shard proxy should started on this node.
        /// </summary>
        /// <param name="cluster"></param>
        /// <returns></returns>
        internal bool ShouldHostShard(Cluster cluster)
        {
            return string.IsNullOrEmpty(Role) || cluster.SelfRoles.Contains(Role);
        }

        /// <summary>
        /// If true, idle entities should be passivated if they have not received any message by this interval, otherwise it is not enabled.
        /// </summary>
        internal bool ShouldPassivateIdleEntities => PassivateIdleEntityAfter > TimeSpan.Zero && !RememberEntities;

        /// <summary>
        /// Returns a copy configured to host shard regions only on nodes with the specified cluster role.
        /// </summary>
        /// <param name="role">The required cluster role, or <see langword="null"/> to retain the current role.</param>
        /// <returns>A copy of these settings with the specified role.</returns>
        public ClusterShardingSettings WithRole(string role)
        {
            return Copy(role: role);
        }

        /// <summary>
        /// Returns a copy with the selected remembered-entity behavior.
        /// </summary>
        /// <param name="rememberEntities">Whether active entity ids are remembered for shard restart and rebalance recovery.</param>
        /// <returns>A copy of these settings with the specified entity-memory behavior.</returns>
        public ClusterShardingSettings WithRememberEntities(bool rememberEntities)
        {
            return Copy(rememberEntities: rememberEntities);
        }

        /// <summary>
        /// Returns a copy with the journal plugin id used for persistence-backed sharding state.
        /// </summary>
        /// <param name="journalPluginId">The plugin id, or <see langword="null"/> to use an empty plugin id.</param>
        /// <returns>A copy of these settings with the specified journal plugin id.</returns>
        public ClusterShardingSettings WithJournalPluginId(string journalPluginId)
        {
            return Copy(journalPluginId: journalPluginId ?? string.Empty);
        }

        /// <summary>
        /// Returns a copy with the snapshot plugin id used for persistence-backed sharding state.
        /// </summary>
        /// <param name="snapshotPluginId">The plugin id, or <see langword="null"/> to use an empty plugin id.</param>
        /// <returns>A copy of these settings with the specified snapshot plugin id.</returns>
        public ClusterShardingSettings WithSnapshotPluginId(string snapshotPluginId)
        {
            return Copy(snapshotPluginId: snapshotPluginId ?? string.Empty);
        }

        public ClusterShardingSettings WithStateStoreMode(StateStoreMode mode)
        {
            return Copy(stateStoreMode: mode);
        }

        /// <summary>
        /// Returns a copy with updated shard timing, buffering, recovery, persistence, and allocation parameters.
        /// </summary>
        /// <param name="tuningParameters">The tuning parameters to use.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="tuningParameters"/> is undefined.
        /// </exception>
        /// <returns>A copy of these settings with the supplied tuning parameters.</returns>
        public ClusterShardingSettings WithTuningParameters(TuningParameters tuningParameters)
        {
            if (tuningParameters == null)
                throw new ArgumentNullException(nameof(tuningParameters), $"ClusterShardingSettings requires {nameof(tuningParameters)} to be provided");

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

        public ClusterShardingSettings WithSupervisorStrategy(SupervisorStrategy supervisorStrategy)
        {
            return Copy(supervisorStrategy: supervisorStrategy);
        }
        
        /// <summary>
        /// Returns a copy with the selected coordinator-singleton settings.
        /// </summary>
        /// <param name="coordinatorSingletonSettings">The settings to use for the coordinator singleton.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="coordinatorSingletonSettings"/> is undefined.
        /// </exception>
        /// <returns>A copy of these settings with the supplied coordinator-singleton settings.</returns>
        public ClusterShardingSettings WithCoordinatorSingletonSettings(ClusterSingletonManagerSettings coordinatorSingletonSettings)
        {
            if (coordinatorSingletonSettings == null)
                throw new ArgumentNullException(nameof(coordinatorSingletonSettings), $"ClusterShardingSettings requires {nameof(coordinatorSingletonSettings)} to be provided");

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
            Option<LeaseUsageSettings> leaseSettings = default,
            SupervisorStrategy? supervisorStrategy = null)
        {
            return new ClusterShardingSettings(
                role: role.HasValue ? role.Value : Role,
                rememberEntities: rememberEntities ?? RememberEntities,
                journalPluginId: journalPluginId ?? JournalPluginId,
                snapshotPluginId: snapshotPluginId ?? SnapshotPluginId,
                passivateIdleEntityAfter: passivateIdleAfter ?? PassivateIdleEntityAfter,
                stateStoreMode: stateStoreMode ?? StateStoreMode,
                rememberEntitiesStore: rememberEntitiesStore ?? RememberEntitiesStore,
                shardRegionQueryTimeout: shardRegionQueryTimeout ?? ShardRegionQueryTimeout,
                tuningParameters: tuningParameters ?? TuningParameters,
                coordinatorSingletonSettings: coordinatorSingletonSettings ?? CoordinatorSingletonSettings,
                leaseSettings: leaseSettings.HasValue ? leaseSettings.Value : LeaseSettings,
                supervisorStrategy: supervisorStrategy ?? SupervisorStrategy);
        }
    }
}
