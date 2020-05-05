//-----------------------------------------------------------------------
// <copyright file="ReplicatorPruningSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;

namespace Akka.DistributedData.Tests.MultiNode
{
    public class ReplicatorPruningSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public ReplicatorPruningSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                # we use 3s as write timeouts in test, make sure we see that
                # and not time out the expectMsg at the same time
                akka.test.single-expect-default = 5s
                akka.log-dead-letters-during-shutdown = off")
                .WithFallback(DistributedData.DefaultConfig());
        }
    }

    public class ReplicatorPruningSpec : MultiNodeClusterSpec
    {
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;

        private readonly Cluster.Cluster _cluster;
        private readonly TimeSpan _maxPruningDissemination = TimeSpan.FromSeconds(3);
        private readonly IActorRef _replicator;
        private readonly TimeSpan _timeout;

        private readonly GCounterKey _keyA = new GCounterKey("A");
        private readonly ORSetKey<string> _keyB = new ORSetKey<string>("B");
        private readonly PNCounterDictionaryKey<string> _keyC = new PNCounterDictionaryKey<string>("C");

        public ReplicatorPruningSpec() : this(new ReplicatorPruningSpecConfig())
        {
        }

        protected ReplicatorPruningSpec(ReplicatorPruningSpecConfig config) : base(config,
            typeof(ReplicatorPruningSpec))
        {
            _cluster = Akka.Cluster.Cluster.Get(Sys);
            _timeout = Dilated(TimeSpan.FromSeconds(3));
            _replicator = Sys.ActorOf(Replicator.Props(ReplicatorSettings.Create(Sys)
                    .WithGossipInterval(TimeSpan.FromSeconds(1))
                    .WithPruning(pruningInterval: TimeSpan.FromSeconds(1),
                        maxPruningDissemination: _maxPruningDissemination)),
                "replicator");

            First = config.First;
            Second = config.Second;
            Third = config.Third;
        }


        [MultiNodeFact()]
        public void Pruning_of_CRDT_should_move_data_from_removed_node()
        {
            Join(First, First);
            Join(Second, First);
            Join(Third, First);

            Within(TimeSpan.FromSeconds(5), () =>
            {
                AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.GetReplicaCount);
                    ExpectMsg(new ReplicaCount(3));
                });
            });

            // we need the UniqueAddress
            var memberProbe = CreateTestProbe();
            _cluster.Subscribe(memberProbe.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents,
                typeof(ClusterEvent.MemberUp));
            var thirdUniqueAddress = memberProbe.FishForMessage(msg =>
                    msg is ClusterEvent.MemberUp up && up.Member.Address == Node(Third).Address)
                .AsInstanceOf<ClusterEvent.MemberUp>().Member.UniqueAddress;

            _replicator.Tell(Dsl.Update(_keyA, GCounter.Empty, new WriteAll(_timeout), x => x.Increment(_cluster, 3)));
            ExpectMsg(new UpdateSuccess(_keyA, null));

            _replicator.Tell(Dsl.Update(_keyB, ORSet<string>.Empty, new WriteAll(_timeout), x => x
                .Add(_cluster, "a")
                .Add(_cluster, "b")
                .Add(_cluster, "c")));
            ExpectMsg(new UpdateSuccess(_keyB, null));

            _replicator.Tell(Dsl.Update(_keyC, PNCounterDictionary<string>.Empty, new WriteAll(_timeout), x => x
                .Increment(_cluster, "x")
                .Increment(_cluster, "y")));
            ExpectMsg(new UpdateSuccess(_keyC, null));

            EnterBarrier("udpates-done");

            _replicator.Tell(Dsl.Get(_keyA, ReadLocal.Instance));
            var oldCounter = ExpectMsg<GetSuccess>().Get(_keyA);
            oldCounter.Value.Should().Be(9);

            _replicator.Tell(Dsl.Get(_keyB, ReadLocal.Instance));
            var oldSet = ExpectMsg<GetSuccess>().Get(_keyB);
            oldSet.Elements.Should().BeEquivalentTo(new[] { "c", "b", "a" });

            _replicator.Tell(Dsl.Get(_keyC, ReadLocal.Instance));
            var oldMap = ExpectMsg<GetSuccess>().Get(_keyC);
            oldMap["x"].Should().Be(3);
            oldMap["y"].Should().Be(3);

            EnterBarrier("get-old");

            RunOn(() => _cluster.Leave(Node(Third).Address), First);

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(15), () => AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.GetReplicaCount);
                    ExpectMsg(new ReplicaCount(2));
                }));
            }, First, Second);

            EnterBarrier("third-removed");

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(15), () =>
                {
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.Get(_keyA, ReadLocal.Instance));
                        var counter = ExpectMsg<GetSuccess>(msg => Equals(msg.Key, _keyA)).Get(_keyA);
                        counter.Value.ShouldBe(9UL);
                        counter.NeedPruningFrom(thirdUniqueAddress).Should()
                            .BeFalse($"{counter} shouldn't need pruning from {thirdUniqueAddress}");
                    });
                });

                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.Get(_keyB, ReadLocal.Instance));
                        var set = ExpectMsg<GetSuccess>(msg => Equals(msg.Key, _keyB)).Get(_keyB);
                        set.Elements.Should().BeEquivalentTo(new[] { "c", "b", "a" });
                        set.NeedPruningFrom(thirdUniqueAddress).Should()
                            .BeFalse($"{set} shouldn't need pruning from {thirdUniqueAddress}");
                    });
                });

                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.Get(_keyC, ReadLocal.Instance));
                        var map = ExpectMsg<GetSuccess>(msg => Equals(msg.Key, _keyC)).Get(_keyC);
                        map["x"].Should().Be(3);
                        map["y"].Should().Be(3);
                        map.NeedPruningFrom(thirdUniqueAddress).Should()
                            .BeFalse($"{map} shouldn't need pruning from {thirdUniqueAddress}");
                    });
                });
            }, First, Second);

            EnterBarrier("pruning-done");

            void UpdateAfterPruning(ulong expectedValue)
            {
                // inject data from removed node to simulate bad data
                _replicator.Tell(Dsl.Update(_keyA, GCounter.Empty, new WriteAll(_timeout), x => x.Merge(oldCounter).Increment(_cluster, 1)));
                ExpectMsg<UpdateSuccess>(msg =>
                {
                    _replicator.Tell(Dsl.Get(_keyA, ReadLocal.Instance));
                    var retrieved = ExpectMsg<GetSuccess>().Get(_keyA);
                    retrieved.Value.Should().Be(expectedValue);
                });
            }

            RunOn(() => UpdateAfterPruning(expectedValue: 10), First);
            EnterBarrier("update-first-after-pruning");

            RunOn(() => UpdateAfterPruning(expectedValue: 11), Second);
            EnterBarrier("update-second-after-pruning");

            // after pruning performed and maxDissemination it is tombstoned
            // and we should still not be able to update with data from removed node
            ExpectNoMsg(_maxPruningDissemination.Add(TimeSpan.FromSeconds(3)));

            RunOn(() => UpdateAfterPruning(expectedValue: 12), First);
            EnterBarrier("update-first-after-tombstone");

            RunOn(() => UpdateAfterPruning(expectedValue: 13), Second);
            EnterBarrier("update-second-after-tombstone");

            EnterBarrier("after-1");
        }

        protected override int InitialParticipantsValueFactory => Roles.Count;

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() => _cluster.Join(Node(to).Address), from);
            EnterBarrier(from.Name + "-joined");
        }
    }
}
