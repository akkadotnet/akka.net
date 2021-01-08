//-----------------------------------------------------------------------
// <copyright file="MembershipChangeListenerExitingSpec.cs" company="Akka.NET Project">
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
    public class MembershipChangeListenerExitingConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public MembershipChangeListenerExitingConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }

    public class MembershipChangeListenerExitingSpec : MultiNodeClusterSpec
    {
        private class Watcher : ReceiveActor
        {
            private readonly TestLatch _exitingLatch;
            private readonly TestLatch _removedLatch;
            private readonly Address _secondAddress;

            public Watcher(TestLatch exitingLatch, TestLatch removedLatch, Address secondAddress)
            {
                _exitingLatch = exitingLatch;
                _removedLatch = removedLatch;
                _secondAddress = secondAddress;

                Receive<ClusterEvent.CurrentClusterState>(state =>
                {
                    if (state.Members.Any(m => m.Address == _secondAddress && m.Status == MemberStatus.Exiting))
                        _exitingLatch.CountDown();
                });
                Receive<ClusterEvent.MemberExited>(m =>
                {
                    if (m.Member.Address == secondAddress)
                    {
                        exitingLatch.CountDown();
                    }
                });
                Receive<ClusterEvent.MemberRemoved>(m =>
                {
                    if (m.Member.Address == secondAddress)
                    {
                        _removedLatch.CountDown();
                    }
                });
            }
        }

        private readonly MembershipChangeListenerExitingConfig _config;

        public MembershipChangeListenerExitingSpec() : this(new MembershipChangeListenerExitingConfig())
        {
        }

        protected MembershipChangeListenerExitingSpec(MembershipChangeListenerExitingConfig config) : base(config, typeof(MembershipChangeListenerExitingSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void MembershipChangeListenerExitingSpecs()
        {
            Registered_MembershipChangeListener_must_be_notified_when_new_node_is_exiting();
        }

        public void Registered_MembershipChangeListener_must_be_notified_when_new_node_is_exiting()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);

            RunOn(() =>
            {
                EnterBarrier("registered-listener");
                Cluster.Leave(GetAddress(_config.Second));
            }, _config.First);

            RunOn(() =>
            {
                var exitingLatch = new TestLatch();
                var removedLatch = new TestLatch();
                var secondAddress = GetAddress(_config.Second);
                var watcher = Sys.ActorOf(Props.Create(() => new Watcher(exitingLatch, removedLatch, secondAddress))
                               .WithDeploy(Deploy.Local));
                Cluster.Subscribe(watcher, new[] { typeof(ClusterEvent.IMemberEvent) });
                EnterBarrier("registered-listener");
                exitingLatch.Ready();
                removedLatch.Ready();
            }, _config.Second);

            RunOn(() =>
            {
                var exitingLatch = new TestLatch();
                var secondAddress = GetAddress(_config.Second);
                var watcher = Sys.ActorOf(Props.Create(() => new Watcher(exitingLatch, null, secondAddress))
                               .WithDeploy(Deploy.Local));
                Cluster.Subscribe(watcher, new[] { typeof(ClusterEvent.IMemberEvent) });
                EnterBarrier("registered-listener");
                exitingLatch.Ready();
            }, _config.Third);

            EnterBarrier("finished");
        }
    }
}
