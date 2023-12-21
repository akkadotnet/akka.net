//-----------------------------------------------------------------------
// <copyright file="ClusterShardingLeavingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit.Sdk;

namespace Akka.Cluster.Sharding.Tests
{
    public static class Bug6973Common
    {
        public const int NodeCount = 3;
        public const int ShardCount = 50;
        public const int EntityCount = ShardCount * 500;
        public const string ShardRoleName = "shard";
        public const string ProxyRoleName = "proxy";
        public const string TypeName = "Entity";
    }
    
    public class ClusterShardingDuplicateShardBugSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName Proxy { get; }
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        
        public RoleName[] RolesWithShard { get; }

        public ClusterShardingDuplicateShardBugSpecConfig(StateStoreMode mode)
            : base(
                mode: mode,
                loglevel: "DEBUG",
                rememberEntitiesStore: RememberEntitiesStore.Eventsourced,
                additionalConfig: $$"""
akka.cluster { 
    auto-down-unreachable-after = off
    run-coordinated-shutdown-when-down = on
    sharding {
        verbose-debug-logging = on
        rebalance-interval = 1s # make rebalancing more likely to happen to test for https://github.com/akka/akka/issues/29093
        distributed-data.majority-min-cap = 1
        coordinator-state.write-majority-plus = 1
        coordinator-state.read-majority-plus = 1
        role = {{Bug6973Common.ShardRoleName}}
    }
}
""")
        {
            Proxy = Role("proxy");
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            RolesWithShard = new[] { First, Second, Third, Fourth };
            
            NodeConfig(new[] { First, Second, Third, Fourth }, new Config[] {
                $"""akka.cluster.roles=["{Bug6973Common.ShardRoleName}"]"""
            });
            NodeConfig(new[] { Proxy }, new Config[] {
                $"""akka.cluster.roles=["{Bug6973Common.ProxyRoleName}"]"""
            });
        }
    }

    public class PersistentClusterShardingDuplicateShardBugSpecConfig : ClusterShardingDuplicateShardBugSpecConfig
    {
        public PersistentClusterShardingDuplicateShardBugSpecConfig()
            : base(StateStoreMode.Persistence)
        {
        }
    }

    public class DDataClusterShardingDuplicateShardBugSpecConfig : ClusterShardingDuplicateShardBugSpecConfig
    {
        public DDataClusterShardingDuplicateShardBugSpecConfig()
            : base(StateStoreMode.DData)
        {
        }
    }

    public class PersistentClusterShardingDuplicateShardBugSpec : ClusterShardingDuplicateShardBugSpec
    {
        public PersistentClusterShardingDuplicateShardBugSpec()
            : base(new PersistentClusterShardingDuplicateShardBugSpecConfig(), typeof(PersistentClusterShardingDuplicateShardBugSpec))
        {
        }
    }

    public class DDataClusterShardingDuplicateShardBugSpec : ClusterShardingDuplicateShardBugSpec
    {
        public DDataClusterShardingDuplicateShardBugSpec()
            : base(new DDataClusterShardingDuplicateShardBugSpecConfig(), typeof(DDataClusterShardingDuplicateShardBugSpec))
        {
        }
    }

    public abstract class ClusterShardingDuplicateShardBugSpec : MultiNodeClusterShardingSpec<ClusterShardingDuplicateShardBugSpecConfig>
    {
        #region setup
        
        #region Entity

        internal interface IWithId
        {
            public int Id { get; }
        }
        
        [Serializable]
        internal sealed class GetLocation: IWithId
        {
            public int Id { get; }

            public GetLocation(int id)
            {
                Id = id;
            }
        }
        
        [Serializable]
        internal sealed class Ping: IWithId
        {
            public int Id { get; }

            public Ping(int id)
            {
                Id = id;
            }
        }
        
        [Serializable]
        internal sealed class Pong: IWithId
        {
            public int Id { get; }

            public Pong(int id)
            {
                Id = id;
            }
        }
        
        [Serializable]
        internal sealed record Location(int Id, IActorRef ActorRef);

        internal class Entity : ReceiveActor
        {
            private readonly int _id;
            public Entity(string id)
            {
                _id = int.Parse(id);
                Receive<Ping>(_ => Sender.Tell(new Pong(_id)));
                Receive<GetLocation>(_ => Sender.Tell(new Location(_id, Self)));
            }
        }

        #endregion
        
        #region State management
        
        internal sealed class Start
        {
            public static readonly Start Instance = new();
            private Start() { }
        }
        
        internal sealed class Stop
        {
            public static readonly Stop Instance = new();
            private Stop() { }
        }
        
        internal sealed class Spawn
        {
            public static readonly Spawn Instance = new();
            private Spawn() { }
        }
        
        [Serializable]
        internal sealed class GetShardStats
        {
            public static readonly GetShardStats Instance = new();
            private GetShardStats() { }
        }
        
        [Serializable]
        internal sealed record ShardStats(IImmutableDictionary<string, ShardRegionStats> ShardRegionStats);

        [Serializable]
        internal sealed record ShardStat(string NodeName, ShardRegionStats Stats);
        
        internal class ShardLocations : ReceiveActor
        {
            private readonly Dictionary<string, ShardRegionStats> _stats = new ();
            private readonly IActorRef _proxy;
            private CancellationTokenSource _stopCts;
            private Task _spamTask;
            private ILoggingAdapter _log;
            
            public ShardLocations()
            {
                _proxy = ClusterSharding.Get(Context.System).ShardRegionProxy(Bug6973Common.TypeName);
                _log = Context.GetLogger();
                
                Receive<GetShardStats>(_ => Sender.Tell(new ShardStats(_stats.ToImmutableDictionary())));
                Receive<ShardStat>(l => _stats[l.NodeName] = l.Stats);
                ReceiveAsync<Spawn>(async _ =>
                {
                    var sender = Sender;
                    
                    var tasks = Enumerable.Range(0, Bug6973Common.EntityCount)
                        .Select(n => _proxy.Ask<Location>(new GetLocation(n)));
                    var locations = (await Task.WhenAll(tasks)).ToDictionary(l => l.Id, l => l.ActorRef);
                    sender.Tell(locations.Keys.ToArray());
                });
                Receive<Start>(_ =>
                {
                    if (_stopCts is not null)
                        return;
                    _stopCts = new CancellationTokenSource();
                    _spamTask = Task.Run(async () =>
                    {
                        while (!_stopCts.IsCancellationRequested)
                        {
                            try
                            {
                                foreach (var id in Enumerable.Range(0, Bug6973Common.EntityCount))
                                {
                                    if (_stopCts.IsCancellationRequested)
                                        break;
                                    _proxy.Tell(new Ping(id));
                                    await Task.Delay(50.Milliseconds(), _stopCts.Token);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // no-op
                            }
                        }
                    });
                });
                ReceiveAsync<Stop>(async _ =>
                {
                    if(_stopCts is null)
                        return;
                    _stopCts.Cancel();
                    if(_spamTask is not null)
                        await _spamTask;
                });
            }

            protected override void PostStop()
            {
                _stopCts?.Dispose();
            }
        }
        
        #endregion

        private readonly ExtractEntityId _extractEntityId = message =>
            message switch
            {
                IWithId msg => (msg.Id.ToString(), message),
                _ => Option<(string, object)>.None
            };

        private readonly ExtractShardId _extractShardId = message =>
            message switch
            {
                IWithId msg => (msg.Id % Bug6973Common.ShardCount).ToString(),
                _ => null
            };

        protected ClusterShardingDuplicateShardBugSpec(ClusterShardingDuplicateShardBugSpecConfig config, Type type)
            : base(config, type)
        {
        }

        private void StartSharding()
        {
            StartSharding(
                typeName: Bug6973Common.TypeName,
                entityPropsFactory: id => Props.Create(() => new Entity(id)),
                extractEntityId: _extractEntityId,
                extractShardId: _extractShardId);
        }

        protected IActorRef StartSharding(
            string typeName,
            Func<string, Props> entityPropsFactory,
            ClusterShardingSettings shardingSettings = null,
            ExtractEntityId extractEntityId = null,
            ExtractShardId extractShardId = null,
            IShardAllocationStrategy allocationStrategy = null,
            object handOffStopMessage = null)
        {
            var sharding = ClusterSharding.Get(Sys);
            return sharding.Start(
                typeName,
                entityPropsFactory,
                shardingSettings ?? settings.Value,
                extractEntityId ?? IntExtractEntityId,
                extractShardId ?? IntExtractShardId,
                allocationStrategy ?? sharding.DefaultShardAllocationStrategy(settings.Value),
                handOffStopMessage ?? PoisonPill.Instance);
        }
        
        private void StartProxy()
        {
            StartProxy(
                sys: Sys,
                typeName: Bug6973Common.TypeName,
                role: Bug6973Common.ShardRoleName, 
                extractShardId: _extractShardId, 
                extractEntityId: _extractEntityId);
        }
        
        private void Join(
            RoleName[] nodes,
            RoleName to,
            Action onJoinedRunOnFrom = null,
            bool assertNodeUp = true,
            TimeSpan? max = null)
        {
            foreach (var from in nodes)
            {
                RunOn(() =>
                {
                    Cluster.Join(Node(to).Address);
                    if (assertNodeUp)
                    {
                        Within(max ?? TimeSpan.FromSeconds(20), () =>
                        {
                            AwaitAssert(() =>
                            {
                                Cluster.State.IsMemberUp(Node(from).Address).Should().BeTrue();
                            });
                        });
                    }
                    onJoinedRunOnFrom?.Invoke();
                }, from);
            }
            EnterBarrier($"[{string.Join(", ", nodes.Select(n => n.Name))}]-joined");
        }
        #endregion

        [MultiNodeFact]
        public async Task BugBug6973Spec()
        {
            BugBug6973Spec_must_join_cluster();
            BugBug6973Spec_must_initialize_shards();
            await BugBug6973Spec_must_survive_coordinator_shutdown();
            BugBug6973Spec_must_not_generate_duplicate_shards();
        }

        private void BugBug6973Spec_must_join_cluster()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                StartPersistenceIfNeeded(startOn: config.First, config.RolesWithShard);
                
                Join(config.First, config.First, onJoinedRunOnFrom: StartSharding);
                Join(config.Proxy, config.First, onJoinedRunOnFrom: StartProxy);
                
                RunOn(() =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.State.Members.Count.Should().Be(2);
                        Cluster.State.Members.Should().OnlyContain(m => m.Status == MemberStatus.Up);
                    });
                }, config.First);
                
                RunOn(() =>
                {
                    AwaitAssert(() =>
                    {
                        ClusterSharding.Get(Sys).ShardRegion(Bug6973Common.TypeName).Tell(GetCurrentRegions.Instance);
                        ExpectMsg<CurrentRegions>().Regions.Count.Should().Be(1);
                    });
                }, config.First);
                
                EnterBarrier("after-2");
            });
        }
        
        private void BugBug6973Spec_must_initialize_shards()
        {
            RunOn( () =>
            {
                var shardLocations = Sys.ActorOf(Props.Create<ShardLocations>(), "shardLocations");
                shardLocations.Tell(Spawn.Instance);
                var entityIds = ExpectMsg<int[]>(1.Minutes());
                entityIds.Should().BeEquivalentTo(Enumerable.Range(0, Bug6973Common.EntityCount));
            }, config.Proxy);
            
            EnterBarrier("after-3");
        }

        private async Task BugBug6973Spec_must_survive_coordinator_shutdown()
        {
            RunOn(() =>
            {
                Sys.ActorSelection(Node(config.Proxy) / "user" / "shardLocations").Tell(Start.Instance);
            });
            EnterBarrier("start-spam");
            
            Join(new[]{config.Second, config.Third, config.Fourth}, config.First, onJoinedRunOnFrom: StartSharding);
            await RunOnAsync(() => Cluster.LeaveAsync(), config.First);
            
            EnterBarrier("after-4");
        }
        
        private void BugBug6973Spec_must_not_generate_duplicate_shards()
        {
            RunOn(() =>
            {
                var region = ClusterSharding.Get(Sys).ShardRegion(Bug6973Common.TypeName);
                region.Tell(GetShardRegionStats.Instance);
                var stats = ExpectMsg<ShardRegionStats>();
                Sys.ActorSelection(Node(config.Proxy) / "user" / "shardLocations").Tell(new ShardStat(nameof(config.Second), stats));
            }, config.Second);
            
            RunOn(() =>
            {
                var region = ClusterSharding.Get(Sys).ShardRegion(Bug6973Common.TypeName);
                region.Tell(GetShardRegionStats.Instance);
                var stats = ExpectMsg<ShardRegionStats>();
                Sys.ActorSelection(Node(config.Proxy) / "user" / "shardLocations").Tell(new ShardStat(nameof(config.Third), stats));
            }, config.Third);
            
            RunOn(() =>
            {
                var region = ClusterSharding.Get(Sys).ShardRegion(Bug6973Common.TypeName);
                region.Tell(GetShardRegionStats.Instance);
                var stats = ExpectMsg<ShardRegionStats>();
                Sys.ActorSelection(Node(config.Proxy) / "user" / "shardLocations").Tell(new ShardStat(nameof(config.Fourth), stats));
            }, config.Fourth);
            
            EnterBarrier("after-5");
            
            RunOn(() =>
            {
                var actor = Sys.ActorSelection(Node(config.Proxy) / "user" / "shardLocations");
                actor.Tell(Stop.Instance);
                actor.Tell(GetShardStats.Instance);
                var stats = ExpectMsg<ShardStats>();
                var duplicates = stats.ShardRegionStats.Values.SelectMany(s => s.Stats)
                    .GroupBy(s => s.Key)
                    .Where(g => g.Skip(1).Any()).ToArray();
                if (duplicates.Length > 0)
                {
                    throw new XunitException($"Duplicate shard detected on shard(s) [{string.Join(", ", duplicates.Select(d => d.Key))}]");
                }
            }, config.Proxy);
            
            EnterBarrier("after-6");
        }
    }
}
