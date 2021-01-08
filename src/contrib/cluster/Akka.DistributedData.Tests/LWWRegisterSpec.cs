//-----------------------------------------------------------------------
// <copyright file="LWWRegisterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class LWWRegisterSpec
    {
        private readonly UniqueAddress _node1;
        private readonly UniqueAddress _node2;

        public LWWRegisterSpec(ITestOutputHelper output)
        {
            _node1 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2551), 1);
            _node2 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2552), 2);
        }

        [Fact]
        public void LWWRegister_must_use_latests_of_successive_assignments()
        {
            var register = Enumerable.Range(1, 100).Aggregate(new LWWRegister<int>(_node1, 0), (r, n) =>
            {
                Assert.Equal(n - 1, r.Value);
                return r.WithValue(_node1, n);
            });

            Assert.Equal(100, register.Value);
        }

        [Fact]
        public void LWWRegister_must_merge_by_picking_max_timestamp()
        {
            var i = From(100).GetEnumerator();
            Clock<string> clock = (timestamp, value) =>
            {
                i.MoveNext();
                return i.Current;
            };

            var r1 = new LWWRegister<string>(_node1, "A", clock);
            Assert.Equal(100, r1.Timestamp);

            var r2 = new LWWRegister<string>(_node2, "B", clock);
            Assert.Equal(101, r2.Timestamp);

            var m1 = r1.Merge(r2);
            Assert.Equal("B", m1.Value);
            Assert.Equal(101, m1.Timestamp);

            var m2 = r2.Merge(r1);
            Assert.Equal("B", m2.Value);
            Assert.Equal(101, m2.Timestamp);
        }

        [Fact]
        public void LWWRegister_must_merge_by_picking_least_address_when_same_timestamp()
        {
            Clock<string> clock = (timestamp, value) => 100;

            var r1 = new LWWRegister<string>(_node1, "A", clock);
            var r2 = new LWWRegister<string>(_node2, "B", clock);

            var m1 = r1.Merge(r2);
            Assert.Equal("A", m1.Value);

            var m2 = r2.Merge(r1);
            Assert.Equal("A", m2.Value);
        }

        [Fact]
        public void LWWRegister_must_use_monotonically_increasing_default_clock()
        {
            Enumerable.Range(1, 100).Aggregate(new LWWRegister<int>(_node1, 0), (r, n) =>
            {
                Assert.Equal(n - 1, r.Value);
                var r2 = r.WithValue(_node1, n);
                Assert.True(r2.Timestamp > r.Timestamp);
                return r2;
            });
        }

        [Fact]
        public void LWWRegister_can_be_used_as_first_write_wins_register()
        {
            var clock = LWWRegister<int>.ReverseClock;
            Enumerable.Range(1, 100).Aggregate(new LWWRegister<int>(_node1, 0, clock), (r, n) =>
            {
                Assert.Equal(0, r.Value);
                var r2 = r.Merge(r.WithValue(_node1, n, clock));
                Assert.Equal(r, r2);
                return r2;
            });
        }

        private IEnumerable<int> From(int n)
        {
            do
            {
                yield return n;
                n++;
            } while (true);
        }
    }
}
