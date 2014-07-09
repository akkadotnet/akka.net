using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Akka.Cluster.Tests
{
    [TestClass]
    public class MemberOrderingSpec
    {
        [TestMethod]
        public void MemberOrderingMustOrderMembersByHostAndPort()
        {
            var sortedSet = new SortedSet<Member>         
            {
                TestMember.Create(Address.Parse("akka://sys@darkstar:1112"), MemberStatus.Up),
                TestMember.Create(Address.Parse("akka://sys@darkstar:1113"), MemberStatus.Joining),
                TestMember.Create(Address.Parse("akka://sys@darkstar:1111"), MemberStatus.Up),
            };;

            var expected = new List<Member>
            {
                TestMember.Create(Address.Parse("akka://sys@darkstar:1111"), MemberStatus.Up),
                TestMember.Create(Address.Parse("akka://sys@darkstar:1112"), MemberStatus.Up),
                TestMember.Create(Address.Parse("akka://sys@darkstar:1113"), MemberStatus.Joining),
            };

            CollectionAssert.AreEqual(expected, sortedSet.ToList());
        }

        [TestMethod]
        public void MemberOrderingMustBeSortedByAddressCorrectly()
        {
            var m1 = TestMember.Create(new Address("akka.tcp", "sys1", "host1", 9000), MemberStatus.Up);
            var m2 = TestMember.Create(new Address("akka.tcp", "sys1", "host1", 10000), MemberStatus.Up);
            var m3 = TestMember.Create(new Address("cluster", "sys2", "host2", 8000), MemberStatus.Up);
            var m4 = TestMember.Create(new Address("cluster", "sys2", "host2", 9000), MemberStatus.Up);
            var m5 = TestMember.Create(new Address("cluster", "sys1", "host2", 10000), MemberStatus.Up);

            var expected = new List<Member> { m1, m2, m3, m4, m5 };

            var shuffled = expected.Shuffle();
            CollectionAssert.AreEqual(expected, new SortedSet<Member>(shuffled));

            shuffled.Sort();
            CollectionAssert.AreEqual(expected, shuffled);
        }

        [TestMethod]
        public void MemberOrderingMustHaveStableEqualsAndHashCode()
        {
            var address = new Address("akka.tcp", "sys1", "host1", 9000);
            var m1 = TestMember.Create(address, MemberStatus.Joining);
            var m11 = Member.Create(new UniqueAddress(address, -3), new HashSet<string>());
            var m2 = m1.Copy(MemberStatus.Up);
            var m22 = m11.Copy(MemberStatus.Up);
            var m3 = TestMember.Create(address.Copy(port: 10000), MemberStatus.Up);

            Assert.AreEqual(m1, m2);
            Assert.AreEqual(m1.GetHashCode(), m2.GetHashCode());
            Assert.AreNotEqual(m3, m2);
            Assert.AreNotEqual(m3, m1);
            Assert.AreEqual(m11, m22);
            Assert.AreNotEqual(m1, m11);
            Assert.AreNotEqual(m2, m22);
        }

        [TestMethod]
        public void MemberOrderingMustConsitentOrderingAndEquals()
        {
            var address1 = new Address("akka.tcp", "sys1", "host1", 9001);
            var address2 = address1.Copy(port: 9002);

            var x = TestMember.Create(address1, MemberStatus.Exiting);
            var y = TestMember.Create(address1, MemberStatus.Removed);
            var z = TestMember.Create(address2, MemberStatus.Up);
            Assert.AreEqual(0, Member.Ordering.Compare(x,y));
            Assert.AreEqual(Member.Ordering.Compare(y, z), Member.Ordering.Compare(x, z));

            //different uid
            var a = TestMember.Create(address1, MemberStatus.Joining);
            var b = Member.Create(new UniqueAddress(address1, -3), new HashSet<string>());
            Assert.AreEqual(1, Member.Ordering.Compare(a, b));
            Assert.AreEqual(-1, Member.Ordering.Compare(b, a));
        }

        [TestMethod]
        public void MemberOrderingMustWorkWithSortedSet()
        {
            var address1 = new Address("akka.tcp", "sys1", "host1", 9001);
            var address2 = address1.Copy(port: 9002);
            var address3 = address1.Copy(port: 9003);

            var set = new SortedSet<Member>
            {
                TestMember.Create(address1, MemberStatus.Joining)
            };
            set.Remove(TestMember.Create(address1, MemberStatus.Up));
            CollectionAssert.AreEqual(new SortedSet<Member>(), set);

            set = new SortedSet<Member>
            {
                TestMember.Create(address1, MemberStatus.Exiting)
            };
            set.Remove(TestMember.Create(address1, MemberStatus.Removed));
            CollectionAssert.AreEqual(new SortedSet<Member>(), set);

            set = new SortedSet<Member>
            {
                TestMember.Create(address1, MemberStatus.Up)
            };
            set.Remove(TestMember.Create(address1, MemberStatus.Exiting));
            CollectionAssert.AreEqual(new SortedSet<Member>(), set);

            set = new SortedSet<Member>
            {
                TestMember.Create(address2, MemberStatus.Up),
                TestMember.Create(address3, MemberStatus.Joining),
                TestMember.Create(address3, MemberStatus.Exiting)
            };
            set.Remove(TestMember.Create(address1, MemberStatus.Removed));
            CollectionAssert.AreEqual(new SortedSet<Member>
            {
                TestMember.Create(address2, MemberStatus.Up),
                TestMember.Create(address3, MemberStatus.Joining)                
            }, set);
        }

        [TestMethod]
        public void AddressOrderingMustOrderAddressesByPort()
        {
            var addresses = new SortedSet<Address>(Member.AddressOrdering)
            {
                Address.Parse("akka://sys@darkstar:1112"),
                Address.Parse("akka://sys@darkstar:1113"),
                Address.Parse("akka://sys@darkstar:1110"),
                Address.Parse("akka://sys@darkstar:1111")
            };

            var seq = addresses.ToList();
            Assert.AreEqual(4, seq.Count);
            Assert.AreEqual(Address.Parse("akka://sys@darkstar:1110"), seq[0]);
            Assert.AreEqual(Address.Parse("akka://sys@darkstar:1111"), seq[1]);
            Assert.AreEqual(Address.Parse("akka://sys@darkstar:1112"), seq[2]);
            Assert.AreEqual(Address.Parse("akka://sys@darkstar:1113"), seq[3]);
        }

        [TestMethod]
        public void AddressOrderingMustOrderAddressesByHostName()
        {
            var addresses = new SortedSet<Address>(Member.AddressOrdering)
            {
                Address.Parse("akka://sys@darkstar2:1110"),
                Address.Parse("akka://sys@darkstar1:1110"),
                Address.Parse("akka://sys@darkstar3:1110"),
                Address.Parse("akka://sys@darkstar0:1110")
            };

            var seq = addresses.ToList();
            Assert.AreEqual(4, seq.Count);
            Assert.AreEqual(Address.Parse("akka://sys@darkstar0:1110"), seq[0]);
            Assert.AreEqual(Address.Parse("akka://sys@darkstar1:1110"), seq[1]);
            Assert.AreEqual(Address.Parse("akka://sys@darkstar2:1110"), seq[2]);
            Assert.AreEqual(Address.Parse("akka://sys@darkstar3:1110"), seq[3]);
        }

        [TestMethod]
        public void AddressOrderingMustOrderAddressesByHostNameAndPort()
        {
            var addresses = new SortedSet<Address>(Member.AddressOrdering)
            {
                Address.Parse("akka://sys@darkstar2:1110"),
                Address.Parse("akka://sys@darkstar0:1111"),
                Address.Parse("akka://sys@darkstar2:1111"),
                Address.Parse("akka://sys@darkstar0:1110")
            };

            var seq = addresses.ToList();
            Assert.AreEqual(4, seq.Count);
            Assert.AreEqual(Address.Parse("akka://sys@darkstar0:1110"), seq[0]);
            Assert.AreEqual(Address.Parse("akka://sys@darkstar0:1111"), seq[1]);
            Assert.AreEqual(Address.Parse("akka://sys@darkstar2:1110"), seq[2]);
            Assert.AreEqual(Address.Parse("akka://sys@darkstar2:1111"), seq[3]);
        }


        [TestMethod]
        public void LeaderOrderingMustOrderMembersWithStatusJoiningExitingDownLast()
        {
            var address = new Address("akka.tcp", "sys1", "host1", 5000);
            var m1 = TestMember.Create(address, MemberStatus.Joining);
            var m2 = TestMember.Create(address.Copy(port:7000), MemberStatus.Joining);
            var m3 = TestMember.Create(address.Copy(port: 3000), MemberStatus.Exiting);
            var m4 = TestMember.Create(address.Copy(port: 6000), MemberStatus.Exiting);
            var m5 = TestMember.Create(address.Copy(port: 2000), MemberStatus.Down);
            var m6 = TestMember.Create(address.Copy(port: 4000), MemberStatus.Down);
            var m7 = TestMember.Create(address.Copy(port: 8000), MemberStatus.Up);
            var m8 = TestMember.Create(address.Copy(port: 9000), MemberStatus.Up);

            var expected = new List<Member> {m7, m8, m1, m2, m3, m4, m5, m6};
            var shuffled = expected.Shuffle();
            shuffled.Sort(Member.LeaderStatusOrdering);
            CollectionAssert.AreEqual(expected, shuffled);
        }
    }

    static class ListExtensions
    {
        public static List<T> Shuffle<T>(this List<T> @this)
        {
            var list = new List<T>(@this);
            var rng = new Random();
            var n = list.Count;
            while (n > 1)
            {
                n--;
                var k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
            return list;
        }
    }
}
