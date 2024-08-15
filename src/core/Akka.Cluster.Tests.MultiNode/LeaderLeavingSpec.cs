﻿// -----------------------------------------------------------------------
//  <copyright file="LeaderLeavingSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode;

public class LeaderLeavingSpecConfig : MultiNodeConfig
{
    public LeaderLeavingSpecConfig()
    {
        First = Role("first");
        Second = Role("second");
        Third = Role("third");

        CommonConfig = MultiNodeLoggingConfig.LoggingConfig
            .WithFallback(DebugConfig(true))
            .WithFallback(@"akka.cluster.auto-down-unreachable-after = 0s
akka.cluster.publish-stats-interval = 25 s")
            .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
    }

    public RoleName First { get; }

    public RoleName Second { get; }

    public RoleName Third { get; }

    public class LeaderLeavingSpec : MultiNodeClusterSpec
    {
        private readonly LeaderLeavingSpecConfig _config;

        public LeaderLeavingSpec()
            : this(new LeaderLeavingSpecConfig())
        {
        }

        private LeaderLeavingSpec(LeaderLeavingSpecConfig config) : base(config, typeof(LeaderLeavingSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void
            A_leader_that_is_leaving_must_be_moved_to_leaving_then_exiting_then_removed_then_be_shut_down_and_then_a_new_leader_should_be_elected
            ()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);

            var oldLeaderAddress = ClusterView.Leader;

            Within(TimeSpan.FromSeconds(30), () =>
            {
                if (ClusterView.IsLeader)
                {
                    EnterBarrier("registered-listener");

                    Cluster.Leave(oldLeaderAddress);
                    EnterBarrier("leader-left");

                    // verify that the LEADER is shut down
                    AwaitCondition(() => Cluster.IsTerminated);
                    EnterBarrier("leader-shutdown");
                }
                else
                {
                    var exitingLatch = new TestLatch();

                    var listener = Sys.ActorOf(Props.Create(() => new Listener(oldLeaderAddress, exitingLatch))
                        .WithDeploy(Deploy.Local));

                    Cluster.Subscribe(listener, typeof(ClusterEvent.IMemberEvent));

                    EnterBarrier("registered-listener");

                    EnterBarrier("leader-left");

                    // verify that the LEADER is EXITING
                    exitingLatch.Ready(TestKitSettings.DefaultTimeout);

                    EnterBarrier("leader-shutdown");
                    MarkNodeAsUnavailable(oldLeaderAddress);

                    // verify that the LEADER is no longer part of the 'members' set
                    AwaitAssert(() =>
                        ClusterView.Members.Select(m => m.Address).Contains(oldLeaderAddress).ShouldBeFalse());

                    // verify that the LEADER is not part of the 'unreachable' set
                    AwaitAssert(() =>
                        ClusterView.UnreachableMembers.Select(m => m.Address).Contains(oldLeaderAddress)
                            .ShouldBeFalse());

                    // verify that we have a new LEADER
                    AwaitAssert(() => ClusterView.Leader.ShouldNotBe(oldLeaderAddress));
                }

                EnterBarrier("finished");
            });
        }
    }

    private class Listener : UntypedActor
    {
        private readonly TestLatch _latch;
        private readonly Address _oldLeaderAddress;

        public Listener(Address oldLeaderAddress, TestLatch latch)
        {
            _oldLeaderAddress = oldLeaderAddress;
            _latch = latch;
        }

        protected override void OnReceive(object message)
        {
            var state = message as ClusterEvent.CurrentClusterState;
            if (state != null)
                if (state.Members.Any(m => m.Address == _oldLeaderAddress && m.Status == MemberStatus.Exiting))
                    _latch.CountDown();
            var memberExited = message as ClusterEvent.MemberExited;
            if (memberExited != null && memberExited.Member.Address == _oldLeaderAddress)
                _latch.CountDown();
        }
    }
}