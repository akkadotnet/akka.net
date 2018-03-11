//-----------------------------------------------------------------------
// <copyright file="GossipSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        
        static readonly Member dc1a1 = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, "dc1");
        static readonly Member dc1b1 = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, "dc1");
        static readonly Member dc2c1 = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, "dc2");
        static readonly Member dc2d1 = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552), MemberStatus.Up, ImmutableHashSet<string>.Empty, "dc2");
        static readonly Member dc2d2 = TestMember.Create(dc2d1.Address, MemberStatus.Down, ImmutableHashSet<string>.Empty, dc2d1.DataCenter);
        // restarted with another uid
        static readonly Member dc2d3 = TestMember.WithUniqueAddress(new UniqueAddress(dc2d1.Address, 3), MemberStatus.Up, ImmutableHashSet<string>.Empty, "dc2");

        private static MembershipState State(Gossip gossip, Member selfMember = null)
        {
            selfMember = selfMember ?? a1;
            return new MembershipState(gossip, selfMember.UniqueAddress, selfMember.DataCenter, crossDataCenterConnections: 5);
        }

        [Fact]
        public void A_gossip_must_have_correct_test_setup()
        {
            foreach (var member in new[] { a1, a2, b1, b2, c1, c2, c3, d1, e1, e2, e3 })
                member.DataCenter.Should().Be(ClusterSettings.DefaultDataCenter);
        }
        
        [Fact]
        public void A_gossip_must_reach_convergence_when_its_empty()
        {
            State(Gossip.Empty).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_for_one_node()
        {
            var g1 = new Gossip(ImmutableSortedSet.Create(a1)).Seen(a1.UniqueAddress);
            State(g1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_not_reach_convergence_until_all_have_seen_version()
        {
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1)).Seen(a1.UniqueAddress);
            State(g1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeFalse();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_for_two_nodes()
        {
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1)).Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            State(g1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_skipping_joining()
        {
            // e1 is joining
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, e1)).Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            State(g1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_skipping_down()
        {
            // e3 is down
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, e3)).Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            State(g1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_skipping_Leaving_with_ExitingConfirmed()
        {
            // c1 is leaving
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, c1)).Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            State(g1).Convergence(ImmutableHashSet.Create(c1.UniqueAddress)).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_skipping_Unreachable_Leaving_with_ExitingConfirmed()
        {
            // c1 is leaving
            var r1 = Reachability.Empty.Unreachable(b1.UniqueAddress, c1.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, c1), new GossipOverview(r1)).Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            State(g1).Convergence(ImmutableHashSet.Create(c1.UniqueAddress)).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_not_reach_convergence_when_unreachable()
        {
            var r1 = Reachability.Empty.Unreachable(b1.UniqueAddress, a1.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1), new GossipOverview(r1))
                .Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            State(g1, b1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeFalse();
            // but from a1's point of view (it knows that itself is not unreachable)
            State(g1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_when_downed_node_has_observed_unreachable()
        {
            // e3 is Down
            var r1 = Reachability.Empty.Unreachable(e3.UniqueAddress, a1.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, e3), new GossipOverview(r1))
                .Seen(a1.UniqueAddress).Seen(b1.UniqueAddress).Seen(e3.UniqueAddress);
            State(g1, b1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();
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
            merged1.Members.Should().BeEquivalentTo(ImmutableSortedSet.Create(a1, b1));
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
            State(new Gossip(ImmutableSortedSet.Create(c2, e2)), c2).Leader.Should().Be(c2.UniqueAddress);
            State(new Gossip(ImmutableSortedSet.Create(c3, e2)), c3).Leader.Should().Be(e2.UniqueAddress);
            State(new Gossip(ImmutableSortedSet.Create(c3)), c3).Leader.Should().Be(c3.UniqueAddress);
        }

        [Fact]
        public void A_gossip_must_not_have_Down_member_as_leader()
        {
            State(new Gossip(ImmutableSortedSet.Create(e3)), e3).Leader.Should().BeNull();
        }

        [Fact]
        public void A_gossip_must_have_leader_per_datacenter()
        {
            var g1 = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d1));
            
            // dc1a1 being leader of dc1
            State(g1, dc1a1).Leader.Should().Be(dc1a1.UniqueAddress);
            State(g1, dc1b1).Leader.Should().Be(dc1a1.UniqueAddress);

            // and dc2c1 being leader of dc2
            State(g1, dc2c1).Leader.Should().Be(dc2c1.UniqueAddress);
            State(g1, dc2d1).Leader.Should().Be(dc2c1.UniqueAddress);
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
            State(g1).YoungestMember.Should().Be(b1);
            var g2 = new Gossip(ImmutableSortedSet.Create(a2, b1.CopyUp(3), e1),
                new GossipOverview(Reachability.Empty.Unreachable(a2.UniqueAddress, b1.UniqueAddress).Unreachable(a2.UniqueAddress, e1.UniqueAddress)));
            State(g2).YoungestMember.Should().Be(b1);
            var g3 = new Gossip(ImmutableSortedSet.Create(a2, b1.CopyUp(3), e2.CopyUp(4)));
            State(g3).YoungestMember.Should().Be(e2);
        }

        [Fact]
        public void A_gossip_must_reach_convergence_per_datacenter()
        {
            var g = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d1))
                .Seen(dc1a1.UniqueAddress)
                .Seen(dc1b1.UniqueAddress)
                .Seen(dc2c1.UniqueAddress)
                .Seen(dc2d1.UniqueAddress);
            State(g, dc1a1).Leader.Should().Be(dc1a1.UniqueAddress);
            State(g, dc1a1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();

            State(g, dc2c1).Leader.Should().Be(dc2c1.UniqueAddress);
            State(g, dc2c1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();
        }
        
        [Fact]
        public void A_gossip_must_reach_convergence_per_datacenter_even_if_members_of_another_datacenter_have_not_seen_the_gossip()
        {
            var g = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d1))
                .Seen(dc1a1.UniqueAddress)
                .Seen(dc1b1.UniqueAddress)
                .Seen(dc2c1.UniqueAddress);
            // dc2d1 has not seen the gossip

            // so dc1 can reach convergence
            State(g, dc1a1).Leader.Should().Be(dc1a1.UniqueAddress);
            State(g, dc1a1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();

            // but dc2 cannot
            State(g, dc2c1).Leader.Should().Be(dc2c1.UniqueAddress);
            State(g, dc2c1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeFalse();
        }
        
        [Fact]
        public void A_gossip_must_reach_convergence_per_datacenter_even_if_another_datacenter_contains_unreachable()
        {
            var r1 = Reachability.Empty.Unreachable(dc2c1.UniqueAddress, dc2d1.UniqueAddress);

            var g = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d1), new GossipOverview(r1))
                .Seen(dc1a1.UniqueAddress)
                .Seen(dc1b1.UniqueAddress)
                .Seen(dc2c1.UniqueAddress)
                .Seen(dc2d1.UniqueAddress);

            // this data center doesn't care about dc2 having reachability problems and can reach convergence
            State(g, dc1a1).Leader.Should().Be(dc1a1.UniqueAddress);
            State(g, dc1a1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();

            // this data center is cannot reach convergence because of unreachability within the data center
            State(g, dc2c1).Leader.Should().Be(dc2c1.UniqueAddress);
            State(g, dc2c1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeFalse();
        }
        
        [Fact]
        public void A_gossip_must_reach_convergence_per_datacenter_even_if_there_is_unreachable_node_in_another_datacenter()
        {
            var r1 = Reachability.Empty
                .Unreachable(dc1a1.UniqueAddress, dc2d1.UniqueAddress)
                .Unreachable(dc2d1.UniqueAddress, dc1a1.UniqueAddress);

            var g = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d1), new GossipOverview(r1))
                .Seen(dc1a1.UniqueAddress)
                .Seen(dc1b1.UniqueAddress)
                .Seen(dc2c1.UniqueAddress)
                .Seen(dc2d1.UniqueAddress);

            // neither data center is affected by the inter data center unreachability as far as convergence goes
            State(g, dc1a1).Leader.Should().Be(dc1a1.UniqueAddress);
            State(g, dc1a1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();

            State(g, dc2c1).Leader.Should().Be(dc2c1.UniqueAddress);
            State(g, dc2c1).Convergence(ImmutableHashSet<UniqueAddress>.Empty).Should().BeTrue();
        }
        
        [Fact]
        public void A_gossip_must_ignore_cross_data_center_unreachability_when_determining_inside_of_data_center_reachability()
        {
            var r1 = Reachability.Empty
                .Unreachable(dc1a1.UniqueAddress, dc2c1.UniqueAddress)
                .Unreachable(dc2c1.UniqueAddress, dc1a1.UniqueAddress);

            var g = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d1), new GossipOverview(r1));
    
            // inside of the data center we don't care about the cross data center unreachability
            g.IsReachable(dc1a1.UniqueAddress, dc1b1.UniqueAddress).Should().BeTrue();
            g.IsReachable(dc1b1.UniqueAddress, dc1a1.UniqueAddress).Should().BeTrue();
            g.IsReachable(dc2c1.UniqueAddress, dc2d1.UniqueAddress).Should().BeTrue();
            g.IsReachable(dc2d1.UniqueAddress, dc2c1.UniqueAddress).Should().BeTrue();
    
            State(g, dc1a1).IsReachableExcludingDownedObservers(dc1b1.UniqueAddress).Should().BeTrue();
            State(g, dc1b1).IsReachableExcludingDownedObservers(dc1a1.UniqueAddress).Should().BeTrue();
            State(g, dc2c1).IsReachableExcludingDownedObservers(dc2d1.UniqueAddress).Should().BeTrue();
            State(g, dc2d1).IsReachableExcludingDownedObservers(dc2c1.UniqueAddress).Should().BeTrue();
    
            // between data centers it matters though
            g.IsReachable(dc1a1.UniqueAddress, dc2c1.UniqueAddress).Should().BeFalse();
            g.IsReachable(dc2c1.UniqueAddress, dc1a1.UniqueAddress).Should().BeFalse();
            // this isReachable method only says false for specific unreachable entries between the nodes
            g.IsReachable(dc1b1.UniqueAddress, dc2c1.UniqueAddress).Should().BeTrue();
            g.IsReachable(dc2d1.UniqueAddress, dc1a1.UniqueAddress).Should().BeTrue();
    
            // this one looks at all unreachable-entries for the to-address
            State(g, dc1a1).IsReachableExcludingDownedObservers(dc2c1.UniqueAddress).Should().BeFalse();
            State(g, dc1b1).IsReachableExcludingDownedObservers(dc2c1.UniqueAddress).Should().BeFalse();
            State(g, dc2c1).IsReachableExcludingDownedObservers(dc1a1.UniqueAddress).Should().BeFalse();
            State(g, dc2d1).IsReachableExcludingDownedObservers(dc1a1.UniqueAddress).Should().BeFalse();
    
            // between the two other nodes there is no unreachability
            g.IsReachable(dc1b1.UniqueAddress, dc2d1.UniqueAddress).Should().BeTrue();
            g.IsReachable(dc2d1.UniqueAddress, dc1b1.UniqueAddress).Should().BeTrue();
    
            State(g, dc1b1).IsReachableExcludingDownedObservers(dc2d1.UniqueAddress).Should().BeTrue();
            State(g, dc2d1).IsReachableExcludingDownedObservers(dc1b1.UniqueAddress).Should().BeTrue();
        }
        
        [Fact]
        public void A_gossip_must_not_returning_a_downed_data_center_leader()
        {
            var g = new Gossip(ImmutableSortedSet.Create(dc1a1.Copy(status: MemberStatus.Down), dc1b1));
            State(g, dc1b1).LeaderOf(g.Members).Should().Be(dc1b1.UniqueAddress);
        }
        
        [Fact]
        public void A_gossip_must_ignore_cross_data_center_unreachability_when_determining_data_center_leader()
        {
            var r1 = Reachability.Empty
                .Unreachable(dc1a1.UniqueAddress, dc2d1.UniqueAddress)
                .Unreachable(dc2d1.UniqueAddress, dc1a1.UniqueAddress);

            var g = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d1), new GossipOverview(r1));

            State(g, dc1a1).LeaderOf(g.Members).Should().Be(dc1a1.UniqueAddress);
            State(g, dc1b1).LeaderOf(g.Members).Should().Be(dc1a1.UniqueAddress);
            State(g, dc2c1).LeaderOf(g.Members).Should().Be(dc2c1.UniqueAddress);
            State(g, dc2d1).LeaderOf(g.Members).Should().Be(dc2c1.UniqueAddress);
        }
        
        // TODO test coverage for when leaderOf returns None - I have not been able to figure it out
        
        [Fact]
        public void A_gossip_must_clear_out_a_bunch_of_stuff_when_removing_a_node()
        {
            var g = new Gossip(
                    members: ImmutableSortedSet.Create(dc1a1, dc1b1, dc2d2),
                    overview: new GossipOverview(reachability:
                        Reachability.Empty
                            .Unreachable(dc1b1.UniqueAddress, dc2d2.UniqueAddress)
                            .Unreachable(dc2d2.UniqueAddress, dc1b1.UniqueAddress)))
                .Increment(VectorClock.Node.Create(Gossip.VectorClockName(dc1b1.UniqueAddress)))
                .Increment(VectorClock.Node.Create(Gossip.VectorClockName(dc2d2.UniqueAddress)))
                .Remove(dc1b1.UniqueAddress, DateTime.UtcNow);

            g.SeenBy.Should().NotContain(dc1b1.UniqueAddress);
            g.Overview.Reachability.Records.Select(r => r.Observer).Should().NotContain(dc1b1.UniqueAddress);
            g.Overview.Reachability.Records.Select(r => r.Subject).Should().NotContain(dc1b1.UniqueAddress);

            // sort order should be kept
            g.Members.ToList().Should().ContainInOrder(dc1a1, dc2d2);
            g.Version.Versions.Keys.Should().NotContain(VectorClock.Node.Create(Gossip.VectorClockName(dc1b1.UniqueAddress)));
            g.Version.Versions.Keys.Should().Contain(VectorClock.Node.Create(Gossip.VectorClockName(dc2d2.UniqueAddress)));
        }
        
        [Fact]
        public void A_gossip_must_not_reintroduce_members_from_out_of_data_center_gossip_when_merging()
        {
            // dc1 does not know about any unreachability nor that the node has been downed
            var gdc1 = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d1))
                .Seen(dc1b1.UniqueAddress)
                .Seen(dc2c1.UniqueAddress)
                .Increment(VectorClock.Node.Create(Gossip.VectorClockName(dc2d1.UniqueAddress))); // just to make sure these are also pruned

            // dc2 has downed the dc2d1 node, seen it as unreachable and removed it
            var gdc2 = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d1))
                .Seen(dc1a1.UniqueAddress)
                .Remove(dc2d1.UniqueAddress, DateTime.UtcNow);

            gdc2.Tombstones.Keys.Should().Contain(dc2d1.UniqueAddress);
            gdc2.Members.Should().NotContain(dc2d1);
            gdc2.Overview.Reachability.Records.Where(r => r.Subject == dc2d1.UniqueAddress || r.Observer == dc2d1.UniqueAddress).Should().BeEmpty();
            gdc2.Overview.Reachability.Versions.Keys.Should().NotContain(dc2d1.UniqueAddress);

            // when we merge the two, it should not be reintroduced
            var merged1 = gdc2.Merge(gdc1);
            merged1.Members.Should().BeEquivalentTo(dc1a1, dc1b1, dc2c1);

            merged1.Tombstones.Keys.Should().Contain(dc2d1.UniqueAddress);
            merged1.Members.Should().NotContain(dc2d1);
            merged1.Overview.Reachability.Records.Where(r => r.Subject == dc2d1.UniqueAddress || r.Observer == dc2d1.UniqueAddress).Should().BeEmpty();
            merged1.Overview.Reachability.Versions.Keys.Should().NotContain(dc2d1.UniqueAddress);
            merged1.Version.Versions.Keys.Should().NotContain(VectorClock.Node.Create(Gossip.VectorClockName(dc2d1.UniqueAddress)));
        }
        
        [Fact]
        public void A_gossip_must_replace_member_when_removed_and_rejoined_in_another_data_center()
        {
            // dc1 does not know removal and rejoin of new incarnation
            var gdc1 = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d1))
                .Seen(dc1b1.UniqueAddress)
                .Seen(dc2c1.UniqueAddress)
                .Increment(VectorClock.Node.Create(Gossip.VectorClockName(dc2d1.UniqueAddress))); // just to make sure these are also pruned
            
            // dc2 has removed the dc2d1 node, and then same host:port is restarted and joins again, without dc1 knowning
            var gdc2 = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d3))
                .Seen(dc1a1.UniqueAddress)
                .Remove(dc2d1.UniqueAddress, DateTime.UtcNow)
                .Copy(members: ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d3));

            gdc2.Tombstones.Keys.Should().Contain(dc2d1.UniqueAddress);
            gdc2.Members.Select(x => x.UniqueAddress).Should().NotContain(dc2d1.UniqueAddress);
            gdc2.Members.Select(x => x.UniqueAddress).Should().Contain(dc2d3.UniqueAddress);
            
            // when we merge the two, it should replace the old with new
            var merged1 = gdc2.Merge(gdc1);
            merged1.Members.Should().BeEquivalentTo(dc1a1, dc1b1, dc2c1, dc2d3);
            merged1.Members.Select(x => x.UniqueAddress).Should().NotContain(dc2d1.UniqueAddress);
            merged1.Members.Select(x => x.UniqueAddress).Should().Contain(dc2d3.UniqueAddress);

            merged1.Tombstones.Keys.Should().Contain(dc2d1.UniqueAddress);
            merged1.Tombstones.Keys.Should().NotContain(dc2d3.UniqueAddress);
            merged1.Overview.Reachability.Records.Where(r => r.Subject == dc2d1.UniqueAddress || r.Observer == dc2d1.UniqueAddress).Should().BeEmpty();
            merged1.Overview.Reachability.Versions.Keys.Should().NotContain(dc2c1.UniqueAddress);
            merged1.Version.Versions.Keys.Should().NotContain(VectorClock.Node.Create(Gossip.VectorClockName(dc2d1.UniqueAddress)));
        }
        
        [Fact]
        public void A_gossip_must_correctly_prune_vector_clocks_based_on_tombstones_when_merging()
        {
            var gdc1 = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d1))
                .Increment(VectorClock.Node.Create(Gossip.VectorClockName(dc1a1.UniqueAddress)))
                .Increment(VectorClock.Node.Create(Gossip.VectorClockName(dc1b1.UniqueAddress)))
                .Increment(VectorClock.Node.Create(Gossip.VectorClockName(dc2c1.UniqueAddress)))
                .Increment(VectorClock.Node.Create(Gossip.VectorClockName(dc2d1.UniqueAddress)))
                .Remove(dc1b1.UniqueAddress, DateTime.UtcNow);

            gdc1.Version.Versions.Keys.Should().NotContain(Gossip.VectorClockName(dc1b1.UniqueAddress));
            
            var gdc2 = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1, dc2c1, dc2d1))
                .Seen(dc1a1.UniqueAddress)
                .Increment(VectorClock.Node.Create(Gossip.VectorClockName(dc1a1.UniqueAddress)))
                .Increment(VectorClock.Node.Create(Gossip.VectorClockName(dc1b1.UniqueAddress)))
                .Increment(VectorClock.Node.Create(Gossip.VectorClockName(dc2c1.UniqueAddress)))
                .Increment(VectorClock.Node.Create(Gossip.VectorClockName(dc2d1.UniqueAddress)))
                .Remove(dc2c1.UniqueAddress, DateTime.UtcNow);
            
            gdc2.Version.Versions.Keys.Should().NotContain(Gossip.VectorClockName(dc2c1.UniqueAddress));
            
            // when we merge the two, the nodes should not be reintroduced
            var merged1 = gdc2.Merge(gdc1);
            merged1.Members.Should().BeEquivalentTo(dc1a1, dc2d1);

            merged1.Version.Versions.Keys.Should().BeEquivalentTo(
                VectorClock.Node.Create(Gossip.VectorClockName(dc1a1.UniqueAddress)),
                VectorClock.Node.Create(Gossip.VectorClockName(dc2d1.UniqueAddress)));
        }
        
        [Fact]
        public void A_gossip_must_prune_old_tombstones()
        {
            var timestamp = DateTime.UtcNow;
            var g = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1))
                .Remove(dc1b1.UniqueAddress, timestamp);

            g.Tombstones.Keys.Should().Contain(dc1b1.UniqueAddress);

            var pruned = g.PruneTombstones(timestamp.AddMilliseconds(1));

            // when we merge the two, it should not be reintroduced
            pruned.Tombstones.Keys.Should().NotContain(dc1b1.UniqueAddress);
        }
        
        [Fact]
        public void A_gossip_must_mark_a_node_as_down()
        {
            var g = new Gossip(ImmutableSortedSet.Create(dc1a1, dc1b1))
                .Seen(dc1a1.UniqueAddress)
                .Seen(dc1b1.UniqueAddress)
                .MarkAsDown(dc1b1);

            g.GetMember(dc1b1.UniqueAddress).Status.Should().Be(MemberStatus.Down);
            g.Overview.Seen.Should().NotContain(dc1b1.UniqueAddress);
            
            // obviously the other member should be unaffected
            g.GetMember(dc1a1.UniqueAddress).Status.Should().Be(dc1a1.Status);
            g.Overview.Seen.Should().Contain(dc1a1.UniqueAddress);
        }
        
        [Fact]
        public void A_gossip_must_update_members()
        {
            var joining = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552), MemberStatus.Joining, ImmutableHashSet<string>.Empty, "dc2");
            var g = new Gossip(ImmutableSortedSet.Create(dc1a1, joining));

            g.GetMember(joining.UniqueAddress).Status.Should().Be(MemberStatus.Joining);
            
            var oldMembers = g.Members;
            var updated = g.Update(ImmutableSortedSet.Create(joining.Copy(status: MemberStatus.Up)));

            updated.GetMember(joining.UniqueAddress).Status.Should().Be(MemberStatus.Up);
            
            // obviously the other member should be unaffected
            updated.GetMember(dc1a1.UniqueAddress).Status.Should().Be(dc1a1.Status);

            // order should be kept
            var l = updated.Members.ToList().Select(x => x.UniqueAddress).ToList();
            l[0].Should().Be(dc1a1.UniqueAddress);
            l[1].Should().Be(joining.UniqueAddress);
        }
    }
}

