//-----------------------------------------------------------------------
// <copyright file="LeaderDowningNodeThatIsUnreachableSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class LeaderDowningNodeThatIsUnreachableConfig : MultiNodeConfig
    {
        public RoleName First { get; private set; }
        public RoleName Second { get; private set; }
        public RoleName Third { get; private set; }
        public RoleName Fourth { get; private set; }

        public LeaderDowningNodeThatIsUnreachableConfig(bool failureDectectorPuppet)
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"akka.cluster.auto-down-unreachable-after = 2s"))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig(failureDectectorPuppet));
        }
    }

    public class LeaderDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode1 : LeaderDowningNodeThatIsUnreachableSpec
    {
        public LeaderDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode1() : base(true)
        {
        }
    }

    public class LeaderDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode2 : LeaderDowningNodeThatIsUnreachableSpec
    {
        public LeaderDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode2() : base(true)
        {
        }
    }

    public class LeaderDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode3 : LeaderDowningNodeThatIsUnreachableSpec
    {
        public LeaderDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode3() : base(true)
        {
        }
    }

    public class LeaderDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode4 : LeaderDowningNodeThatIsUnreachableSpec
    {
        public LeaderDowningNodeThatIsUnreachableWithFailureDetectorPuppetMultiNode4() : base(true)
        {
        }
    }

    public class LeaderDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode1 : LeaderDowningNodeThatIsUnreachableSpec
    {
        public LeaderDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode1() : base(false)
        {
        }
    }

    public class LeaderDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode2 : LeaderDowningNodeThatIsUnreachableSpec
    {
        public LeaderDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode2() : base(false)
        {
        }
    }

    public class LeaderDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode3 : LeaderDowningNodeThatIsUnreachableSpec
    {
        public LeaderDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode3() : base(false)
        {
        }
    }

    public class LeaderDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode4 : LeaderDowningNodeThatIsUnreachableSpec
    {
        public LeaderDowningNodeThatIsUnreachableWithAccrualFailureDetectorMultiNode4() : base(false)
        {
        }
    }

    public abstract class LeaderDowningNodeThatIsUnreachableSpec : MultiNodeClusterSpec
    {
        private readonly LeaderDowningNodeThatIsUnreachableConfig _config;

        protected LeaderDowningNodeThatIsUnreachableSpec(bool failureDetectorPuppet)
            : this(new LeaderDowningNodeThatIsUnreachableConfig(failureDetectorPuppet))
        {

        }

        protected LeaderDowningNodeThatIsUnreachableSpec(LeaderDowningNodeThatIsUnreachableConfig config)
            : base(config)
        {
            _config = config;
            MuteMarkingAsUnreachable();
        }

        [MultiNodeFact]
        public void LeaderDowningNodeThatIsUnreachableSpecs()
        {
            Leader_in_4_node_cluster_must_be_able_to_down_last_node_that_is_unreachable();
            Leader_in_4_node_cluster_must_be_able_to_down_middle_node_that_is_unreachable();
        }

        public void Leader_in_4_node_cluster_must_be_able_to_down_last_node_that_is_unreachable()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third, _config.Fourth);

            var fourthAddress = GetAddress(_config.Fourth);

            EnterBarrier("before-exit-fourth-node");
            RunOn(() =>
            {
                // kill 'fourth' node
                TestConductor.Exit(_config.Fourth, 0).Wait();
                EnterBarrier("down-fourth-node");

                // mark the node as unreachable in the failure detector
                MarkNodeAsUnavailable(fourthAddress);

                // --- HERE THE LEADER SHOULD DETECT FAILURE AND AUTO-DOWN THE UNREACHABLE NODE ---
                AwaitMembersUp(3, ImmutableHashSet.Create(fourthAddress), 30.Seconds());
            }, _config.First);

            RunOn(() =>
            {
                EnterBarrier("down-fourth-node");
            }, _config.Fourth);

            RunOn(() =>
            {
                EnterBarrier("down-fourth-node");
                AwaitMembersUp(3, ImmutableHashSet.Create(fourthAddress), 30.Seconds());
            }, _config.Second, _config.Third);

            EnterBarrier("await-completion-1");
        }

        public void Leader_in_4_node_cluster_must_be_able_to_down_middle_node_that_is_unreachable()
        {
            var secondAddress = GetAddress(_config.Second);

            EnterBarrier("before-down-second-node");
            RunOn(() =>
            {
                // kill 'fourth' node
                TestConductor.Exit(_config.Second, 0).Wait();
                EnterBarrier("down-second-node");

                // mark the node as unreachable in the failure detector
                MarkNodeAsUnavailable(secondAddress);

                // --- HERE THE LEADER SHOULD DETECT FAILURE AND AUTO-DOWN THE UNREACHABLE NODE ---
                AwaitMembersUp(2, ImmutableHashSet.Create(secondAddress), 30.Seconds());
            }, _config.First);

            RunOn(() =>
            {
                EnterBarrier("down-second-node");
            }, _config.Second);

            RunOn(() =>
            {
                EnterBarrier("down-second-node");
                AwaitMembersUp(2, ImmutableHashSet.Create(secondAddress), 30.Seconds());
            }, _config.Second, _config.Third);

            EnterBarrier("await-completion-2");

        }
    }
}
