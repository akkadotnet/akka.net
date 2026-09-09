//-----------------------------------------------------------------------
// <copyright file="ClusterShardingRegistrationCoordinatedShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
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
        public void ClusterShardingRegistrationCoordinatedShutdownSpecs()
        {
            Region_registration_during_CoordinatedShutdown_must_try_next_oldest();
        }

        private void Region_registration_during_CoordinatedShutdown_must_try_next_oldest()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                // second should be oldest
                Join(Config.Second, Config.Second);
                Join(Config.First, Config.Second);
                Join(Config.Third, Config.Second);

                AwaitAssert(() =>
                {
                    Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3);
                });

                var csTaskDone = CreateTestProbe();
                RunOn(() =>
                {
                    CoordinatedShutdown.Get(Sys).AddTask(CoordinatedShutdown.PhaseBeforeClusterShutdown, "test", () =>
                    {
                        Thread.Sleep(200);
                        // Wait on the spec's own Within(30s) budget rather than a TestProbe's flat
                        // akka.test.single-expect-default (5s): the shard home for [1] can't arrive
                        // until the coordinator singleton hands off from `second` to `first`, which
                        // can take longer than 5s. A TestProbe is its own TestKitBase with its own
                        // deadline state, so it would never see this spec's Within budget - calling
                        // ExpectMsg on the spec itself does. This mirrors the JVM spec, which sends
                        // from its own test actor.
                        //
                        // This task body runs on a thread pool thread, not the test thread, so the
                        // implicit sender isn't set there - pass TestActor explicitly or this would
                        // dead-letter.
                        _region.Value.Tell(1, TestActor);
                        ExpectMsg(1);
                        csTaskDone.Ref.Tell(Done.Instance);
                        return Task.FromResult(Done.Instance);
                    });
                }, Config.Third);

                StartSharding(
                    Sys,
                    typeName: "Entity",
                    entityProps: Props.Create(() => new ShardedEntity()));

                EnterBarrier("before-shutdown");

                RunOn(() =>
                {
                    CoordinatedShutdown.Get(Sys).Run(CoordinatedShutdown.UnknownReason.Instance);
                    AwaitCondition(() => Cluster.IsTerminated);
                }, Config.Second);

                RunOn(() =>
                {
                    CoordinatedShutdown.Get(Sys).Run(CoordinatedShutdown.UnknownReason.Instance);
                    AwaitCondition(() => Cluster.IsTerminated);
                    csTaskDone.ExpectMsg<Done>();
                }, Config.Third);

                EnterBarrier("after-shutdown");

                RunOn(() =>
                {
                    _region.Value.Tell(2);
                    ExpectMsg(2);
                    LastSender.Path.Address.HasLocalScope.Should().BeTrue();
                }, Config.First);

                EnterBarrier("after-1");
            });
        }
    }
}
