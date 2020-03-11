//-----------------------------------------------------------------------
// <copyright file="ConvergenceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class ConvergenceSpecConfig : MultiNodeConfig
    {
        readonly RoleName _first;
        public RoleName First { get {return _first;} }
        readonly RoleName _second;
        public RoleName Second { get { return _second; } }
        readonly RoleName _third;
        public RoleName Third { get { return _third; } }
        readonly RoleName _fourth;
        public RoleName Fourth { get { return _fourth; } }
        
        public ConvergenceSpecConfig(bool failureDetectorPuppet)
        {
            _first = Role("first");
            _second = Role("second");
            _third = Role("third");
            _fourth = Role("fourth");

            CommonConfig = ConfigurationFactory.ParseString(@"akka.cluster.publish-stats-interval = 25s")
                .WithFallback(MultiNodeLoggingConfig.LoggingConfig)
                .WithFallback(DebugConfig(true))
                .WithFallback(@"
                    akka.cluster.failure-detector.threshold = 4
                    akka.cluster.allow-weakly-up-members = off")
                .WithFallback(MultiNodeClusterSpec.ClusterConfig(failureDetectorPuppet));
        }
    }
    
    public class ConvergenceWithFailureDetectorPuppetMultiNode : ConvergenceSpec
    {
        public ConvergenceWithFailureDetectorPuppetMultiNode() : base(true, typeof(ConvergenceWithFailureDetectorPuppetMultiNode))
        {
        }
    }

    public class ConvergenceWithAccrualFailureDetectorMultiNode : ConvergenceSpec
    {
        public ConvergenceWithAccrualFailureDetectorMultiNode()
            : base(false, typeof(ConvergenceWithAccrualFailureDetectorMultiNode))
        {
        }
    }
    
    public abstract class ConvergenceSpec : MultiNodeClusterSpec
    {
        readonly ConvergenceSpecConfig _config;

        protected ConvergenceSpec(bool failureDetectorPuppet, Type type)
            : this(new ConvergenceSpecConfig(failureDetectorPuppet), type)
        {
        }

        private ConvergenceSpec(ConvergenceSpecConfig config, Type type) : base(config, type)
        {
            _config = config;
            MuteMarkingAsUnreachable();
        }

        [MultiNodeFact]
        public void ConvergenceSpecTests()
        {
            //TODO: This better
            A_cluster_of_3_members_must_reach_initial_convergence();
            A_cluster_of_3_members_must_not_reach_convergence_while_any_nodes_are_unreachable();
            A_cluster_of_3_members_must_not_move_a_new_joining_node_to_up_while_there_is_no_convergence();
        }

        public void A_cluster_of_3_members_must_reach_initial_convergence()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);

            RunOn(() => { /*doesn't join immediately*/}, _config.Fourth);

            EnterBarrier("after-1");
        }

        public void A_cluster_of_3_members_must_not_reach_convergence_while_any_nodes_are_unreachable()
        {
            var thirdAddress = GetAddress(_config.Third);
            EnterBarrier("before-shutdown");

            RunOn(() =>
            {
                //kill 'third' node
                TestConductor.Exit(_config.Third, 0).Wait();
                MarkNodeAsUnavailable(thirdAddress);
            }, _config.First);

            RunOn(() => Within(TimeSpan.FromSeconds(28), () =>
            {
                //third becomes unreachable
                AwaitAssert(() => ClusterView.UnreachableMembers.Count.ShouldBe(1));
                AwaitSeenSameState(GetAddress(_config.First), GetAddress(_config.Second));
                // still one unreachable
                ClusterView.UnreachableMembers.Count.ShouldBe(1);
                ClusterView.UnreachableMembers.First().Address.ShouldBe(thirdAddress);
                ClusterView.Members.Count.ShouldBe(3);
            }), _config.First, _config.Second);

            EnterBarrier("after-2");
        }

        public void A_cluster_of_3_members_must_not_move_a_new_joining_node_to_up_while_there_is_no_convergence()
        {
            RunOn(() => Cluster.Join(GetAddress(_config.First)), _config.Fourth);

            EnterBarrier("after-join");

            RunOn(() =>
            {
                for (var i = 0; i < 5; i++)
                {
                    AwaitAssert(() => ClusterView.Members.Count.ShouldBe(4));
                    AwaitSeenSameState(GetAddress(_config.First), GetAddress(_config.Second), GetAddress(_config.Fourth));
                    MemberStatus(GetAddress(_config.First)).ShouldBe(Akka.Cluster.MemberStatus.Up);
                    MemberStatus(GetAddress(_config.Second)).ShouldBe(Akka.Cluster.MemberStatus.Up);
                    MemberStatus(GetAddress(_config.Fourth)).ShouldBe(Akka.Cluster.MemberStatus.Joining);
                    // wait and then check again
                    Thread.Sleep(Dilated(TimeSpan.FromSeconds(1)));
                }
            }, _config.First, _config.Second, _config.Fourth);

            EnterBarrier("after-3");
        }

        MemberStatus? MemberStatus(Address address)
        {
            var member = ClusterView.Members.FirstOrDefault(m => m.Address == address);
            if (member == null) return null;
            return member.Status;
        }
    }
}

