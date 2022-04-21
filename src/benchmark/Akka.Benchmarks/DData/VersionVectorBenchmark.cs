using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Benchmarks.Configurations;
using Akka.Cluster;
using Akka.DistributedData;
using BenchmarkDotNet.Attributes;
using FluentAssertions;
using static Akka.DistributedData.VersionVector;

namespace Akka.Benchmarks.DData
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class VersionVectorBenchmarks
    {
        [Params(100)]
        public int ClockSize;

        [Params(1000)]
        public int Iterations;

        internal (VersionVector clock, ImmutableSortedSet<UniqueAddress> nodes) CreateVectorClockOfSize(int size)
        {
            UniqueAddress GenerateUniqueAddress(int nodeCount){
                return new UniqueAddress(new Akka.Actor.Address("akka.tcp", "ClusterSys", "localhost", nodeCount), nodeCount);
            }

            return Enumerable.Range(1, size)
                .Aggregate((VersionVector.Empty, ImmutableSortedSet<UniqueAddress>.Empty),
                    (tuple, i) =>
                    {
                        var (vc, nodes) = tuple;
                        var node = GenerateUniqueAddress(i);
                        return (vc.Increment(node), nodes.Add(node));
                    });
        }

        internal VersionVector CopyVectorClock(VersionVector vc)
        {
            var versions = ImmutableDictionary<UniqueAddress, long>.Empty;
            var enumerator = vc.VersionEnumerator;
            while(enumerator.MoveNext()){
                var nodePair = enumerator.Current;
                versions = versions.SetItem(nodePair.Key, nodePair.Value);
            }

            return VersionVector.Create(versions);
        }

        private UniqueAddress _firstNode;
        private UniqueAddress _lastNode;
        private UniqueAddress _middleNode;
        private ImmutableSortedSet<UniqueAddress> _nodes;
        private VersionVector _vcBefore;
        private VersionVector _vcBaseLast;
        private VersionVector _vcAfterLast;
        private VersionVector _vcConcurrentLast;
        private VersionVector _vcBaseMiddle;
        private VersionVector _vcAfterMiddle;
        private VersionVector _vcConcurrentMiddle;

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

        private void CheckThunkFor(VersionVector vc1, VersionVector vc2, Action<VersionVector, VersionVector> thunk, int times)
        {
            var vcc1 = CopyVectorClock(vc1);
            var vcc2 = CopyVectorClock(vc2);
            for (var i = 0; i < times; i++)
            {
                thunk(vcc1, vcc2);
            }
        }

        private void CompareTo(VersionVector vc1, VersionVector vc2, Ordering ordering)
        {
            vc1.Compare(vc2).Should().Be(ordering);
        }

        private void NotEqual(VersionVector vc1, VersionVector vc2)
        {
            (vc1 == vc2).Should().BeFalse();
        }

        private void Merge(VersionVector vc1, VersionVector vc2)
        {
            vc1.Merge(vc2);
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

        [Benchmark]
        public void VersionVector_merge_Multi_Multi()
        {
            CheckThunkFor(_vcBefore, _vcAfterLast, (vector, versionVector) => Merge(vector, versionVector), Iterations);
        }
    }
}