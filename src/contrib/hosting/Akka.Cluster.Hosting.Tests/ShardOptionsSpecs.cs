using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Hosting.Tests.Lease;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Coordination;
using Akka.DistributedData;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Akka.Remote.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Cluster.Hosting.Tests;

public class ShardOptionsSpecs
{
    private sealed class MyEntityActor : ReceiveActor
    {
        public MyEntityActor(string entityId)
        {
            EntityId = entityId;
            ReceiveAny(m => Sender.Tell(m));
        }

        public string EntityId { get; }
    }
    
    private sealed class Extractor : HashCodeMessageExtractor
    {
        public Extractor() : base(30)
        {
        }

        public override string EntityId(object message)
        {
            return string.Empty;
        }
    }
    
    private sealed class StopMessage
    {
        public static readonly StopMessage Instance = new();
        private StopMessage() { }
    }
    
    [Fact(DisplayName = "Empty ShardOptions and ShardingDDataOptions without DData should contain default HOCON values")]
    public async Task EmptyShardOptionsTest()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddAkka("ConfigSys", (builder, _) =>
                {
                    builder
                        .WithRemoting()
                        .WithClustering()
                        .WithShardRegion<MyEntityActor>(
                            typeName: "entities", 
                            entityPropsFactory: (_, _) =>
                            {
                                return s => Props.Create(() => new MyEntityActor(s));
                            }, 
                            messageExtractor: new Extractor(), 
                            shardOptions: new ShardOptions())
                        .WithShardingDistributedData(new ShardingDDataOptions());
                });
            }).Build();
        await host.StartAsync();

        Config appliedShardingConfig;
        ClusterShardingSettings shardingSettings;
        ReplicatorSettings replicatorSettings;
        try
        {
            var sys = host.Services.GetRequiredService<ActorSystem>();
            (appliedShardingConfig, shardingSettings) = GetClusterShardingSettings(new ShardOptions(), sys);
            replicatorSettings = GetReplicatorSettings(shardingSettings, sys);
        }
        finally
        {
            await host.StopAsync();
        }

        var shardingConfig = ClusterSharding.DefaultConfig().GetConfig("akka.cluster.sharding");

        #region ClusterShardingSettings validation

        Assert.Null(shardingSettings.Role);
        Assert.Equal(shardingConfig.GetBoolean("remember-entities"), shardingSettings.RememberEntities);
        Assert.Equal(shardingConfig.GetString("journal-plugin-id"), shardingSettings.JournalPluginId);
        Assert.Equal(shardingConfig.GetString("snapshot-plugin-id"), shardingSettings.SnapshotPluginId);
        Assert.Equal(Enum.Parse<StateStoreMode>(shardingConfig.GetString("state-store-mode"), true), shardingSettings.StateStoreMode);
        Assert.Equal(Enum.Parse<RememberEntitiesStore>(shardingConfig.GetString("remember-entities-store"), true), shardingSettings.RememberEntitiesStore);
        Assert.Equal(shardingConfig.GetTimeSpan("shard-region-query-timeout"), shardingSettings.ShardRegionQueryTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("passivate-idle-entity-after"), shardingSettings.PassivateIdleEntityAfter);
        Assert.Equal(shardingConfig.GetBoolean("fail-on-invalid-entity-state-transition"),
            appliedShardingConfig.GetBoolean("fail-on-invalid-entity-state-transition"));
        
        Assert.Equal(shardingConfig.GetTimeSpan("coordinator-failure-backoff"), shardingSettings.TuningParameters.CoordinatorFailureBackoff);
        Assert.Equal(shardingConfig.GetTimeSpan("retry-interval"), shardingSettings.TuningParameters.RetryInterval);
        Assert.Equal(shardingConfig.GetInt("buffer-size"), shardingSettings.TuningParameters.BufferSize);
        Assert.Equal(shardingConfig.GetTimeSpan("handoff-timeout"), shardingSettings.TuningParameters.HandOffTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("shard-start-timeout"), shardingSettings.TuningParameters.ShardStartTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("shard-failure-backoff"), shardingSettings.TuningParameters.ShardFailureBackoff);
        Assert.Equal(shardingConfig.GetTimeSpan("entity-restart-backoff"), shardingSettings.TuningParameters.EntityRestartBackoff);
        Assert.Equal(shardingConfig.GetTimeSpan("rebalance-interval"), shardingSettings.TuningParameters.RebalanceInterval);
        Assert.Equal(shardingConfig.GetInt("snapshot-after"), shardingSettings.TuningParameters.SnapshotAfter);
        Assert.Equal(shardingConfig.GetInt("keep-nr-of-batches"), shardingSettings.TuningParameters.KeepNrOfBatches);
        Assert.Equal(shardingConfig.GetInt("least-shard-allocation-strategy.rebalance-threshold"), shardingSettings.TuningParameters.LeastShardAllocationRebalanceThreshold);
        Assert.Equal(shardingConfig.GetInt("least-shard-allocation-strategy.max-simultaneous-rebalance"), shardingSettings.TuningParameters.LeastShardAllocationMaxSimultaneousRebalance);
        Assert.Equal(shardingConfig.GetTimeSpan("waiting-for-state-timeout"), shardingSettings.TuningParameters.WaitingForStateTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("updating-state-timeout"), shardingSettings.TuningParameters.UpdatingStateTimeout);
        Assert.Equal(shardingConfig.GetString("entity-recovery-strategy"), shardingSettings.TuningParameters.EntityRecoveryStrategy);
        Assert.Equal(shardingConfig.GetTimeSpan("entity-recovery-constant-rate-strategy.frequency"), shardingSettings.TuningParameters.EntityRecoveryConstantRateStrategyFrequency);
        Assert.Equal(shardingConfig.GetInt("entity-recovery-constant-rate-strategy.number-of-entities"), shardingSettings.TuningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities);
        Assert.Equal(ConfigMajorityPlus(shardingConfig, "coordinator-state.write-majority-plus"), shardingSettings.TuningParameters.CoordinatorStateWriteMajorityPlus);
        Assert.Equal(ConfigMajorityPlus(shardingConfig, "coordinator-state.read-majority-plus"), shardingSettings.TuningParameters.CoordinatorStateReadMajorityPlus);
        Assert.Equal(shardingConfig.GetInt("least-shard-allocation-strategy.rebalance-absolute-limit"), shardingSettings.TuningParameters.LeastShardAllocationAbsoluteLimit);
        Assert.Equal(shardingConfig.GetDouble("least-shard-allocation-strategy.rebalance-relative-limit"), shardingSettings.TuningParameters.LeastShardAllocationRelativeLimit);

        var singletonConfig = ClusterSingleton.DefaultConfig().GetConfig("akka.cluster.singleton");
        Assert.Equal(singletonConfig.GetString("singleton-name"), shardingSettings.CoordinatorSingletonSettings.SingletonName);
        Assert.Null(shardingSettings.CoordinatorSingletonSettings.Role);
        // https://github.com/akkadotnet/akka.net/blob/4ae47927da9f2539742c336acfa8ae0037fabbb7/src/contrib/cluster/Akka.Cluster.Tools/Singleton/ClusterSingletonManagerSettings.cs#L58
        Assert.Equal(TimeSpan.Zero, shardingSettings.CoordinatorSingletonSettings.RemovalMargin);
        Assert.Equal(singletonConfig.GetTimeSpan("hand-over-retry-interval"), shardingSettings.CoordinatorSingletonSettings.HandOverRetryInterval);
        {
            var expectedLease = GetLeaseUsageSettings(shardingConfig);
            var actualLease = shardingSettings.CoordinatorSingletonSettings.LeaseSettings;
            if (expectedLease is null)
            {
                Assert.Null(actualLease);
            }
            else
            {
                Assert.NotNull(actualLease);
                Assert.Equal(expectedLease.LeaseImplementation, actualLease.LeaseImplementation);
                Assert.Equal(expectedLease.LeaseRetryInterval, actualLease.LeaseRetryInterval);
            }
        }
#pragma warning disable CS0618 // Type or member is obsolete
        Assert.Equal(singletonConfig.GetBoolean("consider-app-version"), shardingSettings.CoordinatorSingletonSettings.ConsiderAppVersion);
#pragma warning restore CS0618 // Type or member is obsolete

        Assert.Null(shardingSettings.LeaseSettings);

        #endregion

        #region ReplicatorSettings validation
        var repConfig = shardingConfig.GetConfig("distributed-data")
            .WithFallback(DistributedData.DistributedData.DefaultConfig().GetConfig("akka.cluster.distributed-data"));

        Assert.Equal(repConfig.GetString("role"), replicatorSettings.Role);
        Assert.Equal(repConfig.GetTimeSpan("gossip-interval"), replicatorSettings.GossipInterval);
        Assert.Equal(repConfig.GetTimeSpan("notify-subscribers-interval"), replicatorSettings.NotifySubscribersInterval);
        Assert.Equal(repConfig.GetInt("max-delta-elements"), replicatorSettings.MaxDeltaElements);
        Assert.Equal("akka.actor.internal-dispatcher", replicatorSettings.Dispatcher);
        Assert.Equal(repConfig.GetTimeSpan("pruning-interval"), replicatorSettings.PruningInterval);
        Assert.Equal(repConfig.GetTimeSpan("max-pruning-dissemination"), replicatorSettings.MaxPruningDissemination);
        Assert.Empty(replicatorSettings.DurableKeys);
        Assert.Equal(repConfig.GetTimeSpan("pruning-marker-time-to-live"), replicatorSettings.PruningMarkerTimeToLive);
        Assert.NotNull(replicatorSettings.DurableStoreProps);
        Assert.Equal(repConfig.GetInt("delta-crdt.max-delta-size"), replicatorSettings.MaxDeltaSize);
        Assert.Equal(repConfig.GetBoolean("recreate-on-failure"), replicatorSettings.RestartReplicatorOnFailure);
        Assert.Equal(repConfig.GetBoolean("prefer-oldest"), replicatorSettings.PreferOldest);
        Assert.Equal(repConfig.GetBoolean("verbose-debug-logging"), replicatorSettings.VerboseDebugLogging);

        #endregion
    }

    [Fact(DisplayName = "Empty ShardOptions and ShardingDDataOptions with DData should contain default HOCON values")]
    public async Task EmptyDDataShardOptionsTest()
    {
        var shardOptions = new ShardOptions
        {
            RememberEntitiesStore = RememberEntitiesStore.DData,
            RememberEntities = true,
        };
        
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddAkka("ConfigSys", (builder, _) =>
                {
                    builder
                        .WithRemoting()
                        .WithClustering()
                        .WithShardRegion<MyEntityActor>(
                            typeName: "entities", 
                            entityPropsFactory: (_, _) =>
                            {
                                return s => Props.Create(() => new MyEntityActor(s));
                            }, 
                            messageExtractor: new Extractor(), 
                            shardOptions: shardOptions)
                        .WithShardingDistributedData(new ShardingDDataOptions());
                });
            }).Build();
        await host.StartAsync();

        Config appliedShardingConfig;
        ClusterShardingSettings shardingSettings;
        ReplicatorSettings replicatorSettings;
        try
        {
            var sys = host.Services.GetRequiredService<ActorSystem>();
            (appliedShardingConfig, shardingSettings) = GetClusterShardingSettings(shardOptions, sys);
            replicatorSettings = GetReplicatorSettings(shardingSettings, sys);
        }
        finally
        {
            await host.StopAsync();
        }

        var shardingConfig = ClusterSharding.DefaultConfig().GetConfig("akka.cluster.sharding");

        #region ClusterShardingSettings validation

        Assert.Null(shardingSettings.Role);
        Assert.True(shardingSettings.RememberEntities);
        Assert.Equal(RememberEntitiesStore.DData, shardingSettings.RememberEntitiesStore);
        Assert.Equal(shardingConfig.GetString("journal-plugin-id"), shardingSettings.JournalPluginId);
        Assert.Equal(shardingConfig.GetString("snapshot-plugin-id"), shardingSettings.SnapshotPluginId);
        Assert.Equal(Enum.Parse<StateStoreMode>(shardingConfig.GetString("state-store-mode"), true), shardingSettings.StateStoreMode);
        Assert.Equal(shardingConfig.GetTimeSpan("shard-region-query-timeout"), shardingSettings.ShardRegionQueryTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("passivate-idle-entity-after"), shardingSettings.PassivateIdleEntityAfter);
        Assert.Equal(shardingConfig.GetBoolean("fail-on-invalid-entity-state-transition"),
            appliedShardingConfig.GetBoolean("fail-on-invalid-entity-state-transition"));
        
        Assert.Equal(shardingConfig.GetTimeSpan("coordinator-failure-backoff"), shardingSettings.TuningParameters.CoordinatorFailureBackoff);
        Assert.Equal(shardingConfig.GetTimeSpan("retry-interval"), shardingSettings.TuningParameters.RetryInterval);
        Assert.Equal(shardingConfig.GetInt("buffer-size"), shardingSettings.TuningParameters.BufferSize);
        Assert.Equal(shardingConfig.GetTimeSpan("handoff-timeout"), shardingSettings.TuningParameters.HandOffTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("shard-start-timeout"), shardingSettings.TuningParameters.ShardStartTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("shard-failure-backoff"), shardingSettings.TuningParameters.ShardFailureBackoff);
        Assert.Equal(shardingConfig.GetTimeSpan("entity-restart-backoff"), shardingSettings.TuningParameters.EntityRestartBackoff);
        Assert.Equal(shardingConfig.GetTimeSpan("rebalance-interval"), shardingSettings.TuningParameters.RebalanceInterval);
        Assert.Equal(shardingConfig.GetInt("snapshot-after"), shardingSettings.TuningParameters.SnapshotAfter);
        Assert.Equal(shardingConfig.GetInt("keep-nr-of-batches"), shardingSettings.TuningParameters.KeepNrOfBatches);
        Assert.Equal(shardingConfig.GetInt("least-shard-allocation-strategy.rebalance-threshold"), shardingSettings.TuningParameters.LeastShardAllocationRebalanceThreshold);
        Assert.Equal(shardingConfig.GetInt("least-shard-allocation-strategy.max-simultaneous-rebalance"), shardingSettings.TuningParameters.LeastShardAllocationMaxSimultaneousRebalance);
        Assert.Equal(shardingConfig.GetTimeSpan("waiting-for-state-timeout"), shardingSettings.TuningParameters.WaitingForStateTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("updating-state-timeout"), shardingSettings.TuningParameters.UpdatingStateTimeout);
        Assert.Equal(shardingConfig.GetString("entity-recovery-strategy"), shardingSettings.TuningParameters.EntityRecoveryStrategy);
        Assert.Equal(shardingConfig.GetTimeSpan("entity-recovery-constant-rate-strategy.frequency"), shardingSettings.TuningParameters.EntityRecoveryConstantRateStrategyFrequency);
        Assert.Equal(shardingConfig.GetInt("entity-recovery-constant-rate-strategy.number-of-entities"), shardingSettings.TuningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities);
        Assert.Equal(ConfigMajorityPlus(shardingConfig, "coordinator-state.write-majority-plus"), shardingSettings.TuningParameters.CoordinatorStateWriteMajorityPlus);
        Assert.Equal(ConfigMajorityPlus(shardingConfig, "coordinator-state.read-majority-plus"), shardingSettings.TuningParameters.CoordinatorStateReadMajorityPlus);
        Assert.Equal(shardingConfig.GetInt("least-shard-allocation-strategy.rebalance-absolute-limit"), shardingSettings.TuningParameters.LeastShardAllocationAbsoluteLimit);
        Assert.Equal(shardingConfig.GetDouble("least-shard-allocation-strategy.rebalance-relative-limit"), shardingSettings.TuningParameters.LeastShardAllocationRelativeLimit);

        var singletonConfig = ClusterSingleton.DefaultConfig().GetConfig("akka.cluster.singleton");
        Assert.Equal(singletonConfig.GetString("singleton-name"), shardingSettings.CoordinatorSingletonSettings.SingletonName);
        Assert.Null(shardingSettings.CoordinatorSingletonSettings.Role);
        // https://github.com/akkadotnet/akka.net/blob/4ae47927da9f2539742c336acfa8ae0037fabbb7/src/contrib/cluster/Akka.Cluster.Tools/Singleton/ClusterSingletonManagerSettings.cs#L58
        Assert.Equal(TimeSpan.Zero, shardingSettings.CoordinatorSingletonSettings.RemovalMargin);
        Assert.Equal(singletonConfig.GetTimeSpan("hand-over-retry-interval"), shardingSettings.CoordinatorSingletonSettings.HandOverRetryInterval);
        {
            var expectedLease = GetLeaseUsageSettings(shardingConfig);
            var actualLease = shardingSettings.CoordinatorSingletonSettings.LeaseSettings;
            if (expectedLease is null)
            {
                Assert.Null(actualLease);
            }
            else
            {
                Assert.NotNull(actualLease);
                Assert.Equal(expectedLease.LeaseImplementation, actualLease.LeaseImplementation);
                Assert.Equal(expectedLease.LeaseRetryInterval, actualLease.LeaseRetryInterval);
            }
        }
#pragma warning disable CS0618 // Type or member is obsolete
        Assert.Equal(singletonConfig.GetBoolean("consider-app-version"), shardingSettings.CoordinatorSingletonSettings.ConsiderAppVersion);
#pragma warning restore CS0618 // Type or member is obsolete

        Assert.Null(shardingSettings.LeaseSettings);

        #endregion

        #region ReplicatorSettings validation
        var repConfig = shardingConfig.GetConfig("distributed-data")
            .WithFallback(DistributedData.DistributedData.DefaultConfig().GetConfig("akka.cluster.distributed-data"));

        Assert.Equal(repConfig.GetString("role"), replicatorSettings.Role);
        Assert.Equal(repConfig.GetTimeSpan("gossip-interval"), replicatorSettings.GossipInterval);
        Assert.Equal(repConfig.GetTimeSpan("notify-subscribers-interval"), replicatorSettings.NotifySubscribersInterval);
        Assert.Equal(repConfig.GetInt("max-delta-elements"), replicatorSettings.MaxDeltaElements);
        Assert.Equal("akka.actor.internal-dispatcher", replicatorSettings.Dispatcher);
        Assert.Equal(repConfig.GetTimeSpan("pruning-interval"), replicatorSettings.PruningInterval);
        Assert.Equal(repConfig.GetTimeSpan("max-pruning-dissemination"), replicatorSettings.MaxPruningDissemination);
        Assert.Single(replicatorSettings.DurableKeys, "shard-*");
        Assert.Equal(repConfig.GetTimeSpan("pruning-marker-time-to-live"), replicatorSettings.PruningMarkerTimeToLive);
        Assert.NotNull(replicatorSettings.DurableStoreProps);
        Assert.Equal(repConfig.GetInt("delta-crdt.max-delta-size"), replicatorSettings.MaxDeltaSize);
        Assert.Equal(repConfig.GetBoolean("recreate-on-failure"), replicatorSettings.RestartReplicatorOnFailure);
        Assert.Equal(repConfig.GetBoolean("prefer-oldest"), replicatorSettings.PreferOldest);
        Assert.Equal(repConfig.GetBoolean("verbose-debug-logging"), replicatorSettings.VerboseDebugLogging);

        #endregion
    }

    [Fact(DisplayName = "Modified ShardOptions and ShardingDDataOptions without DData should contain proper HOCON values")]
    public async Task ModifiedShardOptionsTest()
    {
        var shardOptions = new ShardOptions
        {
            StateStoreMode = StateStoreMode.DData, 
            RememberEntitiesStore = RememberEntitiesStore.Eventsourced, 
            RememberEntities = true, 
            Role = "test", 
            JournalPluginId = "custom-journal", 
            SnapshotPluginId = "custom-snapshot-store", 
            LeaseImplementation = new TestLeaseOption(), 
            LeaseRetryInterval = TimeSpan.FromSeconds(1), 
            HandOffStopMessage = StopMessage.Instance, // can't be tested, assigned directly
            FailOnInvalidEntityStateTransition = true, 
#pragma warning disable CS0618 // Type or member is obsolete
            // This property should never get applied to HOCON
            DistributedData =
            {
                Role = "wrong-role", 
                Name = "wrong-name" 
            },
#pragma warning restore CS0618 // Type or member is obsolete
            ShouldPassivateIdleEntities = false, 
            ShardRegionQueryTimeout = TimeSpan.FromSeconds(2), 
            PassivateIdleEntityAfter = TimeSpan.FromSeconds(3), 
        };
        
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddAkka("ConfigSys", (builder, _) =>
                {
                    builder
                        .WithRemoting()
                        .WithClustering(new ClusterOptions
                        {
                            Roles = ["test"]
                        })
                        .WithShardRegion<MyEntityActor>(
                            typeName: "entities", 
                            entityPropsFactory: (_, _) =>
                            {
                                return s => Props.Create(() => new MyEntityActor(s));
                            }, 
                            messageExtractor: new Extractor(), 
                            shardOptions: shardOptions)
                        .WithInMemoryJournal(_ => { }, "custom-journal")
                        .WithInMemorySnapshotStore("custom-snapshot-store")
                        .WithShardingDistributedData(new ShardingDDataOptions
                        {
                            Name = "customReplicator", 
                            Role = "test", 
                            RecreateOnFailure = true, 
                            PreferOldest = false, 
                            VerboseDebugLogging = true, 
                            Durable = new DurableOptions
                            {
                                Keys = ["custom-*"],
                                Lmdb = new LmdbOptions
                                {
                                    Directory = "lmdb",
                                    MapSize = 1024 * 1024
                                }
                            },
                            MajorityMinimumCapacity = 1,
                            MaxDeltaElements = 2, // This setting ("max-delta-elements") never get used in core
                        });
                });
            }).Build();
        await host.StartAsync();

        Config appliedShardingConfig;
        ClusterShardingSettings shardingSettings;
        ReplicatorSettings replicatorSettings;
        try
        {
            var sys = host.Services.GetRequiredService<ActorSystem>();
            (appliedShardingConfig, shardingSettings) = GetClusterShardingSettings(shardOptions, sys);
            replicatorSettings = GetReplicatorSettings(shardingSettings, sys);
        }
        finally
        {
            await host.StopAsync();
        }

        var shardingConfig = ClusterSharding.DefaultConfig().GetConfig("akka.cluster.sharding");

        #region ClusterShardingSettings validation

        Assert.Equal("test", shardingSettings.Role);
        Assert.True(shardingSettings.RememberEntities);
        Assert.Equal("custom-journal", shardingSettings.JournalPluginId);
        Assert.Equal("custom-snapshot-store", shardingSettings.SnapshotPluginId);
        Assert.Equal(StateStoreMode.DData, shardingSettings.StateStoreMode);
        Assert.Equal(RememberEntitiesStore.Eventsourced, shardingSettings.RememberEntitiesStore);
        Assert.Equal(TimeSpan.FromSeconds(2), shardingSettings.ShardRegionQueryTimeout);
        Assert.Equal(TimeSpan.Zero, shardingSettings.PassivateIdleEntityAfter);
        
        Assert.True(appliedShardingConfig.GetBoolean("fail-on-invalid-entity-state-transition"));
        Assert.Equal(1, appliedShardingConfig.GetInt("distributed-data.majority-min-cap"));
        
        Assert.Equal(shardingConfig.GetTimeSpan("coordinator-failure-backoff"), shardingSettings.TuningParameters.CoordinatorFailureBackoff);
        Assert.Equal(shardingConfig.GetTimeSpan("retry-interval"), shardingSettings.TuningParameters.RetryInterval);
        Assert.Equal(shardingConfig.GetInt("buffer-size"), shardingSettings.TuningParameters.BufferSize);
        Assert.Equal(shardingConfig.GetTimeSpan("handoff-timeout"), shardingSettings.TuningParameters.HandOffTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("shard-start-timeout"), shardingSettings.TuningParameters.ShardStartTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("shard-failure-backoff"), shardingSettings.TuningParameters.ShardFailureBackoff);
        Assert.Equal(shardingConfig.GetTimeSpan("entity-restart-backoff"), shardingSettings.TuningParameters.EntityRestartBackoff);
        Assert.Equal(shardingConfig.GetTimeSpan("rebalance-interval"), shardingSettings.TuningParameters.RebalanceInterval);
        Assert.Equal(shardingConfig.GetInt("snapshot-after"), shardingSettings.TuningParameters.SnapshotAfter);
        Assert.Equal(shardingConfig.GetInt("keep-nr-of-batches"), shardingSettings.TuningParameters.KeepNrOfBatches);
        Assert.Equal(shardingConfig.GetInt("least-shard-allocation-strategy.rebalance-threshold"), shardingSettings.TuningParameters.LeastShardAllocationRebalanceThreshold);
        Assert.Equal(shardingConfig.GetInt("least-shard-allocation-strategy.max-simultaneous-rebalance"), shardingSettings.TuningParameters.LeastShardAllocationMaxSimultaneousRebalance);
        Assert.Equal(shardingConfig.GetTimeSpan("waiting-for-state-timeout"), shardingSettings.TuningParameters.WaitingForStateTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("updating-state-timeout"), shardingSettings.TuningParameters.UpdatingStateTimeout);
        Assert.Equal(shardingConfig.GetString("entity-recovery-strategy"), shardingSettings.TuningParameters.EntityRecoveryStrategy);
        Assert.Equal(shardingConfig.GetTimeSpan("entity-recovery-constant-rate-strategy.frequency"), shardingSettings.TuningParameters.EntityRecoveryConstantRateStrategyFrequency);
        Assert.Equal(shardingConfig.GetInt("entity-recovery-constant-rate-strategy.number-of-entities"), shardingSettings.TuningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities);
        Assert.Equal(ConfigMajorityPlus(shardingConfig, "coordinator-state.write-majority-plus"), shardingSettings.TuningParameters.CoordinatorStateWriteMajorityPlus);
        Assert.Equal(ConfigMajorityPlus(shardingConfig, "coordinator-state.read-majority-plus"), shardingSettings.TuningParameters.CoordinatorStateReadMajorityPlus);
        Assert.Equal(shardingConfig.GetInt("least-shard-allocation-strategy.rebalance-absolute-limit"), shardingSettings.TuningParameters.LeastShardAllocationAbsoluteLimit);
        Assert.Equal(shardingConfig.GetDouble("least-shard-allocation-strategy.rebalance-relative-limit"), shardingSettings.TuningParameters.LeastShardAllocationRelativeLimit);

        var singletonConfig = ClusterSingleton.DefaultConfig().GetConfig("akka.cluster.singleton");
        Assert.Equal(singletonConfig.GetString("singleton-name"), shardingSettings.CoordinatorSingletonSettings.SingletonName);
        Assert.Null(shardingSettings.CoordinatorSingletonSettings.Role);
        // https://github.com/akkadotnet/akka.net/blob/4ae47927da9f2539742c336acfa8ae0037fabbb7/src/contrib/cluster/Akka.Cluster.Tools/Singleton/ClusterSingletonManagerSettings.cs#L58
        Assert.Equal(TimeSpan.Zero, shardingSettings.CoordinatorSingletonSettings.RemovalMargin);
        Assert.Equal(singletonConfig.GetTimeSpan("hand-over-retry-interval"), shardingSettings.CoordinatorSingletonSettings.HandOverRetryInterval);
        {
            var expectedLease = GetLeaseUsageSettings(shardingConfig);
            var actualLease = shardingSettings.CoordinatorSingletonSettings.LeaseSettings;
            if (expectedLease is null)
            {
                Assert.Null(actualLease);
            }
            else
            {
                Assert.NotNull(actualLease);
                Assert.Equal(expectedLease.LeaseImplementation, actualLease.LeaseImplementation);
                Assert.Equal(expectedLease.LeaseRetryInterval, actualLease.LeaseRetryInterval);
            }
        }
#pragma warning disable CS0618 // Type or member is obsolete
        Assert.Equal(singletonConfig.GetBoolean("consider-app-version"), shardingSettings.CoordinatorSingletonSettings.ConsiderAppVersion);
#pragma warning restore CS0618 // Type or member is obsolete

        Assert.NotNull(shardingSettings.LeaseSettings);
        Assert.Equal("test-lease", shardingSettings.LeaseSettings!.LeaseImplementation);
        Assert.Equal(1.Seconds(), shardingSettings.LeaseSettings.LeaseRetryInterval);

        #endregion

        #region ReplicatorSettings validation
        var repConfig = shardingConfig.GetConfig("distributed-data")
            .WithFallback(DistributedData.DistributedData.DefaultConfig().GetConfig("akka.cluster.distributed-data"));

        Assert.NotEqual("wrong-name", appliedShardingConfig.GetString("distributed-data.name"));
        Assert.Equal("customReplicator", appliedShardingConfig.GetString("distributed-data.name"));

        Assert.NotEqual("wrong-role", replicatorSettings.Role);
        Assert.Equal("test", replicatorSettings.Role);

        Assert.Equal(repConfig.GetTimeSpan("gossip-interval"), replicatorSettings.GossipInterval);
        Assert.Equal(repConfig.GetTimeSpan("notify-subscribers-interval"), replicatorSettings.NotifySubscribersInterval);
        Assert.Equal(2, replicatorSettings.MaxDeltaElements);
        Assert.Equal("akka.actor.internal-dispatcher", replicatorSettings.Dispatcher);
        Assert.Equal(repConfig.GetTimeSpan("pruning-interval"), replicatorSettings.PruningInterval);
        Assert.Equal(repConfig.GetTimeSpan("max-pruning-dissemination"), replicatorSettings.MaxPruningDissemination);
        Assert.Empty(replicatorSettings.DurableKeys);
        Assert.Equal(repConfig.GetTimeSpan("pruning-marker-time-to-live"), replicatorSettings.PruningMarkerTimeToLive);
        Assert.NotNull(replicatorSettings.DurableStoreProps);
        Assert.Equal(repConfig.GetInt("delta-crdt.max-delta-size"), replicatorSettings.MaxDeltaSize);
        Assert.True(replicatorSettings.RestartReplicatorOnFailure);
        Assert.False(replicatorSettings.PreferOldest);
        Assert.True(replicatorSettings.VerboseDebugLogging);

        Assert.Equal("lmdb", appliedShardingConfig.GetString("distributed-data.durable.lmdb.dir"));
        Assert.Equal(1024 * 1024, appliedShardingConfig.GetLong("distributed-data.durable.lmdb.map-size"));
        
        #endregion
    }
    
    [Fact(DisplayName = "Modified ShardOptions and ShardingDDataOptions with DData should contain proper HOCON values")]
    public async Task ModifiedDDataShardOptionsTest()
    {
        var shardOptions = new ShardOptions
        {
            StateStoreMode = StateStoreMode.DData, 
            RememberEntitiesStore = RememberEntitiesStore.DData, 
            RememberEntities = true, 
            Role = "test", 
            JournalPluginId = "custom-journal", 
            SnapshotPluginId = "custom-snapshot-store", 
            LeaseImplementation = new TestLeaseOption(), 
            LeaseRetryInterval = TimeSpan.FromSeconds(1), 
            HandOffStopMessage = StopMessage.Instance, // can't be tested, assigned directly
            FailOnInvalidEntityStateTransition = true, 
#pragma warning disable CS0618 // Type or member is obsolete
            // This property should never get applied to HOCON
            DistributedData =
            {
                Role = "wrong-role", 
                Name = "wrong-name" 
            },
#pragma warning restore CS0618 // Type or member is obsolete
            ShouldPassivateIdleEntities = false, 
            ShardRegionQueryTimeout = TimeSpan.FromSeconds(2), 
            PassivateIdleEntityAfter = TimeSpan.FromSeconds(3), 
        };
        
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddAkka("ConfigSys", (builder, _) =>
                {
                    builder
                        .WithRemoting()
                        .WithClustering(new ClusterOptions
                        {
                            Roles = ["test"]
                        })
                        .WithShardRegion<MyEntityActor>(
                            typeName: "entities", 
                            entityPropsFactory: (_, _) =>
                            {
                                return s => Props.Create(() => new MyEntityActor(s));
                            }, 
                            messageExtractor: new Extractor(), 
                            shardOptions: shardOptions)
                        .WithInMemoryJournal(_ => { }, "custom-journal")
                        .WithInMemorySnapshotStore("custom-snapshot-store")
                        .WithShardingDistributedData(new ShardingDDataOptions
                        {
                            Name = "customReplicator", 
                            Role = "test", 
                            RecreateOnFailure = true, 
                            PreferOldest = false, 
                            VerboseDebugLogging = true, 
                            Durable = new DurableOptions
                            {
                                Keys = ["custom-*"],
                                Lmdb = new LmdbOptions
                                {
                                    Directory = "lmdb",
                                    MapSize = 1024 * 1024
                                }
                            },
                            MajorityMinimumCapacity = 1,
                            MaxDeltaElements = 2, // This setting ("max-delta-elements") never get used in core
                        });
                });
            }).Build();
        await host.StartAsync();

        Config appliedShardingConfig;
        ClusterShardingSettings shardingSettings;
        ReplicatorSettings replicatorSettings;
        try
        {
            var sys = host.Services.GetRequiredService<ActorSystem>();
            (appliedShardingConfig, shardingSettings) = GetClusterShardingSettings(shardOptions, sys);
            replicatorSettings = GetReplicatorSettings(shardingSettings, sys);
        }
        finally
        {
            await host.StopAsync();
        }

        var shardingConfig = ClusterSharding.DefaultConfig().GetConfig("akka.cluster.sharding");

        #region ClusterShardingSettings validation

        Assert.Equal("test", shardingSettings.Role);
        Assert.True(shardingSettings.RememberEntities);
        Assert.Equal("custom-journal", shardingSettings.JournalPluginId);
        Assert.Equal("custom-snapshot-store", shardingSettings.SnapshotPluginId);
        Assert.Equal(StateStoreMode.DData, shardingSettings.StateStoreMode);
        Assert.Equal(RememberEntitiesStore.DData, shardingSettings.RememberEntitiesStore);
        Assert.Equal(2.Seconds(), shardingSettings.ShardRegionQueryTimeout);
        Assert.Equal(TimeSpan.Zero, shardingSettings.PassivateIdleEntityAfter);
        
        Assert.True(appliedShardingConfig.GetBoolean("fail-on-invalid-entity-state-transition"));
        Assert.Equal(1, appliedShardingConfig.GetInt("distributed-data.majority-min-cap"));
        
        Assert.Equal(shardingConfig.GetTimeSpan("coordinator-failure-backoff"), shardingSettings.TuningParameters.CoordinatorFailureBackoff);
        Assert.Equal(shardingConfig.GetTimeSpan("retry-interval"), shardingSettings.TuningParameters.RetryInterval);
        Assert.Equal(shardingConfig.GetInt("buffer-size"), shardingSettings.TuningParameters.BufferSize);
        Assert.Equal(shardingConfig.GetTimeSpan("handoff-timeout"), shardingSettings.TuningParameters.HandOffTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("shard-start-timeout"), shardingSettings.TuningParameters.ShardStartTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("shard-failure-backoff"), shardingSettings.TuningParameters.ShardFailureBackoff);
        Assert.Equal(shardingConfig.GetTimeSpan("entity-restart-backoff"), shardingSettings.TuningParameters.EntityRestartBackoff);
        Assert.Equal(shardingConfig.GetTimeSpan("rebalance-interval"), shardingSettings.TuningParameters.RebalanceInterval);
        Assert.Equal(shardingConfig.GetInt("snapshot-after"), shardingSettings.TuningParameters.SnapshotAfter);
        Assert.Equal(shardingConfig.GetInt("keep-nr-of-batches"), shardingSettings.TuningParameters.KeepNrOfBatches);
        Assert.Equal(shardingConfig.GetInt("least-shard-allocation-strategy.rebalance-threshold"), shardingSettings.TuningParameters.LeastShardAllocationRebalanceThreshold);
        Assert.Equal(shardingConfig.GetInt("least-shard-allocation-strategy.max-simultaneous-rebalance"), shardingSettings.TuningParameters.LeastShardAllocationMaxSimultaneousRebalance);
        Assert.Equal(shardingConfig.GetTimeSpan("waiting-for-state-timeout"), shardingSettings.TuningParameters.WaitingForStateTimeout);
        Assert.Equal(shardingConfig.GetTimeSpan("updating-state-timeout"), shardingSettings.TuningParameters.UpdatingStateTimeout);
        Assert.Equal(shardingConfig.GetString("entity-recovery-strategy"), shardingSettings.TuningParameters.EntityRecoveryStrategy);
        Assert.Equal(shardingConfig.GetTimeSpan("entity-recovery-constant-rate-strategy.frequency"), shardingSettings.TuningParameters.EntityRecoveryConstantRateStrategyFrequency);
        Assert.Equal(shardingConfig.GetInt("entity-recovery-constant-rate-strategy.number-of-entities"), shardingSettings.TuningParameters.EntityRecoveryConstantRateStrategyNumberOfEntities);
        Assert.Equal(ConfigMajorityPlus(shardingConfig, "coordinator-state.write-majority-plus"), shardingSettings.TuningParameters.CoordinatorStateWriteMajorityPlus);
        Assert.Equal(ConfigMajorityPlus(shardingConfig, "coordinator-state.read-majority-plus"), shardingSettings.TuningParameters.CoordinatorStateReadMajorityPlus);
        Assert.Equal(shardingConfig.GetInt("least-shard-allocation-strategy.rebalance-absolute-limit"), shardingSettings.TuningParameters.LeastShardAllocationAbsoluteLimit);
        Assert.Equal(shardingConfig.GetDouble("least-shard-allocation-strategy.rebalance-relative-limit"), shardingSettings.TuningParameters.LeastShardAllocationRelativeLimit);

        var singletonConfig = ClusterSingleton.DefaultConfig().GetConfig("akka.cluster.singleton");
        Assert.Equal(singletonConfig.GetString("singleton-name"), shardingSettings.CoordinatorSingletonSettings.SingletonName);
        Assert.Null(shardingSettings.CoordinatorSingletonSettings.Role);
        // https://github.com/akkadotnet/akka.net/blob/4ae47927da9f2539742c336acfa8ae0037fabbb7/src/contrib/cluster/Akka.Cluster.Tools/Singleton/ClusterSingletonManagerSettings.cs#L58
        Assert.Equal(TimeSpan.Zero, shardingSettings.CoordinatorSingletonSettings.RemovalMargin);
        Assert.Equal(singletonConfig.GetTimeSpan("hand-over-retry-interval"), shardingSettings.CoordinatorSingletonSettings.HandOverRetryInterval);
        {
            var expectedLease = GetLeaseUsageSettings(shardingConfig);
            var actualLease = shardingSettings.CoordinatorSingletonSettings.LeaseSettings;
            if (expectedLease is null)
            {
                Assert.Null(actualLease);
            }
            else
            {
                Assert.NotNull(actualLease);
                Assert.Equal(expectedLease.LeaseImplementation, actualLease.LeaseImplementation);
                Assert.Equal(expectedLease.LeaseRetryInterval, actualLease.LeaseRetryInterval);
            }
        }
#pragma warning disable CS0618 // Type or member is obsolete
        Assert.Equal(singletonConfig.GetBoolean("consider-app-version"), shardingSettings.CoordinatorSingletonSettings.ConsiderAppVersion);
#pragma warning restore CS0618 // Type or member is obsolete

        Assert.NotNull(shardingSettings.LeaseSettings);
        Assert.Equal("test-lease", shardingSettings.LeaseSettings!.LeaseImplementation);
        Assert.Equal(TimeSpan.FromSeconds(1), shardingSettings.LeaseSettings.LeaseRetryInterval);

        #endregion

        #region ReplicatorSettings validation
        var repConfig = shardingConfig.GetConfig("distributed-data")
            .WithFallback(DistributedData.DistributedData.DefaultConfig().GetConfig("akka.cluster.distributed-data"));

        Assert.NotEqual("wrong-name", appliedShardingConfig.GetString("distributed-data.name"));
        Assert.Equal("customReplicator", appliedShardingConfig.GetString("distributed-data.name"));

        Assert.NotEqual("wrong-role", replicatorSettings.Role);
        Assert.Equal("test", replicatorSettings.Role);

        Assert.Equal(repConfig.GetTimeSpan("gossip-interval"), replicatorSettings.GossipInterval);
        Assert.Equal(repConfig.GetTimeSpan("notify-subscribers-interval"), replicatorSettings.NotifySubscribersInterval);
        Assert.Equal(2, replicatorSettings.MaxDeltaElements);
        Assert.Equal("akka.actor.internal-dispatcher", replicatorSettings.Dispatcher);
        Assert.Equal(repConfig.GetTimeSpan("pruning-interval"), replicatorSettings.PruningInterval);
        Assert.Equal(repConfig.GetTimeSpan("max-pruning-dissemination"), replicatorSettings.MaxPruningDissemination);
        Assert.Single(replicatorSettings.DurableKeys, "custom-*");
        Assert.Equal(repConfig.GetTimeSpan("pruning-marker-time-to-live"), replicatorSettings.PruningMarkerTimeToLive);
        Assert.NotNull(replicatorSettings.DurableStoreProps);
        Assert.Equal(repConfig.GetInt("delta-crdt.max-delta-size"), replicatorSettings.MaxDeltaSize);
        Assert.True(replicatorSettings.RestartReplicatorOnFailure);
        Assert.False(replicatorSettings.PreferOldest);
        Assert.True(replicatorSettings.VerboseDebugLogging);

        Assert.Equal("lmdb", appliedShardingConfig.GetString("distributed-data.durable.lmdb.dir"));
        Assert.Equal(1024 * 1024, appliedShardingConfig.GetLong("distributed-data.durable.lmdb.map-size"));
        
        #endregion
    }

    #region Helper methods

    // This is how ShardSettings is created in Akka.Cluster.Hosting
    // https://github.com/akkadotnet/Akka.Hosting/blob/2f63b5d14b1664003f166a3f30a913dac1428104/src/Akka.Cluster.Hosting/AkkaClusterHostingExtensions.cs#L977-L982
    private static (Config, ClusterShardingSettings) GetClusterShardingSettings(ShardOptions shardOptions, ActorSystem system)
    {
        var shardingConfig = ConfigurationFactory.ParseString(shardOptions.ToString())
            .WithFallback(system.Settings.Config.GetConfig("akka.cluster.sharding"));
        var coordinatorConfig = system.Settings.Config.GetConfig(
            shardingConfig.GetString("coordinator-singleton"));
                
        return (shardingConfig, ClusterShardingSettings.Create(shardingConfig, coordinatorConfig));
    }
    
    // Copied from Akka core code
    // https://github.com/akkadotnet/akka.net/blob/4ae47927da9f2539742c336acfa8ae0037fabbb7/src/contrib/cluster/Akka.Cluster.Sharding/ClusterShardingSettings.cs#L404-L407
    private static LeaseUsageSettings? GetLeaseUsageSettings(Config config)
    {
        LeaseUsageSettings? lease = null;
        var leaseConfigPath = config.GetString("use-lease");
        if (!string.IsNullOrEmpty(leaseConfigPath))
            lease = new LeaseUsageSettings(leaseConfigPath, config.GetTimeSpan("lease-retry-interval"));

        return lease;
    }
    
    // Copied from Akka core code
    // https://github.com/akkadotnet/akka.net/blob/4ae47927da9f2539742c336acfa8ae0037fabbb7/src/contrib/cluster/Akka.Cluster.Sharding/ClusterShardingSettings.cs#L422-L427
    private static int ConfigMajorityPlus(Config config, string p)
    {
        if (config.GetString(p)?.ToLowerInvariant() == "all")
            return int.MaxValue;
        return config.GetInt(p);
    }
    
    // Copied from Akka core code
    // This is how sharding replicator settings is populated in core
    // https://github.com/akkadotnet/akka.net/blob/4ae47927da9f2539742c336acfa8ae0037fabbb7/src/contrib/cluster/Akka.Cluster.Sharding/ClusterShardingGuardian.cs#L300-L310
    private static ReplicatorSettings GetReplicatorSettings(ClusterShardingSettings shardingSettings, ActorSystem system)
    {
        var config = system.Settings.Config.GetConfig("akka.cluster.sharding.distributed-data")
            .WithFallback(system.Settings.Config.GetConfig("akka.cluster.distributed-data"));
        var configuredSettings = ReplicatorSettings.Create(config);
        var settingsWithRoles = configuredSettings.WithRole(shardingSettings.Role);
        if (shardingSettings is { RememberEntities: true, RememberEntitiesStore: RememberEntitiesStore.DData })
            return settingsWithRoles; // only enable durable keys when using DData for remember-entities
        else
            return settingsWithRoles.WithDurableKeys(ImmutableHashSet<string>.Empty);
    }

    #endregion
}
