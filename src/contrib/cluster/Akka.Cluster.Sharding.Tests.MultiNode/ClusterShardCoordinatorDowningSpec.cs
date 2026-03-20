//-----------------------------------------------------------------------
// <copyright file="ClusterShardCoordinatorDowningSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;
using static Akka.Remote.Transport.ThrottleTransportAdapter;

namespace Akka.Cluster.Sharding.Tests;

public class ClusterShardCoordinatorDowningSpecConfig : MultiNodeClusterShardingConfig
{
    public RoleName Controller { get; }
    public RoleName First { get; }
    public RoleName Second { get; }

    public ClusterShardCoordinatorDowningSpecConfig(StateStoreMode mode)
        : base(mode: mode, loglevel: "DEBUG", additionalConfig: @"
                akka.cluster.sharding.rebalance-interval = 120 s
                # setting down-removal-margin, for testing of issue #29131
                akka.cluster.down-removal-margin = 3 s
                akka.remote.watch-failure-detector.acceptable-heartbeat-pause = 3s
            ")
    {
        Controller = Role("controller");
        First = Role("first");
        Second = Role("second");

        TestTransport = true;
    }
}

public class PersistentClusterShardCoordinatorDowningSpecConfig : ClusterShardCoordinatorDowningSpecConfig
{
    public PersistentClusterShardCoordinatorDowningSpecConfig()
        : base(StateStoreMode.Persistence)
    {
    }
}

public class DDataClusterShardCoordinatorDowningSpecConfig : ClusterShardCoordinatorDowningSpecConfig
{
    public DDataClusterShardCoordinatorDowningSpecConfig()
        : base(StateStoreMode.DData)
    {
    }
}

public class PersistentClusterShardCoordinatorDowningSpec : ClusterShardCoordinatorDowningSpec
{
    public PersistentClusterShardCoordinatorDowningSpec()
        : base(new PersistentClusterShardCoordinatorDowningSpecConfig(), typeof(PersistentClusterShardCoordinatorDowningSpec))
    {
    }
}

public class DDataClusterShardCoordinatorDowningSpec : ClusterShardCoordinatorDowningSpec
{
    public DDataClusterShardCoordinatorDowningSpec()
        : base(new DDataClusterShardCoordinatorDowningSpecConfig(), typeof(DDataClusterShardCoordinatorDowningSpec))
    {
    }
}

public abstract class ClusterShardCoordinatorDowningSpec : MultiNodeClusterShardingSpec<ClusterShardCoordinatorDowningSpecConfig>
{
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
        private Locations _locations;

        public ShardLocations()
        {
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case GetLocations _:
                    Sender.Tell(_locations);
                    return true;
                case Locations l:
                    _locations = l;
                    return true;
            }
            return false;
        }
    }

    private sealed class MessageExtractor: IMessageExtractor
    {
        public string EntityId(object message)
            => message switch
            {
                Ping p => p.Id,
                _ => null
            };

        public object EntityMessage(object message)
            => message;

        public string ShardId(object message)
            => message switch
            {
                Ping p => p.Id[0].ToString(),
                _ => null
            };

        public string ShardId(string entityId, object messageHint = null)
            => entityId[0].ToString();
    }

    private readonly Lazy<IActorRef> _region;

    protected ClusterShardCoordinatorDowningSpec(ClusterShardCoordinatorDowningSpecConfig config, Type type)
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
            messageExtractor: new MessageExtractor());
    }

    #endregion

    [MultiNodeFact]
    public async Task Cluster_sharding_with_down_member_scenario_1_specs()
    {
        await Cluster_sharding_with_down_member_scenario_1_must_join_cluster();
        await Cluster_sharding_with_down_member_scenario_1_must_initialize_shards();
        await Cluster_sharding_with_down_member_scenario_1_must_recover_after_downing_coordinator_node();
    }

    private async Task Cluster_sharding_with_down_member_scenario_1_must_join_cluster()
    {
        await WithinAsync(TimeSpan.FromSeconds(20), async () =>
        {
            StartPersistenceIfNeeded(startOn: Config.Controller, Config.First, Config.Second);

            await JoinAsync(Config.First, Config.First, onJoinedRunOnFrom: StartSharding);
            await JoinAsync(Config.Second, Config.First, onJoinedRunOnFrom: StartSharding, assertNodeUp: false);

            // all Up, everywhere before continuing
            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    Cluster.State.Members.Count.Should().Be(2);
                    Cluster.State.Members.Should().OnlyContain(m => m.Status == MemberStatus.Up);
                });
            }, Config.First, Config.Second);

            await EnterBarrierAsync("after-2");
        });
    }


    private async Task Cluster_sharding_with_down_member_scenario_1_must_initialize_shards()
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
        await EnterBarrierAsync("after-3");
    }

    private async Task Cluster_sharding_with_down_member_scenario_1_must_recover_after_downing_coordinator_node()
    {
        await WithinAsync(TimeSpan.FromSeconds(20), async () =>
        {
            var firstAddress = GetAddress(Config.First);
            Sys.ActorSelection(await NodeAsync(Config.First) / "user" / "shardLocations").Tell(GetLocations.Instance);
            var originalLocations = (await ExpectMsgAsync<Locations>()).Locs;

            await EnterBarrierAsync("after-3-locations");

            await RunOnAsync(async () =>
            {
                await TestConductor.BlackholeAsync(Config.First, Config.Second, Direction.Both);
            }, Config.Controller);

            await Task.Delay(3000);

            await RunOnAsync(async () =>
            {
                Cluster.Down(GetAddress(Config.First));
                await AwaitAssertAsync(() =>
                {
                    Cluster.State.Members.Count.Should().Be(1);
                });

                // start a few more new shards, could be allocated to first but should notice that it's terminated
                ImmutableDictionary<string, IActorRef> additionalLocations = null;
                await AwaitAssertAsync(() =>
                {
                    var probe = CreateTestProbe();
                    additionalLocations = Enumerable.Range(5, 4).Select(n =>
                    {
                        var id = n.ToString();
                        _region.Value.Tell(new Ping(id), probe.Ref);
                        return new KeyValuePair<string, IActorRef>(id, probe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(1)));
                    }).ToImmutableDictionary();
                });
                Sys.Log.Debug("Additional locations: [{0}]", string.Join(", ", additionalLocations.Select(i => $"{i.Key}: {i.Value}")));

                await AwaitAssertAsync(async () =>
                {
                    var probe = CreateTestProbe();
                    foreach (var loc in originalLocations.SetItems(additionalLocations))
                    {
                        _region.Value.Tell(new Ping(loc.Key), probe.Ref);
                        if (loc.Value.Path.Address.Equals(firstAddress))
                        {
                            var newRef = await probe.ExpectMsgAsync<IActorRef>(TimeSpan.FromSeconds(1));
                            newRef.Should().NotBe(loc.Value);
                            Sys.Log.Debug("Moved [{0}] from [{1}] to [{2}]", loc.Key, loc.Value, newRef);
                        }
                        else
                            await probe.ExpectMsgAsync(loc.Value, TimeSpan.FromSeconds(1)); // should not move

                    }
                });
            }, Config.Second);
        });

        await EnterBarrierAsync("after-4");
    }
}