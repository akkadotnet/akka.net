//-----------------------------------------------------------------------
// <copyright file="ClusterDomainEventPublisherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class ClusterDomainEventPublisherSpec : AkkaSpec
    {
        const string Config = @"    
            akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            akka.remote.dot-netty.tcp.port = 0";

        readonly IActorRef _publisher;
        static readonly Member aUp = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Up);
        static readonly Member aLeaving = aUp.Copy(MemberStatus.Leaving);
        static readonly Member aExiting = aLeaving.Copy(MemberStatus.Exiting);
        static readonly Member aRemoved = aExiting.Copy(MemberStatus.Removed);
        static readonly Member bExiting = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Exiting);
        static readonly Member bRemoved = bExiting.Copy(MemberStatus.Removed);
        static readonly Member cJoining = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552), MemberStatus.Joining, ImmutableHashSet.Create("GRP"));
        static readonly Member cUp = cJoining.Copy(MemberStatus.Up);
        static readonly Member cRemoved = cUp.Copy(MemberStatus.Removed);
        static readonly Member a51Up = TestMember.Create(new Address("akk.tcp", "sys", "a", 2551), MemberStatus.Up);
        static readonly Member dUp = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552), MemberStatus.Up, ImmutableHashSet.Create("GRP"));

        static readonly Gossip g0 = new Gossip(ImmutableSortedSet.Create(aUp)).Seen(aUp.UniqueAddress);
        static readonly Gossip g1 = new Gossip(ImmutableSortedSet.Create(aUp, cJoining)).Seen(aUp.UniqueAddress).Seen(cJoining.UniqueAddress);
        static readonly Gossip g2 = new Gossip(ImmutableSortedSet.Create(aUp, bExiting, cUp)).Seen(aUp.UniqueAddress);
        static readonly Gossip g3 = g2.Seen(bExiting.UniqueAddress).Seen(cUp.UniqueAddress);
        static readonly Gossip g4 = new Gossip(ImmutableSortedSet.Create(a51Up, aUp, bExiting, cUp)).Seen(aUp.UniqueAddress);
        static readonly Gossip g5 = new Gossip(ImmutableSortedSet.Create(a51Up, aUp, bExiting, cUp)).Seen(aUp.UniqueAddress).Seen(bExiting.UniqueAddress).Seen(cUp.UniqueAddress);
        static readonly Gossip g6 = new Gossip(ImmutableSortedSet.Create(aLeaving, bExiting, cUp)).Seen(aUp.UniqueAddress);
        static readonly Gossip g7 = new Gossip(ImmutableSortedSet.Create(aExiting, bExiting, cUp)).Seen(aUp.UniqueAddress);
        static readonly Gossip g8 = new Gossip(ImmutableSortedSet.Create(aUp, bExiting, cUp, dUp), new GossipOverview(Reachability.Empty.Unreachable(aUp.UniqueAddress, dUp.UniqueAddress))).Seen(aUp.UniqueAddress);

        readonly TestProbe _memberSubscriber;

        public ClusterDomainEventPublisherSpec() : base(Config)
        {
            _memberSubscriber = CreateTestProbe();
            Sys.EventStream.Subscribe(_memberSubscriber.Ref, typeof(ClusterEvent.IMemberEvent));
            Sys.EventStream.Subscribe(_memberSubscriber.Ref, typeof(ClusterEvent.LeaderChanged));
            Sys.EventStream.Subscribe(_memberSubscriber.Ref, typeof(ClusterEvent.ClusterShuttingDown));

            _publisher = Sys.ActorOf(Props.Create<ClusterDomainEventPublisher>());
            _publisher.Tell(new InternalClusterAction.PublishChanges(g0));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(aUp));
            _memberSubscriber.ExpectMsg(new ClusterEvent.LeaderChanged(aUp.Address));
        }

        [Fact]
        public void ClusterDomainEventPublisher_must_publish_MemberJoined()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(g1));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberJoined(cJoining));
        }

        [Fact]
        public void ClusterDomainEventPublisher_must_publish_MemberUp()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(g2));
            _publisher.Tell(new InternalClusterAction.PublishChanges(g3));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberExited(bExiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(cUp));
        }

        [Fact]
        public void ClusterDomainEventPublisher_must_publish_leader_changed()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(g4));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(a51Up));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberExited(bExiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(cUp));
            _memberSubscriber.ExpectMsg(new ClusterEvent.LeaderChanged(a51Up.Address));
            _memberSubscriber.ExpectNoMsg(500.Milliseconds());
        }

        [Fact]
        public void ClusterDomainEventPublisher_must_publish_leader_changed_when_old_leader_leaves_and_is_removed()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(g3));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberExited(bExiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(cUp));
            _publisher.Tell(new InternalClusterAction.PublishChanges(g6));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberLeft(aLeaving));
            _publisher.Tell(new InternalClusterAction.PublishChanges(g7));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberExited(aExiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.LeaderChanged(cUp.Address));
            _memberSubscriber.ExpectNoMsg(500.Milliseconds());
            // at the removed member a an empty gossip is the last thing
            _publisher.Tell(new InternalClusterAction.PublishChanges(Gossip.Empty));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberRemoved(aRemoved, MemberStatus.Exiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberRemoved(bRemoved, MemberStatus.Exiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberRemoved(cRemoved, MemberStatus.Up));
            _memberSubscriber.ExpectMsg(new ClusterEvent.LeaderChanged(null));
        }

        [Fact]
        public void ClusterDomainEventPublisher_must_not_publish_leader_changed_when_same_leader()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(g4));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(a51Up));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberExited(bExiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(cUp));
            _memberSubscriber.ExpectMsg(new ClusterEvent.LeaderChanged(a51Up.Address));

            _publisher.Tell(new InternalClusterAction.PublishChanges(g5));
            _memberSubscriber.ExpectNoMsg(500.Milliseconds());
        }

        [Fact]
        public void ClusterDomainEventPublisher_must_publish_role_leader_changed()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, ImmutableHashSet.Create(typeof(ClusterEvent.RoleLeaderChanged))));
            subscriber.ExpectMsg<ClusterEvent.CurrentClusterState>();
            _publisher.Tell(new InternalClusterAction.PublishChanges(new Gossip(ImmutableSortedSet.Create(cJoining, dUp))));
            subscriber.ExpectMsg(new ClusterEvent.RoleLeaderChanged("GRP", dUp.Address));
            _publisher.Tell(new InternalClusterAction.PublishChanges(new Gossip(ImmutableSortedSet.Create(cUp, dUp))));
            subscriber.ExpectMsg(new ClusterEvent.RoleLeaderChanged("GRP", cUp.Address));
        }

        [Fact]
        public void ClusterDomainEventPublisher_must_send_CurrentClusterState_when_subscribe()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, ImmutableHashSet.Create(typeof(ClusterEvent.IClusterDomainEvent))));
            subscriber.ExpectMsg<ClusterEvent.CurrentClusterState>();
            // but only to the new subscriber
            _memberSubscriber.ExpectNoMsg(500.Milliseconds());
        }

        [Fact]
        public void ClusterDomainEventPublisher_must_send_events_corresponding_to_current_state_when_subscribe()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.PublishChanges(g8));
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, ImmutableHashSet.Create(typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.ReachabilityEvent))));

            subscriber.ReceiveN(4).Should().BeEquivalentTo(
                new ClusterEvent.MemberUp(aUp),
                new ClusterEvent.MemberUp(cUp),
                new ClusterEvent.MemberUp(dUp),
                new ClusterEvent.MemberExited(bExiting));

            subscriber.ExpectMsg(new ClusterEvent.UnreachableMember(dUp));
            subscriber.ExpectNoMsg(500.Milliseconds());
        }

        [Fact]
        public void ClusterDomainEventPublisher_should_support_unsubscribe()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, ImmutableHashSet.Create(typeof(ClusterEvent.IMemberEvent))));
            subscriber.ExpectMsg<ClusterEvent.CurrentClusterState>();
            _publisher.Tell(new InternalClusterAction.Unsubscribe(subscriber.Ref, typeof(ClusterEvent.IMemberEvent)));
            _publisher.Tell(new InternalClusterAction.PublishChanges(g3));
            subscriber.ExpectNoMsg(500.Milliseconds());
            // but memberSubscriber is still subscriber
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberExited(bExiting));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(cUp));
        }

        [Fact]
        public void ClusterDomainEventPublisher_must_publish_seen_changed()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, ImmutableHashSet.Create(typeof(ClusterEvent.SeenChanged))));
            subscriber.ExpectMsg<ClusterEvent.CurrentClusterState>();
            _publisher.Tell(new InternalClusterAction.PublishChanges(g2));
            subscriber.ExpectMsg<ClusterEvent.SeenChanged>();
            subscriber.ExpectNoMsg(500.Milliseconds());
            _publisher.Tell(new InternalClusterAction.PublishChanges(g3));
            subscriber.ExpectMsg<ClusterEvent.SeenChanged>();
            subscriber.ExpectNoMsg(500.Milliseconds());
        }

        [Fact]
        public void ClusterDomainEventPublisher_must_publish_removed_when_stopped()
        {
            _publisher.Tell(PoisonPill.Instance);
            _memberSubscriber.ExpectMsg(ClusterEvent.ClusterShuttingDown.Instance);
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberRemoved(aRemoved, MemberStatus.Up));
        }

    }
}

