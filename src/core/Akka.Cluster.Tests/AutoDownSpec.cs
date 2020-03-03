//-----------------------------------------------------------------------
// <copyright file="AutoDownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
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

        static readonly Member MemberA = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Up);
        static readonly Member MemberB = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Up);
        static readonly Member MemberC = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552), MemberStatus.Up);

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
        public void AutoDown_must_down_unreachable_when_leader()
        {
            var a = AutoDownActor(TimeSpan.Zero);
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberB));
            ExpectMsg(new DownCalled(MemberB.Address));
        }

        [Fact]
        public void AutoDown_must_not_down_unreachable_when_not_leader()
        {
            var a = AutoDownActor(TimeSpan.Zero);
            a.Tell(new ClusterEvent.LeaderChanged(MemberB.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberC));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void AutoDown_must_down_unreachable_when_becoming_leader()
        {
            var a = AutoDownActor(TimeSpan.Zero);
            a.Tell(new ClusterEvent.LeaderChanged(MemberB.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberC));
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            ExpectMsg(new DownCalled(MemberC.Address));
        }

        [Fact]
        public void AutoDown_must_down_unreachable_after_specified_duration()
        {
            var a = AutoDownActor(TimeSpan.FromSeconds(2));
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberB));
            ExpectNoMsg(1.Seconds());
            ExpectMsg(new DownCalled(MemberB.Address));
        }

        [Fact]
        public void AutoDown_must_down_unreachable_when_becoming_leader_inbetween_detection_and_specified_duration()
        {
            var a = AutoDownActor(TimeSpan.FromSeconds(2));
            a.Tell(new ClusterEvent.LeaderChanged(MemberB.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberC));
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            ExpectNoMsg(1.Seconds());
            ExpectMsg(new DownCalled(MemberC.Address));
        }

        [Fact]
        public void AutoDown_must_not_down_unreachable_when_loosing_leadership_inbetween_detection_and_specified_duration()
        {
            var a = AutoDownActor(TimeSpan.FromSeconds(2));
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberC));
            a.Tell(new ClusterEvent.LeaderChanged(MemberB.Address));
            ExpectNoMsg(3.Seconds());
        }

        [Fact]
        public void AutoDown_must_not_down_when_unreachable_become_reachable_inbetween_detection_and_specified_duration()
        {
            var a = AutoDownActor(TimeSpan.FromSeconds(2));
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberB));
            a.Tell(new ClusterEvent.ReachableMember(MemberB));
            ExpectNoMsg(3.Seconds());
        }

        [Fact]
        public void AutoDown_must_not_down_unreachable_is_removed_inbetween_detection_and_specified_duration()
        {
            var a = AutoDownActor(TimeSpan.FromSeconds(2));
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberB));
            a.Tell(new ClusterEvent.MemberRemoved(MemberB.Copy(MemberStatus.Removed), MemberStatus.Exiting));
            ExpectNoMsg(3.Seconds());
        }

        [Fact]
        public void AutoDown_must_not_down_when_unreachable_is_already_down()
        {
            var a = AutoDownActor(TimeSpan.Zero);
            a.Tell(new ClusterEvent.LeaderChanged(MemberA.Address));
            a.Tell(new ClusterEvent.UnreachableMember(MemberB.Copy(MemberStatus.Down)));
            ExpectNoMsg(1.Seconds());
        }
    }
}
