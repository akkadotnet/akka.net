//-----------------------------------------------------------------------
// <copyright file="ReplicatorPruningSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    public class ReplicatorPruningSpec : MultiNodeClusterSpec
    {
        public static readonly RoleName First = new RoleName("first");
        public static readonly RoleName Second = new RoleName("second");
        public static readonly RoleName Third = new RoleName("third");

        private readonly Cluster.Cluster _cluster;
        private readonly TimeSpan _maxPruningDissemination = TimeSpan.FromSeconds(3);
        private readonly IActorRef _replicator;
        private readonly TimeSpan _timeout;

        private readonly GCounterKey KeyA = new GCounterKey("A");
        private readonly ORSetKey<string> KeyB = new ORSetKey<string>("B");
        private readonly PNCounterDictionaryKey<string> KeyC = new PNCounterDictionaryKey<string>("C");

        public ReplicatorPruningSpec() : this(new ReplicatorPruningSpecConfig()) { }

        protected ReplicatorPruningSpec(ReplicatorPruningSpecConfig config) : base(config, typeof(ReplicatorPruningSpec))
        {
            _cluster = Akka.Cluster.Cluster.Get(Sys);
            _timeout = Dilated(TimeSpan.FromSeconds(3));
            _replicator = Sys.ActorOf(Replicator.Props(ReplicatorSettings.Create(Sys)
                .WithGossipInterval(TimeSpan.FromSeconds(1))
                .WithPruning(pruningInterval: TimeSpan.FromSeconds(1), maxPruningDissemination: _maxPruningDissemination)),
                "replicator");
        }

        [MultiNodeFact(Skip="FIXME")]
        public void Test()
        {
            Pruning_of_CRDT_should_move_data_from_removed_node();
        }

        private void Pruning_of_CRDT_should_move_data_from_removed_node()
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
            _cluster.Subscribe(memberProbe.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, typeof(ClusterEvent.MemberUp));
            var thirdUniqueAddress = memberProbe.FishForMessage(msg =>
                msg is ClusterEvent.MemberUp && ((ClusterEvent.MemberUp)msg).Member.Address == Node(Third).Address)
                .AsInstanceOf<ClusterEvent.MemberUp>().Member.UniqueAddress;

            _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, new WriteAll(_timeout), x => x.Increment(_cluster, 3)));
            ExpectMsg(new UpdateSuccess(KeyA, null));

            _replicator.Tell(Dsl.Update(KeyB, ORSet<string>.Empty, new WriteAll(_timeout), x => x
                .Add(_cluster, "a")
                .Add(_cluster, "b")
                .Add(_cluster, "c")));
            ExpectMsg(new UpdateSuccess(KeyB, null));

            _replicator.Tell(Dsl.Update(KeyC, PNCounterDictionary<string>.Empty, new WriteAll(_timeout), x => x
                .Increment(_cluster, "x")
                .Increment(_cluster, "y")));
            ExpectMsg(new UpdateSuccess(KeyC, null));

            EnterBarrier("udpates-done");

            _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance));
            var oldCounter = ExpectMsg<GetSuccess>().Get(KeyA);
            oldCounter.Value.Should().Be(9);

            _replicator.Tell(Dsl.Get(KeyB, ReadLocal.Instance));
            var oldSet = ExpectMsg<GetSuccess>().Get(KeyB);
            oldSet.Elements.Should().BeEquivalentTo(new[] { "c", "b", "a" });

            _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
            var oldMap = ExpectMsg<GetSuccess>().Get(KeyC);
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
                        _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance));
                        var counter = ExpectMsg<GetSuccess>(msg => Equals(msg.Key, KeyA)).Get(KeyA);
                        counter.Value.ShouldBe(9UL);
                        counter.NeedPruningFrom(thirdUniqueAddress).Should().BeFalse($"{counter} shouldn't need prunning from {thirdUniqueAddress}");
                    });
                });

                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.Get(KeyB, ReadLocal.Instance));
                        var set = ExpectMsg<GetSuccess>(msg => Equals(msg.Key, KeyB)).Get(KeyB);
                        set.Elements.Should().BeEquivalentTo(new[] { "c", "b", "a" });
                        set.NeedPruningFrom(thirdUniqueAddress).Should().BeFalse($"{set} shouldn't need pruning from {thirdUniqueAddress}");
                    });
                });

                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
                        var map = ExpectMsg<GetSuccess>(msg => Equals(msg.Key, KeyC)).Get(KeyC);
                        map["x"].Should().Be(3);
                        map["y"].Should().Be(3);
                        map.NeedPruningFrom(thirdUniqueAddress).Should().BeFalse($"{map} shouldn't need pruning from {thirdUniqueAddress}");
                    });
                });
            }, First, Second);

            EnterBarrier("pruning-done");

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

        /// <summary>
        /// On one of the nodes the data has been updated by the pruning, client can update anyway
        /// </summary>
        private void UpdateAfterPruning(ulong expectedValue)
        {
            _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, new WriteAll(_timeout), x => x.Increment(_cluster, 1)));
            ExpectMsg<UpdateSuccess>(msg =>
            {
                _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance));
                var retrieved = ExpectMsg<GetSuccess>().Get(KeyA);
                retrieved.Value.Should().Be(expectedValue);
            });
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() => _cluster.Join(Node(to).Address), from);
            EnterBarrier(from.Name + "-joined");
        }
    }
    
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
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.log-dead-letters-during-shutdown = off")
                .WithFallback(DistributedData.DefaultConfig());
        }
    }
}