using System;
using System.Threading;
using Akka.Dispatch;
using NBench;

namespace Akka.Tests.Performance.Dispatch
{
    /// <summary>
    /// Base class used to test all <see cref="MessageDispatcher"/> implementations
    /// </summary>
    public abstract class ColdDispatcherThroughputSpecBase
    {
        protected abstract MessageDispatcherConfigurator Configurator();

        private const string DispatcherCounterName = "ScheduledActionCompleted";
        private const long ScheduleCount = 10000;

        private Counter _dispatcherCounter;

        private MessageDispatcher _dispatcher;
        private MessageDispatcherConfigurator _configurator;

        private long _messagesSeen = 0L;

        /// <summary>
        /// Used to block the benchmark method from exiting before all scheduled work is completed
        /// </summary>
        protected readonly ManualResetEventSlim EventBlock = new ManualResetEventSlim(false);

        protected Action ScheduledWork;

        /// <summary>
        /// Warms up <see cref="dispatcher"/> prior to the benchmark running,
        /// so we can exclude initialization overhead from the results of the benchmark.
        /// </summary>
        /// <param name="dispatcher">The <see cref="MessageDispatcher"/> implementaiton we'll be testing.</param>
        /// <remarks>Does nothing by default (includes overhead)</remarks>
        protected virtual void Warmup(MessageDispatcher dispatcher)
        {
            
        }

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _configurator = Configurator();
            _dispatcher = _configurator.Dispatcher();
            _dispatcherCounter = context.GetCounter(DispatcherCounterName);
            ScheduledWork = () =>
            {
                _dispatcherCounter.Increment();
                if (Interlocked.Increment(ref _messagesSeen) == ScheduleCount)
                {
                    EventBlock.Set();
                }
            };
            Warmup(_dispatcher);
        }

        [PerfBenchmark(Description = "Tests how long it takes to schedule items onto the dispatcher", RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [CounterMeasurement(DispatcherCounterName)]
        public void ScheduleThroughput(BenchmarkContext context)
        {
            for (var i = 0L; i < ScheduleCount;)
            {
                _dispatcher.Schedule(ScheduledWork);
                ++i;
            }
            
            EventBlock.Wait();
        }

        [PerfCleanup]
        public void Teardown()
        {
            _dispatcher.Detach(null); //forces disposal of per-actor dispatchers
            EventBlock.Dispose();
        }
    }

    /// <summary>
    /// Warms up a <see cref="MessageDispatcher"/> so we can exclude its initialization overhead
    /// </summary>
    public abstract class WarmDispatcherThroughputSpecBase : ColdDispatcherThroughputSpecBase
    {
        protected override void Warmup(MessageDispatcher dispatcher)
        {
            var warmupCount = 10L;
            var warmupsThusFar = 0L;
            Action warmupWork = () =>
            {
                if (Interlocked.Increment(ref warmupsThusFar) == warmupCount)
                {
                    EventBlock.Set();
                }
            };


            for (var i = 0; i < warmupCount;)
            {
                dispatcher.Schedule(warmupWork);
                ++i;
            }
            EventBlock.Wait();
            EventBlock.Reset();
        }
    }
}
