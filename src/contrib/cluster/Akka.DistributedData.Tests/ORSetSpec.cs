//-----------------------------------------------------------------------
// <copyright file="ORSetSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class ORSetSpec
    {
        private readonly string _user1 = "{\"username\":\"john\",\"password\":\"coltrane\"}";
        private readonly string _user2 = "{\"username\":\"sonny\",\"password\":\"rollins\"}";
        private readonly string _user3 = "{\"username\":\"charlie\",\"password\":\"parker\"}";
        private readonly string _user4 = "{\"username\":\"charles\",\"password\":\"mingus\"}";

        private readonly UniqueAddress _node1;
        private readonly UniqueAddress _node2;

        private readonly UniqueAddress _nodeA;
        private readonly UniqueAddress _nodeB;
        private readonly UniqueAddress _nodeC;
        private readonly UniqueAddress _nodeD;
        private readonly UniqueAddress _nodeE;
        private readonly UniqueAddress _nodeF;
        private readonly UniqueAddress _nodeG;
        private readonly UniqueAddress _nodeH;

        public ORSetSpec(ITestOutputHelper output)
        {
            _node1 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2551), 1);
            _node2 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2552), 2);

            _nodeA = new UniqueAddress(new Address("akka.tcp", "Sys", "a", 2552), 1);
            _nodeB = new UniqueAddress(new Address("akka.tcp", "Sys", "b", 2552), 2);
            _nodeC = new UniqueAddress(new Address("akka.tcp", "Sys", "c", 2552), 3);
            _nodeD = new UniqueAddress(new Address("akka.tcp", "Sys", "d", 2552), 4);
            _nodeE = new UniqueAddress(new Address("akka.tcp", "Sys", "e", 2552), 5);
            _nodeF = new UniqueAddress(new Address("akka.tcp", "Sys", "f", 2552), 6);
            _nodeG = new UniqueAddress(new Address("akka.tcp", "Sys", "g", 2552), 7);
            _nodeH = new UniqueAddress(new Address("akka.tcp", "Sys", "h", 2552), 8);
        }

        [Fact]
        public void A_ORSet_should_be_able_to_add_element()
        {
            var c1 = ORSet<string>.Empty;
            var c2 = c1.Add(_node1, _user1);
            var c3 = c2.Add(_node1, _user2);
            var c4 = c3.Add(_node1, _user4);
            var c5 = c4.Add(_node1, _user3);

            Assert.Contains(_user1, c5.Elements);
            Assert.Contains(_user2, c5.Elements);
            Assert.Contains(_user3, c5.Elements);
            Assert.Contains(_user4, c5.Elements);
        }

        [Fact]
        public void A_ORSet_should_be_able_to_remove_element()
        {
            var c1 = ORSet<string>.Empty;
            var c2 = c1.Add(_node1, _user1);
            var c3 = c2.Add(_node1, _user2);
            var c4 = c3.Remove(_node1, _user2);
            var c5 = c4.Remove(_node1, _user1);

            Assert.DoesNotContain(_user1, c5.Elements);
            Assert.DoesNotContain(_user2, c5.Elements);

            var c6 = c3.Merge(c5);
            Assert.DoesNotContain(_user1, c6.Elements);
            Assert.DoesNotContain(_user2, c6.Elements);

            var c7 = c5.Merge(c3);
            Assert.DoesNotContain(_user1, c7.Elements);
            Assert.DoesNotContain(_user2, c7.Elements);
        }

        [Fact]
        public void A_ORSet_should_be_able_to_add_removed_element()
        {
            var c1 = ORSet<string>.Empty;
            var c2 = c1.Remove(_node1, _user1);
            var c3 = c2.Add(_node1, _user1);

            Assert.Contains(_user1, c3.Elements);
            var c4 = c3.Remove(_node1, _user1);
            Assert.DoesNotContain(_user1, c4.Elements);
            var c5 = c4.Add(_node1, _user1);
            Assert.Contains(_user1, c5.Elements);
        }

        [Fact]
        public void A_ORSet_should_be_able_to_add_and_remove_several_times()
        {
            var c1 = ORSet<string>.Empty;
            var c2 = c1.Add(_node1, _user1);
            var c3 = c2.Add(_node1, _user2);
            var c4 = c3.Remove(_node1, _user1);
            Assert.Contains(_user2, c4.Elements);
            Assert.DoesNotContain(_user1, c4.Elements);

            var c5 = c4.Add(_node1, _user1);
            var c6 = c5.Add(_node1, _user2);
            Assert.Contains(_user1, c6.Elements);
            Assert.Contains(_user2, c6.Elements);

            var c7 = c6.Remove(_node1, _user1);
            var c8 = c7.Add(_node1, _user2);
            var c9 = c8.Remove(_node1, _user1);
            Assert.DoesNotContain(_user1, c9.Elements);
            Assert.Contains(_user2, c9.Elements);
        }

        [Fact]
        public void A_ORSet_should_be_able_to_have_its_element_set_correctly_merged_with_another_ORSet_with_unique_element_sets()
        {
            // set 1
            var c1 = ORSet<string>.Empty.Add(_node1, _user1).Add(_node1, _user2);
            Assert.Contains(_user1, c1.Elements);
            Assert.Contains(_user2, c1.Elements);

            // set 2
            var c2 = ORSet<string>.Empty.Add(_node2, _user3).Add(_node2, _user4).Remove(_node2, _user3);
            Assert.Contains(_user4, c2.Elements);
            Assert.DoesNotContain(_user3, c2.Elements);

            //merge both ways
            var m1 = c1.Merge(c2);
            Assert.Contains(_user1, m1.Elements);
            Assert.Contains(_user2, m1.Elements);
            Assert.DoesNotContain(_user3, m1.Elements);
            Assert.Contains(_user4, m1.Elements);

            var m2 = c2.Merge(c1);
            Assert.Contains(_user1, m2.Elements);
            Assert.Contains(_user2, m2.Elements);
            Assert.DoesNotContain(_user3, m2.Elements);
            Assert.Contains(_user4, m2.Elements);
        }

        [Fact]
        public void A_ORSet_should_be_able_to_have_its_element_set_correctly_merged_with_another_ORSet_with_overlaping_data()
        {
            // set 1
            var c1 = ORSet<string>.Empty.Add(_node1, _user1).Add(_node1, _user2).Add(_node1, _user3).Remove(_node1, _user1).Remove(_node1, _user3);
            Assert.DoesNotContain(_user1, c1.Elements);
            Assert.Contains(_user2, c1.Elements);
            Assert.DoesNotContain(_user3, c1.Elements);
            
            // set 2
            var c2 = ORSet<string>.Empty.Add(_node2, _user1).Add(_node2, _user2).Add(_node2, _user3).Add(_node2, _user4).Remove(_node2, _user3);
            Assert.Contains(_user1, c2.Elements);
            Assert.Contains(_user2, c2.Elements);
            Assert.DoesNotContain(_user3, c2.Elements);
            Assert.Contains(_user4, c2.Elements);

            //merge both ways
            var m1 = c1.Merge(c2);
            Assert.Contains(_user1, m1.Elements);
            Assert.Contains(_user2, m1.Elements);
            Assert.DoesNotContain(_user3, m1.Elements);
            Assert.Contains(_user4, m1.Elements);

            var m2 = c2.Merge(c1);
            Assert.Contains(_user1, m2.Elements);
            Assert.Contains(_user2, m2.Elements);
            Assert.DoesNotContain(_user3, m2.Elements);
            Assert.Contains(_user4, m2.Elements);
        }

        [Fact]
        public void A_ORSet_should_be_able_to_have_its_element_set_correctly_merged_for_concurrent_updates()
        {
            // set 1
            var c1 = ORSet<string>.Empty.Add(_node1, _user1).Add(_node1, _user2).Add(_node1, _user3);
            Assert.Contains(_user1, c1.Elements);
            Assert.Contains(_user2, c1.Elements);
            Assert.Contains(_user3, c1.Elements);

            // set 1
            var c2 = c1.Add(_node2, _user1).Remove(_node2, _user2).Remove(_node2, _user3);
            Assert.Contains(_user1, c2.Elements);
            Assert.DoesNotContain(_user2, c2.Elements);
            Assert.DoesNotContain(_user3, c2.Elements);

            //merge both ways
            var m1 = c1.Merge(c2);
            Assert.Contains(_user1, m1.Elements);
            Assert.DoesNotContain(_user2, m1.Elements);
            Assert.DoesNotContain(_user3, m1.Elements);

            var m2 = c2.Merge(c1);
            Assert.Contains(_user1, m2.Elements);
            Assert.DoesNotContain(_user2, m2.Elements);
            Assert.DoesNotContain(_user3, m2.Elements);

            var c3 = c1.Add(_node1, _user4).Remove(_node1, _user3).Add(_node1, _user2);

            //merge both ways
            var m3 = c2.Merge(c3);
            Assert.Contains(_user1, m3.Elements);
            Assert.Contains(_user2, m3.Elements);
            Assert.DoesNotContain(_user3, m3.Elements);
            Assert.Contains(_user4, m3.Elements);

            var m4 = c3.Merge(c2);
            Assert.Contains(_user1, m4.Elements);
            Assert.Contains(_user2, m4.Elements);
            Assert.DoesNotContain(_user3, m4.Elements);
            Assert.Contains(_user4, m4.Elements);
        }

        [Fact]
        public void A_ORSet_should_be_able_to_have_its_element_set_correctly_merged_after_remove()
        {
            var c1 = ORSet<string>.Empty.Add(_node1, _user1).Add(_node1, _user2);
            var c2 = c1.Remove(_node2, _user2);

            // merge both ways
            var m1 = c1.Merge(c2);
            Assert.Contains(_user1, m1.Elements);
            Assert.DoesNotContain(_user2, m1.Elements);

            var m2 = c2.Merge(c1);
            Assert.Contains(_user1, m2.Elements);
            Assert.DoesNotContain(_user2, m2.Elements);

            var c3 = c1.Add(_node1, _user3);

            // merge both ways
            var m3 = c3.Merge(c2);
            Assert.Contains(_user1, m3.Elements);
            Assert.DoesNotContain(_user2, m3.Elements);
            Assert.Contains(_user1, m3.Elements);

            var m4 = c2.Merge(c3);
            Assert.Contains(_user1, m4.Elements);
            Assert.DoesNotContain(_user2, m4.Elements);
            Assert.Contains(_user1, m4.Elements);
        }
    }
}