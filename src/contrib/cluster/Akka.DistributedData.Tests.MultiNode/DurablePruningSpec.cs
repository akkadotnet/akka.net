//-----------------------------------------------------------------------
// <copyright file="DurablePruningSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.TestKit.Xunit2;

namespace Akka.DistributedData.Tests.MultiNode
{
    public class DurablePruningSpecConfig : MultiNodeConfig
    {
        public readonly RoleName First;
        public readonly RoleName Second;

        public DurablePruningSpecConfig()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(on: false).WithFallback(ConfigurationFactory.ParseString(@"
            akka.loglevel = INFO
            akka.actor.provider = ""cluster""
            akka.log-dead-letters-during-shutdown = off
            akka.cluster.distributed-data.durable.keys = [""*""]
            akka.cluster.distributed-data.durable.lmdb {
              dir = ""target/DurablePruningSpec-" + DateTime.UtcNow.Ticks + @"-ddata""
              map-size = 10 MiB
            }")).WithFallback(DistributedData.DefaultConfig());
        }
    }

    public class DurablePruningSpec : MultiNodeClusterSpec
    {
        private readonly Cluster.Cluster cluster;
        private readonly RoleName first = new RoleName("first");
        private readonly RoleName second = new RoleName("second");
        private readonly TimeSpan maxPruningDissemination = TimeSpan.FromSeconds(3);
        private readonly TimeSpan timeout;
        private readonly GCounterKey keyA = new GCounterKey("A");
        private readonly IActorRef replicator;

        protected DurablePruningSpec() : this(new DurablePruningSpecConfig())
        {
        }

        protected DurablePruningSpec(DurablePruningSpecConfig config) : base(config, typeof(DurablePruningSpec))
        {
            InitialParticipantsValueFactory = Roles.Count;
            cluster = Akka.Cluster.Cluster.Get(Sys);
            timeout = Dilated(TimeSpan.FromSeconds(5));
        }

        protected override int InitialParticipantsValueFactory { get; }

        [MultiNodeFact(Skip = "FIXME")]
        public void Pruning_of_durable_CRDT_should_move_data_from_removed_node()
        {
            Join(first, first);
            Join(second, first);

            var sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            var cluster2 = Akka.Cluster.Cluster.Get(sys2);
            var replicator2 = StartReplicator(sys2);
            var probe2 = new TestProbe(sys2, new XunitAssertions());
            cluster2.Join(Node(first).Address);

            Within(TimeSpan.FromSeconds(5), () => AwaitAssert(() =>
            {
                replicator.Tell(Dsl.GetReplicaCount);
                ExpectMsg(new ReplicaCount(4));
                replicator2.Tell(Dsl.GetReplicaCount, probe2.Ref);
                probe2.ExpectMsg(new ReplicaCount(4));
            }));

            replicator.Tell(Dsl.Update(keyA, GCounter.Empty, WriteLocal.Instance, c => c.Increment(cluster)));
            ExpectMsg(new UpdateSuccess(keyA, null));

            replicator2.Tell(Dsl.Update(keyA, GCounter.Empty, WriteLocal.Instance, c => c.Increment(cluster2, 2)), probe2.Ref);
            probe2.ExpectMsg(new UpdateSuccess(keyA, null));

            EnterBarrier("updates-done");

            Within(TimeSpan.FromSeconds(10), () => AwaitAssert(() =>
            {
                replicator.Tell(Dsl.Get(keyA, new ReadAll(TimeSpan.FromSeconds(1))));
                var counter1 = ExpectMsg<GetSuccess>().Get(keyA);
                counter1.Value.ShouldBe(10UL);
                counter1.State.Count.ShouldBe(4);
            }));

            Within(TimeSpan.FromSeconds(10), () => AwaitAssert(() =>
            {
                replicator2.Tell(Dsl.Get(keyA, new ReadAll(TimeSpan.FromSeconds(1))), probe2.Ref);
                var counter2 = probe2.ExpectMsg<GetSuccess>().Get(keyA);
                counter2.Value.ShouldBe(10UL);
                counter2.State.Count.ShouldBe(4);
            }));
            EnterBarrier("get1");

            RunOn(() => cluster.Leave(cluster2.SelfAddress), first);

            Within(TimeSpan.FromSeconds(15), () => AwaitAssert(() =>
            {
                replicator.Tell(Dsl.GetReplicaCount);
                ExpectMsg(new ReplicaCount(3));
            }));
            EnterBarrier("removed");

            RunOn(() => sys2.Terminate().Wait(TimeSpan.FromSeconds(5)), first);

            Within(TimeSpan.FromSeconds(15), () =>
            {
                var values = ImmutableHashSet<int>.Empty;
                AwaitAssert(() =>
                {
                    replicator.Tell(Dsl.Get(keyA, ReadLocal.Instance));
                    var counter3 = ExpectMsg<GetSuccess>().Get(keyA);
                    var value = counter3.Value;
                    values = values.Add((int) value);
                    value.ShouldBe(10UL);
                    counter3.State.Count.ShouldBe(3);
                });
                values.ShouldBe(ImmutableHashSet.Create(10));
            });
            EnterBarrier("prunned");

            RunOn(() =>
            {
                var addr = cluster2.SelfAddress;
                var sys3 = ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString(@"
                ").WithFallback(Sys.Settings.Config));
                var cluster3 = Akka.Cluster.Cluster.Get(sys3);
                var replicator3 = StartReplicator(sys3);
                var probe3 = new TestProbe(sys3, new XunitAssertions());
                cluster3.Join(Node(first).Address);

                Within(TimeSpan.FromSeconds(10), () =>
                {
                    var values = ImmutableHashSet<int>.Empty;
                    AwaitAssert(() =>
                    {
                        replicator3.Tell(Dsl.Get(keyA, ReadLocal.Instance), probe3.Ref);
                        var counter4 = probe3.ExpectMsg<GetSuccess>().Get(keyA);
                        var value = counter4.Value;
                        values.Add((int) value);
                        value.ShouldBe(10UL);
                        counter4.State.Count.ShouldBe(3);
                    });
                    values.ShouldBe(ImmutableHashSet.Create(10));
                });

                // after merging with others
                replicator3.Tell(Dsl.Get(keyA, new ReadAll(RemainingOrDefault)));
                var counter5 = ExpectMsg<GetSuccess>().Get(keyA);
                counter5.Value.ShouldBe(10UL);
                counter5.State.Count.ShouldBe(3);

            }, first);
            EnterBarrier("sys3-started");

            replicator.Tell(Dsl.Get(keyA, new ReadAll(RemainingOrDefault)));
            var counter6 = ExpectMsg<GetSuccess>().Get(keyA);
            counter6.Value.ShouldBe(10UL);
            counter6.State.Count.ShouldBe(3);

            EnterBarrier("after-1");
        }

        private IActorRef StartReplicator(ActorSystem system)
        {
            return system.ActorOf(Replicator.Props(
                    ReplicatorSettings.Create(system)
                        .WithGossipInterval(TimeSpan.FromSeconds(1))
                        .WithPruning(TimeSpan.FromSeconds(1), maxPruningDissemination)),
                "replicator");
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() => cluster.Join(Node(to).Address), from);
            EnterBarrier(from.Name + "-joined");
        }
    }
}
