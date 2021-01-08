//-----------------------------------------------------------------------
// <copyright file="AskSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using NBench;

namespace Akka.Tests.Performance.Actor.Pattern
{
    /// <summary>
    /// Benchmark and performance test for <see cref="Ask"/>
    /// </summary>
    public class AskSpec
    {
        class EmptyActor : ReceiveActor
        {
            public EmptyActor()
            {
                ReceiveAny(o => Sender.Tell(o));
            }
        }

        public const string AskThroughputCounterName = "AskReplies";
        private Counter _askThroughputCounter;

        private ActorSystem _sys;
        private IActorRef _target;
        private static readonly Identify Msg = new Identify(null);
        private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(1);

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _sys = ActorSystem.Create("AskSpec");
            _target = _sys.ActorOf(Props.Create(() => new EmptyActor()));
            _askThroughputCounter = context.GetCounter(AskThroughputCounterName);
        }

        [PerfBenchmark(Description = "Tests how quickly ICanTell.Ask operations can be performed, and with how much memory", RunMode = RunMode.Throughput,
            RunTimeMilliseconds = 5000, NumberOfIterations = 3)]
        [CounterMeasurement(AskThroughputCounterName)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        public void AskThroughput(BenchmarkContext context)
        {
            _target.Ask<ActorIdentity>(Msg, AskTimeout).Wait();
            _askThroughputCounter.Increment();
        }

        [PerfCleanup]
        public void TearDown(BenchmarkContext context)
        {
            var shutdownTimeout = TimeSpan.FromSeconds(5);
            try
            {
                _sys?.Terminate().Wait(shutdownTimeout);
            }
            catch (Exception ex)
            {
                context.Trace.Error(ex, $"failed to shutdown actorsystem within {shutdownTimeout}");
            }
        }
    }
}
