//-----------------------------------------------------------------------
// <copyright file="ReplicatorChaosSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;

namespace Akka.DistributedData.Tests.MultiNode
{
    public class ReplicatorChaosSpec : MultiNodeSpec
    {
        public static readonly RoleName First = new RoleName("first");
        public static readonly RoleName Second = new RoleName("second");
        public static readonly RoleName Third = new RoleName("third");
        public static readonly RoleName Fourth = new RoleName("fourth");
        public static readonly RoleName Fifth = new RoleName("fifth");

        private readonly Cluster.Cluster _cluster;
        private readonly IActorRef _replicator;
        private readonly TimeSpan _timeout;

        public readonly GCounterKey KeyA = new GCounterKey("A");
        public readonly PNCounterKey KeyB = new PNCounterKey("B");
        public readonly GCounterKey KeyC = new GCounterKey("C");
        public readonly GCounterKey KeyD = new GCounterKey("D");
        public readonly GSetKey<string> KeyE = new GSetKey<string>("E");
        public readonly ORSetKey<string> KeyF = new ORSetKey<string>("F");
        public readonly GCounterKey KeyX = new GCounterKey("X");

        public ReplicatorChaosSpec() : this(new ReplicatorChaosSpecConfig()) { }
        public ReplicatorChaosSpec(ReplicatorChaosSpecConfig config) : base(config)
        {
            _cluster = Cluster.Cluster.Get(Sys);
            _timeout = Dilated(TimeSpan.FromSeconds(3));
            _replicator = Sys.ActorOf(Replicator.Props(ReplicatorSettings.Create(Sys)
                .WithRole("backend")
                .WithGossipInterval(TimeSpan.FromSeconds(1))), "replicator");
        }

        //[MultiNodeFact]
        public void ReplicatorChaos_Tests()
        {
            Replicator_in_chaotic_cluster_should_replicate_data_in_initial_phase();
            Replicator_in_chaotic_cluster_should_be_available_during_network_split();
            Replicator_in_chaotic_cluster_should_converge_after_partition();
        }

        public void Replicator_in_chaotic_cluster_should_replicate_data_in_initial_phase()
        {
            Join(First, First);
            Join(Second, First);
            Join(Third, First);
            Join(Fourth, First);
            Join(Fifth, First);

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.GetReplicaCount);
                    ExpectMsg(new Replicator.ReplicaCount(5));
                });
            });

            RunOn(() =>
            {
                for (int i = 0; i < 5; i++)
                {
                    _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                    _replicator.Tell(Dsl.Update(KeyB, PNCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                    _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, new WriteAll(_timeout), x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                }
                ReceiveN(15).Select(x => x.GetType()).ToImmutableHashSet().ShouldBe(new[] { typeof(Replicator.UpdateSuccess) });
            }, First);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster.SelfUniqueAddress, 20)));
                _replicator.Tell(Dsl.Update(KeyB, PNCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster.SelfUniqueAddress, 20)));
                _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, new WriteAll(_timeout), x => x.Increment(_cluster.SelfUniqueAddress, 20)));

                ReceiveN(3).ToImmutableHashSet().ShouldBe(new[]
                {
                    new Replicator.UpdateSuccess(KeyA, null),
                    new Replicator.UpdateSuccess(KeyB, null),
                    new Replicator.UpdateSuccess(KeyC, null)
                });

                _replicator.Tell(Dsl.Update(KeyE, GSet<string>.Empty, WriteLocal.Instance, x => x.Add("e1").Add("e2")));
                ExpectMsg(new Replicator.UpdateSuccess(KeyE, null));

                _replicator.Tell(Dsl.Update(KeyF, ORSet<string>.Empty, WriteLocal.Instance, x => x
                    .Add(_cluster.SelfUniqueAddress, "e1")
                    .Add(_cluster.SelfUniqueAddress, "e2")));
                ExpectMsg(new Replicator.UpdateSuccess(KeyF, null));
            }, Second);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster.SelfUniqueAddress, 40)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyD, null));

                _replicator.Tell(Dsl.Update(KeyE, GSet<string>.Empty, WriteLocal.Instance, x => x.Add("e2").Add("e3")));
                ExpectMsg(new Replicator.UpdateSuccess(KeyE, null));

                _replicator.Tell(Dsl.Update(KeyF, ORSet<string>.Empty, WriteLocal.Instance, x => x
                    .Add(_cluster.SelfUniqueAddress, "e2")
                    .Add(_cluster.SelfUniqueAddress, "e3")));
                ExpectMsg(new Replicator.UpdateSuccess(KeyF, null));
            }, Fourth);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyX, GCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster.SelfUniqueAddress, 50)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyX, null));
                _replicator.Tell(Dsl.Delete(KeyX, WriteLocal.Instance));
                ExpectMsg(new Replicator.DeleteSuccess(KeyX));
            }, Fifth);

            EnterBarrier("initial-updates-done");

            AssertValue(KeyA, 25);
            AssertValue(KeyB, 15);
            AssertValue(KeyC, 25);
            AssertValue(KeyD, 40);
            AssertValue(KeyE, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3" }));
            AssertValue(KeyF, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3" }));
            AssertDeleted(KeyX);

            EnterBarrier("after-1");
        }

        public void Replicator_in_chaotic_cluster_should_be_available_during_network_split()
        {
            var side1 = new[] { First, Second };
            var side2 = new[] { Third, Fourth, Fifth };

            RunOn(() =>
            {
                foreach (var a in side1)
                    foreach (var b in side2)
                        TestConductor.Blackhole(a, b, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(1));
            }, First);

            EnterBarrier("split");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyA, null));
            }, First);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster.SelfUniqueAddress, 2)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyA, null));

                _replicator.Tell(Dsl.Update(KeyE, GSet<string>.Empty, new WriteTo(2, _timeout), x => x.Add("e4")));
                ExpectMsg(new Replicator.UpdateSuccess(KeyE, null));

                _replicator.Tell(Dsl.Update(KeyF, ORSet<string>.Empty, new WriteTo(2, _timeout), x => x.Remove(_cluster.SelfUniqueAddress, "e2")));
                ExpectMsg(new Replicator.UpdateSuccess(KeyF, null));
            }, Third);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyD, null));
            }, Fourth);

            EnterBarrier("update-during-split");

            RunOn(() =>
            {
                AssertValue(KeyA, 26);
                AssertValue(KeyB, 15);
                AssertValue(KeyD, 41);
                AssertValue(KeyE, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3"}));
                AssertValue(KeyF, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3" }));
            }, side1);

            RunOn(() =>
            {
                AssertValue(KeyA, 27);
                AssertValue(KeyB, 15);
                AssertValue(KeyD, 41);
                AssertValue(KeyE, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3", "e4" }));
                AssertValue(KeyF, ImmutableHashSet.CreateRange(new[] { "e1", "e3" }));
            }, side2);

            EnterBarrier("update-durin-split-verified");

            RunOn(() => TestConductor.Exit(Fourth, 0).Wait(TimeSpan.FromSeconds(5)), First);

            EnterBarrier("after-2");
        }

        public void Replicator_in_chaotic_cluster_should_converge_after_partition()
        {
            var side1 = new[] { First, Second };
            var side2 = new[] { Third, Fifth };
            RunOn(() =>
            {
                foreach (var a in side1)
                    foreach (var b in side2)
                        TestConductor.PassThrough(a, b, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(1));
            }, First);

            EnterBarrier("split-repaired");

            AssertValue(KeyA, 28);
            AssertValue(KeyB, 15);
            AssertValue(KeyC, 25);
            AssertValue(KeyD, 41);
            AssertValue(KeyE, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3", "e4" }));
            AssertValue(KeyF, ImmutableHashSet.CreateRange(new[] { "e1", "e3" }));
            AssertDeleted(KeyX);

            EnterBarrier("after-3");
        }

        protected override int InitialParticipantsValueFactory => Roles.Count;

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() => _cluster.Join(Node(to).Address), from);
            EnterBarrier(from.Name + "-joined");
        }

        private void AssertValue(IKey<IReplicatedData> key, object expected)
        {
            Within(TimeSpan.FromSeconds(10), () => AwaitAssert(() =>
            {
                _replicator.Tell(Dsl.Get(key, ReadLocal.Instance));
                var g = ExpectMsg<Replicator.GetSuccess>().Get(key);
                object value;
                if (g is GCounter) value = ((GCounter)g).Value;
                else if (g is PNCounter) value = ((PNCounter)g).Value;
                else if (g is GSet<string>) value = ((GSet<string>)g).Elements;
                else if (g is ORSet<string>) value = ((ORSet<string>)g).Elements;
                else throw new ArgumentException("input doesn't match");

                value.ShouldBe(expected);
            }));
        }

        private void AssertDeleted(IKey<IReplicatedData> key)
        {
            Within(TimeSpan.FromSeconds(5), () =>
            {
                AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.Get(key, ReadLocal.Instance));
                    ExpectMsg(new Replicator.DataDeleted(key));
                });
            });
        }
    }

    public class ReplicatorChaosSpecNode1 : ReplicatorChaosSpec { }
    public class ReplicatorChaosSpecNode2 : ReplicatorChaosSpec { }
    public class ReplicatorChaosSpecNode3 : ReplicatorChaosSpec { }
    public class ReplicatorChaosSpecNode4 : ReplicatorChaosSpec { }
    public class ReplicatorChaosSpecNode5 : ReplicatorChaosSpec { }

    public class ReplicatorChaosSpecConfig : MultiNodeConfig
    {
        public ReplicatorChaosSpecConfig()
        {
            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.cluster.roles = [""backend""]
                akka.log-dead-letters-during-shutdown = off");

            TestTransport = true;
        }
    }
}