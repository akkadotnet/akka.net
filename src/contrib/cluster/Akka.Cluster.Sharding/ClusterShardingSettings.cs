//-----------------------------------------------------------------------
// <copyright file="ClusterShardingSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;

namespace Akka.Cluster.Sharding
{
    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public class TunningParameters
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan CoordinatorFailureBackoff;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan RetryInterval;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int BufferSize;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan HandOffTimeout;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan ShardStartTimeout;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan ShardFailureBackoff;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan EntityRestartBackoff;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan RebalanceInterval;
        /// <summary>
        /// TBD
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
        /// TBD
        /// </summary>
        public readonly int LeastShardAllocationRebalanceThreshold;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int LeastShardAllocationMaxSimultaneousRebalance;

        public readonly TimeSpan WaitingForStateTimeout;

        public readonly TimeSpan UpdatingStateTimeout;

        public readonly string EntityRecoveryStrategy;
        public readonly TimeSpan EntityRecoveryConstantRateStrategyFrequency;
        public readonly int EntityRecoveryConstantRateStrategyNumberOfEntities;

        /// <summary>
        /// TBD
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
        /// <param name="entityRecoveryStrategy">TBD</param>
        /// <param name="entityRecoveryConstantRateStrategyFrequency">TBD</param>
        /// <param name="entityRecoveryConstantRateStrategyNumberOfEntities">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="entityRecoveryStrategy"/> is invalid.
        /// Acceptable values include: all | constant
        /// </exception>
        public TunningParameters(
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
            int entityRecoveryConstantRateStrategyNumberOfEntities)
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
        }
    }

    public enum StateStoreMode
    {
        Persistence,
        DData
    }

    /// <summary>
    /// TBD
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

        /// <summary>
        /// TBD
        /// </summary>
        public readonly TunningParameters TunningParameters;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly ClusterSingletonManagerSettings CoordinatorSingletonSettings;

        /// <summary>
        /// Create settings from the default configuration `akka.cluster.sharding`.
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static ClusterShardingSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.sharding");
            var coordinatorSingletonPath = config.GetString("coordinator-singleton");

            return Create(config, system.Settings.Config.GetConfig(coordinatorSingletonPath));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="singletonConfig">TBD</param>
        /// <returns>TBD</returns>
        public static ClusterShardingSettings Create(Config config, Config singletonConfig)
        {
            var tuningParameters = new TunningParameters(
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
                entityRecoveryConstantRateStrategyNumberOfEntities: config.GetInt("entity-recovery-constant-rate-strategy.number-of-entities"));

            var coordinatorSingletonSettings = ClusterSingletonManagerSettings.Create(singletonConfig);
            var role = config.GetString("role");
            if (role == string.Empty) role = null;

            return new ClusterShardingSettings(
                role: role,
                rememberEntities: config.GetBoolean("remember-entities"),
                journalPluginId: config.GetString("journal-plugin-id"),
                snapshotPluginId: config.GetString("snapshot-plugin-id"),
                stateStoreMode: (StateStoreMode)Enum.Parse(typeof(StateStoreMode), config.GetString("state-store-mode"), ignoreCase: true),
                tunningParameters: tuningParameters,
                coordinatorSingletonSettings: coordinatorSingletonSettings);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="role">TBD</param>
        /// <param name="rememberEntities">TBD</param>
        /// <param name="journalPluginId">TBD</param>
        /// <param name="snapshotPluginId">TBD</param>
        /// <param name="stateStoreMode">TBD</param>
        /// <param name="tunningParameters">TBD</param>
        /// <param name="coordinatorSingletonSettings">TBD</param>
        public ClusterShardingSettings(
            string role,
            bool rememberEntities,
            string journalPluginId,
            string snapshotPluginId,
            StateStoreMode stateStoreMode,
            TunningParameters tunningParameters,
            ClusterSingletonManagerSettings coordinatorSingletonSettings)
        {
            Role = role;
            RememberEntities = rememberEntities;
            JournalPluginId = journalPluginId;
            SnapshotPluginId = snapshotPluginId;
            StateStoreMode = stateStoreMode;
            TunningParameters = tunningParameters;
            CoordinatorSingletonSettings = coordinatorSingletonSettings;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="role">TBD</param>
        /// <returns>TBD</returns>
        public ClusterShardingSettings WithRole(string role)
        {
            return new ClusterShardingSettings(
                role: role,
                rememberEntities: RememberEntities,
                journalPluginId: JournalPluginId,
                snapshotPluginId: SnapshotPluginId,
                stateStoreMode: StateStoreMode,
                tunningParameters: TunningParameters,
                coordinatorSingletonSettings: CoordinatorSingletonSettings);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="rememberEntities">TBD</param>
        /// <returns>TBD</returns>
        public ClusterShardingSettings WithRememberEntities(bool rememberEntities)
        {
            return Copy(rememberEntities: rememberEntities);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="journalPluginId">TBD</param>
        /// <returns>TBD</returns>
        public ClusterShardingSettings WithJournalPluginId(string journalPluginId)
        {
            return Copy(journalPluginId: journalPluginId ?? string.Empty);
        }

        /// <summary>
        /// TBD
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
        /// TBD
        /// </summary>
        /// <param name="tunningParameters">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="tunningParameters"/> is undefined.
        /// </exception>
        /// <returns>TBD</returns>
        public ClusterShardingSettings WithTuningParameters(TunningParameters tunningParameters)
        {
            if (tunningParameters == null)
                throw new ArgumentNullException(nameof(tunningParameters), $"ClusterShardingSettings requires {nameof(tunningParameters)} to be provided");

            return Copy(tunningParameters: tunningParameters);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="coordinatorSingletonSettings">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="coordinatorSingletonSettings"/> is undefined.
        /// </exception>
        /// <returns>TBD</returns>
        public ClusterShardingSettings WithCoordinatorSingletonSettings(ClusterSingletonManagerSettings coordinatorSingletonSettings)
        {
            if (coordinatorSingletonSettings == null)
                throw new ArgumentNullException(nameof(coordinatorSingletonSettings), $"ClusterShardingSettings requires {nameof(coordinatorSingletonSettings)} to be provided");

            return Copy(coordinatorSingletonSettings: coordinatorSingletonSettings);
        }

        private ClusterShardingSettings Copy(
            string role = null,
            bool? rememberEntities = null,
            string journalPluginId = null,
            string snapshotPluginId = null,
            StateStoreMode? stateStoreMode = null,
            TunningParameters tunningParameters = null,
            ClusterSingletonManagerSettings coordinatorSingletonSettings = null)
        {
            return new ClusterShardingSettings(
                role: role ?? Role,
                rememberEntities: rememberEntities ?? RememberEntities,
                journalPluginId: journalPluginId ?? JournalPluginId,
                snapshotPluginId: snapshotPluginId ?? SnapshotPluginId,
                stateStoreMode: stateStoreMode ?? StateStoreMode,
                tunningParameters: tunningParameters ?? TunningParameters,
                coordinatorSingletonSettings: coordinatorSingletonSettings ?? CoordinatorSingletonSettings);
        }
    }
}
