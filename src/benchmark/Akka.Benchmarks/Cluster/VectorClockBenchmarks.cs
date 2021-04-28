using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Benchmarks.Configurations;
using Akka.Cluster;
using BenchmarkDotNet.Attributes;
using FluentAssertions;
using static Akka.Cluster.VectorClock;

namespace Akka.Benchmarks.Cluster
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class VectorClockBenchmarks
    {
        [Params(100)]
        public int ClockSize;

        [Params(1000)]
        public int Iterations;

        internal (VectorClock clock, ImmutableSortedSet<VectorClock.Node> nodes) CreateVectorClockOfSize(int size)
        {
            return Enumerable.Range(1, size)
                .Aggregate((VectorClock.Create(), ImmutableSortedSet<Node>.Empty),
                    (tuple, i) =>
                    {
                        var (vc, nodes) = tuple;
                        var node = Node.Create(i.ToString());
                        return (vc.Increment(node), nodes.Add(node));
                    });
        }

        internal VectorClock CopyVectorClock(VectorClock vc)
        {
            var versions = vc.Versions.Aggregate(ImmutableSortedDictionary<Node, long>.Empty,
                (newVersions, pair) =>
                {
                    return newVersions.SetItem(Node.FromHash(pair.Key.ToString()), pair.Value);
                });

            return VectorClock.Create(versions);
        }

        private Node _firstNode;
        private Node _lastNode;
        private Node _middleNode;
        private ImmutableSortedSet<Node> _nodes;
        private VectorClock _vcBefore;
        private VectorClock _vcBaseLast;
        private VectorClock _vcAfterLast;
        private VectorClock _vcConcurrentLast;
        private VectorClock _vcBaseMiddle;
        private VectorClock _vcAfterMiddle;
        private VectorClock _vcConcurrentMiddle;

        [GlobalSetup]
        public void Setup()
        {
            var (vcBefore, nodes) = CreateVectorClockOfSize(ClockSize);
            _vcBefore = vcBefore;
            _nodes = nodes;

            _firstNode = nodes.First();
            _lastNode = nodes.Last();
            _middleNode = nodes[ClockSize / 2];

            _vcBaseLast = vcBefore.Increment(_lastNode);
            _vcAfterLast = _vcBaseLast.Increment(_firstNode);
            _vcConcurrentLast = _vcBaseLast.Increment(_lastNode);
            _vcBaseMiddle = _vcBefore.Increment(_middleNode);
            _vcAfterMiddle = _vcBaseMiddle.Increment(_firstNode);
            _vcConcurrentMiddle = _vcBaseMiddle.Increment(_middleNode);
        }

        private void CheckThunkFor(VectorClock vc1, VectorClock vc2, Action<VectorClock, VectorClock> thunk, int times)
        {
            var vcc1 = CopyVectorClock(vc1);
            var vcc2 = CopyVectorClock(vc2);
            for (var i = 0; i < times; i++)
            {
                thunk(vcc1, vcc2);
            }
        }

        private void CompareTo(VectorClock vc1, VectorClock vc2, Ordering ordering)
        {
            vc1.CompareTo(vc2).Should().Be(ordering);
        }

        private void NotEqual(VectorClock vc1, VectorClock vc2)
        {
            (vc1 == vc2).Should().BeFalse();
        }

        [Benchmark]
        public void VectorClock_comparisons_should_compare_same()
        {
            CheckThunkFor(_vcBaseLast, _vcBaseLast, (clock, vectorClock) => CompareTo(clock, vectorClock, Ordering.Same), Iterations);
        }

        [Benchmark]
        public void VectorClock_comparisons_should_compare_Before_last()
        {
            CheckThunkFor(_vcBefore, _vcBaseLast, (clock, vectorClock) => CompareTo(clock, vectorClock, Ordering.Before), Iterations);
        }

        [Benchmark]
        public void VectorClock_comparisons_should_compare_After_last()
        {
            CheckThunkFor(_vcAfterLast, _vcBaseLast, (clock, vectorClock) => CompareTo(clock, vectorClock, Ordering.After), Iterations);
        }

        [Benchmark]
        public void VectorClock_comparisons_should_compare_Concurrent_last()
        {
            CheckThunkFor(_vcAfterLast, _vcConcurrentLast, (clock, vectorClock) => CompareTo(clock, vectorClock, Ordering.Concurrent), Iterations);
        }

        [Benchmark]
        public void VectorClock_comparisons_should_compare_Before_middle()
        {
            CheckThunkFor(_vcBefore, _vcBaseMiddle, (clock, vectorClock) => CompareTo(clock, vectorClock, Ordering.Before), Iterations);
        }

        [Benchmark]
        public void VectorClock_comparisons_should_compare_After_middle()
        {
            CheckThunkFor(_vcAfterMiddle, _vcBaseMiddle, (clock, vectorClock) => CompareTo(clock, vectorClock, Ordering.After), Iterations);
        }

        [Benchmark]
        public void VectorClock_comparisons_should_compare_Concurrent_middle()
        {
            CheckThunkFor(_vcAfterMiddle, _vcConcurrentMiddle, (clock, vectorClock) => CompareTo(clock, vectorClock, Ordering.Concurrent), Iterations);
        }

        [Benchmark]
        public void VectorClock_comparisons_should_compare_notEquals_Before_Middle()
        {
            CheckThunkFor(_vcBefore, _vcBaseMiddle, (clock, vectorClock) => NotEqual(clock, vectorClock), Iterations);
        }

        [Benchmark]
        public void VectorClock_comparisons_should_compare_notEquals_After_Middle()
        {
            CheckThunkFor(_vcAfterMiddle, _vcBaseMiddle, (clock, vectorClock) => NotEqual(clock, vectorClock), Iterations);
        }

        [Benchmark]
        public void VectorClock_comparisons_should_compare_notEquals_Concurrent_Middle()
        {
            CheckThunkFor(_vcAfterMiddle, _vcConcurrentMiddle, (clock, vectorClock) => NotEqual(clock, vectorClock), Iterations);
        }
    }
}
