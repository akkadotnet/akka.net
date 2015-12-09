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
        class MemoryUntypedActor : UntypedActor {
            protected override void OnReceive(object message)
            {
                if (message is string) return;
                if (message is int) return;
                if (message is bool) return;
                Unhandled(message);
            }
        }

        class MemoryReceiveActor : ReceiveActor
        {
            public MemoryReceiveActor()
            {
                Receive<string>(s => { });
                Receive<int>(i => { });
                Receive<bool>(b => { });
            }
        }

        private static readonly AtomicCounter Counter = new AtomicCounter(0);
        private ActorSystem _system;
        private Counter _createActorThroughput;

        private const string CreateThroughputCounter = "ActorCreateThroughput";
        private const int ActorCreateNumber = 10000;

        private static readonly Props UntypedActorProps = Props.Create(() => new MemoryUntypedActor());
        private static readonly Props ReceiveActorProps = Props.Create(() => new MemoryReceiveActor());
        
        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _system = ActorSystem.Create("ActorMemoryFootprintSpec" + Counter.GetAndIncrement());
            _createActorThroughput = context.GetCounter(CreateThroughputCounter);
        }

        [PerfBenchmark(Description = "Measures the amount of memory used by 10,000 UntypedActors", RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [CounterMeasurement(CreateThroughputCounter)]
        public void UntypedActorMemoryFootprint(BenchmarkContext context)
        {
            for (var i = 0; i < ActorCreateNumber; i++)
            {
                _system.ActorOf(UntypedActorProps);
                _createActorThroughput.Increment();
            }
        }

        [PerfBenchmark(Description = "Measures the amount of memory used by 10,000 ReceiveActors", RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [CounterMeasurement(CreateThroughputCounter)]
        public void ReceiveActorMemoryFootprint(BenchmarkContext context)
        {
            for (var i = 0; i < ActorCreateNumber; i++)
            {
                _system.ActorOf(ReceiveActorProps);
                _createActorThroughput.Increment();
            }
        }

        [PerfCleanup]
        public void Teardown(BenchmarkContext context)
        {
            _system.Shutdown();
            _system.AwaitTermination(TimeSpan.FromSeconds(2.0d));
            _system = null;
        }
    }
}
