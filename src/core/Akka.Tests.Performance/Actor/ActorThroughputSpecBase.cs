using System;
using System.Threading;
using Akka.Actor;
using Akka.Util.Internal;
using NBench;

namespace Akka.Tests.Performance.Actor
{
    /// <summary>
    ///     Base class used to test the performance of different <see cref="ActorBase" /> implementations
    /// </summary>
    public abstract class ActorThroughputSpecBase
    {
        private const string MailboxCounterName = "MessageReceived";
        private const long MailboxMessageCount = 10000000;
        private Counter _mailboxThroughput;
        private readonly ManualResetEventSlim _resetEvent = new ManualResetEventSlim(false);
        private IActorRef _receiver;

        private static readonly AtomicCounter Counter = new AtomicCounter(0);
        protected ActorSystem System;

        public abstract IActorRef CreateBenchmarkActor(Counter counter, long maxExpectedMessages, ManualResetEventSlim resetEvent);

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _mailboxThroughput = context.GetCounter(MailboxCounterName);
            System = ActorSystem.Create("MailboxThroughputSpecBase" + Counter.GetAndIncrement());
            _receiver = CreateBenchmarkActor(_mailboxThroughput, MailboxMessageCount, _resetEvent);
        }

        [PerfBenchmark(
            Description =
                "Measures the throughput of an actor implementation",
            RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement,
            RunTimeMilliseconds = 1000)]
        [CounterMeasurement(MailboxCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void Benchmark(BenchmarkContext context)
        {
            for (var i = 0; i < MailboxMessageCount;)
            {
                _receiver.Tell(string.Empty);
                ++i;
            }
            _resetEvent.Wait(); //wait up to a second
        }

        [PerfCleanup]
        public void Cleanup()
        {
            _resetEvent.Dispose();
            System.Shutdown();
            System.TerminationTask.Wait();
        }
    }
}