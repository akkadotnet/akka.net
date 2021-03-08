//-----------------------------------------------------------------------
// <copyright file="ClusterShardingLeavingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingLeavingSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }

        public ClusterShardingLeavingSpecConfig(StateStoreMode mode)
            : base(mode: mode, loglevel: "DEBUG", additionalConfig: @"
            akka.cluster.sharding.verbose-debug-logging = on
            akka.cluster.sharding.rebalance-interval = 1s # make rebalancing more likely to happen to test for https://github.com/akka/akka/issues/29093
            akka.cluster.sharding.distributed-data.majority-min-cap = 1
            akka.cluster.sharding.coordinator-state.write-majority-plus = 1
            akka.cluster.sharding.coordinator-state.read-majority-plus = 1
            ")
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");
        }
    }

    public class PersistentClusterShardingLeavingSpecConfig : ClusterShardingLeavingSpecConfig
    {
        public PersistentClusterShardingLeavingSpecConfig()
            : base(StateStoreMode.Persistence)
        {
        }
    }

    public class DDataClusterShardingLeavingSpecConfig : ClusterShardingLeavingSpecConfig
    {
        public DDataClusterShardingLeavingSpecConfig()
            : base(StateStoreMode.DData)
        {
        }
    }

    public class PersistentClusterShardingLeavingSpec : ClusterShardingLeavingSpec
    {
        public PersistentClusterShardingLeavingSpec()
            : base(new PersistentClusterShardingLeavingSpecConfig(), typeof(PersistentClusterShardingLeavingSpec))
        {
        }
    }

    public class DDataClusterShardingLeavingSpec : ClusterShardingLeavingSpec
    {
        public DDataClusterShardingLeavingSpec()
            : base(new DDataClusterShardingLeavingSpecConfig(), typeof(DDataClusterShardingLeavingSpec))
        {
        }
    }

    public abstract class ClusterShardingLeavingSpec : MultiNodeClusterShardingSpec<ClusterShardingLeavingSpecConfig>
    {
        #region setup

        [Serializable]
        internal sealed class Ping
        {
            public readonly string Id;

            public Ping(string id)
            {
                Id = id;
            }
        }

        [Serializable]
        internal sealed class GetLocations
        {
            public static readonly GetLocations Instance = new GetLocations();

            private GetLocations()
            {
            }
        }

        [Serializable]
        internal sealed class Locations
        {
            public readonly IImmutableDictionary<string, IActorRef> LocationMap;
            public Locations(IImmutableDictionary<string, IActorRef> locationMap)
            {
                LocationMap = locationMap;
            }
        }

        internal class Entity : ReceiveActor
        {
            public Entity()
            {
                Receive<Ping>(_ => Sender.Tell(Self));
            }
        }

        internal class ShardLocations : ReceiveActor
        {
            private Locations _locations = null;

            public ShardLocations()
            {
                Receive<GetLocations>(_ => Sender.Tell(_locations));
                Receive<Locations>(l => _locations = l);
            }
        }

        private ExtractEntityId extractEntityId = message =>
        {
            switch (message)
            {
                case Ping msg:
                    return (msg.Id, message);
            }
            return Option<(string, object)>.None;
        };

        private ExtractShardId extractShardId = message =>
        {
            switch (message)
            {
                case Ping msg:
                    return msg.Id[0].ToString();
            }
            return null;
        };

        private readonly Lazy<IActorRef> _region;

        protected ClusterShardingLeavingSpec(ClusterShardingLeavingSpecConfig config, Type type)
            : base(config, type)
        {
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
        }

        private void StartSharding()
        {
            StartSharding(
                Sys,
                typeName: "Entity",
                entityProps: Props.Create(() => new Entity()),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId);
        }

        #endregion

        [MultiNodeFact]
        public void ClusterSharding_with_leaving_member_specs()
        {
            Cluster_sharding_with_leaving_member_must_join_cluster();
            Cluster_sharding_with_leaving_member_must_initialize_shards();
            Cluster_sharding_with_leaving_member_must_recover_after_leaving_coordinator_node();
        }

        private void Cluster_sharding_with_leaving_member_must_join_cluster()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                StartPersistenceIfNeeded(startOn: config.First, Roles.ToArray());

                Join(config.First, config.First, onJoinedRunOnFrom: StartSharding);
                Join(config.Second, config.First, onJoinedRunOnFrom: StartSharding, assertNodeUp: false);
                Join(config.Third, config.First, onJoinedRunOnFrom: StartSharding, assertNodeUp: false);
                Join(config.Fourth, config.First, onJoinedRunOnFrom: StartSharding, assertNodeUp: false);
                Join(config.Fifth, config.First, onJoinedRunOnFrom: StartSharding, assertNodeUp: false);

                // all Up, everywhere before continuing
                AwaitAssert(() =>
                {
                    Cluster.State.Members.Count.Should().Be(Roles.Count);
                    Cluster.State.Members.Should().OnlyContain(m => m.Status == MemberStatus.Up);
                });

                EnterBarrier("after-2");
            });
        }

        private void Cluster_sharding_with_leaving_member_must_initialize_shards()
        {
            RunOn(() =>
            {
                var shardLocations = Sys.ActorOf(Props.Create<ShardLocations>(), "shardLocations");
                var locations = Enumerable.Range(1, 10).Select(n =>
                {
                    var id = n.ToString();
                    _region.Value.Tell(new Ping(id));
                    return new KeyValuePair<string, IActorRef>(id, ExpectMsg<IActorRef>());
                }).ToImmutableDictionary();

                shardLocations.Tell(new Locations(locations));
                Sys.Log.Debug("Original locations: [{0}]", string.Join(", ", locations.Select(i => $"{i.Key}: {i.Value}")));
            }, config.First);
            EnterBarrier("after-3");
        }

        private void Cluster_sharding_with_leaving_member_must_recover_after_leaving_coordinator_node()
        {
            Sys.ActorSelection(Node(config.First) / "user" / "shardLocations").Tell(GetLocations.Instance);
            var originalLocations = ExpectMsg<Locations>().LocationMap;

            var numberOfNodesLeaving = 2;
            var leavingRoles = Roles.Take(numberOfNodesLeaving).ToArray();
            var leavingNodes = leavingRoles.Select(r => GetAddress(r)).ToImmutableHashSet();
            var remainingRoles = Roles.Skip(numberOfNodesLeaving).ToArray();

            RunOn(() =>
            {
                foreach (var a in leavingNodes)
                    Cluster.Leave(a);
            }, Roles.Last());

            RunOn(() =>
            {
                Watch(_region.Value);
                ExpectTerminated(_region.Value, TimeSpan.FromSeconds(15));
            }, leavingRoles);
            // more stress by not having the barrier here

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(15), () =>
                {
                    AwaitAssert(() =>
                    {
                        //var region = _region.Value;
                        var probe = CreateTestProbe();
                        foreach (var kv in originalLocations)
                        {
                            var id = kv.Key;
                            var r = kv.Value;
                            _region.Value.Tell(new Ping(id), probe.Ref);

                            if (leavingNodes.Contains(r.Path.Address))
                            {
                                var newRef = probe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(1));
                                newRef.Should().NotBe(r);
                                Sys.Log.Debug("Moved [{0}] from [{1}] to [{2}]", id, r, newRef);
                            }
                            else
                                probe.ExpectMsg(r, TimeSpan.FromSeconds(1)); // should not move
                        }
                    });
                });
            }, remainingRoles);
            EnterBarrier("after-4");
        }
    }
}
