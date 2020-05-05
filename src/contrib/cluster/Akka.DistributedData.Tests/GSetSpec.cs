//-----------------------------------------------------------------------
// <copyright file="GSetSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class GSetSpec
    {
        const string user1 = "{\"username\":\"john\",\"password\":\"coltrane\"}";
        const string user2 = "{\"username\":\"sonny\",\"password\":\"rollins\"}";
        const string user3 = "{\"username\":\"charlie\",\"password\":\"parker\"}";
        const string user4 = "{\"username\":\"charles\",\"password\":\"mingus\"}";

        public GSetSpec(ITestOutputHelper output)
        {
        }

        [Fact]
        public void GSet_must_be_able_to_add_user()
        {
            var c1 = new GSet<string>();
            var c2 = c1.Add(user1);
            var c3 = c2.Add(user2);
            var c4 = c3.Add(user3);
            var c5 = c4.Add(user4);

            Assert.True(c5.Contains(user1));
            Assert.True(c5.Contains(user2));
            Assert.True(c5.Contains(user3));
            Assert.True(c5.Contains(user4));
        }

        [Fact]
        public void A_GSet_should_be_able_to_have_its_user_set_correctly_merged_with_another_GSet_with_unique_user_sets()
        {
            var c11 = new GSet<string>();
            var c12 = c11.Add(user1);
            var c13 = c12.Add(user2);

            var c21 = new GSet<string>();
            var c22 = c21.Add(user3);
            var c23 = c22.Add(user4);

            var merged1 = c13.Merge(c23);
            Assert.True(merged1.Contains(user1));
            Assert.True(merged1.Contains(user2));
            Assert.True(merged1.Contains(user3));
            Assert.True(merged1.Contains(user4));

            var merged2 = c23.Merge(c13);
            Assert.True(merged2.Contains(user1));
            Assert.True(merged2.Contains(user2));
            Assert.True(merged2.Contains(user3));
            Assert.True(merged2.Contains(user4));
        }

        [Fact]
        public void GSet_must_work_with_deltas()
        {
            // set 1
            var c11 = GSet<string>.Empty;
            var c12 = c11.Add(user1);
            var c13 = c12.Add(user2);

            c12.Delta.Elements.Should().BeEquivalentTo(user1);
            c13.Delta.Elements.Should().BeEquivalentTo(user1, user2);

            // deltas build state
            c13.MergeDelta(c13.Delta).Should().Equal(c13);

            // own deltas are idempotent
            c13.MergeDelta(c13.Delta).Should().Equal(c13);

            // set 2
            var c21 = GSet<string>.Empty;
            var c22 = c21.Add(user3);
            var c23 = c22.ResetDelta().Add(user4);

            c22.Delta.Elements.Should().BeEquivalentTo(user3);
            c23.Delta.Elements.Should().BeEquivalentTo(user4);

            c23.Elements.Should().BeEquivalentTo(user3, user4);

            var c33 = c13.Merge(c23);

            // merge both ways
            var merged1 = GSet<string>.Empty
                .MergeDelta(c12.Delta)
                .MergeDelta(c13.Delta)
                .MergeDelta(c22.Delta)
                .MergeDelta(c23.Delta);
            merged1.Elements.Should().BeEquivalentTo(user1, user2, user3, user4);

            var merged2 = GSet<string>.Empty
                .MergeDelta(c23.Delta)
                .MergeDelta(c13.Delta)
                .MergeDelta(c22.Delta);
            merged2.Elements.Should().BeEquivalentTo(user1, user2, user3, user4);

            merged1.Should().Equal(c33);
            merged2.Should().Equal(c33);
        }

        [Fact]
        public void GSet_must_be_able_to_have_its_user_set_correctly_merged_with_another_GSet_with_overlapping_user_sets()
        {
            var c11 = new GSet<string>();
            var c12 = c11.Add(user1);
            var c13 = c12.Add(user2);
            var c14 = c13.Add(user3);

            var c21 = new GSet<string>();
            var c22 = c21.Add(user3);
            var c23 = c22.Add(user4);
            var c24 = c23.Add(user2);

            var merged1 = c13.Merge(c23);
            Assert.True(merged1.Contains(user1));
            Assert.True(merged1.Contains(user2));
            Assert.True(merged1.Contains(user3));
            Assert.True(merged1.Contains(user4));

            var merged2 = c23.Merge(c13);
            Assert.True(merged2.Contains(user1));
            Assert.True(merged2.Contains(user2));
            Assert.True(merged2.Contains(user3));
            Assert.True(merged2.Contains(user4));
        }
    }
}
