//-----------------------------------------------------------------------
// <copyright file="ReplicatorChaosSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using FluentAssertions;

namespace Akka.DistributedData.Tests.MultiNode
{

    public class ReplicatorChaosSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }

        public ReplicatorChaosSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.cluster.roles = [""backend""]
                akka.log-dead-letters-during-shutdown = off")
                .WithFallback(DistributedData.DefaultConfig());

            TestTransport = true;
        }
    }

    public class ReplicatorChaosSpec : MultiNodeClusterSpec
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
        protected ReplicatorChaosSpec(ReplicatorChaosSpecConfig config) : base(config, typeof(ReplicatorChaosSpec))
        {
            _cluster = Akka.Cluster.Cluster.Get(Sys);
            _timeout = Dilated(TimeSpan.FromSeconds(3));
            _replicator = Sys.ActorOf(Replicator.Props(ReplicatorSettings.Create(Sys)
                .WithRole("backend")
                .WithGossipInterval(TimeSpan.FromSeconds(1))), "replicator");
        }

        [MultiNodeFact()]
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
                    ExpectMsg(new ReplicaCount(5));
                });
            });

            RunOn(() =>
            {
                for (var i = 0; i < 5; i++)
                {
                    _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 1)));
                    _replicator.Tell(Dsl.Update(KeyB, PNCounter.Empty, WriteLocal.Instance, x => x.Decrement(_cluster, 1)));
                    _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, new WriteAll(_timeout), x => x.Increment(_cluster, 1)));
                }
                ReceiveN(15).Select(x => x.GetType()).ToImmutableHashSet().ShouldBe(new[] { typeof(UpdateSuccess) });
            }, First);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 20)));
                _replicator.Tell(Dsl.Update(KeyB, PNCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster, 20)));
                _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, new WriteAll(_timeout), x => x.Increment(_cluster, 20)));

                ReceiveN(3).ToImmutableHashSet().ShouldBeEquivalentTo(new[]
                {
                    new UpdateSuccess(KeyA, null),
                    new UpdateSuccess(KeyB, null),
                    new UpdateSuccess(KeyC, null)
                });

                _replicator.Tell(Dsl.Update(KeyE, GSet<string>.Empty, WriteLocal.Instance, x => x.Add("e1").Add("e2")));
                ExpectMsg(new UpdateSuccess(KeyE, null));

                _replicator.Tell(Dsl.Update(KeyF, ORSet<string>.Empty, WriteLocal.Instance, x => x
                    .Add(_cluster, "e1")
                    .Add(_cluster, "e2")));
                ExpectMsg(new UpdateSuccess(KeyF, null));
            }, Second);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 40)));
                ExpectMsg(new UpdateSuccess(KeyD, null));

                _replicator.Tell(Dsl.Update(KeyE, GSet<string>.Empty, WriteLocal.Instance, x => x.Add("e2").Add("e3")));
                ExpectMsg(new UpdateSuccess(KeyE, null));

                _replicator.Tell(Dsl.Update(KeyF, ORSet<string>.Empty, WriteLocal.Instance, x => x
                    .Add(_cluster, "e2")
                    .Add(_cluster, "e3")));
                ExpectMsg(new UpdateSuccess(KeyF, null));
            }, Fourth);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyX, GCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster, 50)));
                ExpectMsg(new UpdateSuccess(KeyX, null));
                _replicator.Tell(Dsl.Delete(KeyX, WriteLocal.Instance));
                ExpectMsg(new DeleteSuccess(KeyX));
            }, Fifth);

            EnterBarrier("initial-updates-done");

            AssertValue(KeyA, 25UL);
            AssertValue(KeyB, new BigInteger(15.0));
            AssertValue(KeyC, 25UL);
            AssertValue(KeyD, 40UL);
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
                _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster, 1)));
                ExpectMsg(new UpdateSuccess(KeyA, null));
            }, First);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster, 2)));
                ExpectMsg(new UpdateSuccess(KeyA, null));

                _replicator.Tell(Dsl.Update(KeyE, GSet<string>.Empty, new WriteTo(2, _timeout), x => x.Add("e4")));
                ExpectMsg(new UpdateSuccess(KeyE, null));

                _replicator.Tell(Dsl.Update(KeyF, ORSet<string>.Empty, new WriteTo(2, _timeout), x => x.Remove(_cluster, "e2")));
                ExpectMsg(new UpdateSuccess(KeyF, null));
            }, Third);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster, 1)));
                ExpectMsg(new UpdateSuccess(KeyD, null));
            }, Fourth);

            EnterBarrier("update-during-split");

            RunOn(() =>
            {
                AssertValue(KeyA, 26UL);
                AssertValue(KeyB, new BigInteger(15.0));
                AssertValue(KeyD, 40UL);
                AssertValue(KeyE, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3"}));
                AssertValue(KeyF, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3" }));
            }, side1);

            RunOn(() =>
            {
                AssertValue(KeyA, 27UL);
                AssertValue(KeyB, new BigInteger(15.0));
                AssertValue(KeyD, 41UL);
                AssertValue(KeyE, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3", "e4" }));
                AssertValue(KeyF, ImmutableHashSet.CreateRange(new[] { "e1", "e3" }));
            }, side2);

            EnterBarrier("update-during-split-verified");

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

            AssertValue(KeyA, 28UL);
            AssertValue(KeyB, new BigInteger(15.0));
            AssertValue(KeyC, 25UL);
            AssertValue(KeyD, 41UL);
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
                var g = ExpectMsg<GetSuccess>().Get(key);
                object value;
                switch (g)
                {
                    case GCounter counter:
                        value = counter.Value;
                        break;
                    case PNCounter pnCounter:
                        value = pnCounter.Value;
                        break;
                    case GSet<string> set:
                        value = set.Elements;
                        break;
                    case ORSet<string> orSet:
                        value = orSet.Elements;
                        break;
                    default:
                        throw new ArgumentException("input doesn't match");
                }

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
                    ExpectMsg(new DataDeleted(key));
                });
            });
        }
    }
}
