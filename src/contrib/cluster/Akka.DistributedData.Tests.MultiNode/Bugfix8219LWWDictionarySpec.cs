//-----------------------------------------------------------------------
// <copyright file="Bugfix8219LWWDictionarySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
    public class Bugfix8219LWWDictionarySpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }

        public Bugfix8219LWWDictionarySpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = cluster
                akka.test.single-expect-default = 10s
                akka.log-dead-letters-during-shutdown = off")
                .WithFallback(DistributedData.DefaultConfig());

            TestTransport = true;
        }
    }

    /// <summary>
    /// Multi-node reproduction spec for
    /// https://github.com/akkadotnet/akka.net/issues/8219.
    /// Subscribers to a <see cref="LWWDictionary{TKey,TValue}"/> are reported to briefly
    /// receive <see cref="Changed"/> events whose entry count is strictly less than the
    /// writer ever wrote, even though the writer never removes entries and uses
    /// <see cref="WriteLocal"/>. We model that workload across 5 real-network nodes
    /// in two consecutive phases: (1) a noisy steady state with an injected network
    /// partition, and (2) a writer-migration handover (the customer's reliable
    /// repro). The invariant under test: no subscriber may ever observe an entry
    /// count strictly less than the seeded count, because the writer never removes
    /// entries.
    /// </summary>
    public class Bugfix8219LWWDictionarySpec : MultiNodeClusterSpec
    {
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;
        public readonly RoleName Fourth;
        public readonly RoleName Fifth;

        private readonly Cluster.Cluster _cluster;
        private readonly IActorRef _replicator;

        private const int InitialEntryCount = 300;
        // small batch of "changed agents" per cycle, like the customer's pattern
        private const int UpdatesPerCycle = 5;
        // steady-state cycles before the partition
        private const int PrePartitionCycles = 60;
        // cycles during/after the partition
        private const int PostPartitionCycles = 60;
        // cycles done by the original writer before migration
        private const int PreMigrationCycles = 60;
        // cycles done by the new writer after migration
        private const int PostMigrationCycles = 60;

        private readonly LWWDictionaryKey<int, string> _key = new("agent-list");

        // Per-node observation log; subscribed once at the start of the test and
        // drained at each assertion checkpoint.
        private readonly List<int> _observedCounts = new();
        private TestProbe _subscriberProbe;

        public Bugfix8219LWWDictionarySpec() : this(new Bugfix8219LWWDictionarySpecConfig()) { }

        protected Bugfix8219LWWDictionarySpec(Bugfix8219LWWDictionarySpecConfig config)
            : base(config, typeof(Bugfix8219LWWDictionarySpec))
        {
            _cluster = Akka.Cluster.Cluster.Get(Sys);

            // tight gossip + small delta budget keeps both delta-propagation and
            // gossip-catch-up paths active throughout the run
            var settings = ReplicatorSettings.Create(Sys)
                .WithGossipInterval(TimeSpan.FromMilliseconds(500))
                .WithNotifySubscribersInterval(TimeSpan.FromMilliseconds(200))
                .WithMaxDeltaElements(3);

            _replicator = Sys.ActorOf(Replicator.Props(settings), "replicator");

            First = config.First;
            Second = config.Second;
            Third = config.Third;
            Fourth = config.Fourth;
            Fifth = config.Fifth;
        }

        [MultiNodeFact]
        public async Task Bugfix8219_LWWDictionary_subscribers_should_not_observe_partial_state()
        {
            // First is the multi-node test controller and a regular subscriber.
            // Second is the writer (so First can later issue TestConductor.Exit
            // against the writer in the migration scenario without exiting itself).
            await JoinAsync(First, First);
            await JoinAsync(Second, First);
            await JoinAsync(Third, First);
            await JoinAsync(Fourth, First);
            await JoinAsync(Fifth, First);

            await WithinAsync(TimeSpan.FromSeconds(20), async () =>
            {
                await AwaitAssertAsync(() =>
                {
                    _replicator.Tell(Dsl.GetReplicaCount);
                    ExpectMsg(new ReplicaCount(5));
                });
            });

            await EnterBarrierAsync("cluster-up");

            // Every node subscribes a dedicated probe so Changed messages do not
            // mix with UpdateSuccess/GetSuccess on the TestActor mailbox. Subscribe
            // once at the start and reuse for the whole test.
            _subscriberProbe = CreateTestProbe();
            _replicator.Tell(Dsl.Subscribe(_key, _subscriberProbe.Ref));

            await EnterBarrierAsync("subscribed");

            // ---------------- Phase 1: seed + steady state with partition ----------------

            await RunOnAsync(async () =>
            {
                _replicator.Tell(SeedUpdate());
                await ExpectMsgAsync<UpdateSuccess>(msg => Equals(msg.Key, _key));
            }, Second);

            await EnterBarrierAsync("seeded");

            await WithinAsync(TimeSpan.FromSeconds(20), async () => await AwaitAssertAsync(() =>
            {
                _replicator.Tell(Dsl.Get(_key, ReadLocal.Instance));
                var msg = ExpectMsg<GetSuccess>(m => Equals(m.Key, _key));
                msg.Get(_key).Count.Should().Be(InitialEntryCount);
            }));

            await EnterBarrierAsync("converged");

            await RunOnAsync(() => DoSteadyStateUpdates(seed: 42, cycles: PrePartitionCycles), Second);

            await EnterBarrierAsync("pre-partition-done");

            // Partition Fifth from the writer + Third, forcing Fifth to recover
            // via gossip catch-up when restored
            await RunOnAsync(async () =>
            {
                await TestConductor.BlackholeAsync(Second, Fifth, ThrottleTransportAdapter.Direction.Both);
                await TestConductor.BlackholeAsync(Third, Fifth, ThrottleTransportAdapter.Direction.Both);
            }, First);

            await EnterBarrierAsync("partition-applied");

            await RunOnAsync(() => DoSteadyStateUpdates(seed: 137, cycles: PostPartitionCycles), Second);

            await EnterBarrierAsync("during-partition-done");

            await RunOnAsync(async () =>
            {
                await TestConductor.PassThroughAsync(Second, Fifth, ThrottleTransportAdapter.Direction.Both);
                await TestConductor.PassThroughAsync(Third, Fifth, ThrottleTransportAdapter.Direction.Both);
            }, First);

            await EnterBarrierAsync("partition-healed");

            await WithinAsync(TimeSpan.FromSeconds(20), async () => await AwaitAssertAsync(() =>
            {
                _replicator.Tell(Dsl.Get(_key, ReadLocal.Instance));
                var msg = ExpectMsg<GetSuccess>(m => Equals(m.Key, _key));
                msg.Get(_key).Count.Should().Be(InitialEntryCount);
            }));

            await EnterBarrierAsync("settled-after-partition");

            // Drain observed counts after Phase 1, on every node
            DrainObservedCounts();

            AssertObservationsMonotonic("after Phase 1 (steady state with partition)");

            await EnterBarrierAsync("phase1-asserted");

            // ---------------- Phase 2: writer migration (Second is killed) ----------------

            await RunOnAsync(() => DoSteadyStateUpdates(seed: 1001, cycles: PreMigrationCycles), Second);

            await EnterBarrierAsync("pre-handover-done");

            // The controller (First) forces Second (the writer) to exit, simulating
            // singleton failover when the host node stops without restart.
            await RunOnAsync(async () =>
            {
                await TestConductor.ExitAsync(Second, 0);
            }, First);

            await EnterBarrierAsync("second-exited");

            // Third takes over as the new writer
            await RunOnAsync(() => DoSteadyStateUpdates(seed: 1002, cycles: PostMigrationCycles), Third);

            await EnterBarrierAsync("handover-done");

            // Wait for cluster + DData to settle on the remaining nodes. The
            // departed Second is not in the assertion set.
            await RunOnAsync(async () =>
            {
                await WithinAsync(TimeSpan.FromSeconds(60), async () => await AwaitAssertAsync(() =>
                {
                    _replicator.Tell(Dsl.Get(_key, ReadLocal.Instance));
                    var msg = ExpectMsg<GetSuccess>(m => Equals(m.Key, _key));
                    msg.Get(_key).Count.Should().Be(InitialEntryCount);
                }));
            }, First, Third, Fourth, Fifth);

            await EnterBarrierAsync("settled-after-migration");

            // Drain and assert on the surviving nodes
            await RunOnAsync(() =>
            {
                DrainObservedCounts();
                AssertObservationsMonotonic("after Phase 2 (writer migration)");
                return Task.CompletedTask;
            }, First, Third, Fourth, Fifth);

            await EnterBarrierAsync("done");
        }

        private Update SeedUpdate() =>
            Dsl.Update(_key, LWWDictionary<int, string>.Empty, WriteLocal.Instance, dict =>
            {
                var cur = dict;
                for (var k = 0; k < InitialEntryCount; k++)
                    cur = cur.SetItem(_cluster, k, $"v0_{k}");
                return cur;
            });

        private async Task DoSteadyStateUpdates(int seed, int cycles)
        {
            var rng = new Random(seed);
            for (var cycle = 1; cycle <= cycles; cycle++)
            {
                var toUpdate = Enumerable.Range(0, InitialEntryCount)
                    .OrderBy(_ => rng.Next())
                    .Take(UpdatesPerCycle)
                    .ToArray();
                var version = cycle;
                var seedCapture = seed;

                _replicator.Tell(Dsl.Update(_key, LWWDictionary<int, string>.Empty, WriteLocal.Instance, dict =>
                {
                    var cur = dict;
                    foreach (var k in toUpdate)
                        cur = cur.SetItem(_cluster, k, $"v{seedCapture}_{version}_{k}");
                    return cur;
                }));
                await ExpectMsgAsync<UpdateSuccess>(msg => Equals(msg.Key, _key));
            }
        }

        private void DrainObservedCounts()
        {
            if (_subscriberProbe is null) return;
            while (true)
            {
                if (!_subscriberProbe.TryReceiveOne(out var env, TimeSpan.Zero)) break;
                if (env.Message is Changed c && c.Data is LWWDictionary<int, string> dict)
                    _observedCounts.Add(dict.Count);
            }
        }

        private void AssertObservationsMonotonic(string contextLabel)
        {
            // After the seed converges, every subsequent Changed event on every
            // subscriber must report at least InitialEntryCount entries. We only
            // assert if we have observations; nodes that never produced Changed
            // events (e.g. departed nodes) are skipped silently elsewhere.
            if (_observedCounts.Count == 0) return;

            var minCount = _observedCounts.Min();
            minCount.Should().BeGreaterOrEqualTo(InitialEntryCount,
                $"{contextLabel}: node {_cluster.SelfAddress} observed a Changed event " +
                $"with {minCount} entries (seed was {InitialEntryCount}); " +
                $"full sequence: [{string.Join(", ", _observedCounts)}]");
        }

        protected override int InitialParticipantsValueFactory => Roles.Count;

        private async Task JoinAsync(RoleName from, RoleName to)
        {
            await RunOnAsync(() =>
            {
                _cluster.Join(Node(to).Address);
                return Task.CompletedTask;
            }, from);
            await EnterBarrierAsync(from.Name + "-joined");
        }
    }
}
