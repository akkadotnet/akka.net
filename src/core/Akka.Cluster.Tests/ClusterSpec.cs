//-----------------------------------------------------------------------
// <copyright file="ClusterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

namespace Akka.Cluster.Tests
{
    public class ClusterSpec : AkkaSpec
    {
        const string Config = @"    
            akka.cluster {
              auto-down-unreachable-after = 0s
              periodic-tasks-initial-delay = 120 s
              publish-stats-interval = 0 s # always, when it happens
            }
            akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            akka.remote.log-remote-lifecycle-events = off
            akka.remote.helios.tcp.port = 0";

        public IActorRef Self { get { return TestActor; } }

        readonly Address _selfAddress;
        readonly Cluster _cluster;

        internal ClusterReadView ClusterView { get { return _cluster.ReadView; } }

        public ClusterSpec()
            : base(Config)
        {
            _selfAddress = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
            _cluster = Cluster.Get(Sys);
        }

        public void LeaderActions()
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
                // TODO: this should be removed
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

        // this should be the last test step, since the cluster is shutdown
        [Fact]
        public void A_cluster_must_publish_member_removed_when_shutdown()
        {
            // TODO: this should be removed
            _cluster.Join(_selfAddress);
            LeaderActions(); // Joining -> Up

            var callbackProbe = CreateTestProbe();
            _cluster.RegisterOnMemberRemoved(() =>
            {
                callbackProbe.Tell("OnMemberRemoved");
            });

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
            // TODO: this should be removed
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
            _cluster.Join(_selfAddress);
            LeaderActions(); // Joining -> Up

            _cluster.Subscribe(TestActor, new[] { typeof(ClusterEvent.MemberRemoved) });

            // first, is in response to the subscription
            ExpectMsg<ClusterEvent.CurrentClusterState>();

            var leaveTask = _cluster.LeaveAsync();

            // current node should be marked as leaving, but not removed yet
            AwaitCondition(() => _cluster.State.Members
                .Single(x => x.Address.Equals(_cluster.SelfAddress)).Status == MemberStatus.Leaving, 
                TimeSpan.FromSeconds(10), 
                message: "Failed to observe node as Leaving.");

            // can't run this inside Within block
            ExpectNoMsg();

            Within(TimeSpan.FromSeconds(10), () =>
            {
                leaveTask.IsCompleted.Should().BeFalse();

                LeaderActions(); // Leaving --> Exiting
                AwaitCondition(() => _cluster.State.Members
                   .Single(x => x.Address.Equals(_cluster.SelfAddress)).Status == MemberStatus.Exiting, 
                   TimeSpan.FromSeconds(10), message: "Failed to observe node as Exiting.");

                LeaderActions(); // Exiting --> Removed
                ExpectMsg<ClusterEvent.MemberRemoved>().Member.Address.Should().Be(_selfAddress);
                leaveTask.IsCompleted.Should().BeTrue();
            });

            // A second call for LeaveAsync should complete immediately
            _cluster.LeaveAsync().IsCompleted.Should().BeTrue();
        }

        [Fact(Skip = "This behavior not yet supported.")]
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
            _cluster.LeaveAsync().IsCompleted.Should().BeTrue();
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
            task1.Should(t => t.IsCanceled, "Task should be cancelled.");

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
                task2.Should(t => t.IsCompleted && !t.IsCanceled, "Task should be completed, but not cancelled.");
            });

            // Subsequent LeaveAsync() tasks expected to complete immediately (not cancelled)
            var task3 = _cluster.LeaveAsync();
            task3.Should(t => t.IsCompleted && !t.IsCanceled, "Task should be completed, but not cancelled.");
        }

        [Fact]
        public void A_cluster_must_be_allowed_to_join_and_leave_with_local_address()
        {
            var sys2 = ActorSystem.Create("ClusterSpec2", ConfigurationFactory.ParseString(@"akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
        akka.remote.helios.tcp.port = 0"));

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
        public void A_cluster_must_allow_to_resolve_RemotePathOf_any_actor()
        {
            var remotePath = _cluster.RemotePathOf(TestActor);

            TestActor.Path.Address.Host.Should().BeNull();
            _cluster.RemotePathOf(TestActor).Uid.Should().Be(TestActor.Path.Uid);
            _cluster.RemotePathOf(TestActor).Address.Should().Be(_selfAddress);
        }
    }
}

