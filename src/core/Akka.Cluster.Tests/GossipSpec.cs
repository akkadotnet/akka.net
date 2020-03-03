//-----------------------------------------------------------------------
// <copyright file="GossipSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.TestKit;
using Xunit;
using FluentAssertions;
using static Akka.Cluster.ClusterCoreDaemon;

namespace Akka.Cluster.Tests
{
    public class GossipSpec
    {
        static readonly Member a1 = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Up);
        static readonly Member a2 = TestMember.Create(a1.Address, MemberStatus.Joining);
        static readonly Member b1 = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Up);
        static readonly Member b2 = TestMember.Create(b1.Address, MemberStatus.Removed);
        static readonly Member c1 = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552), MemberStatus.Leaving);
        static readonly Member c2 = TestMember.Create(c1.Address, MemberStatus.Up);
        static readonly Member c3 = TestMember.Create(c1.Address, MemberStatus.Exiting);
        static readonly Member d1 = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552), MemberStatus.Leaving);
        static readonly Member e1 = TestMember.Create(new Address("akka.tcp", "sys", "e", 2552), MemberStatus.Joining);
        static readonly Member e2 = TestMember.Create(e1.Address, MemberStatus.Up);
        static readonly Member e3 = TestMember.Create(e1.Address, MemberStatus.Down);

        [Fact]
        public void A_gossip_must_reach_convergence_when_its_empty()
        {
            Gossip.Empty.Convergence(a1.UniqueAddress, new HashSet<UniqueAddress>()).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_for_one_node()
        {
            var g1 = new Gossip(ImmutableSortedSet.Create(a1)).Seen(a1.UniqueAddress);
            g1.Convergence(a1.UniqueAddress, new HashSet<UniqueAddress>()).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_not_reach_convergence_until_all_have_seen_version()
        {
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1)).Seen(a1.UniqueAddress);
            g1.Convergence(a1.UniqueAddress, new HashSet<UniqueAddress>()).Should().BeFalse();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_for_two_nodes()
        {
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1)).Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            g1.Convergence(a1.UniqueAddress, new HashSet<UniqueAddress>()).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_skipping_joining()
        {
            // e1 is joining
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, e1)).Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            g1.Convergence(a1.UniqueAddress, new HashSet<UniqueAddress>()).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_skipping_down()
        {
            // e3 is down
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, e3)).Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            g1.Convergence(a1.UniqueAddress, new HashSet<UniqueAddress>()).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_skipping_Leaving_with_ExitingConfirmed()
        {
            // c1 is leaving
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, c1)).Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            g1.Convergence(a1.UniqueAddress, new HashSet<UniqueAddress>() { c1.UniqueAddress }).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_skipping_Unreachable_Leaving_with_ExitingConfirmed()
        {
            // c1 is leaving
            var r1 = Reachability.Empty.Unreachable(b1.UniqueAddress, c1.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, c1), new GossipOverview(r1)).Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            g1.Convergence(a1.UniqueAddress, new HashSet<UniqueAddress>() { c1.UniqueAddress }).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_not_reach_convergence_when_unreachable()
        {
            var r1 = Reachability.Empty.Unreachable(b1.UniqueAddress, a1.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1), new GossipOverview(r1))
                .Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            g1.Convergence(b1.UniqueAddress, new HashSet<UniqueAddress>()).Should().BeFalse();
            // but from a1's point of view (it knows that itself is not unreachable)
            g1.Convergence(a1.UniqueAddress, new HashSet<UniqueAddress>()).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_when_downed_node_has_observed_unreachable()
        {
            // e3 is Down
            var r1 = Reachability.Empty.Unreachable(e3.UniqueAddress, a1.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, e3), new GossipOverview(r1))
                .Seen(a1.UniqueAddress).Seen(b1.UniqueAddress).Seen(e3.UniqueAddress);
            g1.Convergence(b1.UniqueAddress, new HashSet<UniqueAddress>()).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_merge_members_by_status_priority()
        {
            var g1 = Gossip.Create(ImmutableSortedSet.Create(a1, c1, e1));
            var g2 = Gossip.Create(ImmutableSortedSet.Create(a2, c2, e2));

            var merged1 = g1.Merge(g2);
            merged1.Members.Should().BeEquivalentTo(ImmutableSortedSet.Create(a2, c1, e1));
            merged1.Members.Select(c => c.Status).ToImmutableList().Should()
                .BeEquivalentTo(ImmutableList.Create(MemberStatus.Up, MemberStatus.Leaving, MemberStatus.Up));

            var merged2 = g2.Merge(g1);
            merged2.Members.Should().BeEquivalentTo(ImmutableSortedSet.Create(a2, c1, e1));
            merged2.Members.Select(c => c.Status).ToImmutableList().Should()
                .BeEquivalentTo(ImmutableList.Create(MemberStatus.Up, MemberStatus.Leaving, MemberStatus.Up));
        }

        [Fact]
        public void A_gossip_must_merge_unreachable()
        {
            var r1 = Reachability.Empty.
                Unreachable(b1.UniqueAddress, a1.UniqueAddress).
                Unreachable(b1.UniqueAddress, c1.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, c1), new GossipOverview(r1));
            var r2 = Reachability.Empty.Unreachable(a1.UniqueAddress, d1.UniqueAddress);
            var g2 = new Gossip(ImmutableSortedSet.Create(a1, b1, c1, d1), new GossipOverview(r2));

            var merged1 = g1.Merge(g2);
            merged1.Overview.Reachability.AllUnreachable.Should()
                .BeEquivalentTo(ImmutableHashSet.Create(a1.UniqueAddress, c1.UniqueAddress, d1.UniqueAddress));

            var merged2 = g2.Merge(g1);
            merged2.Overview.Reachability.AllUnreachable.Should()
                .BeEquivalentTo(merged1.Overview.Reachability.AllUnreachable);
        }

        [Fact]
        public void A_gossip_must_merge_members_by_removing_removed_members()
        {
            // c3 removed
            var r1 = Reachability.Empty.Unreachable(b1.UniqueAddress, a1.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1), new GossipOverview(r1));
            var r2 = r1.Unreachable(b1.UniqueAddress, c3.UniqueAddress);
            var g2 = new Gossip(ImmutableSortedSet.Create(a1, b1, c3), new GossipOverview(r2));

            var merged1 = g1.Merge(g2);
            merged1.Members.Should().BeEquivalentTo(ImmutableHashSet.Create(a1, b1));
            merged1.Overview.Reachability.AllUnreachable.Should()
                .BeEquivalentTo(ImmutableHashSet.Create(a1.UniqueAddress));

            var merged2 = g2.Merge(g1);
            merged2.Overview.Reachability.AllUnreachable.Should()
                .BeEquivalentTo(merged1.Overview.Reachability.AllUnreachable);
            merged2.Members.Should().BeEquivalentTo(merged1.Members);
        }

        [Fact]
        public void A_gossip_must_have_leader_as_first_member_based_on_ordering_except_exiting_status()
        {
            new Gossip(ImmutableSortedSet.Create(c2, e2)).Leader(c2.UniqueAddress).Should().Be(c2.UniqueAddress);
            new Gossip(ImmutableSortedSet.Create(c3, e2)).Leader(c3.UniqueAddress).Should().Be(e2.UniqueAddress);
            new Gossip(ImmutableSortedSet.Create(c3)).Leader(c3.UniqueAddress).Should().Be(c3.UniqueAddress);
        }

        [Fact]
        public void A_gossip_must_not_have_Down_member_as_leader()
        {
            new Gossip(ImmutableSortedSet.Create(e3)).Leader(e3.UniqueAddress).Should().BeNull();
        }

        [Fact]
        public void A_gossip_must_merge_seen_table_correctly()
        {
            var vclockNode = VectorClock.Node.Create("something");
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, c1, d1)).Increment(vclockNode)
                    .Seen(a1.UniqueAddress)
                    .Seen(b1.UniqueAddress);
            var g2 = new Gossip(ImmutableSortedSet.Create(a1, b1, c1, d1)).Increment(vclockNode)
                    .Seen(a1.UniqueAddress)
                    .Seen(c1.UniqueAddress);
            var g3 = g1.Copy(version: g2.Version).Seen(d1.UniqueAddress);

            Action<Gossip> checkMerge = merged =>
            {
                var seen = merged.Overview.Seen;
                seen.Count.Should().Be(0);

                merged.SeenByNode(a1.UniqueAddress).Should().BeFalse();
                merged.SeenByNode(b1.UniqueAddress).Should().BeFalse();
                merged.SeenByNode(c1.UniqueAddress).Should().BeFalse();
                merged.SeenByNode(d1.UniqueAddress).Should().BeFalse();
                merged.SeenByNode(e1.UniqueAddress).Should().BeFalse();
            };

            checkMerge(g3.Merge(g2));
            checkMerge(g2.Merge(g3));
        }

        [Fact]
        public void A_gossip_must_know_who_is_youngest()
        {
            // a2 and e1 is Joining
            var g1 = new Gossip(ImmutableSortedSet.Create(a2, b1.CopyUp(3), e1),
                new GossipOverview(Reachability.Empty.Unreachable(a2.UniqueAddress, e1.UniqueAddress)));
            g1.YoungestMember.Should().Be(b1);
            var g2 = new Gossip(ImmutableSortedSet.Create(a2, b1.CopyUp(3), e1),
                new GossipOverview(Reachability.Empty.Unreachable(a2.UniqueAddress, b1.UniqueAddress).Unreachable(a2.UniqueAddress, e1.UniqueAddress)));
            g2.YoungestMember.Should().Be(b1);
            var g3 = new Gossip(ImmutableSortedSet.Create(a2, b1.CopyUp(3), e2.CopyUp(4)));
            g3.YoungestMember.Should().Be(e2);
        }

        [Fact]
        public void A_gossip_must_find_two_oldest_as_targets_for_Exiting_change()
        {
            Member a1 = TestMember.Create(new Address("akka.tcp", "sys", "a4", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, upNumber: 1);
            Member a2 = TestMember.Create(new Address("akka.tcp", "sys", "a3", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, upNumber: 2);
            Member a3 = TestMember.Create(new Address("akka.tcp", "sys", "a2", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, upNumber: 3);
            Member a4 = TestMember.Create(new Address("akka.tcp", "sys", "a1", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, upNumber: 4);

            var a1Exiting = a1.Copy(MemberStatus.Leaving).Copy(MemberStatus.Exiting);
            var gossip = new Gossip(ImmutableSortedSet.Create(a1Exiting, a2, a3, a4));
            var r = ClusterCoreDaemon.GossipTargetsForExitingMembers(gossip, new Member[] { a1Exiting });
            r.Should().BeEquivalentTo(new[] { a1Exiting, a2 });
        }

        [Fact]
        public void A_gossip_must_find_two_oldest_per_role_as_targets_for_Exiting_change()
        {
            Member a1 = TestMember.Create(new Address("akka.tcp", "sys", "a4", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, upNumber: 1);
            Member a2 = TestMember.Create(new Address("akka.tcp", "sys", "a3", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, upNumber: 2);
            Member a3 = TestMember.Create(new Address("akka.tcp", "sys", "a2", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, upNumber: 3);
            Member a4 = TestMember.Create(new Address("akka.tcp", "sys", "a1", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, upNumber: 4);
            Member a5 = TestMember.Create(new Address("akka.tcp", "sys", "a5", 2552), MemberStatus.Exiting, ImmutableHashSet<string>.Empty.Add("role1").Add("role2"), upNumber: 5);
            Member a6 = TestMember.Create(new Address("akka.tcp", "sys", "a6", 2552), MemberStatus.Exiting, ImmutableHashSet<string>.Empty.Add("role1").Add("role3"), upNumber: 6);
            Member a7 = TestMember.Create(new Address("akka.tcp", "sys", "a7", 2552), MemberStatus.Exiting, ImmutableHashSet<string>.Empty.Add("role1"), upNumber: 7);
            Member a8 = TestMember.Create(new Address("akka.tcp", "sys", "a8", 2552), MemberStatus.Exiting, ImmutableHashSet<string>.Empty.Add("role1"), upNumber: 8);
            Member a9 = TestMember.Create(new Address("akka.tcp", "sys", "a9", 2552), MemberStatus.Exiting, ImmutableHashSet<string>.Empty.Add("role2"), upNumber: 9);

            IEnumerable<Member> theExiting = new Member[] { a5, a6 };
            var gossip = new Gossip(ImmutableSortedSet.Create(a1, a2, a3, a4, a5, a6, a7, a8, a9));

            var r = ClusterCoreDaemon.GossipTargetsForExitingMembers(gossip, theExiting);
            r.Should().BeEquivalentTo(new[] { a1, a2, a5, a6, a9 });
        }
    }
}

