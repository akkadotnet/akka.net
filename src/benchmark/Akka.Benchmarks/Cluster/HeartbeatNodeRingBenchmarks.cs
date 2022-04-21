using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster;
using BenchmarkDotNet.Attributes;
using FluentAssertions;

namespace Akka.Benchmarks.Cluster
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class HeartbeatNodeRingBenchmarks
    {
        [Params(10, 100, 250)]
        public int NodesSize;


        internal static HeartbeatNodeRing CreateHearbeatNodeRingOfSize(int size)
        {
            var nodes = Enumerable.Range(1, size)
                .Select(x => new UniqueAddress(new Address("akka", "sys", "node-" + x, 2552), x))
                .ToList();
            var selfAddress = nodes[size / 2];
            return new HeartbeatNodeRing(selfAddress, nodes.ToImmutableHashSet(), ImmutableHashSet<UniqueAddress>.Empty, 5);
        }

        private HeartbeatNodeRing _ring;

        [GlobalSetup]
        public void Setup()
        {
            _ring = CreateHearbeatNodeRingOfSize(NodesSize);
        }

        private static void MyReceivers(HeartbeatNodeRing ring)
        {
            var r = new HeartbeatNodeRing(ring.SelfAddress, ring.Nodes, ImmutableHashSet<UniqueAddress>.Empty, ring.MonitoredByNumberOfNodes);
            r.MyReceivers.Value.Count.Should().BeGreaterThan(0);
        }

        [Benchmark]
        [Arguments(1000)]
        public void HeartbeatNodeRing_should_produce_MyReceivers(int iterations)
        {
            for(var i = 0; i < iterations; i++)
                MyReceivers(_ring);
        }
    }
}
