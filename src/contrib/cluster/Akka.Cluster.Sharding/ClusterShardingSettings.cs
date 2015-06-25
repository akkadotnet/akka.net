using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;

namespace Akka.Cluster.Sharding
{
    [Serializable]
    public class TunningParameters
    {
        public readonly TimeSpan CoordinatorFailureBackoff;
        public readonly TimeSpan RetryInterval;
        public readonly int BufferSize;
        public readonly TimeSpan HandOffTimeout;
        public readonly TimeSpan ShardStartTimeout;
        public readonly TimeSpan ShardFailureBackoff;
        public readonly TimeSpan EntityRestartBackoff;
        public readonly TimeSpan RebalanceInterval;
        public readonly int SnapshotAfter;
        public readonly int LeastShardAllocationRebalanceThreshold;
        public readonly int LeastShardAllocationMaxSimultaneousRebalance;

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
            int leastShardAllocationRebalanceThreshold, 
            int leastShardAllocationMaxSimultaneousRebalance)
        {
            CoordinatorFailureBackoff = coordinatorFailureBackoff;
            RetryInterval = retryInterval;
            BufferSize = bufferSize;
            HandOffTimeout = handOffTimeout;
            ShardStartTimeout = shardStartTimeout;
            ShardFailureBackoff = shardFailureBackoff;
            EntityRestartBackoff = entityRestartBackoff;
            RebalanceInterval = rebalanceInterval;
            SnapshotAfter = snapshotAfter;
            LeastShardAllocationRebalanceThreshold = leastShardAllocationRebalanceThreshold;
            LeastShardAllocationMaxSimultaneousRebalance = leastShardAllocationMaxSimultaneousRebalance;
        }
    }

    [Serializable]
    public sealed class ClusterShardingSettings : INoSerializationVerificationNeeded
    {
        public readonly string Role;
        public readonly bool RememberEntities;
        public readonly string JournalPluginId;
        public readonly string SnapshotPluginId;
        public readonly TunningParameters TunningParameters;
        public readonly ClusterSingletonManagerSettings CoordinatorSingletonSettings;

        public static ClusterShardingSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.sharding");
            return Create(config);
        }

        public static ClusterShardingSettings Create(Config config)
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
                leastShardAllocationRebalanceThreshold: config.GetInt("least-shard-allocation-strategy.rebalance-threshold"),
                leastShardAllocationMaxSimultaneousRebalance: config.GetInt("least-shard-allocation-strategy.max-simultaneous-rebalance"));

            var coordinatorSingletonSettings = ClusterSingletonManagerSettings.Create(config.GetConfig("coordinator-singleton"));
            var role = config.GetString("role");
            if (role == string.Empty) role = null;

            return new ClusterShardingSettings(
                role: role,
                rememberEntities: config.GetBoolean("remember-entities"),
                journalPluginId: config.GetString("journal-plugin-id"),
                snapshotPluginId: config.GetString("snapshot-plugin-id"),
                tunningParameters: tuningParameters,
                coordinatorSingletonSettings: coordinatorSingletonSettings);
        }

        public ClusterShardingSettings(
            string role, 
            bool rememberEntities, 
            string journalPluginId, 
            string snapshotPluginId, 
            TunningParameters tunningParameters, 
            ClusterSingletonManagerSettings coordinatorSingletonSettings)
        {
            Role = role;
            RememberEntities = rememberEntities;
            JournalPluginId = journalPluginId;
            SnapshotPluginId = snapshotPluginId;
            TunningParameters = tunningParameters;
            CoordinatorSingletonSettings = coordinatorSingletonSettings;
        }

        public ClusterShardingSettings WithRole(string role)
        {
            return new ClusterShardingSettings(
                role: role,
                rememberEntities: RememberEntities,
                journalPluginId: JournalPluginId,
                snapshotPluginId: SnapshotPluginId,
                tunningParameters: TunningParameters,
                coordinatorSingletonSettings: CoordinatorSingletonSettings);
        }

        public ClusterShardingSettings WithRememberEntities(bool rememberEntities)
        {
            return Copy(rememberEntities: rememberEntities);
        }

        public ClusterShardingSettings WithJournalPluginId(string journalPluginId)
        {
            return Copy(journalPluginId: journalPluginId ?? string.Empty);
        }
        
        public ClusterShardingSettings WithSnapshotPluginId(string snapshotPluginId)
        {
            return Copy(snapshotPluginId: snapshotPluginId ?? string.Empty);
        }

        public ClusterShardingSettings WithTuningParameters(TunningParameters tunningParameters)
        {
            if(tunningParameters == null)
                throw new ArgumentNullException("tunningParameters");

            return Copy(tunningParameters: tunningParameters);
        }

        public ClusterShardingSettings WithCoordinatorSingletonSettings(ClusterSingletonManagerSettings coordinatorSingletonSettings)
        {
            if(coordinatorSingletonSettings == null)
                throw new ArgumentNullException("coordinatorSingletonSettings");

            return Copy(coordinatorSingletonSettings: coordinatorSingletonSettings);
        }

        private ClusterShardingSettings Copy(
            string role = null,
            bool? rememberEntities = null,
            string journalPluginId = null,
            string snapshotPluginId = null,
            TunningParameters tunningParameters = null,
            ClusterSingletonManagerSettings coordinatorSingletonSettings = null)
        {
            return new ClusterShardingSettings(
                role: role ?? Role,
                rememberEntities: rememberEntities ?? RememberEntities,
                journalPluginId: journalPluginId ?? JournalPluginId,
                snapshotPluginId: snapshotPluginId ?? SnapshotPluginId,
                tunningParameters: tunningParameters ?? TunningParameters,
                coordinatorSingletonSettings: coordinatorSingletonSettings ?? CoordinatorSingletonSettings);
        }
    }
}