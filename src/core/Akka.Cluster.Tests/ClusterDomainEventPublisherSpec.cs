//-----------------------------------------------------------------------
// <copyright file="ClusterDomainEventPublisherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class ClusterDomainEventPublisherSpec : AkkaSpec
    {
        IActorRef _publisher;
        static readonly Member aUp = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Up);
        static readonly Member aLeaving = aUp.Copy(status: MemberStatus.Leaving);
        static readonly Member aExiting = aLeaving.Copy(status: MemberStatus.Exiting);
        static readonly Member aRemoved = aLeaving.Copy(status: MemberStatus.Removed);
        static readonly Member bExiting = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Exiting);
        static readonly Member bRemoved = bExiting.Copy(status: MemberStatus.Removed);
        static readonly Member cJoining = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552), MemberStatus.Joining, ImmutableHashSet.Create("GRP"));
        static readonly Member cUp = cJoining.Copy(status: MemberStatus.Up);
        static readonly Member cRemoved = cUp.Copy(status: MemberStatus.Removed);
        static readonly Member a51Up = TestMember.Create(new Address("akk.tcp", "sys", "a", 2551), MemberStatus.Up);
        static readonly Member dUp = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552), MemberStatus.Up, ImmutableHashSet.Create("GRP"));

        static readonly Gossip g0 = new Gossip(ImmutableSortedSet.Create(aUp)).Seen(aUp.UniqueAddress);
        static readonly Gossip g1 = new Gossip(ImmutableSortedSet.Create(aUp, bExiting, cJoining)).Seen(aUp.UniqueAddress).Seen(bExiting.UniqueAddress).Seen(cJoining.UniqueAddress);
        static readonly Gossip g2 = new Gossip(ImmutableSortedSet.Create(aUp, bExiting, cUp)).Seen(aUp.UniqueAddress);
        static readonly Gossip g3 = g2.Seen(bExiting.UniqueAddress).Seen(cUp.UniqueAddress);
        static readonly Gossip g4 = new Gossip(ImmutableSortedSet.Create(a51Up, aUp, bExiting, cUp)).Seen(aUp.UniqueAddress);
        static readonly Gossip g5 = new Gossip(ImmutableSortedSet.Create(a51Up, aUp, bExiting, cUp)).Seen(aUp.UniqueAddress).Seen(bExiting.UniqueAddress).Seen(cUp.UniqueAddress).Seen(a51Up.UniqueAddress);
        static readonly Gossip g6 = new Gossip(ImmutableSortedSet.Create(aLeaving, bExiting, cUp)).Seen(aUp.UniqueAddress);
        static readonly Gossip g7 = new Gossip(ImmutableSortedSet.Create(aExiting, bExiting, cUp)).Seen(aUp.UniqueAddress);
        static readonly Gossip g8 = new Gossip(ImmutableSortedSet.Create(aUp, bExiting, cUp, dUp), new GossipOverview(Reachability.Empty.Unreachable(aUp.UniqueAddress, dUp.UniqueAddress))).Seen(aUp.UniqueAddress);
        
        TestProbe _memberSubscriber;

        public ClusterDomainEventPublisherSpec()
        {
            _memberSubscriber = CreateTestProbe();
            Sys.EventStream.Subscribe(_memberSubscriber.Ref, typeof(ClusterEvent.IMemberEvent));
            Sys.EventStream.Subscribe(_memberSubscriber.Ref, typeof(ClusterEvent.LeaderChanged));
            Sys.EventStream.Subscribe(_memberSubscriber.Ref, typeof(ClusterEvent.ClusterShuttingDown));

            _publisher = Sys.ActorOf(Props.Create<ClusterDomainEventPublisher>());
            //TODO: If parent told of exception then test should fail (if not expected in some way)?
            _publisher.Tell(new InternalClusterAction.PublishChanges(g0));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(aUp));
            _memberSubscriber.ExpectMsg(new ClusterEvent.LeaderChanged(aUp.Address));
        }

        [Fact]
        public void ClusterDomainEventPublisherMustPublishMemberUp()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(g2));
            _publisher.Tell(new InternalClusterAction.PublishChanges(g3));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberExited(bExiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(cUp));
        }

        [Fact]
        public void ClusterDomainEventPublisherMustPublishLeaderChanged()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(g4));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(a51Up));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberExited(bExiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(cUp));
            _memberSubscriber.ExpectMsg(new ClusterEvent.LeaderChanged(a51Up.Address));
            _memberSubscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public void ClusterDomainEventPublisherMustPublishLeaderChangedWhenOldLeaderLeavesAndIsRemoved()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(g3));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberExited(bExiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(cUp));
            _publisher.Tell(new InternalClusterAction.PublishChanges(g6));
            _memberSubscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            _publisher.Tell(new InternalClusterAction.PublishChanges(g7));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberExited(aExiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.LeaderChanged(cUp.Address));
            _memberSubscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            // at the removed member a an empty gossip is the last thing
            _publisher.Tell(new InternalClusterAction.PublishChanges(Gossip.Empty));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberRemoved(aRemoved, MemberStatus.Exiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberRemoved(bRemoved, MemberStatus.Exiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberRemoved(cRemoved, MemberStatus.Up));
            //TODO: Null leader stuff is messy. Better approach?
            _memberSubscriber.ExpectMsg(new ClusterEvent.LeaderChanged(null));
        }

        [Fact]
        public void ClusterDomainEventPublisherMustNotPublishLeaderChangedWhenSameLeader()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(g4));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(a51Up));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberExited(bExiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(cUp));
            _memberSubscriber.ExpectMsg(new ClusterEvent.LeaderChanged(a51Up.Address));

            _publisher.Tell(new InternalClusterAction.PublishChanges(g5));
            _memberSubscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public void ClusterDomainEventPublisherMustPublishRoleLeaderChanged()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, ImmutableHashSet.Create(typeof(ClusterEvent.RoleLeaderChanged))));
            subscriber.ExpectMsg<ClusterEvent.CurrentClusterState>();
            // but only to the new subscriber
            _memberSubscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public void ClusterDomainEventPublisherMustSendCurrentClusterStateWhenSubscribe()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, ImmutableHashSet.Create(typeof(ClusterEvent.IClusterDomainEvent))));
            subscriber.ExpectMsg<ClusterEvent.CurrentClusterState>();
            // but only to the new subscriber
            _memberSubscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public void ClusterDomainEventPublisherMustSendEventsCorrespondingToCurrentStateWhenSubscribe()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.PublishChanges(g8));
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, ImmutableHashSet.Create(typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.ReachabilityEvent))));
            var received = subscriber.ReceiveN(4);
            XAssert.Equivalent(
                new object[]
                {
                    new ClusterEvent.MemberUp(aUp), new ClusterEvent.MemberUp(cUp), new ClusterEvent.MemberUp(dUp),
                    new ClusterEvent.MemberExited(bExiting)
                }, received );
            subscriber.ExpectMsg(new ClusterEvent.UnreachableMember(dUp));
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public void ClusterDomainEventPublisherMustSendPublishSeenChanged()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, ImmutableHashSet.Create(typeof(ClusterEvent.SeenChanged))));
            subscriber.ExpectMsg<ClusterEvent.CurrentClusterState>();
            _publisher.Tell(new InternalClusterAction.PublishChanges(g2));
            subscriber.ExpectMsg<ClusterEvent.SeenChanged>();
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            _publisher.Tell(new InternalClusterAction.PublishChanges(g3));
            subscriber.ExpectMsg<ClusterEvent.SeenChanged>();
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public void ClusterDomainEventPublisherMustPublishClusterShuttingDownAndRemovedWhenStopped()
        {
            _publisher.Tell(PoisonPill.Instance);
            _memberSubscriber.ExpectMsg(ClusterEvent.ClusterShuttingDown.Instance);
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberRemoved(aRemoved, MemberStatus.Up));
        }
    }
}

