//-----------------------------------------------------------------------
// <copyright file="ClusterShardingRegistrationCoordinatedShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.TestKit;
using FluentAssertions;
using Akka.Event;
using Akka.MultiNode.TestAdapter;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingRegistrationCoordinatedShutdownSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterShardingRegistrationCoordinatedShutdownSpecConfig()
            : base(
                loglevel: "DEBUG",
                // The `test` task below waits, on the spec's own thread/budget, for the shard home
                // to arrive - which can take several seconds while the coordinator singleton hands
                // off from `second` to `first`. Without this, an incomplete task would arm the
                // default 5s `before-cluster-shutdown` phase timeout and CoordinatedShutdown would
                // stop the region out from under the still-waiting task.
                additionalConfig: "akka.coordinated-shutdown.phases.before-cluster-shutdown.timeout = 30s")
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
        }
    }

    public class ClusterShardingRegistrationCoordinatedShutdownSpec : MultiNodeClusterShardingSpec<ClusterShardingRegistrationCoordinatedShutdownSpecConfig>
    {
        #region setup

        private readonly Lazy<IActorRef> _region;

        public ClusterShardingRegistrationCoordinatedShutdownSpec()
            : this(new ClusterShardingRegistrationCoordinatedShutdownSpecConfig(), typeof(ClusterShardingRegistrationCoordinatedShutdownSpec))
        {
        }

        protected ClusterShardingRegistrationCoordinatedShutdownSpec(ClusterShardingRegistrationCoordinatedShutdownSpecConfig config, Type type)
            : base(config, type)
        {
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
        }

        #endregion

        [MultiNodeFact]
        public async Task ClusterShardingRegistrationCoordinatedShutdownSpecs()
        {
            await Region_registration_during_CoordinatedShutdown_must_try_next_oldest();
        }

        private async Task Region_registration_during_CoordinatedShutdown_must_try_next_oldest()
        {
            // This 60s is a ceiling sized above every bounded wait this block contains, not a
            // widened wait of its own. The "test" CoordinatedShutdown task below bounds its
            // ExpectMsgAsync at 30s (matching before-cluster-shutdown.timeout in the
            // config above), and csTaskDone's ExpectMsgAsync<Done> immediately after it bounds at
            // 20s - both run sequentially on Config.Third, so 30 + 20 = 50s for those two
            // alone. The rest of this block (three JoinAsync calls, the members-up
            // AwaitAssertAsync, StartSharding, and two EnterBarrierAsync calls) has never measured
            // above a couple of seconds locally, so 60s keeps 10s of margin above that 50s sum -
            // enough that WithinAsync's own outer race timer (TestKitBase_Within.cs:346-350) does
            // not fire before either inner bound gets a chance to.
            await WithinAsync(TimeSpan.FromSeconds(60), async () =>
            {
                // second should be oldest
                await JoinAsync(Config.Second, Config.Second);
                await JoinAsync(Config.First, Config.Second);
                await JoinAsync(Config.Third, Config.Second);

                await AwaitAssertAsync(() =>
                {
                    Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3);
                });

                var csTaskDone = CreateTestProbe();
                await RunOnAsync(() =>
                {
                    CoordinatedShutdown.Get(Sys).AddTask(CoordinatedShutdown.PhaseBeforeClusterShutdown, "test", async () =>
                    {
                        await Task.Delay(200);
                        // Bound this explicitly at 30s - the same 30s the config above
                        // gives before-cluster-shutdown.timeout - rather than calling
                        // ExpectMsgAsync(1) with no timeout. An unbounded call resolves through
                        // RemainingOrDefault, which on THIS TestKitBase instance falls back to
                        // whatever the outer WithinAsync below set as its deadline when the block
                        // started (TestKitBase.cs:483-504) - i.e. the block's remainder, not a
                        // fresh 30s. By the time this task actually runs (after the coordinator
                        // singleton hands off from `second` to `first`), a meaningful slice of that
                        // outer budget can already be gone, so an unbounded wait here would shrink
                        // over time and, on a slow enough agent, end up shorter than the 5s
                        // TestProbe default (akka.test.single-expect-default) it was written to
                        // beat. An explicit 30s gives it the same fresh window every run,
                        // tied to the timeout that actually governs how long CoordinatedShutdown
                        // will wait on this task, independent of how much of the outer block's
                        // budget has already elapsed. ExpectMsgAsync dilates an explicit timeout
                        // itself (RemainingOrDilated), so it is passed undilated here.
                        //
                        // A TestProbe is its own TestKitBase with its own deadline state, so it
                        // would never see either budget - calling ExpectMsgAsync on the spec itself
                        // does. This mirrors the JVM spec, which sends from its own test actor.
                        //
                        // This task body runs on a thread pool thread, not the test thread, so the
                        // implicit sender isn't set there - pass TestActor explicitly or this would
                        // dead-letter.
                        //
                        // Making this body async means AddTask's delegate now returns an incomplete
                        // Task the moment it hits its first await, instead of the old synchronous
                        // body's already-completed Task.FromResult - so CoordinatedShutdown actually
                        // waits on it, up to the phase's own timeout. That is safe only because the
                        // config above raises before-cluster-shutdown's timeout to 30s; at the
                        // default 5s, CoordinatedShutdown would time the phase out and tear the
                        // region down while this task is still waiting on the shard-home handoff.
                        _region.Value.Tell(1, TestActor);
                        await ExpectMsgAsync(1, TimeSpan.FromSeconds(30));
                        csTaskDone.Ref.Tell(Done.Instance);
                        return Done.Instance;
                    });
                    return Task.CompletedTask;
                }, Config.Third);

                StartSharding(
                    Sys,
                    typeName: "Entity",
                    entityProps: Props.Create(() => new ShardedEntity()));

                await EnterBarrierAsync("before-shutdown");

                await RunOnAsync(async () =>
                {
                    // Fire-and-forget, same as before the migration: the assertions below poll
                    // Cluster.IsTerminated rather than awaiting this task directly.
                    _ = CoordinatedShutdown.Get(Sys).Run(CoordinatedShutdown.UnknownReason.Instance);
                    await AwaitConditionAsync(() => Cluster.IsTerminated);
                }, Config.Second);

                await RunOnAsync(async () =>
                {
                    // Fire-and-forget, same as before the migration: the assertions below poll
                    // Cluster.IsTerminated rather than awaiting this task directly.
                    _ = CoordinatedShutdown.Get(Sys).Run(CoordinatedShutdown.UnknownReason.Instance);
                    await AwaitConditionAsync(() => Cluster.IsTerminated);

                    // csTaskDone is its own TestProbe / TestKitBase and never inherits this
                    // spec's Within budget, so its wait needs an explicit bound, which ExpectMsgAsync dilates. A
                    // clean local run measured the full handoff this depends on - the
                    // coordinator singleton migrating to `first`, the shard home for [1]
                    // arriving, and the "test" task's ExpectMsgAsync/Tell above completing -
                    // at 5.6s; 20s leaves roughly 3.5x margin for slower/loaded CI machines
                    // while still finishing well inside the phase's own 30s timeout.
                    await csTaskDone.ExpectMsgAsync<Done>(TimeSpan.FromSeconds(20));
                }, Config.Third);

                await EnterBarrierAsync("after-shutdown");

                await RunOnAsync(async () =>
                {
                    _region.Value.Tell(2);
                    await ExpectMsgAsync(2);
                    LastSender.Path.Address.HasLocalScope.Should().BeTrue();
                }, Config.First);

                await EnterBarrierAsync("after-1");
            });
        }
    }
}
