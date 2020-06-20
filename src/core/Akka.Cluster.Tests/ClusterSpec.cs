//-----------------------------------------------------------------------
// <copyright file="ClusterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using FluentAssertions;
using Xunit.Abstractions;

namespace Akka.Cluster.Tests
{
    public class ClusterSpec : AkkaSpec
    {

        /*
         * Portability note: all of the _cluster.Join(selfAddress) calls are necessary here, whereas they are not in the JVM
         * because the JVM test suite relies on side-effects from one test to another, whereas all of our tests are fully isolated.
         */

        const string Config = @"    
            akka.cluster {
              auto-down-unreachable-after = 0s
              periodic-tasks-initial-delay = 120 s
              publish-stats-interval = 0 s # always, when it happens
              run-coordinated-shutdown-when-down = off
            }
            akka.actor.serialize-messages = on
            akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            akka.coordinated-shutdown.terminate-actor-system = off
            akka.coordinated-shutdown.run-by-actor-system-terminate = off
            akka.remote.log-remote-lifecycle-events = off
            akka.remote.dot-netty.tcp.port = 0";

        public IActorRef Self { get { return TestActor; } }

        readonly Address _selfAddress;
        readonly Cluster _cluster;

        internal ClusterReadView ClusterView { get { return _cluster.ReadView; } }

        public ClusterSpec(ITestOutputHelper output)
            : base(Config, output)
        {
            _selfAddress = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
            _cluster = Cluster.Get(Sys);
        }

        internal void LeaderActions()
        {
            _cluster.ClusterCore.Tell(InternalClusterAction.LeaderActionsTick.Instance);
        }

        [Fact]
        public void A_cluster_must_use_the_address_of_the_remote_transport()
        {
            _cluster.SelfAddress.Should().Be(_selfAddress);
        }

        [Fact]
        public void A_cluster_must_initially_become_singleton_cluster_when_joining_itself_and_reach_convergence()
        {
            ClusterView.Members.Count.Should().Be(0);
            _cluster.Join(_selfAddress);
            LeaderActions(); // Joining -> Up
            AwaitCondition(() => ClusterView.IsSingletonCluster);
            ClusterView.Self.Address.Should().Be(_selfAddress);
            ClusterView.Members.Select(m => m.Address).ToImmutableHashSet()
                .Should().BeEquivalentTo(ImmutableHashSet.Create(_selfAddress));
            AwaitAssert(() => ClusterView.Status.Should().Be(MemberStatus.Up));
        }

        [Fact]
        public void A_cluster_must_publish_initial_state_as_snapshot_to_subscribers()
        {
            try
            {
                _cluster.Subscribe(TestActor, ClusterEvent.InitialStateAsSnapshot, new[] { typeof(ClusterEvent.IMemberEvent) });
                ExpectMsg<ClusterEvent.CurrentClusterState>();
            }
            finally
            {
                _cluster.Unsubscribe(TestActor);
            }
        }

        [Fact]
        public void A_cluster_must_publish_initial_state_as_events_to_subscribers()
        {
            try
            {
                _cluster.Join(_selfAddress);
                LeaderActions(); // Joining -> Up

                _cluster.Subscribe(TestActor, ClusterEvent.InitialStateAsEvents, new[] { typeof(ClusterEvent.IMemberEvent) });
                ExpectMsg<ClusterEvent.MemberUp>();
            }
            finally
            {
                _cluster.Unsubscribe(TestActor);
            }
        }

        [Fact]
        public void A_cluster_must_send_current_cluster_state_to_one_receiver_when_requested()
        {
            _cluster.SendCurrentClusterState(TestActor);
            ExpectMsg<ClusterEvent.CurrentClusterState>();
        }

        [Fact]
        public void A_cluster_must_publish_member_removed_when_shutdown()
        {
            var callbackProbe = CreateTestProbe();
            _cluster.RegisterOnMemberRemoved(() =>
            {
                callbackProbe.Tell("OnMemberRemoved");
            });

            _cluster.RegisterOnMemberUp(() =>
            {
                callbackProbe.Tell("OnMemberUp");
            });

            _cluster.Join(_selfAddress);
            LeaderActions(); // Joining -> Up
            callbackProbe.ExpectMsg("OnMemberUp"); // verify that callback hooks are registered


            _cluster.Subscribe(TestActor, new[] { typeof(ClusterEvent.MemberRemoved) });
            // first, is in response to the subscription
            ExpectMsg<ClusterEvent.CurrentClusterState>();

            _cluster.Shutdown();
            ExpectMsg<ClusterEvent.MemberRemoved>().Member.Address.Should().Be(_selfAddress);

            callbackProbe.ExpectMsg("OnMemberRemoved");
        }

        /// <summary>
        /// https://github.com/akkadotnet/akka.net/issues/2442
        /// </summary>
        [Fact]
        public void BugFix_2442_RegisterOnMemberUp_should_fire_if_node_already_up()
        {
            _cluster.Join(_selfAddress);
            LeaderActions(); // Joining -> Up

            // Member should already be up
            _cluster.Subscribe(TestActor, ClusterEvent.InitialStateAsEvents, new[] { typeof(ClusterEvent.IMemberEvent) });
            ExpectMsg<ClusterEvent.MemberUp>();

            var callbackProbe = CreateTestProbe();
            _cluster.RegisterOnMemberUp(() =>
            {
                callbackProbe.Tell("RegisterOnMemberUp");
            });
            callbackProbe.ExpectMsg("RegisterOnMemberUp");
        }

        [Fact]
        public void A_cluster_must_complete_LeaveAsync_task_upon_being_removed()
        {
            var sys2 = ActorSystem.Create("ClusterSpec2", ConfigurationFactory.ParseString(@"
                akka.actor.provider = ""cluster""
                akka.remote.dot-netty.tcp.port = 0
                akka.coordinated-shutdown.run-by-clr-shutdown-hook = off
                akka.coordinated-shutdown.terminate-actor-system = off
                akka.coordinated-shutdown.run-by-actor-system-terminate = off
                akka.cluster.run-coordinated-shutdown-when-down = off
            ").WithFallback(Akka.TestKit.Configs.TestConfigs.DefaultConfig));

            var probe = CreateTestProbe(sys2);
            Cluster.Get(sys2).Subscribe(probe.Ref, typeof(ClusterEvent.IMemberEvent));
            probe.ExpectMsg<ClusterEvent.CurrentClusterState>();

            Cluster.Get(sys2).Join(Cluster.Get(sys2).SelfAddress);
            probe.ExpectMsg<ClusterEvent.MemberUp>();

            var leaveTask = Cluster.Get(sys2).LeaveAsync();

            leaveTask.IsCompleted.Should().BeFalse();
            probe.ExpectMsg<ClusterEvent.MemberLeft>();
            // MemberExited might not be published before MemberRemoved
            var removed = (ClusterEvent.MemberRemoved)probe.FishForMessage(m => m is ClusterEvent.MemberRemoved);
            removed.PreviousStatus.ShouldBeEquivalentTo(MemberStatus.Exiting);

            AwaitCondition(() => leaveTask.IsCompleted);

            // A second call for LeaveAsync should complete immediately (should be the same task as before)
            Cluster.Get(sys2).LeaveAsync().IsCompleted.Should().BeTrue();
        }

        [Fact]
        public void A_cluster_must_return_completed_LeaveAsync_task_if_member_already_removed()
        {
            // Join cluster
            _cluster.Join(_selfAddress);
            LeaderActions(); // Joining -> Up

            // Subscribe to MemberRemoved and wait for confirmation
            _cluster.Subscribe(TestActor, typeof(ClusterEvent.MemberRemoved));
            ExpectMsg<ClusterEvent.CurrentClusterState>();

            // Leave the cluster prior to calling LeaveAsync()
            _cluster.Leave(_selfAddress);

            Within(TimeSpan.FromSeconds(10), () =>
            {
                LeaderActions(); // Leaving --> Exiting
                LeaderActions(); // Exiting --> Removed

                // Member should leave
                ExpectMsg<ClusterEvent.MemberRemoved>().Member.Address.Should().Be(_selfAddress);
            });

            // LeaveAsync() task expected to complete immediately
            AwaitCondition(() => _cluster.LeaveAsync().IsCompleted);
        }

        [Fact]
        public void A_cluster_must_cancel_LeaveAsync_task_if_CancellationToken_fired_before_node_left()
        {
            // Join cluster
            _cluster.Join(_selfAddress);
            LeaderActions(); // Joining -> Up

            // Subscribe to MemberRemoved and wait for confirmation
            _cluster.Subscribe(TestActor, typeof(ClusterEvent.MemberRemoved));
            ExpectMsg<ClusterEvent.CurrentClusterState>();

            // Requesting leave with cancellation token
            var cts = new CancellationTokenSource();
            var task1 = _cluster.LeaveAsync(cts.Token);

            // Requesting another leave without cancellation
            var task2 = _cluster.LeaveAsync(new CancellationTokenSource().Token);

            // Cancelling the first task
            cts.Cancel();
            AwaitCondition(() => task1.IsCanceled, null, "Task should be cancelled");

            Within(TimeSpan.FromSeconds(10), () =>
            {
                // Second task should continue awaiting for cluster leave
                task2.IsCompleted.Should().BeFalse();

                // Waiting for leave
                LeaderActions(); // Leaving --> Exiting
                LeaderActions(); // Exiting --> Removed

                // Member should leave even a task was cancelled
                ExpectMsg<ClusterEvent.MemberRemoved>().Member.Address.Should().Be(_selfAddress);

                // Second task should complete (not cancelled)
                AwaitCondition(() => task2.IsCompleted && !task2.IsCanceled, null, "Task should be completed, but not cancelled.");
            });

            // Subsequent LeaveAsync() tasks expected to complete immediately (not cancelled)
            var task3 = _cluster.LeaveAsync();
            AwaitCondition(() => task3.IsCompleted && !task3.IsCanceled, null, "Task should be completed, but not cancelled.");
        }

        [Fact]
        public void A_cluster_must_be_allowed_to_join_and_leave_with_local_address()
        {
            var sys2 = ActorSystem.Create("ClusterSpec2", ConfigurationFactory.ParseString(@"akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
        akka.remote.dot-netty.tcp.port = 0"));

            try
            {
                var @ref = sys2.ActorOf(Props.Empty);
                Cluster.Get(sys2).Join(@ref.Path.Address); // address doesn't contain full address information
                Within(5.Seconds(), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.Get(sys2).State.Members.Count.Should().Be(1);
                        Cluster.Get(sys2).State.Members.First().Status.Should().Be(MemberStatus.Up);
                    });
                });

                Cluster.Get(sys2).Leave(@ref.Path.Address);

                Within(5.Seconds(), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.Get(sys2).IsTerminated.Should().BeTrue();
                    });
                });
            }
            finally
            {
                Shutdown(sys2);
            }
        }

        [Fact]
        public void A_cluster_must_be_able_to_JoinAsync()
        {
            var timeout = TimeSpan.FromSeconds(10);

            try
            {
                _cluster.JoinAsync(_selfAddress).Wait(timeout).Should().BeTrue();
                LeaderActions();
                // Member should already be up
                _cluster.Subscribe(TestActor, ClusterEvent.InitialStateAsEvents, new[] { typeof(ClusterEvent.IMemberEvent) });
                ExpectMsg<ClusterEvent.MemberUp>();

                // join second time - response should be immediate success
                _cluster.JoinAsync(_selfAddress).Wait(TimeSpan.FromMilliseconds(100)).Should().BeTrue();
            }
            finally
            {
                _cluster.Shutdown();
            }

            // JoinAsync should fail after cluster has been shutdown - a manual actor system restart is required
            Assert.ThrowsAsync<ClusterJoinFailedException>(async () =>
            {
                await _cluster.JoinAsync(_selfAddress);
                LeaderActions();
                ExpectMsg<ClusterEvent.MemberRemoved>();
            }).Wait(timeout);
        }

        [Fact]
        public void A_cluster_JoinAsync_must_fail_if_could_not_connect_to_cluster()
        {
            var timeout = TimeSpan.FromSeconds(10);

            try
            {
                _cluster.Subscribe(TestActor, ClusterEvent.InitialStateAsEvents, new[] { typeof(ClusterEvent.IMemberEvent) });

                var nonexisting = Address.Parse($"akka.tcp://{_selfAddress.System}@127.0.0.1:9999/");
                Assert.ThrowsAsync<ClusterJoinFailedException>(async () =>
                {
                    await _cluster.JoinAsync(nonexisting);
                    LeaderActions();

                    ExpectMsg<ClusterEvent.MemberRemoved>();
                }).Wait(timeout);

            }
            finally
            {
                _cluster.Shutdown();
            }
        }

        [Fact]
        public void A_cluster_must_be_able_to_join_async_to_seed_nodes()
        {
            var timeout = TimeSpan.FromSeconds(10);

            try
            {
                _cluster.JoinSeedNodesAsync(new[] { _selfAddress }).Wait(timeout).Should().BeTrue();
                LeaderActions();
                // Member should already be up
                _cluster.Subscribe(TestActor, ClusterEvent.InitialStateAsEvents, new[] { typeof(ClusterEvent.IMemberEvent) });
                ExpectMsg<ClusterEvent.MemberUp>();

                // join second time - response should be immediate success
                _cluster.JoinSeedNodesAsync(new[] { _selfAddress }).Wait(TimeSpan.FromMilliseconds(100)).Should().BeTrue();
            }
            finally
            {
                _cluster.Shutdown();
            }

            // JoinSeedNodesAsync should fail after cluster has been shutdown - a manual actor system restart is required
            Assert.ThrowsAsync<ClusterJoinFailedException>(async () =>
            {
                await _cluster.JoinSeedNodesAsync(new[] { _selfAddress });
                LeaderActions();
                ExpectMsg<ClusterEvent.MemberRemoved>();
            }).Wait(timeout);
        }

        [Fact]
        public void A_cluster_JoinSeedNodesAsync_must_fail_if_could_not_connect_to_cluster()
        {
            var timeout = TimeSpan.FromSeconds(10);

            try
            {
                _cluster.Subscribe(TestActor, ClusterEvent.InitialStateAsEvents, new[] { typeof(ClusterEvent.IMemberEvent) });

                var nonexisting = Address.Parse($"akka.tcp://{_selfAddress.System}@127.0.0.1:9999/");
                Assert.ThrowsAsync<ClusterJoinFailedException>(async () =>
                {
                    await _cluster.JoinSeedNodesAsync(new[] { nonexisting });
                    LeaderActions();

                    ExpectMsg<ClusterEvent.MemberRemoved>();
                }).Wait(timeout);

            }
            finally
            {
                _cluster.Shutdown();
            }
        }

        [Fact]
        public void A_cluster_must_allow_to_resolve_RemotePathOf_any_actor()
        {
            var remotePath = _cluster.RemotePathOf(TestActor);

            TestActor.Path.Address.Host.Should().BeNull();
            _cluster.RemotePathOf(TestActor).Uid.Should().Be(TestActor.Path.Uid);
            _cluster.RemotePathOf(TestActor).Address.Should().Be(_selfAddress);
        }

        [Fact]
        public void A_cluster_must_leave_via_CoordinatedShutdownRun()
        {
            var sys2 = ActorSystem.Create("ClusterSpec2", ConfigurationFactory.ParseString(@"
                akka.actor.provider = ""cluster""
                akka.remote.dot-netty.tcp.port = 0
                akka.coordinated-shutdown.run-by-clr-shutdown-hook = off
                akka.coordinated-shutdown.terminate-actor-system = off
                akka.coordinated-shutdown.run-by-actor-system-terminate = off
                akka.cluster.run-coordinated-shutdown-when-down = off
            ").WithFallback(Akka.TestKit.Configs.TestConfigs.DefaultConfig));

            try
            {
                var probe = CreateTestProbe(sys2);
                Cluster.Get(sys2).Subscribe(probe.Ref, typeof(ClusterEvent.IMemberEvent));
                probe.ExpectMsg<ClusterEvent.CurrentClusterState>();
                Cluster.Get(sys2).Join(Cluster.Get(sys2).SelfAddress);
                probe.ExpectMsg<ClusterEvent.MemberUp>();

                CoordinatedShutdown.Get(sys2).Run(CoordinatedShutdown.UnknownReason.Instance);

                probe.ExpectMsg<ClusterEvent.MemberLeft>();
                // MemberExited might not be published before MemberRemoved
                var removed = (ClusterEvent.MemberRemoved)probe.FishForMessage(m => m is ClusterEvent.MemberRemoved);
                removed.PreviousStatus.ShouldBeEquivalentTo(MemberStatus.Exiting);
            }
            finally
            {
                Shutdown(sys2);
            }
        }

        [Fact]
        public void A_cluster_must_leave_via_CoordinatedShutdownRun_when_member_status_is_Joining()
        {
            var sys2 = ActorSystem.Create("ClusterSpec2", ConfigurationFactory.ParseString(@"
                akka.actor.provider = ""cluster""
                akka.remote.dot-netty.tcp.port = 0
                akka.coordinated-shutdown.run-by-clr-shutdown-hook = off
                akka.coordinated-shutdown.terminate-actor-system = off
                akka.coordinated-shutdown.run-by-actor-system-terminate = off
                akka.cluster.run-coordinated-shutdown-when-down = off
                akka.cluster.min-nr-of-members = 2
            ").WithFallback(Akka.TestKit.Configs.TestConfigs.DefaultConfig));

            try
            {
                var probe = CreateTestProbe(sys2);
                Cluster.Get(sys2).Subscribe(probe.Ref, typeof(ClusterEvent.IMemberEvent));
                probe.ExpectMsg<ClusterEvent.CurrentClusterState>();
                Cluster.Get(sys2).Join(Cluster.Get(sys2).SelfAddress);
                probe.ExpectMsg<ClusterEvent.MemberJoined>();

                CoordinatedShutdown.Get(sys2).Run(CoordinatedShutdown.UnknownReason.Instance);

                probe.ExpectMsg<ClusterEvent.MemberLeft>();
                // MemberExited might not be published before MemberRemoved
                var removed = (ClusterEvent.MemberRemoved)probe.FishForMessage(m => m is ClusterEvent.MemberRemoved);
                removed.PreviousStatus.ShouldBeEquivalentTo(MemberStatus.Exiting);
            }
            finally
            {
                Shutdown(sys2);
            }
        }

        [Fact]
        public void A_cluster_must_terminate_ActorSystem_via_leave_CoordinatedShutdown()
        {
            var sys2 = ActorSystem.Create("ClusterSpec2", ConfigurationFactory.ParseString(@"
                akka.actor.provider = ""cluster""
                akka.remote.dot-netty.tcp.port = 0
                akka.coordinated-shutdown.terminate-actor-system = on
            ").WithFallback(Akka.TestKit.Configs.TestConfigs.DefaultConfig));

            try
            {
                var probe = CreateTestProbe(sys2);
                Cluster.Get(sys2).Subscribe(probe.Ref, typeof(ClusterEvent.IMemberEvent));
                probe.ExpectMsg<ClusterEvent.CurrentClusterState>();
                Cluster.Get(sys2).Join(Cluster.Get(sys2).SelfAddress);
                probe.ExpectMsg<ClusterEvent.MemberUp>();

                Cluster.Get(sys2).Leave(Cluster.Get(sys2).SelfAddress);

                probe.ExpectMsg<ClusterEvent.MemberLeft>();
                // MemberExited might not be published before MemberRemoved
                var removed = (ClusterEvent.MemberRemoved)probe.FishForMessage(m => m is ClusterEvent.MemberRemoved);
                removed.PreviousStatus.ShouldBeEquivalentTo(MemberStatus.Exiting);
                AwaitCondition(() => sys2.WhenTerminated.IsCompleted, TimeSpan.FromSeconds(10));
                Cluster.Get(sys2).IsTerminated.Should().BeTrue();
                CoordinatedShutdown.Get(sys2).ShutdownReason.Should().BeOfType<CoordinatedShutdown.ClusterLeavingReason>();
            }
            finally
            {
                Shutdown(sys2);
            }
        }

        [Fact]
        public void A_cluster_must_terminate_ActorSystem_via_Down_CoordinatedShutdown()
        {
            var sys3 = ActorSystem.Create("ClusterSpec3", ConfigurationFactory.ParseString(@"
                akka.actor.provider = ""cluster""
                akka.remote.dot-netty.tcp.port = 0
                akka.coordinated-shutdown.terminate-actor-system = on
                akka.cluster.run-coordinated-shutdown-when-down = on
                akka.loglevel=DEBUG
            ").WithFallback(Akka.TestKit.Configs.TestConfigs.DefaultConfig));

            try
            {
                var probe = CreateTestProbe(sys3);
                Cluster.Get(sys3).Subscribe(probe.Ref, typeof(ClusterEvent.IMemberEvent));
                probe.ExpectMsg<ClusterEvent.CurrentClusterState>();
                Cluster.Get(sys3).Join(Cluster.Get(sys3).SelfAddress);
                probe.ExpectMsg<ClusterEvent.MemberUp>();

                Cluster.Get(sys3).Down(Cluster.Get(sys3).SelfAddress);

                probe.ExpectMsg<ClusterEvent.MemberDowned>();
                probe.ExpectMsg<ClusterEvent.MemberRemoved>();
                AwaitCondition(() => sys3.WhenTerminated.IsCompleted, TimeSpan.FromSeconds(10));
                Cluster.Get(sys3).IsTerminated.Should().BeTrue();
                CoordinatedShutdown.Get(sys3).ShutdownReason.Should().BeOfType<CoordinatedShutdown.ClusterDowningReason>();
            }
            finally
            {
                Shutdown(sys3);
            }
        }
    }
}

