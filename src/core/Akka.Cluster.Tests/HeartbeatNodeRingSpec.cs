//-----------------------------------------------------------------------
// <copyright file="HeartbeatNodeRingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class HeartbeatNodeRingSpec : ClusterSpecBase
    {
        public HeartbeatNodeRingSpec()
        {
            _nodes = ImmutableHashSet.Create(aa, bb, cc, dd, ee);
        }
        
        private UniqueAddress aa = new UniqueAddress(new Address("akka.tcp", "sys", "aa", 2552), 1);
        private UniqueAddress bb = new UniqueAddress(new Address("akka.tcp", "sys", "bb", 2552), 2);
        private UniqueAddress cc = new UniqueAddress(new Address("akka.tcp", "sys", "cc", 2552), 3);
        private UniqueAddress dd = new UniqueAddress(new Address("akka.tcp", "sys", "dd", 2552), 4);
        private UniqueAddress ee = new UniqueAddress(new Address("akka.tcp", "sys", "ee", 2552), 5);

        private readonly ImmutableHashSet<UniqueAddress> _nodes;

        [Fact]
        public void HeartbeatNodeRing_must_pick_specified_number_of_nodes_as_receivers()
        {
            var ring = new HeartbeatNodeRing(cc, _nodes, 3);
            ring.MyReceivers.Value.ShouldBe(ring.Receivers(cc));

            foreach (var node in _nodes)
            {
                var receivers = ring.Receivers(node);
                receivers.Count.ShouldBe(3);
                receivers.Contains(node).ShouldBeFalse();
            }
        }

        [Fact]
        public void HeartbeatNodeRing_must_pick_all_except_own_as_receivers_when_less_than_total_number_of_nodes()
        {
            var expected = ImmutableHashSet.Create(aa, bb, dd, ee);
            new HeartbeatNodeRing(cc, _nodes, 4).MyReceivers.Value.ShouldBe(expected);
            new HeartbeatNodeRing(cc, _nodes, 5).MyReceivers.Value.ShouldBe(expected);
            new HeartbeatNodeRing(cc, _nodes, 6).MyReceivers.Value.ShouldBe(expected);
        }

        [Fact]
        public void HeartbeatNodeRing_must_pick_none_when_alone()
        {
            var ring = new HeartbeatNodeRing(cc, new[] {cc}, 3);
            ring.MyReceivers.Value.ShouldBe(ImmutableHashSet.Create<UniqueAddress>());
        }
    }
}

