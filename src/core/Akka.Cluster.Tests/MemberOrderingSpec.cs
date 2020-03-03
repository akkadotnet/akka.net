//-----------------------------------------------------------------------
// <copyright file="MemberOrderingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Util.Internal.Collections;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class MemberOrderingSpec
    {
        [Fact]
        public void MemberOrdering_must_order_members_by_host_and_port()
        {
            var members = new SortedSet<Member>       
            {
                TestMember.Create(Address.Parse("akka://sys@darkstar:1112"), MemberStatus.Up),
                TestMember.Create(Address.Parse("akka://sys@darkstar:1113"), MemberStatus.Joining),
                TestMember.Create(Address.Parse("akka://sys@darkstar:1111"), MemberStatus.Up),
            };;

            var seq = members.ToList();
            seq.Count.Should().Be(3);
            seq[0].Should().Be(TestMember.Create(Address.Parse("akka://sys@darkstar:1111"), MemberStatus.Up));
            seq[1].Should().Be(TestMember.Create(Address.Parse("akka://sys@darkstar:1112"), MemberStatus.Up));
            seq[2].Should().Be(TestMember.Create(Address.Parse("akka://sys@darkstar:1113"), MemberStatus.Joining));
        }

        [Fact]
        public void MemberOrdering_must_be_sorted_by_address_correctly()
        {
            // sorting should be done on host and port, only
            var m1 = TestMember.Create(new Address("akka.tcp", "sys1", "host1", 9000), MemberStatus.Up);
            var m2 = TestMember.Create(new Address("akka.tcp", "sys1", "host1", 10000), MemberStatus.Up);
            var m3 = TestMember.Create(new Address("cluster", "sys2", "host2", 8000), MemberStatus.Up);
            var m4 = TestMember.Create(new Address("cluster", "sys2", "host2", 9000), MemberStatus.Up);
            var m5 = TestMember.Create(new Address("cluster", "sys1", "host2", 10000), MemberStatus.Up);

            var expected = new List<Member> { m1, m2, m3, m4, m5 };
            var shuffled = expected.Shuffle().ToImmutableList();
            new SortedSet<Member>(shuffled).Should().BeEquivalentTo(expected);
            shuffled.Sort().Should().BeEquivalentTo(expected);
        }

        [Fact]
        public void MemberOrdering_must_have_stable_equals_and_hash_code()
        {
            var address = new Address("akka.tcp", "sys1", "host1", 9000);
            var m1 = TestMember.Create(address, MemberStatus.Joining);
            var m11 = Member.Create(new UniqueAddress(address, -3), ImmutableHashSet<string>.Empty);
            var m2 = m1.Copy(status: MemberStatus.Up);
            var m22 = m11.Copy(status: MemberStatus.Up);
            var m3 = TestMember.Create(address.WithPort(10000), MemberStatus.Up);

            m1.Should().Be(m2);
            m1.GetHashCode().Should().Be(m2.GetHashCode());

            m3.Should().NotBe(m2);
            m3.Should().NotBe(m1);

            m11.Should().Be(m22);
            m11.GetHashCode().Should().Be(m22.GetHashCode());

            // different uid
            m1.Should().NotBe(m11);
            m2.Should().NotBe(m22);
        }

        [Fact]
        public void MemberOrdering_must_consistent_ordering_and_equals()
        {
            var address1 = new Address("akka.tcp", "sys1", "host1", 9001);
            var address2 = address1.WithPort(9002);

            var x = TestMember.Create(address1, MemberStatus.Exiting);
            var y = TestMember.Create(address1, MemberStatus.Removed);
            var z = TestMember.Create(address2, MemberStatus.Up);
            Member.Ordering.Compare(x, y).Should().Be(0);
            Member.Ordering.Compare(x, z).Should().Be(Member.Ordering.Compare(y, z));

            //different uid
            var a = TestMember.Create(address1, MemberStatus.Joining);
            var b = Member.Create(new UniqueAddress(address1, -3), ImmutableHashSet<string>.Empty);
            Member.Ordering.Compare(a, b).Should().Be(1);
            Member.Ordering.Compare(b, a).Should().Be(-1);
        }

        [Fact]
        public void MemberOrdering_must_work_pick_member_with_legal_transition()
        {
            var address1 = new Address("akka.tcp", "sys1", "host1", 9001);
            var address2 = address1.WithPort(9002);
            var address3 = address1.WithPort(9003);
            var address4 = address1.WithPort(9004);

            var s1 = ImmutableSortedSet
                .Create(TestMember.Create(address1, MemberStatus.Joining))
                .Add(TestMember.Create(address2, MemberStatus.Up, ImmutableHashSet<string>.Empty, upNumber: 1));

            var s2 = ImmutableSortedSet.Create(TestMember.Create(address1, MemberStatus.Up, ImmutableHashSet<string>.Empty, upNumber:2));

            var s3 = ImmutableSortedSet
                .Create(TestMember.Create(address1, MemberStatus.Up))
                .Add(TestMember.Create(address2, MemberStatus.Up));

            var u1 = Member.PickNextTransition(s2, s1);
            u1.Should().BeEquivalentTo(s3);
            u1.Single(x => x.Address.Equals(address1)).Status.Should().Be(MemberStatus.Up);

            var s4 = ImmutableSortedSet
                .Create(TestMember.Create(address1, MemberStatus.Up))
                .Add(TestMember.Create(address2, MemberStatus.Up))
                .Add(TestMember.Create(address3, MemberStatus.Joining));

            var s5 = ImmutableSortedSet
                .Create(TestMember.Create(address1, MemberStatus.Up))
                .Add(TestMember.Create(address2, MemberStatus.Up))
                .Add(TestMember.Create(address3, MemberStatus.Up));

            var u2 = Member.PickNextTransition(s4, s1);
            u2.Should().BeEquivalentTo(s5);
            u2.Single(x => x.Address.Equals(address1)).Status.Should().Be(MemberStatus.Up);

            // WeaklyUp assertions
            var s6 = ImmutableSortedSet
                .Create(TestMember.Create(address1, MemberStatus.Up))
                .Add(TestMember.Create(address2, MemberStatus.Up))
                .Add(TestMember.Create(address3, MemberStatus.Up))
                .Add(TestMember.Create(address4, MemberStatus.Joining));

            var s7 = ImmutableSortedSet
                .Create(TestMember.Create(address1, MemberStatus.Up))
                .Add(TestMember.Create(address2, MemberStatus.Up))
                .Add(TestMember.Create(address3, MemberStatus.Up))
                .Add(TestMember.Create(address4, MemberStatus.WeaklyUp));

            var u3 = Member.PickNextTransition(s6, s7);
            u3.Should().BeEquivalentTo(s7);
            u3.Single(x => x.Address.Equals(address4)).Status.Should().Be(MemberStatus.WeaklyUp);

            var s8 = ImmutableSortedSet
                .Create(TestMember.Create(address1, MemberStatus.Up))
                .Add(TestMember.Create(address2, MemberStatus.Up))
                .Add(TestMember.Create(address3, MemberStatus.Up))
                .Add(TestMember.Create(address4, MemberStatus.Up));

            var u4 = Member.PickNextTransition(s8, s7);
            u4.Should().BeEquivalentTo(s8);
            u4.Single(x => x.Address.Equals(address4)).Status.Should().Be(MemberStatus.Up);
        }

        [Fact]
        public void MemberOrdering_must_work_with_sorted_set()
        {
            var address1 = new Address("akka.tcp", "sys1", "host1", 9001);
            var address2 = address1.WithPort(9002);
            var address3 = address1.WithPort(9003);

            ImmutableSortedSet
                .Create(TestMember.Create(address1, MemberStatus.Joining))
                .Remove(TestMember.Create(address1, MemberStatus.Up))
                .Should().BeEmpty();

            ImmutableSortedSet
                .Create(TestMember.Create(address1, MemberStatus.Exiting))
                .Remove(TestMember.Create(address1, MemberStatus.Removed))
                .Should().BeEmpty();

            ImmutableSortedSet
                .Create(TestMember.Create(address1, MemberStatus.Up))
                .Remove(TestMember.Create(address1, MemberStatus.Exiting))
                .Should().BeEmpty();

            ImmutableSortedSet
                .Create(
                    TestMember.Create(address2, MemberStatus.Up),
                    TestMember.Create(address3, MemberStatus.Joining),
                    TestMember.Create(address1, MemberStatus.Exiting))
                .Remove(
                    TestMember.Create(address1, MemberStatus.Removed))
                .Should().BeEquivalentTo(
                    TestMember.Create(address2, MemberStatus.Up),
                    TestMember.Create(address3, MemberStatus.Joining));
        }

        [Fact]
        public void AddressOrdering_must_order_addresses_by_port()
        {
            var addresses = new SortedSet<Address>(Member.AddressOrdering)
            {
                Address.Parse("akka://sys@darkstar:1112"),
                Address.Parse("akka://sys@darkstar:1113"),
                Address.Parse("akka://sys@darkstar:1110"),
                Address.Parse("akka://sys@darkstar:1111")
            };

            var seq = addresses.ToList();
            seq.Count.Should().Be(4);
            seq[0].Should().Be(Address.Parse("akka://sys@darkstar:1110"));
            seq[1].Should().Be(Address.Parse("akka://sys@darkstar:1111"));
            seq[2].Should().Be(Address.Parse("akka://sys@darkstar:1112"));
            seq[3].Should().Be(Address.Parse("akka://sys@darkstar:1113"));
        }

        [Fact]
        public void AddressOrdering_must_order_addresses_by_host_name()
        {
            var addresses = new SortedSet<Address>(Member.AddressOrdering)
            {
                Address.Parse("akka://sys@darkstar2:1110"),
                Address.Parse("akka://sys@darkstar1:1110"),
                Address.Parse("akka://sys@darkstar3:1110"),
                Address.Parse("akka://sys@darkstar0:1110")
            };

            var seq = addresses.ToList();
            seq.Count.Should().Be(4);
            seq[0].Should().Be(Address.Parse("akka://sys@darkstar0:1110"));
            seq[1].Should().Be(Address.Parse("akka://sys@darkstar1:1110"));
            seq[2].Should().Be(Address.Parse("akka://sys@darkstar2:1110"));
            seq[3].Should().Be(Address.Parse("akka://sys@darkstar3:1110"));
        }

        [Fact]
        public void AddressOrdering_must_order_addresses_by_host_name_and_port()
        {
            var addresses = new SortedSet<Address>(Member.AddressOrdering)
            {
                Address.Parse("akka://sys@darkstar2:1110"),
                Address.Parse("akka://sys@darkstar0:1111"),
                Address.Parse("akka://sys@darkstar2:1111"),
                Address.Parse("akka://sys@darkstar0:1110")
            };

            var seq = addresses.ToList();
            seq.Count.Should().Be(4);
            seq[0].Should().Be(Address.Parse("akka://sys@darkstar0:1110"));
            seq[1].Should().Be(Address.Parse("akka://sys@darkstar0:1111"));
            seq[2].Should().Be(Address.Parse("akka://sys@darkstar2:1110"));
            seq[3].Should().Be(Address.Parse("akka://sys@darkstar2:1111"));
        }

        [Fact]
        public void LeaderOrdering_must_order_members_with_status_joining_exiting_down_last()
        {
            var address = new Address("akka.tcp", "sys1", "host1", 5000);
            var m1 = TestMember.Create(address, MemberStatus.Joining);
            var m2 = TestMember.Create(address.WithPort(7000), MemberStatus.Joining);
            var m3 = TestMember.Create(address.WithPort(3000), MemberStatus.Exiting);
            var m4 = TestMember.Create(address.WithPort(6000), MemberStatus.Exiting);
            var m5 = TestMember.Create(address.WithPort(2000), MemberStatus.Down);
            var m6 = TestMember.Create(address.WithPort(4000), MemberStatus.Down);
            var m7 = TestMember.Create(address.WithPort(8000), MemberStatus.Up);
            var m8 = TestMember.Create(address.WithPort(9000), MemberStatus.Up);

            var expected = new List<Member> {m7, m8, m1, m2, m3, m4, m5, m6};
            var shuffled = expected.Shuffle().ToImmutableList();
            shuffled.Sort(Member.LeaderStatusOrdering).Should().BeEquivalentTo(expected);
        }
    }
}

