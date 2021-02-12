//-----------------------------------------------------------------------
// <copyright file="DurablePruningSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            akka.actor.provider = cluster
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
        private readonly Cluster.Cluster _cluster;
        private readonly RoleName _first = new RoleName("first");
        private readonly RoleName _second = new RoleName("second");
        private readonly TimeSpan _maxPruningDissemination = TimeSpan.FromSeconds(3);
        private readonly GCounterKey _keyA = new GCounterKey("A");
        private readonly TimeSpan _timeout;
        private readonly IActorRef _replicator;

        protected DurablePruningSpec(IActorRef replicator) : this(new DurablePruningSpecConfig(), replicator)
        {
        }

        protected DurablePruningSpec(DurablePruningSpecConfig config, IActorRef replicator) : base(config, typeof(DurablePruningSpec))
        {
            this._replicator = replicator;
            InitialParticipantsValueFactory = Roles.Count;
            _cluster = Akka.Cluster.Cluster.Get(Sys);
            _timeout = Dilated(TimeSpan.FromSeconds(5));
        }

        protected override int InitialParticipantsValueFactory { get; }

        [MultiNodeFact()]
        public void Pruning_of_durable_CRDT_should_move_data_from_removed_node()
        {
            Join(_first, _first);
            Join(_second, _first);

            var sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            var cluster2 = Akka.Cluster.Cluster.Get(sys2);
            var replicator2 = StartReplicator(sys2);
            var probe2 = new TestProbe(sys2, new XunitAssertions());
            cluster2.Join(Node(_first).Address);

            Within(TimeSpan.FromSeconds(5), () => AwaitAssert(() =>
            {
                _replicator.Tell(Dsl.GetReplicaCount);
                ExpectMsg(new ReplicaCount(4));
                replicator2.Tell(Dsl.GetReplicaCount, probe2.Ref);
                probe2.ExpectMsg(new ReplicaCount(4));
            }));

            _replicator.Tell(Dsl.Update(_keyA, GCounter.Empty, WriteLocal.Instance, c => c.Increment(_cluster)));
            ExpectMsg(new UpdateSuccess(_keyA, null));

            replicator2.Tell(Dsl.Update(_keyA, GCounter.Empty, WriteLocal.Instance, c => c.Increment(cluster2, 2)), probe2.Ref);
            probe2.ExpectMsg(new UpdateSuccess(_keyA, null));

            EnterBarrier("updates-done");

            Within(TimeSpan.FromSeconds(10), () => AwaitAssert(() =>
            {
                _replicator.Tell(Dsl.Get(_keyA, new ReadAll(TimeSpan.FromSeconds(1))));
                var counter1 = ExpectMsg<GetSuccess>().Get(_keyA);
                counter1.Value.ShouldBe(10UL);
                counter1.State.Count.ShouldBe(4);
            }));

            Within(TimeSpan.FromSeconds(10), () => AwaitAssert(() =>
            {
                replicator2.Tell(Dsl.Get(_keyA, new ReadAll(TimeSpan.FromSeconds(1))), probe2.Ref);
                var counter2 = probe2.ExpectMsg<GetSuccess>().Get(_keyA);
                counter2.Value.ShouldBe(10UL);
                counter2.State.Count.ShouldBe(4);
            }));
            EnterBarrier("get1");

            RunOn(() => _cluster.Leave(cluster2.SelfAddress), _first);

            Within(TimeSpan.FromSeconds(15), () => AwaitAssert(() =>
            {
                _replicator.Tell(Dsl.GetReplicaCount);
                ExpectMsg(new ReplicaCount(3));
            }));
            EnterBarrier("removed");

            RunOn(() => sys2.Terminate().Wait(TimeSpan.FromSeconds(5)), _first);

            Within(TimeSpan.FromSeconds(15), () =>
            {
                var values = ImmutableHashSet<int>.Empty;
                AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.Get(_keyA, ReadLocal.Instance));
                    var counter3 = ExpectMsg<GetSuccess>().Get(_keyA);
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
                cluster3.Join(Node(_first).Address);

                Within(TimeSpan.FromSeconds(10), () =>
                {
                    var values = ImmutableHashSet<int>.Empty;
                    AwaitAssert(() =>
                    {
                        replicator3.Tell(Dsl.Get(_keyA, ReadLocal.Instance), probe3.Ref);
                        var counter4 = probe3.ExpectMsg<GetSuccess>().Get(_keyA);
                        var value = counter4.Value;
                        values.Add((int) value);
                        value.ShouldBe(10UL);
                        counter4.State.Count.ShouldBe(3);
                    });
                    values.ShouldBe(ImmutableHashSet.Create(10));
                });

                // after merging with others
                replicator3.Tell(Dsl.Get(_keyA, new ReadAll(RemainingOrDefault)));
                var counter5 = ExpectMsg<GetSuccess>().Get(_keyA);
                counter5.Value.ShouldBe(10UL);
                counter5.State.Count.ShouldBe(3);

            }, _first);
            EnterBarrier("sys3-started");

            _replicator.Tell(Dsl.Get(_keyA, new ReadAll(RemainingOrDefault)));
            var counter6 = ExpectMsg<GetSuccess>().Get(_keyA);
            counter6.Value.ShouldBe(10UL);
            counter6.State.Count.ShouldBe(3);

            EnterBarrier("after-1");
        }

        private IActorRef StartReplicator(ActorSystem system)
        {
            return system.ActorOf(Replicator.Props(
                    ReplicatorSettings.Create(system)
                        .WithGossipInterval(TimeSpan.FromSeconds(1))
                        .WithPruning(TimeSpan.FromSeconds(1), _maxPruningDissemination)),
                "replicator");
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() => _cluster.Join(Node(to).Address), from);
            EnterBarrier(from.Name + "-joined");
        }
    }
}
