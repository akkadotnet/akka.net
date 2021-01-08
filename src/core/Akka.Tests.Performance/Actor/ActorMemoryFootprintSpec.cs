//-----------------------------------------------------------------------
// <copyright file="ActorMemoryFootprintSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util.Internal;
using NBench;

namespace Akka.Tests.Performance.Actor
{
    /// <summary>
    /// Tests the default memory footprint of an Akka.NET actor
    /// </summary>
    public class ActorMemoryFootprintSpec
    {
        #region Actor classes
        internal class MemoryActorBasePatternMatchActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                return message.Match()
                    .With<string>(s => { })
                    .With<int>(i => { })
                    .With<bool>(b => { })
                    .WasHandled;
            }

            public static Props Props { get; } = Props.Create(() => new MemoryActorBasePatternMatchActor());
        }

        internal class MemoryUntypedActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message is string) return;
                if (message is int) return;
                if (message is bool) return;
                Unhandled(message);
            }

            public static Props Props { get; } = Props.Create(() => new MemoryUntypedActor());
        }

        internal class MemoryReceiveActor : ReceiveActor
        {
            public MemoryReceiveActor()
            {
                Receive<string>(s => { });
                Receive<int>(i => { });
                Receive<bool>(b => { });
            }

            public static Props Props { get; } = Props.Create(() => new MemoryReceiveActor());
        }
        #endregion

        private static readonly AtomicCounter Counter = new AtomicCounter(0);
        private ActorSystem _system;
        private Counter _createActorThroughput;

        private const string CreateThroughputCounter = "ActorCreateThroughput";
        private const int ActorCreateNumber = 10000;
        
        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _system = ActorSystem.Create($"ActorMemoryFootprintSpec{Counter.GetAndIncrement()}");
            _createActorThroughput = context.GetCounter(CreateThroughputCounter);
        }

        [PerfBenchmark(Description = "Measures the amount of memory used by 10,000 ActorBase + PatternMatch", RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [CounterMeasurement(CreateThroughputCounter)]
        public void ActorBase_PatternMatch_memory_footprint(BenchmarkContext context)
        {
            for (var i = 0; i < ActorCreateNumber; i++)
            {
                _system.ActorOf(MemoryActorBasePatternMatchActor.Props);
                _createActorThroughput.Increment();
            }
        }

        [PerfBenchmark(Description = "Measures the amount of memory used by 10,000 UntypedActors", RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [CounterMeasurement(CreateThroughputCounter)]
        public void UntypedActor_memory_footprint(BenchmarkContext context)
        {
            for (var i = 0; i < ActorCreateNumber; i++)
            {
                _system.ActorOf(MemoryUntypedActor.Props);
                _createActorThroughput.Increment();
            }
        }

        [PerfBenchmark(Description = "Measures the amount of memory used by 10,000 ReceiveActors", RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [CounterMeasurement(CreateThroughputCounter)]
        public void ReceiveActor_memory_footprint(BenchmarkContext context)
        {
            for (var i = 0; i < ActorCreateNumber; i++)
            {
                _system.ActorOf(MemoryReceiveActor.Props);
                _createActorThroughput.Increment();
            }
        }

        [PerfCleanup]
        public void Teardown(BenchmarkContext context)
        {
            _system.Terminate().Wait(TimeSpan.FromSeconds(2.0d));
            _system = null;
        }
    }
}
