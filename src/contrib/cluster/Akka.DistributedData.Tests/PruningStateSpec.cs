//-----------------------------------------------------------------------
// <copyright file="PruningStateSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class PruningStateSpec
    {
        private readonly UniqueAddress _node1 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2551), 1);
        private readonly UniqueAddress _node2 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2552), 2);
        private readonly UniqueAddress _node3 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2553), 3);
        private readonly UniqueAddress _node4 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2554), 4);

        public PruningStateSpec(ITestOutputHelper output)
        {
        }

        [Fact]
        public void PruningState_must_merge_state_correctly()
        {
            var p1 = new PruningInitialized(_node1, ImmutableHashSet<Address>.Empty);
            var p2 = new PruningPerformed(DateTime.UtcNow.AddHours(1));
            p1.Merge(p2).Should().Be(p2);
            p2.Merge(p1).Should().Be(p2);

            var p3 = new PruningPerformed(p2.ObsoleteTime.AddMilliseconds(-1));
            p2.Merge(p3).Should().Be(p2); // keep greatest obsoleteTime
            p3.Merge(p2).Should().Be(p2);
        }

        [Fact]
        public void PruningState_must_merge_owner_correctly()
        {
            var p1 = new PruningInitialized(_node1, ImmutableHashSet<Address>.Empty);
            var p2 = new PruningInitialized(_node2, ImmutableHashSet<Address>.Empty);
            var expected = new PruningInitialized(_node1, ImmutableHashSet<Address>.Empty);
            p1.Merge(p2).Should().Be(expected);
            p2.Merge(p1).Should().Be(expected);
        }

        [Fact]
        public void PruningState_must_merge_seen_correctly()
        {
            var p1 = new PruningInitialized(_node1, ImmutableHashSet.Create(_node2.Address));
            var p2 = new PruningInitialized(_node1, ImmutableHashSet.Create(_node4.Address));
            var expected = new PruningInitialized(_node1, ImmutableHashSet.Create(_node2.Address, _node4.Address));
            p1.Merge(p2).Should().Be(expected);
            p2.Merge(p1).Should().Be(expected);
        }
    }
}
