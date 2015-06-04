using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;

namespace Akka.Cluster.Sharding.Tests
{
    public class CustomShardAllocationSpec : MultiNodeConfig
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

        private readonly IdExtractor _idExtractor = id => Tuple.Create(id.ToString(), id);
        private readonly ShardResolver _shardResolver = msg => msg.ToString();
        
        private readonly RoleName _first;
        private readonly RoleName _second;

        public CustomShardAllocationSpec()
        {
            _first = Role("first");
            _second = Role("second");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""akka.cluster.ClusterActorRefProvider""
                akka.remote.log-remote-lifecycle-events = off
                akka.persistence.journal.plugin = ""akka.persistence.journal.leveldb-shared""
                akka.persistence.journal.leveldb-shared {
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
}