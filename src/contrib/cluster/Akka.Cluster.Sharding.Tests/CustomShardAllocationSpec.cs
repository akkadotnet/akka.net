//-----------------------------------------------------------------------
// <copyright file="CustomShardAllocationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tests.MultiNode;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class CustomShardAllocationSpecConfig : MultiNodeConfig
    {

        public CustomShardAllocationSpecConfig()
        {
            var first = Role("first");
            var second = Role("second");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""akka.cluster.ClusterActorRefProvider""
                akka.remote.log-remote-lifecycle-events = off
                akka.persistence.journal.plugin = ""akka.persistence.journal.in-mem""
                akka.persistence.journal.in-mem {
                  timeout = 5s
                  store {
                    native = off
                    dir = ""target/journal-ClusterShardingCustomShardAllocationSpec""
                  }
                }
                akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.local""
                akka.persistence.snapshot-store.local.dir = ""target/snapshots-ClusterShardingCustomShardAllocationSpec""");
        }
    }

    public class CustomShardAllocationSpecNode1 : CustomShardAllocationSpec { }
    public class CustomShardAllocationSpecNode2 : CustomShardAllocationSpec { }

    public abstract class CustomShardAllocationSpec : MultiNodeClusterSpec
    {
        #region messages

        sealed class AllocateReq
        {
            public static readonly AllocateReq Instance = new AllocateReq();
            private AllocateReq() { }
        }

        sealed class UseRegion
        {
            public readonly IActorRef Region;

            public UseRegion(IActorRef region)
            {
                Region = region;
            }
        }

        sealed class UseRegionAck
        {
            public static readonly UseRegionAck Instance = new UseRegionAck();
            private UseRegionAck() { }
        }

        sealed class RebalanceReq
        {
            public static readonly RebalanceReq Instance = new RebalanceReq();
            private RebalanceReq() { }
        }

        sealed class RebalanceShards
        {
            public readonly string[] Shards;

            public RebalanceShards(string[] shards)
            {
                Shards = shards;
            }
        }

        public sealed class RebalanceShardsAck
        {
            public static readonly RebalanceShardsAck Instance = new RebalanceShardsAck();
            private RebalanceShardsAck() { }
        }

        #endregion

        #region actors

        class Entity : ReceiveActor
        {
            public Entity()
            {
                Receive<int>(id => Sender.Tell(id));
            }
        }

        class Allocator : ReceiveActor
        {
            private IActorRef _useRegion = null;
            private ISet<string> _rebalance = new HashSet<string>();

            public Allocator()
            {
                Receive<UseRegion>(m =>
                {
                    _useRegion = m.Region;
                    Sender.Tell(UseRegionAck.Instance);
                });
                Receive<AllocateReq>(_ =>
                {
                    if (_useRegion != null)
                    {
                        Sender.Tell(_useRegion);
                    }
                });
                Receive<RebalanceShards>(m =>
                {
                    _rebalance = new HashSet<string>(m.Shards);
                    Sender.Tell(RebalanceShardsAck.Instance);
                });
                Receive<RebalanceReq>(_ =>
                {
                    Sender.Tell(_rebalance);
                    _rebalance = new HashSet<string>();
                });
            }
        }

        class TestAllocationStrategy : IShardAllocationStrategy
        {
            private readonly IActorRef _ref;
            private readonly TimeSpan _timeout = TimeSpan.FromSeconds(3);

            public TestAllocationStrategy(IActorRef @ref)
            {
                _ref = @ref;
            }

            public Task<IActorRef> AllocateShard(IActorRef requester, string shardId, IDictionary<IActorRef, string[]> currentShardAllocations)
            {
                return _ref.Ask<IActorRef>(AllocateReq.Instance, _timeout);
            }

            public Task<ISet<string>> Rebalance(IDictionary<IActorRef, string[]> currentShardAllocations, ISet<string> rebalanceInProgress)
            {
                return _ref.Ask<ISet<string>>(RebalanceReq.Instance, _timeout);
            }
        }

        #endregion

        private readonly IdExtractor extractEntityId = id => Tuple.Create(id.ToString(), id);
        private readonly ShardResolver extractShardId = msg => msg.ToString();

        private readonly RoleName _first;
        private readonly RoleName _second;
        private readonly DirectoryInfo[] _storageLocations;
        private readonly Lazy<IActorRef> _region;
        private readonly Lazy<IActorRef> _allocator;

        protected CustomShardAllocationSpec() : base(new CustomShardAllocationSpecConfig())
        {
            _storageLocations = new[]
            {
                "akka.persistence.journal.leveldb.dir",
                "akka.persistence.journal.leveldb-shared.store.dir",
                "akka.persistence.snapshot-store.local.dir"
            }.Select(s => new DirectoryInfo(Sys.Settings.Config.GetString(s))).ToArray();

            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
            _allocator = new Lazy<IActorRef>(() => Sys.ActorOf(Props.Create<Allocator>(), "allocator"));

            _first = new RoleName("first");
            _second = new RoleName("second");
        }

        protected override void AtStartup()
        {
            base.AtStartup();
            RunOn(() =>
            {
                foreach (var location in _storageLocations) if (location.Exists) location.Delete();
            }, _first);
        }

        protected override void AfterTermination()
        {
            base.AfterTermination();
            RunOn(() =>
            {
                foreach (var location in _storageLocations) if (location.Exists) location.Delete();
            }, _first);
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
                StartSharding();
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        private void StartSharding()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(2, 1);
            ClusterSharding.Get(Sys).Start(
                typeName: "Entity",
                entryProps: Props.Create<ClusterShardingGracefulShutdownSpec.Entity>(),
                settings: ClusterShardingSettings.Create(Sys),
                idExtractor: extractEntityId,
                shardResolver: extractShardId,
                allocationStrategy: new TestAllocationStrategy(_allocator.Value),
                handOffStopMessage: PoisonPill.Instance);
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_with_custom_allocation_strategy_should_setup_shared_journal()
        {
            // start the Persistence extension
            Persistence.Persistence.Instance.Apply(Sys);
            RunOn(() =>
            {
                Sys.ActorOf(Props.Create<MemoryJournal>(), "store");
            }, _first);
            EnterBarrier("persistence-started");

            RunOn(() =>
            {
                Sys.ActorSelection(Node(_first) / "user" / "store").Tell(new Identify(null));
                var sharedStore = ExpectMsg<ActorIdentity>().Subject;
                //TODO: SharedLeveldbJournal.setStore(sharedStore, system)
            }, _first, _second);
            EnterBarrier("after-1");
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_with_custom_allocation_strategy_should_use_specified_region()
        {
            ClusterSharding_with_custom_allocation_strategy_should_setup_shared_journal();

            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(_first, _first);

                RunOn(() =>
                {
                    _allocator.Value.Tell(new UseRegion(_region.Value));
                    ExpectMsg<UseRegionAck>();
                    _region.Value.Tell(1);
                    ExpectMsg(1);
                    Assert.Equal(_region.Value.Path / "1" / "1", LastSender.Path);
                }, _first);
                EnterBarrier("first-started");

                Join(_second, _first);

                _region.Value.Tell(2);
                ExpectMsg(2);

                RunOn(() =>
                {
                    Assert.Equal(_region.Value.Path / "2" / "2", LastSender.Path);
                }, _first);
                RunOn(() =>
                {
                    Assert.Equal(Node(_first) / "user" / "sharding" / "Entity" / "2" / "2", LastSender.Path);
                }, _second);
                EnterBarrier("second-started");

                RunOn(() =>
                {
                    Sys.ActorSelection(Node(_second) / "user" / "sharding" / "Entity").Tell(new Identify(null));
                    var secondRegion = ExpectMsg<ActorIdentity>().Subject;
                    _allocator.Value.Tell(new UseRegion(secondRegion));
                    ExpectMsg<UseRegionAck>();
                }, _first);
                EnterBarrier("second-active");

                _region.Value.Tell(3);
                ExpectMsg(3);

                RunOn(() =>
                {
                    Assert.Equal(_region.Value.Path / "3" / "3", LastSender.Path);
                }, _second);

                RunOn(() =>
                {
                    Assert.Equal(Node(_second) / "user" / "sharding" / "Entity" / "3" / "3", LastSender.Path);
                }, _first);
                EnterBarrier("after-2");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_with_custom_allocation_strategy_should_rebalance_specified_shards()
        {
            ClusterSharding_with_custom_allocation_strategy_should_use_specified_region();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    _allocator.Value.Tell(new RebalanceShards(new[] { "2" }));
                    ExpectMsg<RebalanceShardsAck>();

                    AwaitAssert(() =>
                    {
                        var probe = CreateTestProbe();
                        _region.Value.Tell(2, probe.Ref);
                        probe.ExpectMsg(2, TimeSpan.FromSeconds(2));
                        Assert.Equal(Node(_second) / "user" / "sharding" / "Entity" / "2" / "2", LastSender.Path);
                    });

                    _region.Value.Tell(1);
                    ExpectMsg(1);
                    Assert.Equal(_region.Value.Path / "1" / "1", LastSender.Path);
                }, _first);
            });
        }
    }
}