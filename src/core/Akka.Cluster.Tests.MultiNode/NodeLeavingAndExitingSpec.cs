//-----------------------------------------------------------------------
// <copyright file="NodeLeavingAndExitingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class NodeLeavingAndExitingConfig : MultiNodeConfig
    {
        public RoleName First { get; set; }
        public RoleName Second { get; set; }
        public RoleName Third { get; set; }

        public NodeLeavingAndExitingConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }

    public class NodeLeavingAndExitingSpec : MultiNodeClusterSpec
    {
        private class Listener : UntypedActor
        {
            private TestLatch _exitingLatch;
            private readonly Address _secondAddress;

            public Listener(TestLatch exitingLatch, Address secondAddress)
            {
                _exitingLatch = exitingLatch;
                _secondAddress = secondAddress;
            }

            protected override void OnReceive(object message)
            {
                message.Match()
                    .With<ClusterEvent.CurrentClusterState>(state =>
                    {
                        if (state.Members.Any(c => c.Address.Equals(_secondAddress) && c.Status == MemberStatus.Exiting))
                        {
                            _exitingLatch.CountDown();
                        }
                    })
                    .With<ClusterEvent.MemberExited>(m =>
                    {
                        if (m.Member.Address.Equals(_secondAddress))
                        {
                            _exitingLatch.CountDown();
                        }
                    })
                    .With<ClusterEvent.MemberRemoved>(_ =>
                    {
                        // not tested here
                    });
            }
        }

        private readonly NodeLeavingAndExitingConfig _config;

        public NodeLeavingAndExitingSpec() : this(new NodeLeavingAndExitingConfig())
        {
        }

        protected NodeLeavingAndExitingSpec(NodeLeavingAndExitingConfig config) : base(config, typeof(NodeLeavingAndExitingSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void NodeLeavingAndExitingSpecs()
        {
            Node_that_is_leaving_non_singleton_cluster_must_be_moved_to_exiting_by_the_leader();
        }

        public void Node_that_is_leaving_non_singleton_cluster_must_be_moved_to_exiting_by_the_leader()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);

            RunOn(() =>
            {
                var secondAddress = GetAddress(_config.Second);
                var exitingLatch = new TestLatch();
                Cluster.Subscribe(Sys.ActorOf(Props
                    .Create(() => new Listener(exitingLatch, secondAddress))
                    .WithDeploy(Deploy.Local)), new[] { typeof(ClusterEvent.IMemberEvent) });

                EnterBarrier("registered-listener");

                RunOn(() =>
                {
                    Cluster.Leave(GetAddress(_config.Second));
                }, _config.Third);
                EnterBarrier("second-left");

                // Verify that 'second' node is set to EXITING
                exitingLatch.Ready();
            }, _config.First, _config.Third);


            // node that is leaving
            RunOn(() =>
            {
                EnterBarrier("registered-listener");
                EnterBarrier("second-left");
            }, _config.Second);

            EnterBarrier("finished");
        }
    }
}
