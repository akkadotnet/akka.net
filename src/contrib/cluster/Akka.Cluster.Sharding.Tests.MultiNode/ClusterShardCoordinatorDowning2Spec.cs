﻿// -----------------------------------------------------------------------
//  <copyright file="ClusterShardCoordinatorDowning2Spec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using FluentAssertions;
using static Akka.Remote.Transport.ThrottleTransportAdapter;

namespace Akka.Cluster.Sharding.Tests;

public class ClusterShardCoordinatorDowning2SpecConfig : MultiNodeClusterShardingConfig
{
    public ClusterShardCoordinatorDowning2SpecConfig(StateStoreMode mode)
        : base(mode, loglevel: "DEBUG", additionalConfig: @"
                akka.cluster.sharding.rebalance-interval = 120 s
                # setting down-removal-margin, for testing of issue #29131
                akka.cluster.down-removal-margin = 3 s
                akka.remote.watch-failure-detector.acceptable-heartbeat-pause = 3s
            ")
    {
        First = Role("first");
        Second = Role("second");

        TestTransport = true;
    }

    public RoleName First { get; }
    public RoleName Second { get; }
}

public class PersistentClusterShardCoordinatorDowning2SpecConfig : ClusterShardCoordinatorDowning2SpecConfig
{
    public PersistentClusterShardCoordinatorDowning2SpecConfig()
        : base(StateStoreMode.Persistence)
    {
    }
}

public class DDataClusterShardCoordinatorDowning2SpecConfig : ClusterShardCoordinatorDowning2SpecConfig
{
    public DDataClusterShardCoordinatorDowning2SpecConfig()
        : base(StateStoreMode.DData)
    {
    }
}

public class PersistentClusterShardCoordinatorDowning2Spec : ClusterShardCoordinatorDowning2Spec
{
    public PersistentClusterShardCoordinatorDowning2Spec()
        : base(new PersistentClusterShardCoordinatorDowning2SpecConfig(),
            typeof(PersistentClusterShardCoordinatorDowning2Spec))
    {
    }
}

public class DDataClusterShardCoordinatorDowning2Spec : ClusterShardCoordinatorDowning2Spec
{
    public DDataClusterShardCoordinatorDowning2Spec()
        : base(new DDataClusterShardCoordinatorDowning2SpecConfig(), typeof(DDataClusterShardCoordinatorDowning2Spec))
    {
    }
}

public abstract class
    ClusterShardCoordinatorDowning2Spec : MultiNodeClusterShardingSpec<ClusterShardCoordinatorDowning2SpecConfig>
{
    [MultiNodeFact]
    public void Cluster_sharding_with_down_member_scenario_2_specs()
    {
        Cluster_sharding_with_down_member_scenario_2_must_join_cluster();
        Cluster_sharding_with_down_member_scenario_2_must_initialize_shards();
        Cluster_sharding_with_down_member_scenario_2_must_recover_after_downing_other_node_not_coordinator();
    }

    private void Cluster_sharding_with_down_member_scenario_2_must_join_cluster()
    {
        Within(TimeSpan.FromSeconds(20), () =>
        {
            StartPersistenceIfNeeded(Config.First, Config.First, Config.Second);

            Join(Config.First, Config.First, StartSharding);
            Join(Config.Second, Config.First, StartSharding, false);

            // all Up, everywhere before continuing
            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    Cluster.State.Members.Count.Should().Be(2);
                    Cluster.State.Members.Should().OnlyContain(m => m.Status == MemberStatus.Up);
                });
            }, Config.First, Config.Second);

            EnterBarrier("after-2");
        });
    }

    private void Cluster_sharding_with_down_member_scenario_2_must_initialize_shards()
    {
        RunOn(() =>
        {
            var shardLocations = Sys.ActorOf(Props.Create(() => new ShardLocations()), "shardLocations");
            var locations = Enumerable.Range(1, 4).Select(n =>
            {
                var id = n.ToString();
                _region.Value.Tell(new Ping(id));
                return new KeyValuePair<string, IActorRef>(id, ExpectMsg<IActorRef>());
            }).ToImmutableDictionary();
            shardLocations.Tell(new Locations(locations));
            Sys.Log.Debug("Original locations: [{0}]", string.Join(", ", locations.Select(i => $"{i.Key}: {i.Value}")));
        }, Config.First);
        EnterBarrier("after-3");
    }

    private void Cluster_sharding_with_down_member_scenario_2_must_recover_after_downing_other_node_not_coordinator()
    {
        Within(TimeSpan.FromSeconds(20), () =>
        {
            var secondAddress = GetAddress(Config.Second);

            RunOn(() => { TestConductor.Blackhole(Config.First, Config.Second, Direction.Both).Wait(); }, Config.First);

            Thread.Sleep(3000);

            RunOn(() =>
            {
                Cluster.Down(GetAddress(Config.Second));
                AwaitAssert(() => { Cluster.State.Members.Count.Should().Be(1); });

                // start a few more new shards, could be allocated to second but should notice that it's terminated
                ImmutableDictionary<string, IActorRef> additionalLocations = null;
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe();
                    additionalLocations = Enumerable.Range(5, 4).Select(n =>
                    {
                        var id = n.ToString();
                        _region.Value.Tell(new Ping(id), probe.Ref);
                        return new KeyValuePair<string, IActorRef>(id,
                            probe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(1)));
                    }).ToImmutableDictionary();
                });
                Sys.Log.Debug("Additional locations: [{0}]",
                    string.Join(", ", additionalLocations.Select(i => $"{i.Key}: {i.Value}")));

                Sys.ActorSelection(Node(Config.First) / "user" / "shardLocations").Tell(GetLocations.Instance);
                var originalLocations = ExpectMsg<Locations>().Locs;

                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe();
                    foreach (var loc in originalLocations.SetItems(additionalLocations))
                    {
                        _region.Value.Tell(new Ping(loc.Key), probe.Ref);
                        if (loc.Value.Path.Address.Equals(secondAddress))
                        {
                            var newRef = probe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(1));
                            newRef.Should().NotBe(loc.Value);
                            Sys.Log.Debug("Moved [{0}] from [{1}] to [{2}]", loc.Key, loc.Value, newRef);
                        }
                        else
                        {
                            probe.ExpectMsg(loc.Value, TimeSpan.FromSeconds(1)); // should not move
                        }
                    }
                });
            }, Config.First);
        });

        EnterBarrier("after-4");
    }

    #region setup

    internal sealed class Ping
    {
        public readonly string Id;

        public Ping(string id)
        {
            Id = id;
        }
    }

    internal class Entity : ActorBase
    {
        protected override bool Receive(object message)
        {
            if (message is Ping)
            {
                Sender.Tell(Self);
                return true;
            }

            return false;
        }
    }

    internal class GetLocations
    {
        public static readonly GetLocations Instance = new();

        private GetLocations()
        {
        }
    }

    internal class Locations
    {
        public Locations(ImmutableDictionary<string, IActorRef> locations)
        {
            Locs = locations;
        }

        public ImmutableDictionary<string, IActorRef> Locs { get; }
    }

    internal class ShardLocations : ActorBase
    {
        private Locations locations;

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case GetLocations _:
                    Sender.Tell(locations);
                    return true;
                case Locations l:
                    locations = l;
                    return true;
            }

            return false;
        }
    }

    private sealed class MessageExtractor : IMessageExtractor
    {
        public string EntityId(object message)
        {
            return message switch
            {
                Ping p => p.Id,
                _ => null
            };
        }

        public object EntityMessage(object message)
        {
            return message;
        }

        public string ShardId(object message)
        {
            return message switch
            {
                Ping p => p.Id[0].ToString(),
                _ => null
            };
        }

        public string ShardId(string entityId, object messageHint = null)
        {
            return entityId[0].ToString();
        }
    }

    private readonly Lazy<IActorRef> _region;

    protected ClusterShardCoordinatorDowning2Spec(ClusterShardCoordinatorDowning2SpecConfig config, Type type)
        : base(config, type)
    {
        _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
    }

    private void StartSharding()
    {
        StartSharding(
            Sys,
            "Entity",
            entityProps: Props.Create(() => new Entity()),
            messageExtractor: new MessageExtractor());
    }

    #endregion
}