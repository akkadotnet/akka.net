//-----------------------------------------------------------------------
// <copyright file="ClusterDomainEventSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Xunit;
using FluentAssertions;

namespace Akka.Cluster.Tests
{
    public class ClusterDomainEventSpec
    {
        static readonly ImmutableHashSet<string> aRoles = ImmutableHashSet.Create("AA", "AB");
        static readonly Member aJoining = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Joining, aRoles);
        static readonly Member aUp = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Up, aRoles);
        static readonly Member aRemoved = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Removed, aRoles);
        static readonly ImmutableHashSet<string> bRoles = ImmutableHashSet.Create("AB", "BB");
        static readonly Member bUp = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Up, bRoles);
        static readonly Member bDown = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Down, bRoles);
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
        static readonly UniqueAddress selfDummyAddress = new UniqueAddress(new Address("akka.tcp", "sys", "selfDummy", 2552), 17);

        private static Tuple<Gossip, ImmutableHashSet<UniqueAddress>> Converge(Gossip gossip)
        {
            var seed = Tuple.Create(gossip, ImmutableHashSet.Create<UniqueAddress>());

            return gossip.Members.Aggregate(seed,
                (t, m) => Tuple.Create(t.Item1.Seen(m.UniqueAddress), t.Item2.Add(m.UniqueAddress)));
        }

        private static MembershipState State(Gossip g) => State(g, selfDummyAddress);
        private static MembershipState State(Gossip g, UniqueAddress self) => 
            new MembershipState(g, self, ClusterSettings.DefaultDataCenter, crossDataCenterConnections: 5);

        [Fact]
        public void DomainEvents_must_be_empty_for_the_same_gossip()
        {
            var g1 = new Gossip(ImmutableSortedSet.Create(aUp));
            ClusterEvent.DiffUnreachable(State(g1), State(g1)).Should().BeEmpty();
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_new_members()
        {
            var t1 = Converge(new Gossip(ImmutableSortedSet.Create(aUp)));
            var t2 = Converge(new Gossip(ImmutableSortedSet.Create(aUp, bUp, eJoining)));

            var g1 = t1.Item1;
            var g2 = t2.Item1;
            var s2 = t2.Item2;

            ClusterEvent.DiffMemberEvents(State(g1), State(g2))
                .Should()
                .BeEquivalentTo(new ClusterEvent.MemberUp(bUp), new ClusterEvent.MemberJoined(eJoining));

            ClusterEvent.DiffUnreachable(State(g1), State(g2))
                .Should()
                .BeEmpty();

            var expected = new ClusterEvent.SeenChanged(true, s2.Select(s => s.Address).ToImmutableHashSet());
            var actual = ClusterEvent.DiffSeen(State(g1), State(g2));
            actual.Should().Be(expected);
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_changed_status_of_members()
        {
            var t1 = Converge(new Gossip(ImmutableSortedSet.Create(aJoining, bUp, cUp)));
            var t2 = Converge(new Gossip(ImmutableSortedSet.Create(aUp, bUp, cLeaving, eJoining)));

            var g1 = t1.Item1;
            var g2 = t2.Item1;
            var s2 = t2.Item2;

            ClusterEvent.DiffMemberEvents(State(g1), State(g2))
                .Should()
                .BeEquivalentTo(new ClusterEvent.MemberUp(aUp), new ClusterEvent.MemberLeft(cLeaving), new ClusterEvent.MemberJoined(eJoining));

            ClusterEvent.DiffUnreachable(State(g1), State(g2))
                .Should()
                .BeEmpty();

            var expected = new ClusterEvent.SeenChanged(true, s2.Select(s => s.Address).ToImmutableHashSet());
            var actual = ClusterEvent.DiffSeen(State(g1), State(g2));
            actual.Should().Be(expected);
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

            ClusterEvent.DiffUnreachable(State(g1), State(g2))
                 .Should()
                 .BeEquivalentTo(ImmutableList.Create(new ClusterEvent.UnreachableMember(bDown)));

            // never include self member in unreachable
            ClusterEvent.DiffUnreachable(State(g1, bDown.UniqueAddress), State(g2, bDown.UniqueAddress))
                .Should()
                .BeEmpty();

            ClusterEvent.DiffSeen(State(g1), State(g2)).Should().BeNull();
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_reachability_observations_between_data_centers()
        {
            var dc2AMemberUp = TestMember.Create(new Address("akka.tcp", "sys", "dc2A", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, "dc2");
            var dc2AMemberDown = TestMember.Create(new Address("akka.tcp", "sys", "dc2A", 2552), MemberStatus.Down, ImmutableHashSet<string>.Empty, "dc2");
            var dc2BMemberUp = TestMember.Create(new Address("akka.tcp", "sys", "dc2B", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, "dc2");

            var dc3AMemberUp = TestMember.Create(new Address("akka.tcp", "sys", "dc3A", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, "dc3");
            var dc3BMemberUp = TestMember.Create(new Address("akka.tcp", "sys", "dc3B", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, "dc3");

            var reachability1 = Reachability.Empty;
            var g1 = new Gossip(
                members: ImmutableSortedSet.Create(aUp, bUp, dc2AMemberUp, dc2BMemberUp, dc3AMemberUp, dc3BMemberUp),
                overview: new GossipOverview(reachability: reachability1));

            var reachability2 = reachability1
                .Unreachable(aUp.UniqueAddress, dc2AMemberDown.UniqueAddress)
                .Unreachable(dc2BMemberUp.UniqueAddress, dc2AMemberDown.UniqueAddress);
            var g2 = new Gossip(
                members: ImmutableSortedSet.Create(aUp, bUp, dc2AMemberDown, dc2BMemberUp, dc3AMemberUp, dc3BMemberUp),
                overview: new GossipOverview(reachability: reachability2));

            foreach (var member in new []{aUp, bUp, dc2AMemberUp, dc2BMemberUp, dc3AMemberUp, dc3BMemberUp})
            {
                var otherDc = member.DataCenter == ClusterSettings.DefaultDataCenter
                    ? new []{"dc2"}
                    : Enumerable.Empty<string>();

                ClusterEvent.DiffUnreachableDataCenters(
                        new MembershipState(g1, member.UniqueAddress, member.DataCenter, crossDataCenterConnections: 5),
                        new MembershipState(g2, member.UniqueAddress, member.DataCenter, crossDataCenterConnections: 5))
                    .Should().BeEquivalentTo(otherDc.Select(x => new ClusterEvent.UnreachableDataCenter(x)));

                ClusterEvent.DiffReachableDataCenters(
                        new MembershipState(g2, member.UniqueAddress, member.DataCenter, crossDataCenterConnections: 5),
                        new MembershipState(g1, member.UniqueAddress, member.DataCenter, crossDataCenterConnections: 5))
                    .Should().BeEquivalentTo(otherDc.Select(x => new ClusterEvent.ReachableDataCenter(x)));
            }
        }

        [Fact]
        public void DomainEvents_must_not_be_produced_for_same_reachability_observations_between_data_centers()
        {
            var dc2AMemberUp = TestMember.Create(new Address("akka.tcp", "sys", "dc2A", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, "dc2");
            var dc2AMemberDown = TestMember.Create(new Address("akka.tcp", "sys", "dc2A", 2552), MemberStatus.Down, ImmutableHashSet<string>.Empty, "dc2");

            var reachability1 = Reachability.Empty;
            var g1 = new Gossip(
                members: ImmutableSortedSet.Create(aUp, dc2AMemberUp), 
                overview: new GossipOverview(reachability: reachability1));

            var reachability2 = reachability1.Unreachable(aUp.UniqueAddress, dc2AMemberDown.UniqueAddress);
            var g2 = new Gossip(
                members: ImmutableSortedSet.Create(aUp, dc2AMemberDown), 
                overview: new GossipOverview(reachability: reachability2));

            ClusterEvent.DiffUnreachableDataCenters(
                    new MembershipState(g1, aUp.UniqueAddress, aUp.DataCenter, crossDataCenterConnections: 5),
                    new MembershipState(g1, aUp.UniqueAddress, aUp.DataCenter, crossDataCenterConnections: 5))
                .Should().BeEmpty();

            ClusterEvent.DiffUnreachableDataCenters(
                    new MembershipState(g2, aUp.UniqueAddress, aUp.DataCenter, crossDataCenterConnections: 5),
                    new MembershipState(g2, aUp.UniqueAddress, aUp.DataCenter, crossDataCenterConnections: 5)) 
                .Should().BeEmpty();

            ClusterEvent.DiffUnreachableDataCenters(
                    new MembershipState(g1, aUp.UniqueAddress, aUp.DataCenter, crossDataCenterConnections: 5),
                    new MembershipState(g1, aUp.UniqueAddress, aUp.DataCenter, crossDataCenterConnections: 5))
                .Should().BeEmpty();

            ClusterEvent.DiffUnreachableDataCenters(
                    new MembershipState(g2, aUp.UniqueAddress, aUp.DataCenter, crossDataCenterConnections:  5),
                    new MembershipState(g2, aUp.UniqueAddress, aUp.DataCenter, crossDataCenterConnections:  5))
                .Should().BeEmpty();
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_members_becoming_reachable_after_unreachable()
        {
            var reachability1 = Reachability.Empty.
                Unreachable(aUp.UniqueAddress, cUp.UniqueAddress).
                Reachable(aUp.UniqueAddress, cUp.UniqueAddress).
                Unreachable(aUp.UniqueAddress, eUp.UniqueAddress).
                Unreachable(aUp.UniqueAddress, bUp.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(aUp, bUp, cUp, eUp), new GossipOverview(reachability1));
            var reachability2 = reachability1.
                Unreachable(aUp.UniqueAddress, cUp.UniqueAddress).
                Reachable(aUp.UniqueAddress, bUp.UniqueAddress);
            var g2 = new Gossip(ImmutableSortedSet.Create(aUp, cUp, bUp, eUp), new GossipOverview(reachability2));

            ClusterEvent.DiffUnreachable(State(g1), State(g2))
                .Should()
                .BeEquivalentTo(ImmutableList.Create(new ClusterEvent.UnreachableMember(cUp)));
            // never include self member in unreachable
            ClusterEvent.DiffUnreachable(State(g1, cUp.UniqueAddress), State(g2, cUp.UniqueAddress))
                .Should()
                .BeEmpty();

            ClusterEvent.DiffReachable(State(g1), State(g2))
                .Should()
                .BeEquivalentTo(ImmutableList.Create(new ClusterEvent.ReachableMember(bUp)));
            // never include self member in reachable
            ClusterEvent.DiffReachable(State(g1, bUp.UniqueAddress), State(g2, bUp.UniqueAddress))
                .Should()
                .BeEmpty();
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_downed_members()
        {
            var t1 = Converge(new Gossip(ImmutableSortedSet.Create(aUp, eUp)));
            var t2 = Converge(new Gossip(ImmutableSortedSet.Create(aUp, eDown)));

            var g1 = t1.Item1;
            var g2 = t2.Item1;

            ClusterEvent.DiffMemberEvents(State(g1), State(g2))
                .Should()
                .BeEquivalentTo(ImmutableList.Create<ClusterEvent.IMemberEvent>(new ClusterEvent.MemberDowned(eDown)));

            ClusterEvent.DiffUnreachable(State(g1), State(g2))
                .Should()
                .BeEquivalentTo(ImmutableList.Create<ClusterEvent.UnreachableMember>());
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_removed_members()
        {
            var t1 = Converge(new Gossip(ImmutableSortedSet.Create(aUp, dExiting)));
            var t2 = Converge(new Gossip(ImmutableSortedSet.Create(aUp)));

            var g1 = t1.Item1;
            var g2 = t2.Item1;
            var s2 = t2.Item2;

            ClusterEvent.DiffMemberEvents(State(g1), State(g2))
                .Should()
                .BeEquivalentTo(ImmutableList.Create<ClusterEvent.IMemberEvent>(new ClusterEvent.MemberRemoved(dRemoved, MemberStatus.Exiting)));

            ClusterEvent.DiffUnreachable(State(g1), State(g2))
                .Should()
                .BeEmpty();

            ClusterEvent.DiffSeen(State(g1), State(g2))
                .Should()
                .Be(new ClusterEvent.SeenChanged(true, s2.Select(s => s.Address).ToImmutableHashSet()));
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_removed_and_rejoined_members_in_another_data_center()
        {
            var bUpDc2 = TestMember.Create(bUp.Address, MemberStatus.Up, bRoles, dataCenter: "dc2");
            var bUpDc2Removed = TestMember.Create(bUpDc2.Address, MemberStatus.Removed, bRoles, dataCenter: "dc2");
            var bUpDc2Restarted =
                TestMember.WithUniqueAddress(new UniqueAddress(bUpDc2.Address, 2), MemberStatus.Up, bRoles, dataCenter: "dc2");
            var g1 = new Gossip(ImmutableSortedSet.Create(aUp, bUpDc2));
            var g2 = g1
                .Remove(bUpDc2.UniqueAddress, DateTime.UtcNow) // adds tombstone
                .Copy(ImmutableSortedSet.Create(aUp, bUpDc2Restarted))
                .Merge(g1);

            ClusterEvent.DiffMemberEvents(State(g1), State(g2)).Should().BeEquivalentTo(
                new ClusterEvent.MemberRemoved(bUpDc2Removed, MemberStatus.Up), new ClusterEvent.MemberUp(bUpDc2Restarted));
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_convergence_changes()
        {
            var g1 = new Gossip(ImmutableSortedSet.Create(aUp, bUp, eJoining))
                .Seen(aUp.UniqueAddress)
                .Seen(bUp.UniqueAddress)
                .Seen(eJoining.UniqueAddress);
            var g2 = new Gossip(ImmutableSortedSet.Create(aUp, bUp, eJoining))
                .Seen(aUp.UniqueAddress)
                .Seen(bUp.UniqueAddress);

            ClusterEvent.DiffMemberEvents(State(g1), State(g2))
                .Should()
                .BeEquivalentTo(ImmutableList.Create<ClusterEvent.IMemberEvent>());
            ClusterEvent.DiffUnreachable(State(g1), State(g2))
                .Should()
                .BeEmpty();
            ClusterEvent.DiffSeen(State(g1), State(g2))
                .Should()
                .Be(new ClusterEvent.SeenChanged(true, ImmutableHashSet.Create(aUp.Address, bUp.Address)));
            ClusterEvent.DiffMemberEvents(State(g2), State(g1))
                .Should()
                .BeEquivalentTo(ImmutableList.Create<ClusterEvent.IMemberEvent>());
            ClusterEvent.DiffUnreachable(State(g2), State(g1))
                .Should()
                .BeEquivalentTo(ImmutableList.Create<ClusterEvent.UnreachableMember>());
            ClusterEvent.DiffSeen(State(g2), State(g1))
                .Should()
                .Be(new ClusterEvent.SeenChanged(true, ImmutableHashSet.Create(aUp.Address, bUp.Address, eJoining.Address)));
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_leader_changes()
        {
            var t1 = Converge(new Gossip(ImmutableSortedSet.Create(aUp, bUp, eJoining)));
            var t2 = Converge(new Gossip(ImmutableSortedSet.Create(bUp, eJoining)));

            var g1 = t1.Item1;
            var g2 = t2.Item1;
            var s2 = t2.Item2;

            ClusterEvent.DiffMemberEvents(State(g1), State(g2))
                .Should()
                .BeEquivalentTo(ImmutableList.Create(new ClusterEvent.MemberRemoved(aRemoved, MemberStatus.Up)));
            ClusterEvent.DiffUnreachable(State(g1), State(g2))
                .Should()
                .BeEmpty();
            ClusterEvent.DiffSeen(State(g1), State(g2))
                .Should()
                .Be(new ClusterEvent.SeenChanged(true, s2.Select(a => a.Address).ToImmutableHashSet()));
            ClusterEvent.DiffLeader(State(g1), State(g2))
                .Should()
                .Be(new ClusterEvent.LeaderChanged(bUp.Address));
        }

        [Fact]
        public void DomainEvents_must_be_produced_for_role_leader_changes_in_the_same_data_center()
        {
            var g0 = Gossip.Empty;
            var g1 = new Gossip(ImmutableSortedSet.Create(aUp, bUp, cUp, dLeaving, eJoining));
            var g2 = new Gossip(ImmutableSortedSet.Create(bUp, cUp, dExiting, eJoining));

            ClusterEvent.DiffRolesLeader(State(g0), State(g1)).Should().BeEquivalentTo(
                // since this role is implicitly added
                new ClusterEvent.RoleLeaderChanged(ClusterSettings.DcRolePrefix + ClusterSettings.DefaultDataCenter, aUp.Address),
                new ClusterEvent.RoleLeaderChanged("AA", aUp.Address),
                new ClusterEvent.RoleLeaderChanged("AB", aUp.Address),
                new ClusterEvent.RoleLeaderChanged("BB", bUp.Address),
                new ClusterEvent.RoleLeaderChanged("DD", dLeaving.Address),
                new ClusterEvent.RoleLeaderChanged("DE", dLeaving.Address),
                new ClusterEvent.RoleLeaderChanged("EE", eUp.Address));

            ClusterEvent.DiffRolesLeader(State(g1), State(g2)).Should().BeEquivalentTo(
                new ClusterEvent.RoleLeaderChanged(ClusterSettings.DcRolePrefix + ClusterSettings.DefaultDataCenter, bUp.Address),
                new ClusterEvent.RoleLeaderChanged("AA", null),
                new ClusterEvent.RoleLeaderChanged("AB", bUp.Address),
                new ClusterEvent.RoleLeaderChanged("DE", eJoining.Address));
        }

        [Fact]
        public void DomainEvents_must_not_be_produced_for_role_leader_changes_in_other_data_centers()
        {
            var g0 = Gossip.Empty;
            var s0 = State(g0).Copy(selfDataCenter: "dc2");
            var g1 = new Gossip(ImmutableSortedSet.Create(aUp, bUp, cUp, dLeaving, eJoining));
            var s1 = State(g1).Copy(selfDataCenter: "dc2");
            var g2 = new Gossip(ImmutableSortedSet.Create(bUp, cUp, dExiting, eJoining));
            var s2 = State(g2).Copy(selfDataCenter: "dc2");

            ClusterEvent.DiffRolesLeader(s0, s1).Should().BeEmpty();
            ClusterEvent.DiffRolesLeader(s1, s2).Should().BeEmpty();
            
        }
    }
}

