//-----------------------------------------------------------------------
// <copyright file="ClusterDomainEventPublisherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
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
        static readonly MembershipState state0 = new MembershipState(g0, aUp.UniqueAddress);
        static readonly Gossip g1 = new Gossip(ImmutableSortedSet.Create(aUp, cJoining)).Seen(aUp.UniqueAddress).Seen(cJoining.UniqueAddress);
        static readonly MembershipState state1 = new MembershipState(g1, aUp.UniqueAddress);
        static readonly Gossip g2 = new Gossip(ImmutableSortedSet.Create(aUp, bExiting, cUp)).Seen(aUp.UniqueAddress);
        static readonly MembershipState state2 = new MembershipState(g2, aUp.UniqueAddress);
        static readonly Gossip g3 = g2.Seen(bExiting.UniqueAddress).Seen(cUp.UniqueAddress);
        static readonly MembershipState state3 = new MembershipState(g3, aUp.UniqueAddress);
        static readonly Gossip g4 = new Gossip(ImmutableSortedSet.Create(a51Up, aUp, bExiting, cUp)).Seen(aUp.UniqueAddress);
        static readonly MembershipState state4 = new MembershipState(g4, aUp.UniqueAddress);
        static readonly Gossip g5 = new Gossip(ImmutableSortedSet.Create(a51Up, aUp, bExiting, cUp)).Seen(aUp.UniqueAddress).Seen(bExiting.UniqueAddress).Seen(cUp.UniqueAddress);
        static readonly MembershipState state5 = new MembershipState(g5, aUp.UniqueAddress);
        static readonly Gossip g6 = new Gossip(ImmutableSortedSet.Create(aLeaving, bExiting, cUp)).Seen(aUp.UniqueAddress);
        static readonly MembershipState state6 = new MembershipState(g6, aUp.UniqueAddress);
        static readonly Gossip g7 = new Gossip(ImmutableSortedSet.Create(aExiting, bExiting, cUp)).Seen(aUp.UniqueAddress);
        static readonly MembershipState state7 = new MembershipState(g7, aUp.UniqueAddress);
        static readonly Gossip g8 = new Gossip(ImmutableSortedSet.Create(aUp, bExiting, cUp, dUp), new GossipOverview(Reachability.Empty.Unreachable(aUp.UniqueAddress, dUp.UniqueAddress))).Seen(aUp.UniqueAddress);
        static readonly MembershipState state8 = new MembershipState(g8, aUp.UniqueAddress);

        static readonly MembershipState _emptyMembershipState = new MembershipState(Gossip.Empty, aUp.UniqueAddress);
        readonly TestProbe _memberSubscriber;

        public ClusterDomainEventPublisherSpec() : base(Config)
        {
            _memberSubscriber = CreateTestProbe();
            Sys.EventStream.Subscribe(_memberSubscriber.Ref, typeof(ClusterEvent.IMemberEvent));
            Sys.EventStream.Subscribe(_memberSubscriber.Ref, typeof(ClusterEvent.LeaderChanged));
            Sys.EventStream.Subscribe(_memberSubscriber.Ref, typeof(ClusterEvent.ClusterShuttingDown));

            var selfUniqueAddress = Cluster.Get(Sys).SelfUniqueAddress;
            _publisher = Sys.ActorOf(Props.Create(() => new ClusterDomainEventPublisher(selfUniqueAddress)));
            _publisher.Tell(new InternalClusterAction.PublishChanges(state0));
            _memberSubscriber.ExpectMsg(new ClusterEvent.MemberUp(aUp));
            _memberSubscriber.ExpectMsg(new ClusterEvent.LeaderChanged(aUp.Address));
        }

        [Fact]
        public async Task ClusterDomainEventPublisher_must_publish_MemberJoined()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(state1));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberJoined(cJoining));
        }

        [Fact]
        public async Task ClusterDomainEventPublisher_must_publish_MemberUp()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(state2));
            _publisher.Tell(new InternalClusterAction.PublishChanges(state3));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberExited(bExiting));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberUp(cUp));
        }

        [Fact]
        public async Task ClusterDomainEventPublisher_must_publish_leader_changed()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(state4));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberUp(a51Up));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberExited(bExiting));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberUp(cUp));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.LeaderChanged(a51Up.Address));
            await _memberSubscriber.ExpectNoMsgAsync(500.Milliseconds());
        }

        [Fact]
        public async Task ClusterDomainEventPublisher_must_publish_leader_changed_when_old_leader_leaves_and_is_removed()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(state3));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberExited(bExiting));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberUp(cUp));
            _publisher.Tell(new InternalClusterAction.PublishChanges(state6));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberLeft(aLeaving));
            _publisher.Tell(new InternalClusterAction.PublishChanges(state7));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberExited(aExiting));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.LeaderChanged(cUp.Address));
            await _memberSubscriber.ExpectNoMsgAsync(500.Milliseconds());
            // at the removed member a an empty gossip is the last thing
            _publisher.Tell(new InternalClusterAction.PublishChanges(_emptyMembershipState));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberRemoved(aRemoved, MemberStatus.Exiting));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberRemoved(bRemoved, MemberStatus.Exiting));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberRemoved(cRemoved, MemberStatus.Up));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.LeaderChanged(null));
        }

        [Fact]
        public async Task ClusterDomainEventPublisher_must_not_publish_leader_changed_when_same_leader()
        {
            _publisher.Tell(new InternalClusterAction.PublishChanges(state4));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberUp(a51Up));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberExited(bExiting));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberUp(cUp));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.LeaderChanged(a51Up.Address));

            _publisher.Tell(new InternalClusterAction.PublishChanges(state5));
            await _memberSubscriber.ExpectNoMsgAsync(500.Milliseconds());
        }

        [Fact]
        public async Task ClusterDomainEventPublisher_must_publish_role_leader_changed()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, ImmutableHashSet.Create(typeof(ClusterEvent.RoleLeaderChanged))));
            await subscriber.ExpectMsgAsync<ClusterEvent.CurrentClusterState>();
            _publisher.Tell(new InternalClusterAction.PublishChanges(new MembershipState(new Gossip(ImmutableSortedSet.Create(cJoining, dUp)), dUp.UniqueAddress)));
            await subscriber.ExpectMsgAsync(new ClusterEvent.RoleLeaderChanged("GRP", dUp.Address));
            _publisher.Tell(new InternalClusterAction.PublishChanges(new MembershipState(new Gossip(ImmutableSortedSet.Create(cUp, dUp)), dUp.UniqueAddress)));
            await subscriber.ExpectMsgAsync(new ClusterEvent.RoleLeaderChanged("GRP", cUp.Address));
        }

        [Fact]
        public async Task ClusterDomainEventPublisher_must_send_CurrentClusterState_when_subscribe()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, ImmutableHashSet.Create(typeof(ClusterEvent.IClusterDomainEvent))));
            await subscriber.ExpectMsgAsync<ClusterEvent.CurrentClusterState>();
            // but only to the new subscriber
            await _memberSubscriber.ExpectNoMsgAsync(500.Milliseconds());
        }

        [Fact]
        public async Task ClusterDomainEventPublisher_must_send_events_corresponding_to_current_state_when_subscribe()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.PublishChanges(state8));
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, ImmutableHashSet.Create(typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.ReachabilityEvent))));

            (await subscriber.ReceiveNAsync(4).ToListAsync()).Should().BeEquivalentTo( 
                new ClusterEvent.MemberStatusChange[] {
                    new ClusterEvent.MemberUp(aUp),
                    new ClusterEvent.MemberUp(cUp),
                    new ClusterEvent.MemberUp(dUp),
                    new ClusterEvent.MemberExited(bExiting)
                });

            await subscriber.ExpectMsgAsync(new ClusterEvent.UnreachableMember(dUp));
            await subscriber.ExpectNoMsgAsync(500.Milliseconds());
        }

        [Fact]
        public async Task ClusterDomainEventPublisher_should_support_unsubscribe()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, ImmutableHashSet.Create(typeof(ClusterEvent.IMemberEvent))));
            await subscriber.ExpectMsgAsync<ClusterEvent.CurrentClusterState>();
            _publisher.Tell(new InternalClusterAction.Unsubscribe(subscriber.Ref, typeof(ClusterEvent.IMemberEvent)));
            _publisher.Tell(new InternalClusterAction.PublishChanges(state3));
            await subscriber.ExpectNoMsgAsync(500.Milliseconds());
            // but memberSubscriber is still subscriber
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberExited(bExiting));
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberUp(cUp));
        }

        [Fact]
        public async Task ClusterDomainEventPublisher_must_publish_seen_changed()
        {
            var subscriber = CreateTestProbe();
            _publisher.Tell(new InternalClusterAction.Subscribe(subscriber.Ref, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, ImmutableHashSet.Create(typeof(ClusterEvent.SeenChanged))));
            await subscriber.ExpectMsgAsync<ClusterEvent.CurrentClusterState>();
            _publisher.Tell(new InternalClusterAction.PublishChanges(state2));
            await subscriber.ExpectMsgAsync<ClusterEvent.SeenChanged>();
            await subscriber.ExpectNoMsgAsync(500.Milliseconds());
            _publisher.Tell(new InternalClusterAction.PublishChanges(state3));
            await subscriber.ExpectMsgAsync<ClusterEvent.SeenChanged>();
            await subscriber.ExpectNoMsgAsync(500.Milliseconds());
        }

        [Fact]
        public async Task ClusterDomainEventPublisher_must_publish_removed_when_stopped()
        {
            _publisher.Tell(PoisonPill.Instance);
            await _memberSubscriber.ExpectMsgAsync(ClusterEvent.ClusterShuttingDown.Instance);
            await _memberSubscriber.ExpectMsgAsync(new ClusterEvent.MemberRemoved(aRemoved, MemberStatus.Up));
        }

    }
}

