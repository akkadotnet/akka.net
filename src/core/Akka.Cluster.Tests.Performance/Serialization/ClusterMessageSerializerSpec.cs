//-----------------------------------------------------------------------
// <copyright file="ClusterMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Cluster.Serialization;
using Akka.Routing;
using Akka.Util.Internal;
using NBench;

namespace Akka.Cluster.Tests.Performance.Serialization
{
    public class ClusterMessageSerializerSpec
    {
        private const string SerializerCounterName = "SerializerCounter";
        private static readonly AtomicCounter Counter = new AtomicCounter(0);
        private Counter _serializationThroughput;
        private ActorSystem _system;
        private ClusterMessageSerializer _serializer;
        private const int IterationCount = 10000;

        // MESSAGES

        internal Lazy<ClusterRouterPool> ClusterRouterPoolMessage { get; } = new Lazy<ClusterRouterPool>(() =>
        {
            var roundRobinPool = new RoundRobinPool(nrOfInstances: 4);
            var clusterRouterPoolSettings = new ClusterRouterPoolSettings(2, 5, true, "Richard, Duke");
            return new ClusterRouterPool(roundRobinPool, clusterRouterPoolSettings);
        });

        internal Lazy<InternalClusterAction.Welcome> WelcomeMessage { get; } = new Lazy<InternalClusterAction.Welcome>(() =>
        {
            var member1 = new Member(new UniqueAddress(new Address("akka.tcp", "system", "some.host.org", 4718), 34), 1, MemberStatus.Joining, ImmutableHashSet<string>.Empty);
            var member2 = new Member(new UniqueAddress(new Address("akka.tcp", "system", "some.host.org", 4710), 35), 1, MemberStatus.Joining, ImmutableHashSet<string>.Empty);

            var node1 = new VectorClock.Node("node1");
            var node2 = new VectorClock.Node("node2");
            var gossip = new Gossip(ImmutableSortedSet.Create(member1, member2)).Increment(node1)
                    .Increment(node2)
                    .Seen(member1.UniqueAddress)
                    .Seen(member2.UniqueAddress);

            var addressFrom = new Address("akka.tcp", "system", "some.host.org", 4711);
            var uniqueAddressFrom = new UniqueAddress(addressFrom, 17);
            return new InternalClusterAction.Welcome(uniqueAddressFrom, gossip);
        });

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _system = ActorSystem.Create($"ClusterMessageSerializerSpec-{Counter.GetAndIncrement()}");
            _serializer = new ClusterMessageSerializer(_system.AsInstanceOf<ExtendedActorSystem>());
            _serializationThroughput = context.GetCounter(SerializerCounterName);
        }

        [PerfBenchmark(Description = "Measures the throughput of an ClusterRouterPool serialization", RunMode = RunMode.Iterations, NumberOfIterations = 5, TestMode = TestMode.Measurement, RunTimeMilliseconds = 1000)]
        [CounterMeasurement(SerializerCounterName)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void ClusterRouterPool_Serialization_Throughput(BenchmarkContext context)
        {
            for (int i = 0; i < IterationCount; i++)
            {
                var serializedBytes = _serializer.ToBinary(ClusterRouterPoolMessage.Value);
                var deserialized = _serializer.FromBinary(serializedBytes, ClusterRouterPoolMessage.Value.GetType());
                _serializationThroughput.Increment();
            }
        }

        [PerfBenchmark(Description = "Measures the throughput of an Welcome message serialization", RunMode = RunMode.Iterations, NumberOfIterations = 5, TestMode = TestMode.Measurement, RunTimeMilliseconds = 1000)]
        [CounterMeasurement(SerializerCounterName)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void Welcome_Serialization_Throughput(BenchmarkContext context)
        {
            for (int i = 0; i < IterationCount; i++)
            {
                var serializedBytes = _serializer.ToBinary(WelcomeMessage.Value);
                var deserialized = _serializer.FromBinary(serializedBytes, WelcomeMessage.Value.GetType());
                _serializationThroughput.Increment();
            }
        }

        [PerfCleanup]
        public void Cleanup()
        {
            _system.Terminate().Wait(TimeSpan.FromSeconds(3));
        }
    }
}
