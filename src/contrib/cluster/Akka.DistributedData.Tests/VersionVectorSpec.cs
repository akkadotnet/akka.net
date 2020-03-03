//-----------------------------------------------------------------------
// <copyright file="VersionVectorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class VersionVectorSpec
    {
        private readonly UniqueAddress _node1 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2551), 1);
        private readonly UniqueAddress _node2 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2552), 2);
        private readonly UniqueAddress _node3 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2553), 3);
        private readonly UniqueAddress _node4 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2554), 4);

        public VersionVectorSpec(ITestOutputHelper output)
        {
        }

        [Fact]
        public void VersionVector_must_have_zero_versions_when_created()
        {
            var versionVector = VersionVector.Empty;
            Assert.Equal(0, versionVector.Count);
        }

        [Fact]
        public void VersionVector_must_increment_correctly()
        {
            var vv1 = VersionVector.Empty;
            var vv2 = vv1.Increment(_node1);
            Assert.True(vv2.VersionAt(_node1) > vv1.VersionAt(_node1));
            var vv3 = vv2.Increment(_node1);
            Assert.True(vv3.VersionAt(_node1) > vv2.VersionAt(_node1));

            var vv4 = vv3.Increment(_node2);
            Assert.Equal(vv4.VersionAt(_node1), vv3.VersionAt(_node1));
            Assert.True(vv4.VersionAt(_node2) > vv3.VersionAt(_node2));
        }

        [Fact]
        public void VersionVector_must_not_happen_before_itself()
        {
            var versionVector1 = VersionVector.Empty;
            var versionVector2 = VersionVector.Empty;
            Assert.False(versionVector1.IsConcurrent(versionVector2));
        }

        [Fact]
        public void VersionVector_must_pass_misc_comparison_test_1()
        {
            var vv1_1 = VersionVector.Empty;
            var vv2_1 = vv1_1.Increment(_node1);
            var vv3_1 = vv2_1.Increment(_node2);
            var vv4_1 = vv3_1.Increment(_node1);
            var vv1_2 = VersionVector.Empty;
            var vv2_2 = vv1_2.Increment(_node1);
            var vv3_2 = vv2_2.Increment(_node2);
            var vv4_2 = vv3_2.Increment(_node1);
            Assert.False(vv4_1.IsConcurrent(vv4_2));
        }
        [Fact]
        public void VersionVector_must_pass_misc_comparison_test_2()
        {
            var vv1_1 = VersionVector.Empty;
            var vv2_1 = vv1_1.Increment(_node1);
            var vv3_1 = vv2_1.Increment(_node2);
            var vv4_1 = vv3_1.Increment(_node1);

            var vv1_2 = VersionVector.Empty;
            var vv2_2 = vv1_2.Increment(_node1);
            var vv3_2 = vv2_2.Increment(_node2);
            var vv4_2 = vv3_2.Increment(_node1);
            var vv5_2 = vv4_2.Increment(_node3);
            Assert.True(vv4_1.IsBefore(vv5_2));
        }

        [Fact]
        public void VersionVector_must_pass_misc_comparison_test_3()
        {
            var vv1_1 = VersionVector.Empty;
            var vv2_1 = vv1_1.Increment(_node1);

            var vv1_2 = VersionVector.Empty;
            var vv2_2 = vv1_2.Increment(_node2);
            Assert.True(vv2_1.IsConcurrent(vv2_2));
        }

        [Fact]
        public void VersionVector_must_pass_misc_comparison_test_4()
        {
            var vv1_3 = VersionVector.Empty;
            var vv2_3 = vv1_3.Increment(_node1);
            var vv3_3 = vv2_3.Increment(_node2);
            var vv4_3 = vv3_3.Increment(_node1);

            var vv1_4 = VersionVector.Empty;
            var vv2_4 = vv1_4.Increment(_node1);
            var vv3_4 = vv2_4.Increment(_node1);
            var vv4_4 = vv3_4.Increment(_node3);
            Assert.True(vv4_3.IsConcurrent(vv4_4));
        }

        [Fact]
        public void VersionVector_must_pass_misc_comparison_test_5()
        {
            var vv1_1 = VersionVector.Empty;
            var vv2_1 = vv1_1.Increment(_node2);
            var vv3_1 = vv2_1.Increment(_node2);

            var vv1_2 = VersionVector.Empty;
            var vv2_2 = vv1_2.Increment(_node1);
            var vv3_2 = vv2_2.Increment(_node2);
            var vv4_2 = vv3_2.Increment(_node2);
            var vv5_2 = vv4_2.Increment(_node3);
            Assert.True(vv3_1.IsBefore(vv5_2));
            Assert.True(vv5_2.IsAfter(vv3_1));
        }
        [Fact]
        public void VersionVector_must_pass_misc_comparison_test_6()
        {
            var vv1_1 = VersionVector.Empty;
            var vv2_1 = vv1_1.Increment(_node1);
            var vv3_1 = vv2_1.Increment(_node2);

            var vv1_2 = VersionVector.Empty;
            var vv2_2 = vv1_2.Increment(_node1);
            var vv3_2 = vv2_2.Increment(_node1);

            Assert.True(vv3_1.IsConcurrent(vv3_2));
            Assert.True(vv3_2.IsConcurrent(vv3_1));
        }
        [Fact]
        public void VersionVector_must_pass_misc_comparison_test_7()
        {
            var vv1_1 = VersionVector.Empty;
            var vv2_1 = vv1_1.Increment(_node1);
            var vv3_1 = vv2_1.Increment(_node2);
            var vv4_1 = vv3_1.Increment(_node2);
            var vv5_1 = vv4_1.Increment(_node3);

            var vv1_2 = VersionVector.Empty;
            var vv2_2 = vv1_2.Increment(_node2);
            var vv3_2 = vv2_2.Increment(_node2);

            Assert.True(vv5_1.IsConcurrent(vv3_2));
            Assert.True(vv3_2.IsConcurrent(vv5_1));
        }
        [Fact]
        public void VersionVector_must_pass_misc_comparison_test_8()
        {
            var vv1_1 = VersionVector.Empty;
            var vv2_1 = vv1_1.Increment(_node1);
            var vv3_1 = vv2_1.Increment(_node3);

            var vv1_2 = vv3_1.Increment(_node2);

            var vv4_1 = vv3_1.Increment(_node3);

            Assert.True(vv4_1.IsConcurrent(vv1_2));
            Assert.True(vv1_2.IsConcurrent(vv4_1));
        }

        [Fact]
        public void VersionVector_must_correctly_merge_two_version_vectors()
        {
            var vv1_1 = VersionVector.Empty;
            var vv2_1 = vv1_1.Increment(_node1);
            var vv3_1 = vv2_1.Increment(_node2);
            var vv4_1 = vv3_1.Increment(_node2);
            var vv5_1 = vv4_1.Increment(_node3);

            var vv1_2 = vv4_1;
            var vv2_2 = vv1_2.Increment(_node2);
            var vv3_2 = vv2_2.Increment(_node2);

            var merged1 = vv3_2.Merge(vv5_1);
            Assert.Equal(3, merged1.Count);
            Assert.True(merged1.Contains(_node1));
            Assert.True(merged1.Contains(_node2));
            Assert.True(merged1.Contains(_node3));

            var merged2 = vv5_1.Merge(vv3_2);
            Assert.Equal(3, merged1.Count);
            Assert.True(merged2.Contains(_node1));
            Assert.True(merged2.Contains(_node2));
            Assert.True(merged2.Contains(_node3));

            Assert.True(vv3_2.IsBefore(merged1));
            Assert.True(vv5_1.IsBefore(merged1));

            Assert.True(vv3_2.IsBefore(merged2));
            Assert.True(vv5_1.IsBefore(merged2));

            Assert.True(merged1.Equals(merged2));
        }

        [Fact]
        public void VersionVector_must_correctly_merge_two_disjoint_version_vectors()
        {
            var vv1_1 = VersionVector.Empty;
            var vv2_1 = vv1_1.Increment(_node1);
            var vv3_1 = vv2_1.Increment(_node2);
            var vv4_1 = vv3_1.Increment(_node2);
            var vv5_1 = vv4_1.Increment(_node3);

            var vv1_2 = VersionVector.Empty;
            var vv2_2 = vv1_2.Increment(_node4);
            var vv3_2 = vv2_2.Increment(_node4);

            var merged1 = vv3_2.Merge(vv5_1);
            Assert.Equal(4, merged1.Count);
            Assert.True(merged1.Contains(_node1));
            Assert.True(merged1.Contains(_node2));
            Assert.True(merged1.Contains(_node3));
            Assert.True(merged1.Contains(_node4));


            var merged2 = vv5_1.Merge(vv3_2);
            Assert.Equal(4, merged2.Count);
            Assert.True(merged2.Contains(_node1));
            Assert.True(merged2.Contains(_node2));
            Assert.True(merged2.Contains(_node3));
            Assert.True(merged2.Contains(_node4));

            Assert.True(vv3_2.IsBefore(merged1));
            Assert.True(vv5_1.IsBefore(merged1));

            Assert.True(vv3_2.IsBefore(merged2));
            Assert.True(vv5_1.IsBefore(merged2));

            Assert.True(merged1.Equals(merged2));
        }

        [Fact]
        public void VersionVector_must_pass_blank_version_vector_incrementing()
        {
            var v1 = VersionVector.Empty;
            var v2 = VersionVector.Empty;

            var vv1 = v1.Increment(_node1);
            var vv2 = v2.Increment(_node2);

            Assert.True(vv1.IsAfter(v1));
            Assert.True(vv2.IsAfter(v2));
            Assert.True(vv1.IsAfter(v2));
            Assert.True(vv2.IsAfter(v1));

            Assert.False(vv2.IsAfter(vv1));
            Assert.False(vv1.IsAfter(vv2));


        }

        [Fact]
        public void VersionVector_must_pass_merging_behavior()
        {
            var a = VersionVector.Empty;
            var b = VersionVector.Empty;

            var a1 = a.Increment(_node1);
            var b1 = b.Increment(_node2);

            var a2 = a1.Increment(_node1);
            var c = a2.Merge(b1);
            var c1 = c.Increment(_node3);
            Assert.True(c1.IsAfter(a2));
            Assert.True(c1.IsAfter(b1));
        }
    }
}
