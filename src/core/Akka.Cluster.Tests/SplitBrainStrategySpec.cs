//-----------------------------------------------------------------------
// <copyright file="SplitBrainStrategySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        private static readonly Address a = new Address("akka.tcp", "system", "localhost", 10000);
        private static readonly Address b = new Address("akka.tcp", "system", "localhost", 10001);
        private static readonly Address c = new Address("akka.tcp", "system", "localhost", 10002);
        private static readonly Address d = new Address("akka.tcp", "system", "localhost", 10003);
        private static readonly Address e = new Address("akka.tcp", "system", "localhost", 10004);
        private static readonly Address f = new Address("akka.tcp", "system", "localhost", 10005);

        [Fact]
        public void StaticQuorum_must_down_unreachable_nodes_if_remaining_size_is_equal_quorum_size()
        {
            var unreachable = ImmutableSortedSet.CreateRange(new[] { Member(e) });
            var remaining = ImmutableSortedSet.CreateRange(new[] { Member(a), Member(b), Member(c), Member(d) });
            
            var strategy = new StaticQuorum(quorumSize: 4, role: null);
            strategy.Apply(new NetworkPartitionContext(a, unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void StaticQuorum_must_down_unreachable_nodes_if_remaining_size_is_greater_quorum_size()
        {
            var unreachable = ImmutableSortedSet.CreateRange(new[] { Member(e) });
            var remaining = ImmutableSortedSet.CreateRange(new[] { Member(a), Member(b), Member(c), Member(d) });
            
            var strategy = new StaticQuorum(quorumSize: 3, role: null);
            strategy.Apply(new NetworkPartitionContext(a, unreachable, remaining)).Should().Equal(unreachable);
        }


        [Fact]
        public void StaticQuorum_must_down_remaining_nodes_if_remaining_size_is_less_than_quorum_size()
        {
            var unreachable = ImmutableSortedSet.CreateRange(new[] { Member(e), Member(d) });
            var remaining = ImmutableSortedSet.CreateRange(new[] { Member(a), Member(b), Member(c) });
            
            var strategy = new StaticQuorum(quorumSize: 4, role: null);
            strategy.Apply(new NetworkPartitionContext(a, unreachable, remaining)).Should().Equal(remaining);
        }

        [Fact]
        public void StaticQuorum_must_limit_node_counts_to_role_if_provided()
        {
            const string role = "test";
            var unreachable = ImmutableSortedSet.CreateRange(new[] { Member(e), Member(d) });
            var remaining = ImmutableSortedSet.CreateRange(new[] { Member(a, role: role), Member(b, role: role), Member(c) });

            var strategy = new StaticQuorum(quorumSize: 3, role: role);
            // quorum size is 3, but only 2 remaining nodes have configured role
            strategy.Apply(new NetworkPartitionContext(a, unreachable, remaining)).Should().Equal(remaining);
        }

        [Fact]
        public void KeepMajority_must_down_unreachable_nodes_if_remaining_nodes_have_majority()
        {
            var unreachable = ImmutableSortedSet.CreateRange(new[] { Member(e), Member(d) });
            var remaining = ImmutableSortedSet.CreateRange(new[] { Member(a), Member(b), Member(c) });

            var strategy = new KeepMajority();
            strategy.Apply(new NetworkPartitionContext(a, unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void KeepMajority_must_down_remaining_nodes_if_unreachable_nodes_have_majority()
        {
            var unreachable = ImmutableSortedSet.CreateRange(new[] { Member(e), Member(d), Member(c) });
            var remaining = ImmutableSortedSet.CreateRange(new[] { Member(a), Member(b) });

            var strategy = new KeepMajority();
            strategy.Apply(new NetworkPartitionContext(a, unreachable, remaining)).Should().Equal(remaining);
        }

        [Fact]
        public void KeepMajority_must_keep_the_part_with_the_lowest_nodes_address_in_case_of_equal_size()
        {
            var unreachable = ImmutableSortedSet.CreateRange(new[] { Member(e), Member(d) });
            var remaining = ImmutableSortedSet.CreateRange(new[] { Member(a), Member(b) }); // `a` is the lowst address

            var strategy = new KeepMajority();
            strategy.Apply(new NetworkPartitionContext(a, unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void KeepMajority_must_down_unreachable_nodes_if_remaining_nodes_have_majority_role_based()
        {
            const string role = "test";
            var unreachable = ImmutableSortedSet.CreateRange(new[] { Member(e, role: role), Member(d), Member(c) });
            var remaining = ImmutableSortedSet.CreateRange(new[] { Member(a, role: role), Member(b, role: role) });

            var strategy = new KeepMajority(role);
            strategy.Apply(new NetworkPartitionContext(a, unreachable, remaining)).Should().Equal(unreachable);
        }

        [Fact]
        public void KeepMajority_must_down_remaining_nodes_if_unreachable_nodes_have_majority_role_based()
        {
            const string role = "test";
            var unreachable = ImmutableSortedSet.CreateRange(new[] { Member(e, role: role), Member(d, role: role) });
            var remaining = ImmutableSortedSet.CreateRange(new[] { Member(a, role: role), Member(b), Member(c) });

            var strategy = new KeepMajority(role);
            strategy.Apply(new NetworkPartitionContext(a, unreachable, remaining)).Should().Equal(remaining);
        }

        [Fact]
        public void KeepMajority_must_keep_the_part_with_the_lowest_nodes_address_in_case_of_equal_size_role_based()
        {
            const string role = "test";
            var unreachable = ImmutableSortedSet.CreateRange(new[] { Member(e, role: role), Member(d, role: role) });
            var remaining = ImmutableSortedSet.CreateRange(new[] { Member(a, role: role), Member(b, role: role), Member(c) }); // `a` is the lowst address

            var strategy = new KeepMajority(role);
            strategy.Apply(new NetworkPartitionContext(a, unreachable, remaining)).Should().Equal(unreachable);
        }
    }
}