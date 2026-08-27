//-----------------------------------------------------------------------
// <copyright file="GossipSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

        private MembershipState State(Gossip g, Member selfMember = null)
        {
            selfMember = selfMember ?? a1;
            return new MembershipState(g, selfMember.UniqueAddress);
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
            State(g1).Convergence(ImmutableHashSet<UniqueAddress>.Empty.Add(c1.UniqueAddress)).Should().BeTrue();
        }

        [Fact]
        public void A_gossip_must_reach_convergence_skipping_Unreachable_Leaving_with_ExitingConfirmed()
        {
            // c1 is leaving
            var r1 = Reachability.Empty.Unreachable(b1.UniqueAddress, c1.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, c1), new GossipOverview(r1)).Seen(a1.UniqueAddress).Seen(b1.UniqueAddress);
            State(g1).Convergence(ImmutableHashSet<UniqueAddress>.Empty.Add(c1.UniqueAddress)).Should().BeTrue();
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
            merged1.Members.Should().BeEquivalentTo(ImmutableHashSet.Create(a1, b1));
            merged1.Overview.Reachability.AllUnreachable.Should()
                .BeEquivalentTo(ImmutableHashSet.Create(a1.UniqueAddress));

            var merged2 = g2.Merge(g1);
            merged2.Overview.Reachability.AllUnreachable.Should()
                .BeEquivalentTo(merged1.Overview.Reachability.AllUnreachable);
            merged2.Members.Should().BeEquivalentTo(merged1.Members);
        }

        // ---------------------------------------------------------------------------------------------
        // Removal tombstones
        // ---------------------------------------------------------------------------------------------

        private static ImmutableDictionary<UniqueAddress, long> Tombstone(Member m, long timestamp = 1000L) =>
            ImmutableDictionary<UniqueAddress, long>.Empty.Add(m.UniqueAddress, timestamp);

        private static VectorClock.Node VclockNodeOf(Member m) => VectorClock.Node.Create(VclockName(m.UniqueAddress));

        [Fact(DisplayName = "Merge should not resurrect a removed member that a lagging peer still holds as Leaving")]
        public void A_gossip_must_not_resurrect_a_tombstoned_member()
        {
            // the leader removed c1 while it was Exiting, and recorded a tombstone for it
            var leaderGossip = new Gossip(ImmutableSortedSet.Create(a1, b1), new GossipOverview(),
                VectorClock.Create(), Tombstone(c1));

            // a peer that has not caught up still holds c1 as Leaving - the status alone gives the merge
            // nothing to go on, because Leaving is not terminal
            var laggingPeerGossip = Gossip.Create(ImmutableSortedSet.Create(a1, b1, c1));

            leaderGossip.Merge(laggingPeerGossip).Members.Should().BeEquivalentTo(ImmutableHashSet.Create(a1, b1));
            laggingPeerGossip.Merge(leaderGossip).Members.Should().BeEquivalentTo(ImmutableHashSet.Create(a1, b1));
        }

        [Fact(DisplayName = "Merge should resurrect a one-sided Leaving member when no tombstone says otherwise")]
        public void A_gossip_must_keep_a_one_sided_leaving_member_without_a_tombstone()
        {
            // exactly the setup above minus the tombstone. c1 is kept, which is both the bug being fixed
            // and the correct answer here: without evidence of a removal, a one-sided Leaving member may
            // simply be a node the other side has not heard about yet, and dropping it would strand a live
            // process that can never rejoin.
            var g1 = Gossip.Create(ImmutableSortedSet.Create(a1, b1));
            var g2 = Gossip.Create(ImmutableSortedSet.Create(a1, b1, c1));

            g1.Merge(g2).Members.Should().Contain(c1);
            g2.Merge(g1).Members.Should().Contain(c1);
        }

        [Fact(DisplayName = "Merge should union tombstones the same way in both directions")]
        public void A_gossip_must_union_tombstones_commutatively()
        {
            var g1 = new Gossip(ImmutableSortedSet.Create(a1), new GossipOverview(), VectorClock.Create(),
                Tombstone(c1, 100L));
            var g2 = new Gossip(ImmutableSortedSet.Create(a1), new GossipOverview(), VectorClock.Create(),
                Tombstone(d1, 200L));

            var merged1 = g1.Merge(g2);
            var merged2 = g2.Merge(g1);

            merged1.Tombstones.Should().Equal(merged2.Tombstones);
            merged1.Tombstones.Keys.Should().BeEquivalentTo(new[] { c1.UniqueAddress, d1.UniqueAddress });
            merged1.Members.Should().BeEquivalentTo(merged2.Members);
        }

        [Fact(DisplayName = "Merge should keep the later timestamp when both sides tombstone the same node")]
        public void A_gossip_must_keep_the_later_timestamp_on_a_tombstone_collision()
        {
            var g1 = new Gossip(ImmutableSortedSet.Create(a1), new GossipOverview(), VectorClock.Create(),
                Tombstone(c1, 100L));
            var g2 = new Gossip(ImmutableSortedSet.Create(a1), new GossipOverview(), VectorClock.Create(),
                Tombstone(c1, 500L));

            g1.Merge(g2).Tombstones[c1.UniqueAddress].Should().Be(500L);
            g2.Merge(g1).Tombstones[c1.UniqueAddress].Should().Be(500L);
        }

        [Fact(DisplayName = "Merge should prune the merged vector clock for every tombstoned node")]
        public void A_gossip_must_prune_the_merged_vector_clock_by_tombstones()
        {
            // each side carries a clock entry for BOTH removed nodes, and a tombstone the other side lacks.
            // Unioning the clocks would put both entries back, exactly the way unioning members would put
            // the members back.
            var clock = VectorClock.Create()
                .Increment(VclockNodeOf(c1))
                .Increment(VclockNodeOf(d1))
                .Increment(VclockNodeOf(a1));

            var g1 = new Gossip(ImmutableSortedSet.Create(a1, d1), new GossipOverview(), clock, Tombstone(c1));
            var g2 = new Gossip(ImmutableSortedSet.Create(a1, c1), new GossipOverview(), clock, Tombstone(d1));

            foreach (var merged in new[] { g1.Merge(g2), g2.Merge(g1) })
            {
                merged.Members.Should().BeEquivalentTo(ImmutableHashSet.Create(a1));
                merged.Version.Versions.Keys.Should().NotContain(VclockNodeOf(c1));
                merged.Version.Versions.Keys.Should().NotContain(VclockNodeOf(d1));
                merged.Version.Versions.Keys.Should().Contain(VclockNodeOf(a1));
            }
        }

        [Fact(DisplayName = "Merge should still drop one-sided Down and Exiting members with no tombstones present")]
        public void A_gossip_must_still_drop_one_sided_terminal_members_without_tombstones()
        {
            // the drop condition was widened with OR, not replaced
            var g1 = Gossip.Create(ImmutableSortedSet.Create(a1, b1));
            var g2 = Gossip.Create(ImmutableSortedSet.Create(a1, b1, c3, e3));

            foreach (var merged in new[] { g1.Merge(g2), g2.Merge(g1) })
            {
                merged.Members.Should().BeEquivalentTo(ImmutableHashSet.Create(a1, b1));
                merged.Tombstones.Should().BeEmpty();
            }
        }

        [Fact(DisplayName = "Merge should let a new incarnation join even while its predecessor is tombstoned")]
        public void A_gossip_must_not_block_a_new_incarnation_with_an_old_tombstone()
        {
            // same host and port, different UID. The tombstone key carries the UID, so it says nothing
            // about the new incarnation.
            var oldIncarnation = TestMember.Create(c1.Address, MemberStatus.Leaving, uid: 1);
            var newIncarnation = TestMember.Create(c1.Address, MemberStatus.Joining, uid: 2);

            var g1 = new Gossip(ImmutableSortedSet.Create(a1), new GossipOverview(), VectorClock.Create(),
                Tombstone(oldIncarnation));
            var g2 = Gossip.Create(ImmutableSortedSet.Create(a1, newIncarnation));

            foreach (var merged in new[] { g1.Merge(g2), g2.Merge(g1) })
            {
                merged.Members.Should().Contain(newIncarnation);
                merged.Tombstones.Keys.Should().NotContain(newIncarnation.UniqueAddress);
                merged.Tombstones.Keys.Should().Contain(oldIncarnation.UniqueAddress);
            }
        }

        [Fact(DisplayName = "RemoveAll should strip a node from members, seen, reachability and the vector clock at once")]
        public void A_gossip_must_strip_everything_when_removing_a_node()
        {
            var reachability = Reachability.Empty
                .Unreachable(c1.UniqueAddress, b1.UniqueAddress)  // c1 as observer
                .Unreachable(b1.UniqueAddress, c1.UniqueAddress); // c1 as subject
            var clock = VectorClock.Create().Increment(VclockNodeOf(a1)).Increment(VclockNodeOf(c1));

            var g = new Gossip(ImmutableSortedSet.Create(a1, b1, c1), new GossipOverview(reachability), clock)
                .Seen(a1.UniqueAddress)
                .Seen(c1.UniqueAddress);

            var removed = g.RemoveAll(ImmutableHashSet.Create(c1.UniqueAddress), 4242L);

            removed.Members.Should().BeEquivalentTo(ImmutableSortedSet.Create(a1, b1));
            removed.Overview.Seen.Should().BeEquivalentTo(ImmutableHashSet.Create(a1.UniqueAddress));
            removed.Overview.Reachability.AllObservers.Should().NotContain(c1.UniqueAddress);
            removed.Overview.Reachability.AllUnreachable.Should().NotContain(c1.UniqueAddress);
            removed.Version.Versions.Keys.Should().NotContain(VclockNodeOf(c1));
            removed.Version.Versions.Keys.Should().Contain(VclockNodeOf(a1));
            removed.Tombstones[c1.UniqueAddress].Should().Be(4242L);
        }

        [Fact(DisplayName = "RemoveAll should hand back the same gossip when there is nothing to remove")]
        public void A_gossip_must_return_the_same_instance_when_removing_nothing()
        {
            var g = Gossip.Create(ImmutableSortedSet.Create(a1, b1));
            g.RemoveAll(ImmutableHashSet<UniqueAddress>.Empty, 1L).Should().BeSameAs(g);
        }

        [Fact(DisplayName = "PruneTombstones should drop entries at or before the threshold and keep later ones")]
        public void A_gossip_must_prune_expired_tombstones()
        {
            var tombstones = ImmutableDictionary<UniqueAddress, long>.Empty
                .Add(c1.UniqueAddress, 100L)
                .Add(d1.UniqueAddress, 101L);
            var g = new Gossip(ImmutableSortedSet.Create(a1), new GossipOverview(), VectorClock.Create(), tombstones);

            g.PruneTombstones(99L).Should().BeSameAs(g);

            var atThreshold = g.PruneTombstones(100L);
            atThreshold.Tombstones.Keys.Should().BeEquivalentTo(new[] { d1.UniqueAddress });

            g.PruneTombstones(101L).Tombstones.Should().BeEmpty();
        }

        [Fact(DisplayName = "PruneTombstones should hand back the same gossip when nothing expired")]
        public void A_gossip_must_return_the_same_instance_when_pruning_nothing()
        {
            // the leader publishes only when this reference changes, so a quiet tick must not allocate
            var g = new Gossip(ImmutableSortedSet.Create(a1), new GossipOverview(), VectorClock.Create(),
                Tombstone(c1, 100L));

            g.PruneTombstones(99L).Should().BeSameAs(g);

            var noTombstones = Gossip.Create(ImmutableSortedSet.Create(a1));
            noTombstones.PruneTombstones(long.MaxValue).Should().BeSameAs(noTombstones);
        }

        // The four comparison branches of gossip reception. Only the concurrent one merges; the other
        // three pick a whole gossip as the winner, so each has to union tombstones explicitly or removals
        // decay out of the cluster.

        [Fact(DisplayName = "The Same branch should keep tombstones from both sides")]
        public void A_gossip_must_carry_tombstones_through_the_same_branch()
        {
            var version = VectorClock.Create().Increment(VclockNodeOf(a1));
            var local = new Gossip(ImmutableSortedSet.Create(a1, b1), new GossipOverview(), version, Tombstone(c1));
            var remote = new Gossip(ImmutableSortedSet.Create(a1, b1), new GossipOverview(), version, Tombstone(d1));

            var winner = remote.MergeSeen(local).MergeTombstones(local);

            winner.Tombstones.Keys.Should().BeEquivalentTo(new[] { c1.UniqueAddress, d1.UniqueAddress });
        }

        [Fact(DisplayName = "The Older branch should keep the remote side's tombstones")]
        public void A_gossip_must_carry_tombstones_through_the_older_branch()
        {
            var local = new Gossip(ImmutableSortedSet.Create(a1, b1), new GossipOverview(), VectorClock.Create(),
                Tombstone(c1));
            var remote = new Gossip(ImmutableSortedSet.Create(a1, b1), new GossipOverview(), VectorClock.Create(),
                Tombstone(d1));

            // local wins, but remote's tombstone survives
            local.MergeTombstones(remote).Tombstones.Keys
                .Should().BeEquivalentTo(new[] { c1.UniqueAddress, d1.UniqueAddress });
        }

        [Fact(DisplayName = "The Newer branch should keep the local side's tombstones")]
        public void A_gossip_must_carry_tombstones_through_the_newer_branch()
        {
            var local = new Gossip(ImmutableSortedSet.Create(a1, b1), new GossipOverview(), VectorClock.Create(),
                Tombstone(c1));
            var remote = new Gossip(ImmutableSortedSet.Create(a1, b1), new GossipOverview(), VectorClock.Create(),
                Tombstone(d1));

            // remote wins, but local's tombstone survives
            remote.MergeTombstones(local).Tombstones.Keys
                .Should().BeEquivalentTo(new[] { c1.UniqueAddress, d1.UniqueAddress });
        }

        [Fact(DisplayName = "MergeTombstones should not adopt a tombstone for a node it still holds as a member")]
        public void A_gossip_must_keep_members_and_tombstones_disjoint_when_unioning()
        {
            var winner = Gossip.Create(ImmutableSortedSet.Create(a1, c1));
            var loser = new Gossip(ImmutableSortedSet.Create(a1), new GossipOverview(), VectorClock.Create(),
                Tombstone(c1));

            var merged = winner.MergeTombstones(loser);

            merged.Should().BeSameAs(winner);
            merged.Members.Should().Contain(c1);
        }

        [Fact(DisplayName = "MergeTombstones should hand back the same gossip when it adds nothing")]
        public void A_gossip_must_return_the_same_instance_when_unioning_nothing()
        {
            var g = new Gossip(ImmutableSortedSet.Create(a1), new GossipOverview(), VectorClock.Create(),
                Tombstone(c1, 500L));
            var older = new Gossip(ImmutableSortedSet.Create(a1), new GossipOverview(), VectorClock.Create(),
                Tombstone(c1, 100L));

            g.MergeTombstones(older).Should().BeSameAs(g);
            g.MergeTombstones(Gossip.Create(ImmutableSortedSet.Create(a1))).Should().BeSameAs(g);
        }

        [Fact]
        public void A_gossip_must_have_leader_as_first_member_based_on_ordering_except_exiting_status()
        {
            State(new Gossip(ImmutableSortedSet.Create(c2, e2))).Leader.Should().Be(c2.UniqueAddress);
            State(new Gossip(ImmutableSortedSet.Create(c3, e2))).Leader.Should().Be(e2.UniqueAddress);
            State(new Gossip(ImmutableSortedSet.Create(c3))).Leader.Should().Be(c3.UniqueAddress);
        }

        [Fact]
        public void A_gossip_must_not_have_Down_member_as_leader()
        {
            State(new Gossip(ImmutableSortedSet.Create(e3))).Leader.Should().BeNull();
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

            var theExiting = new Member[] { a5, a6 };
            var gossip = new Gossip(ImmutableSortedSet.Create(a1, a2, a3, a4, a5, a6, a7, a8, a9));

            var r = ClusterCoreDaemon.GossipTargetsForExitingMembers(gossip, theExiting);
            r.Should().BeEquivalentTo(a1, a2, a5, a6, a9);
        }
    }
}

