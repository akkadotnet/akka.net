using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Util;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster;
using BenchmarkDotNet.Attributes;
using FluentAssertions;

namespace Akka.Benchmarks.Cluster
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class ReachabilityBenchmarks
    {
        [Params(100)]
        public int NodesSize;

        [Params(100)]
        public int Iterations;

        public Address Address = new Address("akka", "sys", "a", 2552);
        public Address Node = new Address("akka", "sys", "a", 2552);

        private Reachability CreateReachabilityOfSize(Reachability baseReachability, int size)
        {
            return Enumerable.Range(1, size).Aggregate(baseReachability, (r, i) =>
            {
                var obs = new UniqueAddress(Address.WithHost("node-" + i), i);
                var j = i == size ? 1 : i + 1;
                var subject = new UniqueAddress(Address.WithHost("node-" + j), j);
                return r.Unreachable(obs, subject).Reachable(obs, subject);
            });
        }

        private Reachability AddUnreachable(Reachability baseReachability, int count)
        {
            var observers = baseReachability.Versions.Keys.Take(count);
            // the Keys HashSet<T> IEnumerator does not support Reset, hence why we have to convert it to a list
            using var subjects = baseReachability.Versions.Keys.ToList().GetContinuousEnumerator();
            return observers.Aggregate(baseReachability, (r, o) =>
            {
                return Enumerable.Range(1, 5).Aggregate(r, (r2, i) =>
                {
                    subjects.MoveNext();
                    return r2.Unreachable(o, subjects.Current);
                });
            });
        }

        internal Reachability Reachability1;
        internal Reachability Reachability2;
        internal Reachability Reachability3;
        internal ImmutableHashSet<UniqueAddress> Allowed;

        [GlobalSetup]
        public void Setup()
        {
            Reachability1 = CreateReachabilityOfSize(Reachability.Empty, NodesSize);
            Reachability2 = CreateReachabilityOfSize(Reachability1, NodesSize);
            Reachability3 = AddUnreachable(Reachability1, NodesSize / 2);
            Allowed =  Reachability1.Versions.Keys.ToImmutableHashSet();
        }

        private void CheckThunkFor(Reachability r1, Reachability r2, Action<Reachability, Reachability> thunk,
            int times)
        {
            for (var i = 0; i < times; i++)
                thunk(new Reachability(r1.Records, r1.Versions), new Reachability(r2.Records, r2.Versions));
        }

        private void CheckThunkFor(Reachability r1, Action<Reachability> thunk, int times)
        {
            for (var i = 0; i < times; i++)
                thunk(new Reachability(r1.Records, r1.Versions));
        }

        private void Merge(Reachability r1, Reachability r2, int expectedRecords)
        {
            r1.Merge(Allowed, r2).Records.Count.Should().Be(expectedRecords);
        }

        private void CheckStatus(Reachability r1)
        {
            var record = r1.Records.First();
            r1.Status(record.Observer, record.Subject).Should().Be(record.Status);
        }

        private void CheckAggregatedStatus(Reachability r1)
        {
            var record = r1.Records.First();
            r1.Status(record.Subject).Should().Be(record.Status);
        }

        private void AllUnreachableOrTerminated(Reachability r1)
        {
            r1.AllUnreachableOrTerminated.IsEmpty.Should().BeFalse();
        }

        private void AllUnreachable(Reachability r1)
        {
            r1.AllUnreachable.IsEmpty.Should().BeFalse();
        }

        private void RecordsFrom(Reachability r1)
        {
            foreach (var o in r1.AllObservers)
            {
                r1.RecordsFrom(o).Should().NotBeNull();
            }
        }

        [Benchmark]
        public void Reachability_must_merge_with_same_versions()
        {
            CheckThunkFor(Reachability1, Reachability1, (r1, r2) => Merge(r1, r2, 0), Iterations);
        }

        [Benchmark]
        public void Reachability_must_merge_with_all_older_versions()
        {
            CheckThunkFor(Reachability2, Reachability1, (r1, r2) => Merge(r1, r2, 0), Iterations);
        }

        [Benchmark]
        public void Reachability_must_merge_with_all_newer_versions()
        {
            CheckThunkFor(Reachability1, Reachability2, (r1, r2) => Merge(r1, r2, 0), Iterations);
        }

        [Benchmark]
        public void Reachability_must_merge_with_half_nodes_unreachable()
        {
            CheckThunkFor(Reachability1, Reachability3, (r1, r2) => Merge(r1, r2, 5* NodesSize/2), Iterations);
        }

        [Benchmark]
        public void Reachability_must_merge_with_half_nodes_unreachable_opposite()
        {
            CheckThunkFor(Reachability3, Reachability1, (r1, r2) => Merge(r1, r2, 5 * NodesSize / 2), Iterations);
        }

        [Benchmark]
        public void Reachability_must_check_status_with_half_nodes_unreachable()
        {
            CheckThunkFor(Reachability3,  CheckAggregatedStatus, Iterations);
        }

        [Benchmark]
        public void Reachability_must_check_AllUnreachableOrTerminated_with_half_nodes_unreachable()
        {
            CheckThunkFor(Reachability3, AllUnreachableOrTerminated, Iterations);
        }

        [Benchmark]
        public void Reachability_must_check_AllUnreachable_with_half_nodes_unreachable()
        {
            CheckThunkFor(Reachability3, AllUnreachable, Iterations);
        }

        [Benchmark]
        public void Reachability_must_check_RecordsFrom_with_half_nodes_unreachable()
        {
            CheckThunkFor(Reachability3, RecordsFrom, Iterations);
        }
    }
}
