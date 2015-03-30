using System;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class AutoDownSpec : AkkaSpec
    {
        sealed class DownCalled
        {
            readonly Address _address;

            public DownCalled(Address address)
            {
                _address = address;
            }

            public override bool Equals(object obj)
            {
                var other = obj as DownCalled;
                if (other == null) return false;
                return _address.Equals(other._address);
            }

            public override int GetHashCode()
            {
                return _address.GetHashCode();
            }
        }

        readonly static Member MemberA = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Up);
        readonly static Member MemberB = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Up);
        readonly static Member MemberC = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552), MemberStatus.Up);

        class AutoDownTestActor : AutoDownBase
        {
            readonly IActorRef _probe;

            public AutoDownTestActor(TimeSpan autoDownUnreachableAfter, IActorRef probe): base(autoDownUnreachableAfter)
            {
                _probe = probe;
            }

            public override Address SelfAddress
            {
                get { return MemberA.Address; }
            }

            public override IScheduler Scheduler
            {
                get { return Context.System.Scheduler; }
            }

            public override void Down(Address node)
            {
                if (_leader)
                {
                    _probe.Tell(new DownCalled(node));
                }
                else
                {
                    _probe.Tell("down must only be done by leader");
                }
            }
        }

        private IActorRef AutoDownActor(TimeSpan autoDownUnreachableAfter)
        {
            return
                Sys.ActorOf(new Props(typeof(AutoDownTestActor),
                    new object[] { autoDownUnreachableAfter, this.TestActor }));
        }

        [Fact]
        public void AutoDownMustDownUnreachableWhenLeader()
        {
            var a = AutoDownActor(TimeSpan.Zero);
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberB));
            ExpectMsg(new DownCalled(MemberB.Address));
        }

        [Fact]
        public void AutoDownMustNotDownUnreachableWhenNotLeader()
        {
            var a = AutoDownActor(TimeSpan.Zero);
            a.Tell(new ClusterEvent.LeaderChanged(MemberB.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberC));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void AutoDownMustDownUnreachableWhenBecomingLeader()
        {
            var a = AutoDownActor(TimeSpan.Zero);
            a.Tell(new ClusterEvent.LeaderChanged(MemberB.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberC));
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            ExpectMsg(new DownCalled(MemberC.Address));
        }

        [Fact]
        public void AutoDownMustDownUnreachableAfterSpecifiedDuration()
        {
            var a = AutoDownActor(TimeSpan.FromSeconds(2));
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberB));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
            ExpectMsg(new DownCalled(MemberB.Address));
        }

        [Fact]
        public void AutoDownMustDownUnreachableWhenBecomingLeaderInbetweenDetectionAndSpecifiedDuration()
        {
            var a = AutoDownActor(TimeSpan.FromSeconds(2));
            a.Tell(new ClusterEvent.LeaderChanged(MemberB.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberC));
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
            ExpectMsg(new DownCalled(MemberC.Address));
        }

        [Fact]
        public void AutoDownMustNotDownUnreachableWhenLoosingLeadershipInbetweenDetectionAndSpecifiedDuration()
        {
            var a = AutoDownActor(TimeSpan.FromSeconds(2));
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberC));
            a.Tell(new ClusterEvent.LeaderChanged(MemberB.Address));
            ExpectNoMsg(TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void AutoDownMustNotDownWhenUnreachableBecomeReachableInbetweenDetectionAndSpecifiedDuration()
        {
            var a = AutoDownActor(TimeSpan.FromSeconds(2));
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberB));
            a.Tell(new ClusterEvent.ReachableMember(MemberB));
            ExpectNoMsg(TimeSpan.FromSeconds(3));
        }


        [Fact]
        public void AutoDownMustNotDownUnreachableIsRemovedInbetweenDetectionAndSpecifiedDuration()
        {
            var a = AutoDownActor(TimeSpan.FromSeconds(2));
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberB));
            a.Tell(new ClusterEvent.MemberRemoved(MemberB.Copy(MemberStatus.Removed), MemberStatus.Exiting));
            ExpectNoMsg(TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void AutoDownMustNotDownWhenUnreachableIsAlreadyDown()
        {
            var a = AutoDownActor(TimeSpan.Zero);
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberB.Copy(MemberStatus.Down)));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

    }
}
