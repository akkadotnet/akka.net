//-----------------------------------------------------------------------
// <copyright file="HeartbeatNodeRingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Xunit;
using FluentAssertions;
using Xunit.Abstractions;

namespace Akka.Cluster.Tests
{
    public class HeartbeatNodeRingSpec : HeartbeatNodeRingBase
    {
        public HeartbeatNodeRingSpec(ITestOutputHelper output) : base(output, false)
        {
        }
    }
    
    public class HeartbeatNodeRingLegacySpec : HeartbeatNodeRingBase
    {
        public HeartbeatNodeRingLegacySpec(ITestOutputHelper output) : base(output, true)
        {
        }
    }
    
    public abstract class HeartbeatNodeRingBase : ClusterSpecBase
    {
        protected HeartbeatNodeRingBase(ITestOutputHelper output, bool useLegacyMessage) : base(output, useLegacyMessage)
        {
            _nodes = ImmutableHashSet.Create(aa, bb, cc, dd, ee, ff);
        }
        
        private UniqueAddress aa = new(new Address("akka.tcp", "sys", "aa", 2552), 1);
        private UniqueAddress bb = new(new Address("akka.tcp", "sys", "bb", 2552), 2);
        private UniqueAddress cc = new(new Address("akka.tcp", "sys", "cc", 2552), 3);
        private UniqueAddress dd = new(new Address("akka.tcp", "sys", "dd", 2552), 4);
        private UniqueAddress ee = new(new Address("akka.tcp", "sys", "ee", 2552), 5);
        private UniqueAddress ff = new(new Address("akka.tcp", "sys", "ff", 2552), 6);

        private readonly ImmutableHashSet<UniqueAddress> _nodes;

        [Fact]
        public void HeartbeatNodeRing_must_pick_specified_number_of_nodes_as_receivers()
        {
            var ring = new HeartbeatNodeRing(cc, _nodes, ImmutableHashSet<UniqueAddress>.Empty, 3);
            ring.MyReceivers.Value.Should().BeEquivalentTo(ring.Receivers(cc));

            foreach (var node in _nodes)
            {
                var receivers = ring.Receivers(node);
                receivers.Count.Should().Be(3);
                receivers.Should().NotContain(node);
            }
        }

        [Fact]
        public void HeartbeatNodeRing_must_pick_specified_number_of_nodes_plus_unreachable_as_receivers()
        {
            var ring = new HeartbeatNodeRing(cc, _nodes, ImmutableHashSet.Create(aa, dd, ee), 3);
            ring.MyReceivers.Value.Should().BeEquivalentTo(ring.Receivers(cc));

            ring.Receivers(aa).Should().BeEquivalentTo(ImmutableHashSet.Create(bb, cc, dd, ff)); // unreachable ee skipped
            ring.Receivers(bb).Should().BeEquivalentTo(ImmutableHashSet.Create(cc, dd, ee, ff)); // unreachable ee skipped
            ring.Receivers(cc).Should().BeEquivalentTo(ImmutableHashSet.Create(dd, ee, ff, bb)); // unreachable ee skipped
            ring.Receivers(dd).Should().BeEquivalentTo(ImmutableHashSet.Create(ee, ff, aa, bb, cc));
            ring.Receivers(ee).Should().BeEquivalentTo(ImmutableHashSet.Create(ff, aa, bb, cc));
            ring.Receivers(ff).Should().BeEquivalentTo(ImmutableHashSet.Create(aa, bb, cc)); // unreachable dd and ee skipped
        }

        [Fact]
        public void HeartbeatNodeRing_must_pick_all_except_own_as_receivers_when_less_than_total_number_of_nodes()
        {
            var expected = ImmutableHashSet.Create(aa, bb, dd, ee, ff);
            new HeartbeatNodeRing(cc, _nodes, ImmutableHashSet<UniqueAddress>.Empty, 5).MyReceivers.Value.Should().BeEquivalentTo(expected);
            new HeartbeatNodeRing(cc, _nodes, ImmutableHashSet<UniqueAddress>.Empty, 6).MyReceivers.Value.Should().BeEquivalentTo(expected);
            new HeartbeatNodeRing(cc, _nodes, ImmutableHashSet<UniqueAddress>.Empty, 7).MyReceivers.Value.Should().BeEquivalentTo(expected);
        }

        [Fact]
        public void HeartbeatNodeRing_must_pick_none_when_alone()
        {
            var ring = new HeartbeatNodeRing(cc, ImmutableHashSet.Create(cc), ImmutableHashSet<UniqueAddress>.Empty, 3);
            ring.MyReceivers.Value.Should().BeEquivalentTo(ImmutableHashSet<UniqueAddress>.Empty);
        }
    }
}
