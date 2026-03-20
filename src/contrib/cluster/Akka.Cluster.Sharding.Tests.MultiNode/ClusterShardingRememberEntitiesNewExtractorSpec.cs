//-----------------------------------------------------------------------
// <copyright file="ClusterShardingRememberEntitiesNewExtractorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests;

public class ClusterShardingRememberEntitiesNewExtractorSpecConfig : MultiNodeClusterShardingConfig
{
    public RoleName First { get; }
    public RoleName Second { get; }
    public RoleName Third { get; }

    public ClusterShardingRememberEntitiesNewExtractorSpecConfig(StateStoreMode mode)
        : base(mode: mode, loglevel: "DEBUG")
    {
        First = Role("first");
        Second = Role("second");
        Third = Role("third");

        var roleConfig = ConfigurationFactory.ParseString(@"akka.cluster.roles = [sharding]");

        // we pretend node 4 and 5 are new incarnations of node 2 and 3 as they never run in parallel
        // so we can use the same lmdb store for them and have node 4 pick up the persisted data of node 2
        var ddataNodeAConfig = ConfigurationFactory.ParseString(@"
                akka.cluster.sharding.distributed-data.durable.lmdb {
                    dir = ""target/ShardingRememberEntitiesNewExtractorSpec/sharding-node-a""
                }
                ");
        var ddataNodeBConfig = ConfigurationFactory.ParseString(@"
                akka.cluster.sharding.distributed-data.durable.lmdb {
                dir = ""target/ShardingRememberEntitiesNewExtractorSpec/sharding-node-b""
                }
                ");

        NodeConfig(new[] { Second }, new[] { roleConfig.WithFallback(ddataNodeAConfig) });
        NodeConfig(new[] { Third }, new[] { roleConfig.WithFallback(ddataNodeBConfig) });
    }
}

public class PersistentClusterShardingRememberEntitiesSpecNewExtractorConfig : ClusterShardingRememberEntitiesNewExtractorSpecConfig
{
    public PersistentClusterShardingRememberEntitiesSpecNewExtractorConfig()
        : base(StateStoreMode.Persistence)
    {
    }
}

public class DDataClusterShardingRememberEntitiesNewExtractorSpecConfig : ClusterShardingRememberEntitiesNewExtractorSpecConfig
{
    public DDataClusterShardingRememberEntitiesNewExtractorSpecConfig()
        : base(StateStoreMode.DData)
    {
    }
}

public class PersistentClusterShardingRememberEntitiesNewExtractorSpec : ClusterShardingRememberEntitiesNewExtractorSpec
{
    public PersistentClusterShardingRememberEntitiesNewExtractorSpec()
        : base(new PersistentClusterShardingRememberEntitiesSpecNewExtractorConfig(), typeof(PersistentClusterShardingRememberEntitiesNewExtractorSpec))
    {
    }
}

public class DDataClusterShardingRememberEntitiesNewExtractorSpec : ClusterShardingRememberEntitiesNewExtractorSpec
{
    public DDataClusterShardingRememberEntitiesNewExtractorSpec()
        : base(new DDataClusterShardingRememberEntitiesNewExtractorSpecConfig(), typeof(DDataClusterShardingRememberEntitiesNewExtractorSpec))
    {
    }
}

public abstract class ClusterShardingRememberEntitiesNewExtractorSpec : MultiNodeClusterShardingSpec<ClusterShardingRememberEntitiesNewExtractorSpecConfig>
{
    #region setup

    [Serializable]
    internal sealed record Started(IActorRef Ref);

    internal class TestEntity : ActorBase
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public TestEntity(IActorRef probe)
        {
            _log.Info("Entity started: " + Self.Path);
            probe?.Tell(new Started(Self));
        }

        protected override bool Receive(object message)
        {
            Sender.Tell(message);
            return true;
        }
    }

    private sealed class MessageExtractor1: IMessageExtractor
    {
        public string EntityId(object message)
            => message switch
            {
                int id => id.ToString(),
                _ => null
            };

        public object EntityMessage(object message)
            => message;

        public string ShardId(object message)
            => message switch
            {
                int id => (id % ShardCount).ToString(),
                _ => null
            };

        public string ShardId(string entityId, object messageHint = null)
            => (int.Parse(entityId) % ShardCount).ToString();
    }
        
    private sealed class MessageExtractor2: IMessageExtractor
    {
        public string EntityId(object message)
            => message switch
            {
                int id => id.ToString(),
                _ => null
            };

        public object EntityMessage(object message)
            => message;

        public string ShardId(object message)
            => message switch
            {
                int id => ((id + 1) % ShardCount).ToString(),
                _ => null
            };

        public string ShardId(string entityId, object messageHint = null)
            => ((int.Parse(entityId) + 1) % ShardCount).ToString();
    }

    private const int ShardCount = 3;
    private const string TypeName = "Entity";

    protected ClusterShardingRememberEntitiesNewExtractorSpec(ClusterShardingRememberEntitiesNewExtractorSpecConfig config, Type type)
        : base(config, type)
    {
    }

    private void StartShardingWithExtractor1()
    {
        StartSharding(
            Sys,
            typeName: TypeName,
            entityProps: Props.Create(() => new TestEntity(null)),
            settings: Settings.Value.WithRole("sharding"),
            messageExtractor: new MessageExtractor1());
    }

    private void StartShardingWithExtractor2(ActorSystem sys, IActorRef probe)
    {
        StartSharding(
            sys,
            typeName: TypeName,
            entityProps: Props.Create(() => new TestEntity(probe)),
            settings: ClusterShardingSettings.Create(sys).WithRememberEntities(Config.RememberEntities).WithRole("sharding"),
            messageExtractor: new MessageExtractor2());
    }

    private IActorRef Region(ActorSystem sys = null)
    {
        return ClusterSharding.Get(sys ?? Sys).ShardRegion(TypeName);
    }


    #endregion

    [MultiNodeFact]
    public async Task Cluster_sharding_with_remember_entities_specs()
    {
        await Cluster_with_min_nr_of_members_using_sharding_must_start_up_first_cluster_and_sharding();
        await Cluster_with_min_nr_of_members_using_sharding_must_shutdown_sharding_nodes();
        await Cluster_with_min_nr_of_members_using_sharding_must_start_new_nodes_with_different_extractor_and_have_the_entities_running_on_the_right_shards();
    }

    private async Task Cluster_with_min_nr_of_members_using_sharding_must_start_up_first_cluster_and_sharding()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            StartPersistenceIfNeeded(startOn: Config.First, Config.Second, Config.Third);

            await JoinAsync(Config.First, Config.First);
            await JoinAsync(Config.Second, Config.First);
            await JoinAsync(Config.Third, Config.First);

            RunOn(() =>
            {
                Within(Remaining, () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3);
                    });
                });
            }, Config.First, Config.Second, Config.Third);

            RunOn(() =>
            {
                StartShardingWithExtractor1();
            }, Config.Second, Config.Third);
            await EnterBarrierAsync("first-cluster-up");

            await RunOnAsync(async () =>
            {
                // one entity for each shard id
                foreach (var n in Enumerable.Range(1, 10))
                {
                    Region().Tell(n);
                    await ExpectMsgAsync(n);
                }
            }, Config.Second, Config.Third);
            await EnterBarrierAsync("first-cluster-entities-up");
        });
    }

    private async Task Cluster_with_min_nr_of_members_using_sharding_must_shutdown_sharding_nodes()
    {
        await WithinAsync(TimeSpan.FromSeconds(30), async () =>
        {
            await RunOnAsync(async () =>
            {
                await TestConductor.ExitAsync(Config.Second, 0);
                await TestConductor.ExitAsync(Config.Third, 0);
            }, Config.First);

            RunOn(() =>
            {
                Within(Remaining, () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(1);
                    });
                });
            }, Config.First);

        });
        await EnterBarrierAsync("first-sharding-cluster-stopped");
    }

    private async Task Cluster_with_min_nr_of_members_using_sharding_must_start_new_nodes_with_different_extractor_and_have_the_entities_running_on_the_right_shards()
    {
        await WithinAsync(TimeSpan.FromSeconds(30), async () =>
        {
            // start it with a new shard id messageExtractor, which will put the entities
            // on different shards

            await RunOnAsync(async () =>
            {
                await WatchAsync(Region());
                Cluster.Get(Sys).Leave(Cluster.Get(Sys).SelfAddress);
                await ExpectTerminatedAsync(Region());
                AwaitAssert(() =>
                {
                    Cluster.Get(Sys).IsTerminated.Should().BeTrue();
                });

            }, Config.Second, Config.Third);
            await EnterBarrierAsync("first-cluster-terminated");

            // no sharding nodes left of the original cluster, start a new nodes
            await RunOnAsync(async () =>
            {
                var sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
                var probe2 = CreateTestProbe(sys2);

                if (PersistenceIsNeeded)
                {
                    await SetStoreAsync(sys2, storeOn: Config.First);

                    ////Persistence.Persistence.Instance.Apply(sys2);
                    //sys2.ActorSelection(Node(_config.First) / "system" / "akka.persistence.journal.sqlite").Tell(new Identify(null), probe2.Ref);
                    //var sharedStore = probe2.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(10)).Subject;
                    //sharedStore.Should().NotBeNull();
                    //SqliteJournalShared.SetStore(sharedStore, sys2);
                }

                Cluster.Get(sys2).Join((await NodeAsync(Config.First)).Address);
                StartShardingWithExtractor2(sys2, probe2.Ref);
                await probe2.ExpectMsgAsync<Started>(TimeSpan.FromSeconds(20));

                CurrentShardRegionState stats = null;
                await WithinAsync(TimeSpan.FromSeconds(10), async() =>
                {
                    await AwaitAssertAsync(async () =>
                    {
                        Region(sys2).Tell(GetShardRegionState.Instance);
                        var reply = await ExpectMsgAsync<CurrentShardRegionState>();
                        reply.Shards.Should().NotBeEmpty();
                        stats = reply;
                    });
                });

                var extractor = new MessageExtractor2();
                foreach (var shardState in stats.Shards)
                {
                    foreach (var entityId in shardState.EntityIds)
                    {
                        var calculatedShardId = extractor.ShardId(entityId);
                        calculatedShardId.Should().BeEquivalentTo(shardState.ShardId);
                    }
                }

                await EnterBarrierAsync("verified");
                Shutdown(sys2);
            }, Config.Second, Config.Third);

            await RunOnAsync(async () =>
            {
                await EnterBarrierAsync("verified");
            }, Config.First);

            await EnterBarrierAsync("done");
        });
    }
}