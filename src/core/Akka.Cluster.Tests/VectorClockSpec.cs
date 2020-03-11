//-----------------------------------------------------------------------
// <copyright file="VectorClockSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class VectorClockSpec
    {
        [Fact]
        public void A_VectorClock_must_have_zero_versions_when_created()
        {
            var clock = VectorClock.Create();
            clock.Versions.Should().BeEmpty();
        }

        [Fact]
        public void A_VectorClock_must_not_happen_before_itself()
        {
            var clock1 = VectorClock.Create();
            var clock2 = VectorClock.Create();

            (clock1 != clock2).Should().BeFalse();
        }

        [Fact]
        public void A_VectorClock_must_pass_misc_comparison_test1()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.Create("1"));
            var clock3_1 = clock2_1.Increment(VectorClock.Node.Create("2"));
            var clock4_1 = clock3_1.Increment(VectorClock.Node.Create("1"));

            var clock1_2 = VectorClock.Create();
            var clock2_2 = clock1_2.Increment(VectorClock.Node.Create("1"));
            var clock3_2 = clock2_2.Increment(VectorClock.Node.Create("2"));
            var clock4_2 = clock3_2.Increment(VectorClock.Node.Create("1"));

            (clock4_1 != clock4_2).Should().BeFalse();
        }

        [Fact]
        public void A_VectorClock_must_pass_misc_comparison_test2()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.Create("1"));
            var clock3_1 = clock2_1.Increment(VectorClock.Node.Create("2"));
            var clock4_1 = clock3_1.Increment(VectorClock.Node.Create("1"));

            var clock1_2 = VectorClock.Create();
            var clock2_2 = clock1_2.Increment(VectorClock.Node.Create("1"));
            var clock3_2 = clock2_2.Increment(VectorClock.Node.Create("2"));
            var clock4_2 = clock3_2.Increment(VectorClock.Node.Create("1"));
            var clock5_2 = clock4_2.Increment(VectorClock.Node.Create("3"));

            (clock4_1 < clock5_2).Should().BeTrue();
        }

        [Fact]
        public void A_VectorClock_must_pass_misc_comparison_test3()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.Create("1"));

            var clock1_2 = VectorClock.Create();
            var clock2_2 = clock1_2.Increment(VectorClock.Node.Create("2"));

            Assert.True(clock2_1.IsConcurrentWith(clock2_2));
            (clock2_1 != clock2_2).Should().BeTrue();
        }

        [Fact]
        public void A_VectorClock_must_pass_misc_comparison_test4()
        {
            var clock1_3 = VectorClock.Create();
            var clock2_3 = clock1_3.Increment(VectorClock.Node.Create("1"));
            var clock3_3 = clock2_3.Increment(VectorClock.Node.Create("2"));
            var clock4_3 = clock3_3.Increment(VectorClock.Node.Create("1"));

            var clock1_4 = VectorClock.Create();
            var clock2_4 = clock1_4.Increment(VectorClock.Node.Create("1"));
            var clock3_4 = clock2_4.Increment(VectorClock.Node.Create("1"));
            var clock4_4 = clock3_4.Increment(VectorClock.Node.Create("3"));

            (clock4_3 != clock4_4).Should().BeTrue();
        }

        [Fact]
        public void A_VectorClock_must_pass_misc_comparison_test5()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.Create("2"));
            var clock3_1 = clock2_1.Increment(VectorClock.Node.Create("2"));

            var clock1_2 = VectorClock.Create();
            var clock2_2 = clock1_2.Increment(VectorClock.Node.Create("1"));
            var clock3_2 = clock2_2.Increment(VectorClock.Node.Create("2"));
            var clock4_2 = clock3_2.Increment(VectorClock.Node.Create("2"));
            var clock5_2 = clock4_2.Increment(VectorClock.Node.Create("3"));
            
            (clock3_1 < clock5_2).Should().BeTrue();
            (clock5_2 > clock3_1).Should().BeTrue();
        }

        [Fact]
        public void A_VectorClock_must_pass_misc_comparison_test6()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.Create("1"));
            var clock3_1 = clock2_1.Increment(VectorClock.Node.Create("2"));

            var clock1_2 = VectorClock.Create();
            var clock2_2 = clock1_2.Increment(VectorClock.Node.Create("1"));
            var clock3_2 = clock2_2.Increment(VectorClock.Node.Create("1"));

            (clock3_1 != clock3_2).Should().BeTrue();
            (clock3_2 != clock3_1).Should().BeTrue();
        }

        [Fact]
        public void A_VectorClock_must_pass_misc_comparison_test7()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.Create("1"));
            var clock3_1 = clock2_1.Increment(VectorClock.Node.Create("2"));
            var clock4_1 = clock3_1.Increment(VectorClock.Node.Create("2"));
            var clock5_1 = clock4_1.Increment(VectorClock.Node.Create("3"));

            var clock1_2 = clock4_1;
            var clock2_2 = clock1_2.Increment(VectorClock.Node.Create("2"));
            var clock3_2 = clock2_2.Increment(VectorClock.Node.Create("2"));

            (clock5_1 != clock3_2).Should().BeTrue();
            (clock3_2 != clock5_1).Should().BeTrue();
        }

        [Fact]
        public void A_VectorClock_must_pass_misc_comparison_test8()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.FromHash("1"));
            var clock3_1 = clock2_1.Increment(VectorClock.Node.FromHash("3"));

            var clock1_2 = clock3_1.Increment(VectorClock.Node.FromHash("2"));

            var clock4_1 = clock3_1.Increment(VectorClock.Node.FromHash("3"));

            (clock4_1 != clock1_2).Should().BeTrue();
            (clock1_2 != clock4_1).Should().BeTrue();
        }

        [Fact]
        public void A_VectorClock_must_correctly_merge_two_clocks()
        {
            var node1 = VectorClock.Node.Create("1");
            var node2 = VectorClock.Node.Create("2");
            var node3 = VectorClock.Node.Create("3");

            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(node1);
            var clock3_1 = clock2_1.Increment(node2);
            var clock4_1 = clock3_1.Increment(node2);
            var clock5_1 = clock4_1.Increment(node3);

            var clock1_2 = clock4_1;
            var clock2_2 = clock1_2.Increment(node2);
            var clock3_2 = clock2_2.Increment(node2);

            var merged1 = clock3_2.Merge(clock5_1);
            merged1.Versions.Count.Should().Be(3);
            merged1.Versions.ContainsKey(node1).Should().BeTrue();
            merged1.Versions.ContainsKey(node2).Should().BeTrue();
            merged1.Versions.ContainsKey(node3).Should().BeTrue();

            var merged2 = clock5_1.Merge(clock3_2);
            merged2.Versions.Count.Should().Be(3);
            merged2.Versions.ContainsKey(node1).Should().BeTrue();
            merged2.Versions.ContainsKey(node2).Should().BeTrue();
            merged2.Versions.ContainsKey(node3).Should().BeTrue();

            (clock3_2 < merged1).Should().BeTrue();
            (clock5_1 < merged1).Should().BeTrue();

            (clock3_2 < merged2).Should().BeTrue();
            (clock5_1 < merged2).Should().BeTrue();

            (merged1 == merged2).Should().BeTrue();
        }

        [Fact]
        public void A_VectorClock_must_correctly_merge_two_disjoint_vector_clocks()
        {
            var node1 = VectorClock.Node.Create("1");
            var node2 = VectorClock.Node.Create("2");
            var node3 = VectorClock.Node.Create("3");
            var node4 = VectorClock.Node.Create("4");

            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(node1);
            var clock3_1 = clock2_1.Increment(node2);
            var clock4_1 = clock3_1.Increment(node2);
            var clock5_1 = clock4_1.Increment(node3);

            var clock1_2 = VectorClock.Create();
            var clock2_2 = clock1_2.Increment(node4);
            var clock3_2 = clock2_2.Increment(node4);

            var merged1 = clock3_2.Merge(clock5_1);
            merged1.Versions.Count.Should().Be(4);
            merged1.Versions.ContainsKey(node1).Should().BeTrue();
            merged1.Versions.ContainsKey(node2).Should().BeTrue();
            merged1.Versions.ContainsKey(node3).Should().BeTrue();
            merged1.Versions.ContainsKey(node4).Should().BeTrue();

            var merged2 = clock5_1.Merge(clock3_2);
            merged2.Versions.Count.Should().Be(4);
            merged2.Versions.ContainsKey(node1).Should().BeTrue();
            merged2.Versions.ContainsKey(node2).Should().BeTrue();
            merged2.Versions.ContainsKey(node3).Should().BeTrue();
            merged2.Versions.ContainsKey(node4).Should().BeTrue();

            (clock3_2 < merged1).Should().BeTrue();
            (clock5_1 < merged1).Should().BeTrue();

            (clock3_2 < merged2).Should().BeTrue();
            (clock5_1 < merged2).Should().BeTrue();

            (merged1 == merged2).Should().BeTrue();
        }

        [Fact]
        public void A_VectorClock_must_pass_blank_clock_incrementing()
        {
            var node1 = VectorClock.Node.Create("1");
            var node2 = VectorClock.Node.Create("2");

            var v1 = VectorClock.Create();
            var v2 = VectorClock.Create();

            var vv1 = v1.Increment(node1);
            var vv2 = v2.Increment(node2);

            (vv1 > v1).Should().BeTrue();
            (vv2 > v2).Should().BeTrue();

            (vv1 > v2).Should().BeTrue();
            (vv2 > v1).Should().BeTrue();

            (vv2 > vv1).Should().BeFalse();
            (vv1 > vv2).Should().BeFalse();
        }

        [Fact]
        public void A_VectorClock_must_pass_merging_behavior()
        {
            var node1 = VectorClock.Node.Create("1");
            var node2 = VectorClock.Node.Create("2");
            var node3 = VectorClock.Node.Create("3");

            var a = VectorClock.Create();
            var b = VectorClock.Create();

            var a1 = a.Increment(node1);
            var b1 = b.Increment(node2);

            var a2 = a1.Increment(node1);
            var c = a2.Merge(b1);
            var c1 = c.Increment(node3);

            (c1 > a2).Should().BeTrue();
            (c1 > b1).Should().BeTrue();
        }

        [Fact]
        public void A_VectorClock_must_support_pruning()
        {
            var node1 = VectorClock.Node.Create("1");
            var node2 = VectorClock.Node.Create("2");
            var node3 = VectorClock.Node.Create("3");

            var a = VectorClock.Create();
            var b = VectorClock.Create();

            var a1 = a.Increment(node1);
            var b1 = b.Increment(node2);

            var c = a1.Merge(b1);
            var c1 = c.Prune(node1).Increment(node3);
            c1.Versions.ContainsKey(node1).Should().BeFalse();
            (c1 != c).Should().BeTrue();

            (c.Prune(node1).Merge(c1)).Versions.ContainsKey(node1).Should().BeFalse();

            var c2 = c.Increment(node2);
            (c1 != c2).Should().BeTrue();
        }

        [Fact]
        public void A_VectorClock_must_compare_as_Concurrent_when_Member_removed()
        {
            var node1 = VectorClock.Node.Create("1");
            var node2 = VectorClock.Node.Create("2");

            var a = VectorClock.Create().Increment(node1).Increment(node2);
            var b = a.Prune(node2).Increment(node1); // remove node2, increment node1

            a.CompareTo(b).Should().Be(VectorClock.Ordering.Concurrent);
        }
    }
}

