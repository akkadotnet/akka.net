//-----------------------------------------------------------------------
// <copyright file="ReplicatorPruningSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.Util.Internal;

namespace Akka.DistributedData.Tests.MultiNode
{
    public class ReplicatorPruningSpec : MultiNodeSpec
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

        public ReplicatorPruningSpec(ReplicatorPruningSpecConfig config) : base(config)
        {
            _cluster = Cluster.Cluster.Get(Sys);
            _timeout = Dilated(TimeSpan.FromSeconds(3));
            _replicator = Sys.ActorOf(Replicator.Props(ReplicatorSettings.Create(Sys)
                .WithGossipInterval(TimeSpan.FromSeconds(1))
                .WithPruning(pruningInterval: TimeSpan.FromSeconds(1), maxPruningDissemination: _maxPruningDissemination)),
                "replicator");
        }

        //[MultiNodeFact]
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
                    ExpectMsg(new Replicator.ReplicaCount(3));
                });
            });

            // we need the UniqueAddress
            var memberProbe = CreateTestProbe();
            _cluster.Subscribe(memberProbe.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, typeof(ClusterEvent.MemberUp));
            var thirdUniqueAddress = memberProbe.FishForMessage(msg =>
                msg is ClusterEvent.MemberUp && ((ClusterEvent.MemberUp)msg).Member.Address == Node(Third).Address)
                .AsInstanceOf<ClusterEvent.MemberUp>().Member.UniqueAddress;

            _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, new WriteAll(_timeout), x => x.Increment(_cluster.SelfUniqueAddress, 3)));
            ExpectMsg(new Replicator.UpdateSuccess(KeyA, null));

            _replicator.Tell(Dsl.Update(KeyB, ORSet<string>.Empty, new WriteAll(_timeout), x => x
                .Add(_cluster.SelfUniqueAddress, "a")
                .Add(_cluster.SelfUniqueAddress, "b")
                .Add(_cluster.SelfUniqueAddress, "c")));
            ExpectMsg(new Replicator.UpdateSuccess(KeyB, null));

            _replicator.Tell(Dsl.Update(KeyC, PNCounterDictionary<string>.Empty, new WriteAll(_timeout), x => x
                .Increment(_cluster.SelfUniqueAddress, "x")
                .Increment(_cluster.SelfUniqueAddress, "y")));
            ExpectMsg(new Replicator.UpdateSuccess(KeyC, null));

            EnterBarrier("udpates-done");

            _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance));
            var oldCounter = ExpectMsg<Replicator.GetSuccess>().Get(KeyA);
            oldCounter.Value.ShouldBe(9);

            _replicator.Tell(Dsl.Get(KeyB, ReadLocal.Instance));
            var oldSet = ExpectMsg<Replicator.GetSuccess>().Get(KeyB);
            oldSet.Elements.ShouldBe(new[] { "a", "b", "c" });

            _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
            var oldMap = ExpectMsg<Replicator.GetSuccess>().Get(KeyC);
            oldMap["x"].ShouldBe(3);
            oldMap["y"].ShouldBe(3);

            EnterBarrier("get-old");

            RunOn(() => _cluster.Leave(Node(Third).Address), First);

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(15), () => AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.GetReplicaCount);
                    ExpectMsg(new Replicator.ReplicaCount(2));
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
                        ExpectMsg<Replicator.GetSuccess>(msg =>
                        {
                            var counter = msg.Get(KeyA);
                            counter.Value.ShouldBe(9);
                            counter.NeedPruningFrom(thirdUniqueAddress).ShouldBeFalse();
                        });
                    });
                });

                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.Get(KeyB, ReadLocal.Instance));
                        ExpectMsg<Replicator.GetSuccess>(msg =>
                        {
                            var set = msg.Get(KeyB);
                            set.Elements.ShouldBe(new[] { "a", "b", "c" });
                            set.NeedPruningFrom(thirdUniqueAddress).ShouldBeFalse();
                        });
                    });
                });

                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
                        ExpectMsg<Replicator.GetSuccess>(msg =>
                        {
                            var map = msg.Get(KeyC);
                            map["x"].ShouldBe(3);
                            map["y"].ShouldBe(3);
                            map.NeedPruningFrom(thirdUniqueAddress).ShouldBeFalse();
                        });
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
        private void UpdateAfterPruning(int expectedValue)
        {
            _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, new WriteAll(_timeout), x => x.Increment(_cluster.SelfUniqueAddress, 1)));
            ExpectMsg<Replicator.UpdateSuccess>(msg =>
            {
                _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance));
                var retrieved = ExpectMsg<Replicator.GetSuccess>().Get(KeyA);
                retrieved.Value.ShouldBe(expectedValue);
            });
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() => _cluster.Join(Node(to).Address), from);
            EnterBarrier(from.Name + "-joined");
        }
    }

    public class ReplicatorPruningSpecNode1 : ReplicatorPruningSpec { }
    public class ReplicatorPruningSpecNode2 : ReplicatorPruningSpec { }
    public class ReplicatorPruningSpecNode3 : ReplicatorPruningSpec { }

    public class ReplicatorPruningSpecConfig : MultiNodeConfig
    {
        public ReplicatorPruningSpecConfig()
        {
            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.log-dead-letters-during-shutdown = off");
        }
    }
}