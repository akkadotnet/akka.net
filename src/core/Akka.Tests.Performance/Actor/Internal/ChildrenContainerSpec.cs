//-----------------------------------------------------------------------
// <copyright file="ChildrenContainerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Util.Internal;
using NBench;

namespace Akka.Tests.Performance.Actor.Internal
{
    /// <summary>
    /// Performance specifications for <see cref="IChildrenContainer"/>.
    /// </summary>
    /// <remarks>
    /// Mostly used to gauge how performance improvements to System.Collections.Immutable affect the build.
    /// </remarks>
    public class ChildrenContainerSpec
    {
        private IChildrenContainer _emptyContainer;
        private IChildrenContainer _fullContainer;
        private Counter _childContainerOpsCounter;
        private const string ChildContainerOperationsName = "ChildContainerMutations";
        private const int PrePopulatedActorCount = 50000;
        private int nameCounter = 0;

        private class PlaceHolderActorRef : MinimalActorRef
        {
            public PlaceHolderActorRef(string name)
            {
                Path = new RootActorPath(Address.AllSystems, name);
            }

            public override ActorPath Path { get; }

            public override IActorRefProvider Provider { get { return null; } }
        }


        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _emptyContainer = EmptyChildrenContainer.Instance;
            _fullContainer = EmptyChildrenContainer.Instance;
            for (var i = 0; i < PrePopulatedActorCount; i++)
            {
                var name = i.ToString();
                _fullContainer.Add(name, new ChildRestartStats(new PlaceHolderActorRef(name)));
            }
            _childContainerOpsCounter = context.GetCounter(ChildContainerOperationsName);
            nameCounter = 0;
        }

        [PerfBenchmark(Description = "Add as many children as possible to an EmptyChildContainerCollection",
            TestMode = TestMode.Measurement, NumberOfIterations = 13, RunMode = RunMode.Throughput, RunTimeMilliseconds = 1000)]
        [CounterMeasurement(ChildContainerOperationsName)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void EmptyChildrenContainer_add_children_speed_test(BenchmarkContext context)
        {
            var name = nameCounter++.ToString();
            _emptyContainer = _emptyContainer.Add(name, new ChildRestartStats(new PlaceHolderActorRef(name)));
            _childContainerOpsCounter.Increment();
        }

        [PerfBenchmark(Description = "Add as many children as possible to an a child container with lots of children already",
            TestMode = TestMode.Measurement, NumberOfIterations = 13, RunMode = RunMode.Throughput, RunTimeMilliseconds = 1000)]
        [CounterMeasurement(ChildContainerOperationsName)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void FullChildrenContainer_add_children_speed_test(BenchmarkContext context)
        {
            var name = (nameCounter += PrePopulatedActorCount).ToString();
            _emptyContainer = _emptyContainer.Add(name, new ChildRestartStats(new PlaceHolderActorRef(name)));
            _childContainerOpsCounter.Increment();
        }

        [PerfBenchmark(Description = "Remove as many children as possible to an a child container with lots of children already",
            TestMode = TestMode.Measurement, NumberOfIterations = 13, RunMode = RunMode.Throughput, RunTimeMilliseconds = 1000)]
        [CounterMeasurement(ChildContainerOperationsName)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void FullChildrenContainer_remove_children_speed_test(BenchmarkContext context)
        {
            var name = nameCounter++.ToString();
            _emptyContainer = _emptyContainer.Remove(new PlaceHolderActorRef(name));
            _childContainerOpsCounter.Increment();
        }
    }
}
