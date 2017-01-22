//-----------------------------------------------------------------------
// <copyright file="GSetSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
        public void A_GSet_should_be_able_to_add_user()
        {
            var c1 = new GSet<string>();
            var c2 = c1.Add(user1);
            var c3 = c2.Add(user2);
            var c4 = c3.Add(user3);
            var c5 = c4.Add(user4);

            Assert.Equal(true, c5.Contains(user1));
            Assert.Equal(true, c5.Contains(user2));
            Assert.Equal(true, c5.Contains(user3));
            Assert.Equal(true, c5.Contains(user4));
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
        public void A_GSet_should_be_able_to_have_its_user_set_correctly_merged_with_another_GSet_with_overlapping_user_sets()
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
