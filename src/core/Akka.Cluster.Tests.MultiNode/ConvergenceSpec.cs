//-----------------------------------------------------------------------
// <copyright file="ConvergenceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Akka.Actor;
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
                .WithFallback(@"akka.cluster.failure-detector.threshold = 4")
                .WithFallback(MultiNodeClusterSpec.ClusterConfig(failureDetectorPuppet));
        }
    }
    
    public class ConvergenceWithFailureDetectorPuppetMultiNode1 : ConvergenceSpec
    {
        public ConvergenceWithFailureDetectorPuppetMultiNode1() : base(true)
        {
        }
    }

    public class ConvergenceWithFailureDetectorPuppetMultiNode2 : ConvergenceSpec
    {
        public ConvergenceWithFailureDetectorPuppetMultiNode2() : base(true)
        {
        }
    }
    
    public class ConvergenceWithFailureDetectorPuppetMultiNode3 : ConvergenceSpec
    {
        public ConvergenceWithFailureDetectorPuppetMultiNode3() : base(true)
        {
        }
    }
    
    public class ConvergenceWithFailureDetectorPuppetMultiNode4 : ConvergenceSpec
    {
        public ConvergenceWithFailureDetectorPuppetMultiNode4() : base(true)
        {
        }
    }

    public class ConvergenceWithAccrualFailureDetectorMultiNode1 : ConvergenceSpec
    {
        public ConvergenceWithAccrualFailureDetectorMultiNode1()
            : base(false)
        {
        }
    }

    public class ConvergenceWithAccrualFailureDetectorMultiNode2 : ConvergenceSpec
    {
        public ConvergenceWithAccrualFailureDetectorMultiNode2()
            : base(false)
        {
        }
    }

    public class ConvergenceWithAccrualFailureDetectorMultiNode3 : ConvergenceSpec
    {
        public ConvergenceWithAccrualFailureDetectorMultiNode3()
            : base(false)
        {
        }
    }

    public class ConvergenceWithAccrualFailureDetectorMultiNode4 : ConvergenceSpec
    {
        public ConvergenceWithAccrualFailureDetectorMultiNode4()
            : base(false)
        {
        }
    }
    
    public abstract class ConvergenceSpec : MultiNodeClusterSpec
    {
        readonly ConvergenceSpecConfig _config;

        protected ConvergenceSpec(bool failureDetectorPuppet)
            : this(new ConvergenceSpecConfig(failureDetectorPuppet))
        {
        }

        private ConvergenceSpec(ConvergenceSpecConfig config) : base(config)
        {
            _config = config;
            MuteMarkingAsUnreachable();
        }

        [MultiNodeFact]
        public void ConvergenceSpecTests()
        {
            //TODO: This better
            AClusterOf3MembersMustReachInitialConvergence();
            AClusterOf3MembersMustNotReachConvergenceWhileAnyNodesAreUnreachable();
            AClusterOf3MembersMustNotMoveANewJoiningNodeToUpWhileThereIsNoConvergence();
        }

        public void AClusterOf3MembersMustReachInitialConvergence()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);

            RunOn(() => { /*doesn't join immediately*/}, _config.Fourth);

            EnterBarrier("after-1");
        }

        public void AClusterOf3MembersMustNotReachConvergenceWhileAnyNodesAreUnreachable()
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

        public void AClusterOf3MembersMustNotMoveANewJoiningNodeToUpWhileThereIsNoConvergence()
        {
            RunOn(() => Cluster.Join(GetAddress(_config.First)), _config.Fourth);

            EnterBarrier("after-join");

            RunOn(() =>
            {
                for (var i = 0; i < 5; i++)
                {
                    AwaitAssert(() => ClusterView.Members.Count.ShouldBe(3));
                    AwaitSeenSameState(GetAddress(_config.First), GetAddress(_config.Second), GetAddress(_config.Fourth));
                    MemberStatus(GetAddress(_config.First)).ShouldBe(Akka.Cluster.MemberStatus.Up);
                    MemberStatus(GetAddress(_config.Second)).ShouldBe(Akka.Cluster.MemberStatus.Up);
                    Assert.True(MemberStatus(GetAddress(_config.Fourth)) == null);
                    // wait and then check again
                    //TODO: Dilation?
                    Thread.Sleep(TimeSpan.FromSeconds(1));
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

