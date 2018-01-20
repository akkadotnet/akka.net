#region copyright
//-----------------------------------------------------------------------
// <copyright file="MultiDcClusterShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    interface IEntityMessage
    {
        string Id { get; }
    }

    sealed class Ping : IEntityMessage
    {
        public Ping(string id)
        {
            Id = id;
        }

        public string Id { get; }
    }

    sealed class GetCount : IEntityMessage
    {
        public GetCount(string id)
        {
            Id = id;
        }

        public string Id { get; }
    }

    sealed class Entity : ReceiveActor
    {
        public Entity()
        {
            var count = 0;
            Receive<Ping>(_ =>
            {
                count++;
                Sender.Tell(Self);
            });
            Receive<GetCount>(_ => Sender.Tell(count));
        }
    }

    public class MultiDcClusterShardingConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }

        public MultiDcClusterShardingConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            CommonConfig = ConfigurationFactory.ParseString(@"
                # DEBUG because of failing test, issue #23741
                akka.loglevel = DEBUG
                akka.cluster.debug.verbose-heartbeat-logging = on
                akka.cluster.debug.verbose-gossip-logging = on
                akka.actor.provider = cluster
                akka.remote.log-remote-lifecycle-events = on
                akka.cluster.auto-down-unreachable-after = 0s");

            NodeConfig(new[] { First, Second }, new[]
            {
                ConfigurationFactory.ParseString("akka.cluster.multi-data-center.self-data-center = DC1")
            });

            NodeConfig(new[] { Third, Fourth }, new[]
            {
                ConfigurationFactory.ParseString("akka.cluster.multi-data-center.self-data-center = DC2")
            });
        }
    }

    public class MultiDcClusterShardingSpec : MultiNodeClusterSpec
    {
        private static readonly ExtractEntityId extractEntityId = msg => msg is IEntityMessage message ? Tuple.Create(message.Id, (object)message) : null;
        private static readonly ExtractShardId extractShardId = msg => msg is IEntityMessage message ? message.Id[0].ToString() : null;

        private readonly MultiDcClusterShardingConfig _config;
        private IActorRef _region;

        public MultiDcClusterShardingSpec() : this(new MultiDcClusterShardingConfig()) { }

        protected MultiDcClusterShardingSpec(MultiDcClusterShardingConfig config) : base(config, typeof(MultiDcClusterShardingSpec))
        {
            _config = config;
        }

        protected override int InitialParticipantsValueFactory => Roles.Count;

        protected IActorRef Region => _region ?? (_region = ClusterSharding.Get(Sys).ShardRegion("Entity"));
        protected RoleName First => _config.First;
        protected RoleName Second => _config.Second;
        protected RoleName Third => _config.Third;
        protected RoleName Fourth => _config.Fourth;

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
                StartSharding();
                Within(TimeSpan.FromSeconds(15), () => AwaitAssert(() =>
                    Cluster.State.Members
                    .Any(m => m.UniqueAddress == Cluster.SelfUniqueAddress && m.Status == MemberStatus.Up)
                    .Should().BeTrue()));
            }, from);
        }

        private void StartSharding()
        {
            ClusterSharding.Get(Sys).Start(
                typeName: "Entity",
                entityProps: Props.Create<Entity>(),
                settings: ClusterShardingSettings.Create(Sys),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId);
        }

        private Address FillAddress(Address a) => a.HasLocalScope ? Cluster.SelfAddress : a;

        private void AssertCurrentRegions(ImmutableHashSet<Address> expected) => AwaitAssert(() =>
        {
            var p = this.CreateTestProbe();
            Region.Tell(GetCurrentRegions.Instance, p.Ref);
            p.ExpectMsg(new CurrentRegions(expected));
        }, TimeSpan.FromSeconds(10));

        [MultiNodeFact]
        public void Test()
        {
            Cluster_sharding_in_multi_data_center_cluster_must_join_cluster();
            Cluster_sharding_in_multi_data_center_cluster_must_initialize_shards();
            Cluster_sharding_in_multi_data_center_cluster_must_not_mix_entities_in_different_data_centers();
            Cluster_sharding_in_multi_data_center_cluster_must_allow_proxy_within_the_same_data_center();
            Cluster_sharding_in_multi_data_center_cluster_must_allow_proxy_across_different_data_centers();
        }

        private void Cluster_sharding_in_multi_data_center_cluster_must_join_cluster()
        {
            Join(First, First);
            Join(Second, First);
            Join(Third, First);
            Join(Fourth, First);

            AwaitAssert(() =>
            {
                Cluster.State.Members.Count.Should().Be(4);
                Cluster.State.Members.Select(m => m.Status).ToImmutableHashSet().Should().BeEquivalentTo(MemberStatus.Up);
            }, TimeSpan.FromSeconds(10));

            RunOn(() => AssertCurrentRegions(new[] { Node(First).Address, Node(Second).Address }.ToImmutableHashSet()), First, Second);
            RunOn(() => AssertCurrentRegions(new[] { Node(Third).Address, Node(Fourth).Address }.ToImmutableHashSet()), Third, Fourth);

            EnterBarrier("after-1");
        }

        private void Cluster_sharding_in_multi_data_center_cluster_must_initialize_shards()
        {
            RunOn(() =>
                {
                    var locations = Enumerable.Range(1, 10).Select(n =>
                    {
                        var id = n.ToString();
                        Region.Tell(new Ping(id));
                        return new KeyValuePair<string, IActorRef>(id, ExpectMsg<IActorRef>());
                    }).ToImmutableDictionary();

                    var firstAddress = Node(First).Address;
                    var secondAddress = Node(Second).Address;
                    var hosts = locations.Values.Select(r => FillAddress(r.Path.Address)).ToImmutableHashSet();

                    hosts.Should().BeEquivalentTo(firstAddress, secondAddress);
                }, First);

            RunOn(() =>
            {
                var locations = Enumerable.Range(1, 10).Select(n =>
                {
                    var id = n.ToString();
                    Region.Tell(new Ping(id));
                    var ref1 = ExpectMsg<IActorRef>();
                    Region.Tell(new Ping(id));
                    var ref2 = ExpectMsg<IActorRef>();

                    ref1.Should().Be(ref2);
                    return new KeyValuePair<string, IActorRef>(id, ref1);
                }).ToImmutableDictionary();

                var thirdAddress = Node(Third).Address;
                var fourthAddress = Node(Fourth).Address;
                var hosts = locations.Values.Select(r => FillAddress(r.Path.Address)).ToImmutableHashSet();

                hosts.Should().BeEquivalentTo(thirdAddress, fourthAddress);
            }, Third);

            EnterBarrier("after-2");
        }

        private void Cluster_sharding_in_multi_data_center_cluster_must_not_mix_entities_in_different_data_centers()
        {
            RunOn(() =>
            {
                Region.Tell(new GetCount("5"));
                ExpectMsg(1);
            }, Second);

            RunOn(() =>
            {
                Region.Tell(new GetCount("5"));
                ExpectMsg(2);
            }, Fourth);

            EnterBarrier("after-3");
        }

        private void Cluster_sharding_in_multi_data_center_cluster_must_allow_proxy_within_the_same_data_center()
        {
            RunOn(() =>
            {
                var proxy = ClusterSharding.Get(Sys).StartProxy(
                    typeName: "Entity",
                    role: null,
                    dataCenter: null,
                    extractEntityId: extractEntityId,
                    extractShardId: extractShardId);

                proxy.Tell(new GetCount("5"));
                ExpectMsg(2);
            }, Second);
            EnterBarrier("after-4");
        }

        private void Cluster_sharding_in_multi_data_center_cluster_must_allow_proxy_across_different_data_centers()
        {
            RunOn(() =>
            {
                var proxy = ClusterSharding.Get(Sys).StartProxy(
                    typeName: "Entity",
                    role: null,
                    dataCenter: "DC2",
                    extractEntityId: extractEntityId,
                    extractShardId: extractShardId);

                proxy.Tell(new GetCount("5"));
                ExpectMsg(2);
            }, Second);
            EnterBarrier("after-5");
        }
    }
}