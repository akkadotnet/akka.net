//-----------------------------------------------------------------------
// <copyright file="ClusterDomainEventSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class ClusterDomainEventSpec
    {
        static readonly ImmutableHashSet<string> aRoles = ImmutableHashSet.Create("AA", "AB");
        static readonly Member aJoining = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Joining, aRoles);
        static readonly Member aUp = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Up, aRoles);
        static readonly Member aRemoved = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Removed, aRoles);
        static readonly ImmutableHashSet<string> bRoles = ImmutableHashSet.Create("AB", "BB");
        static readonly Member bJoining = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Joining, bRoles);
        static readonly Member bUp = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Up, bRoles);
        static readonly Member bDown = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Down, bRoles);
        static readonly Member bRemoved = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Removed, bRoles);
        static readonly ImmutableHashSet<string> cRoles = ImmutableHashSet.Create<string>();
        static readonly Member cUp = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552), MemberStatus.Up, cRoles);
        static readonly Member cLeaving = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552), MemberStatus.Leaving, cRoles);
        static readonly ImmutableHashSet<string> dRoles = ImmutableHashSet.Create("DD", "DE");
        static readonly Member dLeaving = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552), MemberStatus.Leaving, dRoles);
        static readonly Member dExiting = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552), MemberStatus.Exiting, dRoles);
        static readonly Member dRemoved = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552), MemberStatus.Removed, dRoles);
        static readonly ImmutableHashSet<string> eRoles = ImmutableHashSet.Create("EE", "DE");
        static readonly Member eJoining = TestMember.Create(new Address("akka.tcp", "sys", "e", 2552), MemberStatus.Joining, eRoles);
        static readonly Member eUp = TestMember.Create(new Address("akka.tcp", "sys", "e", 2552), MemberStatus.Up, eRoles);
        static readonly Member eDown = TestMember.Create(new Address("akka.tcp", "sys", "e", 2552), MemberStatus.Down, eRoles);

        private static readonly UniqueAddress selfDummyAddress =
            new UniqueAddress(new Address("akka.tcp", "sys", "selfDummy", 2552), 17);

        private static Tuple<Gossip, ImmutableHashSet<UniqueAddress>> Converge(Gossip gossip)
        {
            var seed = Tuple.Create(gossip, ImmutableHashSet.Create<UniqueAddress>());

            return gossip.Members.Aggregate(seed,
                (t, m) => Tuple.Create(t.Item1.Seen(m.UniqueAddress), t.Item2.Add(m.UniqueAddress)));
        }

        [Fact]
        public void DomainEvents_must_be_empty_for_the_same_gossip()
        {
            var g1 = new Gossip(ImmutableSortedSet.Create(aUp));

            Assert.Equal(ImmutableSortedSet.Create<object>(), ClusterEvent.DiffUnreachable(g1, g1));
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_new_members()
        {
            var t1 = Converge(new Gossip(ImmutableSortedSet.Create(aUp)));
            var t2 = Converge(new Gossip(ImmutableSortedSet.Create(aUp, bUp, eJoining)));

            var g1 = t1.Item1;
            var g2 = t2.Item1;
            var s2 = t2.Item2;

            Assert.Equal(ImmutableList.Create(new ClusterEvent.MemberUp(bUp)), ClusterEvent.DiffMemberEvents(g1, g2));
            Assert.Equal(ImmutableList.Create<ClusterEvent.UnreachableMember>(), ClusterEvent.DiffUnreachable(g1, g2));

            Assert.Equal(ImmutableList.Create(new ClusterEvent.SeenChanged(true, s2.Select(s => s.Address).ToImmutableHashSet())), ClusterEvent.DiffSeen(g1, g2, selfDummyAddress));
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_changed_status_of_members()
        {
            var t1 = Converge(new Gossip(ImmutableSortedSet.Create(aJoining, bUp, cUp)));
            var t2 = Converge(new Gossip(ImmutableSortedSet.Create(aUp, bUp, cLeaving, eJoining)));

            var g1 = t1.Item1;
            var g2 = t2.Item1;
            var s2 = t2.Item2;

            Assert.Equal(ImmutableList.Create(new ClusterEvent.MemberUp(aUp)), ClusterEvent.DiffMemberEvents(g1, g2));
            Assert.Equal(ImmutableList.Create<ClusterEvent.UnreachableMember>(), ClusterEvent.DiffUnreachable(g1, g2));
            Assert.Equal(ImmutableList.Create(new ClusterEvent.SeenChanged(true, s2.Select(s => s.Address).ToImmutableHashSet())), ClusterEvent.DiffSeen(g1, g2, selfDummyAddress));
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_members_in_unreachable()
        {
            var reachability1 = Reachability.Empty.
                Unreachable(aUp.UniqueAddress, cUp.UniqueAddress).
                Unreachable(aUp.UniqueAddress, eUp.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(aUp, bUp, cUp, eUp), new GossipOverview(reachability1));
            var reachability2 = reachability1.
                Unreachable(aUp.UniqueAddress, bDown.UniqueAddress);
            var g2 = new Gossip(ImmutableSortedSet.Create(aUp, cUp, bDown, eDown), new GossipOverview(reachability2));

            Assert.Equal(ImmutableList.Create(new ClusterEvent.UnreachableMember(bDown)), ClusterEvent.DiffUnreachable(g1, g2));
            Assert.Equal(ImmutableList.Create<ClusterEvent.SeenChanged>(), ClusterEvent.DiffSeen(g1, g2, selfDummyAddress));
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_members_becoming_reachable_after_unreachable()
        {
            var reachability1 = Reachability.Empty.
                Unreachable(aUp.UniqueAddress, cUp.UniqueAddress).Reachable(aUp.UniqueAddress, cUp.UniqueAddress).
                Unreachable(aUp.UniqueAddress, eUp.UniqueAddress).
                Unreachable(aUp.UniqueAddress, bUp.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(aUp, bUp, cUp, eUp), new GossipOverview(reachability1));
            var reachability2 = reachability1.
                Unreachable(aUp.UniqueAddress, cUp.UniqueAddress).
                Reachable(aUp.UniqueAddress, bUp.UniqueAddress);
            var g2 = new Gossip(ImmutableSortedSet.Create(aUp, cUp, bUp, eUp), new GossipOverview(reachability2));

            Assert.Equal(ImmutableList.Create(new ClusterEvent.UnreachableMember(cUp)), ClusterEvent.DiffUnreachable(g1, g2));
            Assert.Equal(ImmutableList.Create(new ClusterEvent.ReachableMember(bUp)), ClusterEvent.DiffReachable(g1,g2));
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_convergence_changes()
        {
            var g1 =
                new Gossip(ImmutableSortedSet.Create(aUp, bUp, eJoining)).Seen(aUp.UniqueAddress)
                    .Seen(bUp.UniqueAddress)
                    .Seen(eJoining.UniqueAddress);
            var g2 = new Gossip(ImmutableSortedSet.Create(aUp, bUp, eJoining)).Seen(aUp.UniqueAddress).Seen(bUp.UniqueAddress);

            Assert.Equal(ImmutableList.Create<ClusterEvent.IMemberEvent>(), ClusterEvent.DiffMemberEvents(g1, g2));
            Assert.Equal(ImmutableList.Create<ClusterEvent.UnreachableMember>(), ClusterEvent.DiffUnreachable(g1, g2));
            Assert.Equal(ImmutableList.Create(new ClusterEvent.SeenChanged(true, ImmutableHashSet.Create(aUp.Address, bUp.Address))), ClusterEvent.DiffSeen(g1, g2, selfDummyAddress));
            Assert.Equal(ImmutableList.Create<ClusterEvent.IMemberEvent>(), ClusterEvent.DiffMemberEvents(g2, g1));
            Assert.Equal(ImmutableList.Create<ClusterEvent.UnreachableMember>(), ClusterEvent.DiffUnreachable(g2, g1));
            Assert.Equal(ImmutableList.Create(new ClusterEvent.SeenChanged(true, ImmutableHashSet.Create(aUp.Address, bUp.Address, eJoining.Address))), ClusterEvent.DiffSeen(g2, g1, selfDummyAddress));
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_leader_changes()
        {
            var t1 = Converge(new Gossip(ImmutableSortedSet.Create(aUp, bUp, eJoining)));
            var t2 = Converge(new Gossip(ImmutableSortedSet.Create(bUp, eJoining)));

            var g1 = t1.Item1;
            var g2 = t2.Item1;
            var s2 = t2.Item2;

            Assert.Equal(ImmutableList.Create(new ClusterEvent.MemberRemoved(aRemoved, MemberStatus.Up)), ClusterEvent.DiffMemberEvents(g1, g2));
            Assert.Equal(ImmutableList.Create<ClusterEvent.UnreachableMember>(), ClusterEvent.DiffUnreachable(g1, g2));
            Assert.Equal(ImmutableList.Create(new ClusterEvent.SeenChanged(true, s2.Select(a => a.Address).ToImmutableHashSet())), ClusterEvent.DiffSeen(g1, g2, selfDummyAddress));
            Assert.Equal(ImmutableList.Create(new ClusterEvent.LeaderChanged(bUp.Address)), ClusterEvent.DiffLeader(g1, g2, selfDummyAddress));
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_role_leader_changes()
        {
            var g0 = Gossip.Empty;
            var g1 = new Gossip(ImmutableSortedSet.Create(aUp, bUp, cUp, dLeaving, eJoining));
            var g2 = new Gossip(ImmutableSortedSet.Create(bUp, cUp, dExiting, eJoining));
            var expected = ImmutableHashSet.Create(
                new ClusterEvent.RoleLeaderChanged("AA", aUp.Address),
                new ClusterEvent.RoleLeaderChanged("AB", aUp.Address),
                new ClusterEvent.RoleLeaderChanged("BB", bUp.Address),
                new ClusterEvent.RoleLeaderChanged("DD", dLeaving.Address),
                new ClusterEvent.RoleLeaderChanged("DE", dLeaving.Address),
                new ClusterEvent.RoleLeaderChanged("EE", eUp.Address)
                );
            Assert.Equal(expected, ClusterEvent.DiffRolesLeader(g0, g1, selfDummyAddress));
            var expected2 = ImmutableHashSet.Create(
                new ClusterEvent.RoleLeaderChanged("AA", null),
                new ClusterEvent.RoleLeaderChanged("AB", bUp.Address),
                new ClusterEvent.RoleLeaderChanged("DE", eJoining.Address)
                );
            Assert.Equal(expected2, ClusterEvent.DiffRolesLeader(g1, g2, selfDummyAddress));
        }
    }
}

