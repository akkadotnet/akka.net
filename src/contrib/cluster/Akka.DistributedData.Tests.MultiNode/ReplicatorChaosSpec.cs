//-----------------------------------------------------------------------
// <copyright file="ReplicatorChaosSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
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
        public static readonly RoleName First = new("first");
        public static readonly RoleName Second = new("second");
        public static readonly RoleName Third = new("third");
        public static readonly RoleName Fourth = new("fourth");
        public static readonly RoleName Fifth = new("fifth");

        /// <summary>
        /// Raw budget for the WriteTo/WriteAll consistency levels used below.
        /// <see cref="_timeout"/> holds the dilated form. The replicator schedules on real
        /// time, so the dilation is applied before the value reaches it.
        /// </summary>
        private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Budget for a test-side expect that waits on a WriteTo/WriteAll reply.
        /// <para>
        /// A write aggregator answers - UpdateSuccess or UpdateTimeout - no earlier than
        /// its own deadline, <see cref="WriteTimeout"/>, counted from the moment the
        /// replicator picks the Update up. That moment is at or after the expect that
        /// waits for the answer. An unbounded expect inherits
        /// akka.test.single-expect-default, which is 3s here - the same 3s as
        /// <see cref="WriteTimeout"/> - so it always expires first. The wait is a dead
        /// heat with the thing it waits on.
        /// </para>
        /// <para>
        /// Budget = <see cref="WriteTimeout"/>, the aggregator deadline this expect waits
        /// on, plus one akka.test.single-expect-default (3s) of slack for the local hop
        /// that carries the answer. 3s is exactly what these expects used to get.
        /// </para>
        /// <para>
        /// Undilated on purpose. TestKit dilates whatever timeout it is handed, so passing
        /// the already-dilated <see cref="_timeout"/> would dilate twice.
        /// </para>
        /// </summary>
        private static readonly TimeSpan WriteReplyTimeout = WriteTimeout + TimeSpan.FromSeconds(3);

        /// <summary>
        /// Per-attempt budget inside the AwaitAssert loops below. An unbounded inner
        /// expect inherits the whole remaining loop budget, so the loop makes one attempt
        /// and then a useless one - CI logged a final attempt with a 00:00:00.0000044
        /// budget. 1s per attempt keeps every attempt equal and leaves the loop budget to
        /// decide how many attempts run.
        /// </summary>
        private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Poll interval for the AwaitAssert loops. Each attempt allocates a fresh probe,
        /// so keep the attempt count modest.
        /// </summary>
        private static readonly TimeSpan AttemptInterval = TimeSpan.FromMilliseconds(500);

        private readonly Cluster.Cluster _cluster;
        private readonly IActorRef _replicator;
        private readonly TimeSpan _timeout;

        public readonly GCounterKey KeyA = new("A");
        public readonly PNCounterKey KeyB = new("B");
        public readonly GCounterKey KeyC = new("C");
        public readonly GCounterKey KeyD = new("D");
        public readonly GSetKey<string> KeyE = new("E");
        public readonly ORSetKey<string> KeyF = new("F");
        public readonly GCounterKey KeyX = new("X");

        public ReplicatorChaosSpec() : this(new ReplicatorChaosSpecConfig()) { }
        protected ReplicatorChaosSpec(ReplicatorChaosSpecConfig config) : base(config, typeof(ReplicatorChaosSpec))
        {
            _cluster = Akka.Cluster.Cluster.Get(Sys);
            _timeout = Dilated(WriteTimeout);
            _replicator = Sys.ActorOf(Replicator.Props(ReplicatorSettings.Create(Sys)
                .WithRole("backend")
                .WithGossipInterval(TimeSpan.FromSeconds(1))), "replicator");
        }

        [MultiNodeFact()]
        public async Task ReplicatorChaos_Tests()
        {
            await Replicator_in_chaotic_cluster_should_replicate_data_in_initial_phase();
            await Replicator_in_chaotic_cluster_should_be_available_during_network_split();
            await Replicator_in_chaotic_cluster_should_converge_after_partition();
        }

        public async Task Replicator_in_chaotic_cluster_should_replicate_data_in_initial_phase()
        {
            await JoinAsync(First, First);
            await JoinAsync(Second, First);
            await JoinAsync(Third, First);
            await JoinAsync(Fourth, First);
            await JoinAsync(Fifth, First);

            // Every node proves its own replicator has seen all five members. ReplicaCount
            // answers _nodes.Count + 1, and _nodes only holds members the replicator has
            // already processed a MemberUp for, so ReplicaCount(5) means this replicator
            // knows every other node.
            await AwaitAssertAsync(async () =>
            {
                // Fresh probe per attempt. A late ReplicaCount from a timed-out attempt
                // must not be read as this attempt's answer.
                var probe = CreateTestProbe();
                _replicator.Tell(Dsl.GetReplicaCount, probe.Ref);
                await probe.ExpectMsgAsync(new ReplicaCount(5), AttemptTimeout);
            }, TimeSpan.FromSeconds(10), AttemptInterval);

            // The barrier turns the per-node fact above into a global one, and it is
            // required before any node writes.
            //
            // A replicator drops a Write whose sender it does not yet know - it logs
            // "Ignoring message [Write] from [...] unknown node" - and sends no ack. A
            // WriteAll update has no re-send path to recover from that: every node is
            // primary, so SendToSecondary re-sends to nobody, and a GCounter update
            // carries no delta on the aggregator, so the delta re-send does not run
            // either. One dropped Write therefore costs the whole update; it can only end
            // in UpdateTimeout.
            //
            // Without this barrier each node starts writing the moment it passes its own
            // check. CI caught first writing 0.8s before fifth received its Welcome; fifth
            // ignored all five Writes and every KeyC update timed out.
            await EnterBarrierAsync("replicas-ready");

            await RunOnAsync(async () =>
            {
                for (var i = 0; i < 5; i++)
                {
                    _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 1)));
                    _replicator.Tell(Dsl.Update(KeyB, PNCounter.Empty, WriteLocal.Instance, x => x.Decrement(_cluster, 1)));
                    _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, new WriteAll(_timeout), x => x.Increment(_cluster, 1)));
                }

                // Five of these fifteen replies come from WriteAll aggregators, so this
                // collect needs WriteReplyTimeout - see the note on that field. CI lost
                // that race by about 70ms. The Tell burst started at 40.136, an unbounded
                // ReceiveN gave up 00:00:02.9997005 later with "Only got 10", and the
                // aggregators answered at 43.226. The ten replies it did collect were
                // exactly the WriteLocal ones, which need no aggregator.
                var replies = await CollectRepliesAsync(15, WriteReplyTimeout);
                replies.Select(x => x.GetType()).ToImmutableHashSet().ShouldBe(new[] { typeof(UpdateSuccess) });
            }, First);

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 20)));
                _replicator.Tell(Dsl.Update(KeyB, PNCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster, 20)));
                _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, new WriteAll(_timeout), x => x.Increment(_cluster, 20)));

                // Two of these three replies come from write aggregates.
                var replies = await CollectRepliesAsync(3, WriteReplyTimeout);
                replies.ToImmutableHashSet().Should().BeEquivalentTo(new[]
                {
                    new UpdateSuccess(KeyA, null),
                    new UpdateSuccess(KeyB, null),
                    new UpdateSuccess(KeyC, null)
                });

                _replicator.Tell(Dsl.Update(KeyE, GSet<string>.Empty, WriteLocal.Instance, x => x.Add("e1").Add("e2")));
                await ExpectMsgAsync(new UpdateSuccess(KeyE, null));

                _replicator.Tell(Dsl.Update(KeyF, ORSet<string>.Empty, WriteLocal.Instance, x => x
                    .Add(_cluster, "e1")
                    .Add(_cluster, "e2")));
                await ExpectMsgAsync(new UpdateSuccess(KeyF, null));
            }, Second);

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 40)));
                await ExpectMsgAsync(new UpdateSuccess(KeyD, null));

                _replicator.Tell(Dsl.Update(KeyE, GSet<string>.Empty, WriteLocal.Instance, x => x.Add("e2").Add("e3")));
                await ExpectMsgAsync(new UpdateSuccess(KeyE, null));

                _replicator.Tell(Dsl.Update(KeyF, ORSet<string>.Empty, WriteLocal.Instance, x => x
                    .Add(_cluster, "e2")
                    .Add(_cluster, "e3")));
                await ExpectMsgAsync(new UpdateSuccess(KeyF, null));
            }, Fourth);

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Update(KeyX, GCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster, 50)));
                await ExpectMsgAsync(new UpdateSuccess(KeyX, null), WriteReplyTimeout);
                _replicator.Tell(Dsl.Delete(KeyX, WriteLocal.Instance));
                await ExpectMsgAsync(new DeleteSuccess(KeyX));
            }, Fifth);

            await EnterBarrierAsync("initial-updates-done");

            await AssertValueAsync(KeyA, 25UL);
            await AssertValueAsync(KeyB, new BigInteger(15.0));
            await AssertValueAsync(KeyC, 25UL);
            await AssertValueAsync(KeyD, 40UL);
            await AssertValueAsync(KeyE, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3" }));
            await AssertValueAsync(KeyF, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3" }));
            await AssertDeletedAsync(KeyX);

            await EnterBarrierAsync("after-1");
        }

        public async Task Replicator_in_chaotic_cluster_should_be_available_during_network_split()
        {
            var side1 = new[] { First, Second };
            var side2 = new[] { Third, Fourth, Fifth };

            await RunOnAsync(async () =>
            {
                foreach (var a in side1)
                    foreach (var b in side2)
                        await TestConductor.Blackhole(a, b, ThrottleTransportAdapter.Direction.Both);
            }, First);

            await EnterBarrierAsync("split");

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster, 1)));
                await ExpectMsgAsync(new UpdateSuccess(KeyA, null), WriteReplyTimeout);
            }, First);

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Update(KeyA, GCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster, 2)));
                await ExpectMsgAsync(new UpdateSuccess(KeyA, null), WriteReplyTimeout);

                _replicator.Tell(Dsl.Update(KeyE, GSet<string>.Empty, new WriteTo(2, _timeout), x => x.Add("e4")));
                await ExpectMsgAsync(new UpdateSuccess(KeyE, null), WriteReplyTimeout);

                _replicator.Tell(Dsl.Update(KeyF, ORSet<string>.Empty, new WriteTo(2, _timeout), x => x.Remove(_cluster, "e2")));
                await ExpectMsgAsync(new UpdateSuccess(KeyF, null), WriteReplyTimeout);
            }, Third);

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty, new WriteTo(2, _timeout), x => x.Increment(_cluster, 1)));
                await ExpectMsgAsync(new UpdateSuccess(KeyD, null), WriteReplyTimeout);
            }, Fourth);

            await EnterBarrierAsync("update-during-split");

            await RunOnAsync(async () =>
            {
                await AssertValueAsync(KeyA, 26UL);
                await AssertValueAsync(KeyB, new BigInteger(15.0));
                await AssertValueAsync(KeyD, 40UL);
                await AssertValueAsync(KeyE, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3"}));
                await AssertValueAsync(KeyF, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3" }));
            }, side1);

            await RunOnAsync(async () =>
            {
                await AssertValueAsync(KeyA, 27UL);
                await AssertValueAsync(KeyB, new BigInteger(15.0));
                await AssertValueAsync(KeyD, 41UL);
                await AssertValueAsync(KeyE, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3", "e4" }));
                await AssertValueAsync(KeyF, ImmutableHashSet.CreateRange(new[] { "e1", "e3" }));
            }, side2);

            await EnterBarrierAsync("update-during-split-verified");

            await RunOnAsync(async () => await TestConductor.Exit(Fourth, 0), First);

            await EnterBarrierAsync("after-2");
        }

        public async Task Replicator_in_chaotic_cluster_should_converge_after_partition()
        {
            var side1 = new[] { First, Second };
            var side2 = new[] { Third, Fifth };
            await RunOnAsync(async () =>
            {
                foreach (var a in side1)
                    foreach (var b in side2)
                        await TestConductor.PassThrough(a, b, ThrottleTransportAdapter.Direction.Both);
            }, First);

            await EnterBarrierAsync("split-repaired");

            await AssertValueAsync(KeyA, 28UL);
            await AssertValueAsync(KeyB, new BigInteger(15.0));
            await AssertValueAsync(KeyC, 25UL);
            await AssertValueAsync(KeyD, 41UL);
            await AssertValueAsync(KeyE, ImmutableHashSet.CreateRange(new[] { "e1", "e2", "e3", "e4" }));
            await AssertValueAsync(KeyF, ImmutableHashSet.CreateRange(new[] { "e1", "e3" }));
            await AssertDeletedAsync(KeyX);

            await EnterBarrierAsync("after-3");
        }

        protected override int InitialParticipantsValueFactory => Roles.Count;

        private async Task JoinAsync(RoleName from, RoleName to)
        {
            RunOn(() => _cluster.Join(Node(to).Address), from);
            await EnterBarrierAsync(from.Name + "-joined");
        }

        /// <summary>
        /// Collects <paramref name="count"/> messages from the test actor within
        /// <paramref name="max"/>.
        /// </summary>
        private async Task<IReadOnlyList<object>> CollectRepliesAsync(int count, TimeSpan max)
        {
            var received = new List<object>(count);
            await foreach (var message in ReceiveNAsync(count, max))
                received.Add(message);
            return received;
        }

        private async Task AssertValueAsync(IKey<IReplicatedData> key, object expected)
        {
            await AwaitAssertAsync(async () =>
            {
                // Fresh probe and an explicit bound per attempt - see the note on
                // AttemptTimeout. The probe also keeps a late GetSuccess from a timed-out
                // attempt out of the next attempt's queue, where it would be read as a
                // stale answer.
                var probe = CreateTestProbe();
                _replicator.Tell(Dsl.Get(key, ReadLocal.Instance), probe.Ref);
                var g = (await probe.ExpectMsgAsync<GetSuccess>(AttemptTimeout)).Get(key);
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
            }, TimeSpan.FromSeconds(10), AttemptInterval);
        }

        private async Task AssertDeletedAsync(IKey<IReplicatedData> key)
        {
            await AwaitAssertAsync(async () =>
            {
                // Same shape as AssertValueAsync: fresh probe, explicit per-attempt bound.
                var probe = CreateTestProbe();
                _replicator.Tell(Dsl.Get(key, ReadLocal.Instance), probe.Ref);
                await probe.ExpectMsgAsync(new DataDeleted(key), AttemptTimeout);
            }, TimeSpan.FromSeconds(5), AttemptInterval);
        }
    }
}
