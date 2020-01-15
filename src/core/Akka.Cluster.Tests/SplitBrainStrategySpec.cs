//-----------------------------------------------------------------------
// <copyright file="SplitBrainStrategySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class SplitBrainStrategySpec
    {
        private static Member Member(Address address, int upNumber = 1, MemberStatus status = MemberStatus.Up, string role = null) =>
            new Member(new UniqueAddress(address, ThreadLocalRandom.Current.Next()), upNumber, status, role == null ? ImmutableHashSet<string>.Empty : ImmutableHashSet.Create(role));

        private static ImmutableSortedSet<Member> Members(params Member[] members) => ImmutableSortedSet.CreateRange(Akka.Cluster.Member.AgeOrdering, members);

        private static readonly Address a = new Address("akka.tcp", "system", "localhost", 10000);
        private static readonly Address b = new Address("akka.tcp", "system", "localhost", 10001);
        private static readonly Address c = new Address("akka.tcp", "system", "localhost", 10002);
        private static readonly Address d = new Address("akka.tcp", "system", "localhost", 10003);
        private static readonly Address e = new Address("akka.tcp", "system", "localhost", 10004);
        private static readonly Address f = new Address("akka.tcp", "system", "localhost", 10005);

        [Fact]
        public void StaticQuorum_must_down_unreachable_nodes_if_remaining_size_is_equal_quorum_size()
        {
            var unreachable = Members(Member(e));
            var remaining = Members(Member(a), Member(b), Member(c), Member(d));

            var strategy = new StaticQuorum(quorumSize: 4, role: null);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void StaticQuorum_must_down_unreachable_nodes_if_remaining_size_is_greater_quorum_size()
        {
            var unreachable = Members(Member(e));
            var remaining = Members(Member(a), Member(b), Member(c), Member(d));

            var strategy = new StaticQuorum(quorumSize: 3, role: null);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(unreachable);
        }


        [Fact]
        public void StaticQuorum_must_down_remaining_nodes_if_remaining_size_is_less_than_quorum_size()
        {
            var unreachable = Members(Member(e), Member(d));
            var remaining = Members(Member(a), Member(b), Member(c));

            var strategy = new StaticQuorum(quorumSize: 4, role: null);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(remaining);
        }

        [Fact]
        public void StaticQuorum_must_limit_node_counts_to_role_if_provided()
        {
            const string role = "test";
            var unreachable = Members(Member(e), Member(d));
            var remaining = Members(Member(a, role: role), Member(b, role: role), Member(c));

            var strategy = new StaticQuorum(quorumSize: 3, role: role);
            // quorum size is 3, but only 2 remaining nodes have configured role
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(remaining);
        }

        [Fact]
        public void KeepMajority_must_down_unreachable_nodes_if_remaining_nodes_have_majority()
        {
            var unreachable = Members(Member(e), Member(d));
            var remaining = Members(Member(a), Member(b), Member(c));

            var strategy = new KeepMajority();
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void KeepMajority_must_down_remaining_nodes_if_unreachable_nodes_have_majority()
        {
            var unreachable = Members(Member(e), Member(d), Member(c));
            var remaining = Members(Member(a), Member(b));

            var strategy = new KeepMajority();
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(remaining);
        }

        [Fact]
        public void KeepMajority_must_keep_the_part_with_the_lowest_nodes_address_in_case_of_equal_size()
        {
            var unreachable = Members(Member(e), Member(d));
            var remaining = Members(Member(a), Member(b)); // `a` is the lowest address

            var strategy = new KeepMajority();
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void KeepMajority_must_down_unreachable_nodes_if_remaining_nodes_have_majority_role_based()
        {
            const string role = "test";
            var unreachable = Members(Member(e, role: role), Member(d), Member(c));
            var remaining = Members(Member(a, role: role), Member(b, role: role));

            var strategy = new KeepMajority(role);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void KeepMajority_must_down_remaining_nodes_if_unreachable_nodes_have_majority_role_based()
        {
            const string role = "test";
            var unreachable = Members(Member(e, role: role), Member(d, role: role));
            var remaining = Members(Member(a, role: role), Member(b), Member(c));

            var strategy = new KeepMajority(role);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(remaining);
        }

        [Fact]
        public void KeepMajority_must_keep_the_part_with_the_lowest_nodes_address_in_case_of_equal_size_role_based()
        {
            const string role = "test";
            var unreachable = Members(Member(e, role: role), Member(d, role: role));
            var remaining = Members(Member(a, role: role), Member(b, role: role), Member(c)); // `a` is the lowest address

            var strategy = new KeepMajority(role);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void KeepOldest_must_down_remaining_if_oldest_was_unreachable()
        {
            var unreachable = Members(Member(e, upNumber: 1), Member(d, upNumber: 5));
            var remaining = Members(Member(a, upNumber: 2), Member(b, upNumber: 3), Member(c, upNumber: 4));

            var strategy = new KeepOldest(downIfAlone: false);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(remaining);
        }

        [Fact]
        public void KeepOldest_must_down_unreachable_nodes_if_oldest_was_found_in_remaining()
        {
            var unreachable = Members(Member(e, upNumber: 2), Member(d, upNumber: 5));
            var remaining = Members(Member(a, upNumber: 1), Member(b, upNumber: 3), Member(c, upNumber: 4));

            var strategy = new KeepOldest(downIfAlone: false);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void KeepOldest_must_down_remaining_if_oldest_was_unreachable_role_based()
        {
            const string role = "test";
            var unreachable = Members(Member(e, upNumber: 2, role: role), Member(d, upNumber: 5));
            var remaining = Members(Member(a, upNumber: 1), Member(b, upNumber: 3, role: role), Member(c, upNumber: 4, role: role));

            var strategy = new KeepOldest(downIfAlone: false, role: role);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(remaining);
        }

        [Fact]
        public void KeepOldest_must_down_unreachable_nodes_if_oldest_was_found_in_remaining_role_based()
        {
            const string role = "test";
            var unreachable = Members(Member(e, upNumber: 1), Member(d, upNumber: 3, role: role));
            var remaining = Members(Member(a, upNumber: 2, role: role), Member(b, upNumber: 5, role: role), Member(c, upNumber: 4));

            var strategy = new KeepOldest(downIfAlone: false, role: role);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void KeepOldest_when_downIfAlone_must_down_oldest_if_it_was_the_only_unreachable_node()
        {
            var unreachable = Members(Member(e, upNumber: 1));
            var remaining = Members(Member(a, upNumber: 2), Member(b, upNumber: 3), Member(c, upNumber: 4));

            var strategy = new KeepOldest(downIfAlone: true);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void KeepOldest_when_downIfAlone_must_down_oldest_if_it_was_the_only_remaining_node()
        {
            var unreachable = Members(Member(e, upNumber: 2), Member(b, upNumber: 3), Member(c, upNumber: 4));
            var remaining = Members(Member(a, upNumber: 1));

            var strategy = new KeepOldest(downIfAlone: true);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(remaining);
        }

        [Fact]
        public void KeepOldest_when_downIfAlone_must_keep_oldest_up_if_is_reachable_and_only_node_in_cluster()
        {
            var unreachable = Members();
            var remaining = Members(Member(a, upNumber: 1));

            var strategy = new KeepOldest(downIfAlone: true);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void KeepReferee_must_down_remaining_if_referee_node_was_unreachable()
        {
            var referee = a;
            var unreachable = Members(Member(referee), Member(d));
            var remaining = Members(Member(b), Member(c), Member(e));

            var strategy = new KeepReferee(address: referee, downAllIfLessThanNodes: 1);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(remaining);
        }

        [Fact]
        public void KeepReferee_must_down_unreachable_if_referee_node_was_seen_in_remaining()
        {
            var referee = a;
            var unreachable = Members(Member(d), Member(e));
            var remaining = Members(Member(referee), Member(b), Member(c));

            var strategy = new KeepReferee(address: referee, downAllIfLessThanNodes: 1);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void KeepReferee_must_down_all_nodes_if_referee_node_was_in_remaining_but_DownAllIfLessThanNodes_was_not_reached()
        {
            var referee = a;
            var unreachable = Members(Member(d), Member(e), Member(c));
            var remaining = Members(Member(referee), Member(b));

            var strategy = new KeepReferee(address: referee, downAllIfLessThanNodes: 3);
            strategy.Apply(new NetworkPartitionContext(unreachable, remaining)).Should().Equal(unreachable.Union(remaining));
        }
    }
}
