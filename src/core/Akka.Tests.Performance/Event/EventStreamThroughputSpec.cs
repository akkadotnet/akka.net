//-----------------------------------------------------------------------
// <copyright file="EventStreamThroughputSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util.Internal;
using NBench;

namespace Akka.Tests.Performance.Event
{
    public class EventStreamThroughputSpec
    {
        /// <summary>
        /// Not thread-safe, but called by a single thread in the benchmark
        /// </summary>
        internal class CounterRef : MinimalActorRef
        {
            private readonly Counter _counter;

            public CounterRef(Counter counter)
            {
                _counter = counter;
            }

            protected override void TellInternal(object message, IActorRef sender)
            {
                _counter.Increment();
            }

            public override ActorPath Path { get { return new RootActorPath(Address.AllSystems) / "user" / "foo"; } }
            public override IActorRefProvider Provider { get { return null; } }
        }

        private string stringExample = "just_string";

        private const string MailboxCounterName = "MessageReceived";
        private const long MailboxMessageCount = 100000000;
        private Counter _mailboxThroughput;

        private IActorRef _targetActor;

        private static readonly AtomicCounter Counter = new AtomicCounter(0);
        protected ActorSystem System;

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _mailboxThroughput = context.GetCounter(MailboxCounterName);
            System = ActorSystem.Create($"{GetType().Name}{Counter.GetAndIncrement()}");

            _targetActor = new CounterRef(_mailboxThroughput);
            System.EventStream.Subscribe(_targetActor, typeof(string));
        }

        [PerfBenchmark(
            Description = "Measures the throughput of an ActorBase + Pattern match class",
            RunMode = RunMode.Iterations, NumberOfIterations = 5, TestMode = TestMode.Measurement,
            RunTimeMilliseconds = 1000)]
        [CounterMeasurement(MailboxCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void EventStream_Publish_Throughput(BenchmarkContext context)
        {
            for (var i = 0; i < MailboxMessageCount;)
            {
               System.EventStream.Publish(stringExample);
               ++i;
            }
        }

        [PerfCleanup]
        public void Cleanup()
        {
            System.Terminate().Wait();
            System = null;
        }
    }
}
