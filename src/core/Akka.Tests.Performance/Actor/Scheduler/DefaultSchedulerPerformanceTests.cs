//-----------------------------------------------------------------------
// <copyright file="DefaultSchedulerPerformanceTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using NBench;

namespace Akka.Tests.Performance.Actor.Scheduler
{
    /// <summary>
    /// Designed to measure how many operations per-second the scheduler can perform in a fixed window of time.
    /// 
    /// Meant to measure the efficiency of the default <see cref="IScheduler"/> and to see if there's much contention
    /// around the 
    /// </summary>
    public class DefaultSchedulerPerformanceTests
    {
        private ActorSystem _actorSystem;
        private const string ScheduledOps = "ScheduleInvokes";
        private Counter _scheduledOpsCounter;
        private const string ScheduledJobs = "ScheduledJobs";
        private Counter _jobsScheduled;
        private ICancelable _cancelSignal;

        /// <summary>
        ///  number of concurrent schedulers
        /// </summary>
        private const int DegreeOfParallelism = 10;

        private const int SchedulePerBatch = 200;
        private static readonly TimeSpan RunTime = TimeSpan.FromSeconds(20);
        private Action _eventLoop;
        private Action _counterIncrement;

        public const int IterationCount = 1; // these are LONG-running benchmarks

        [PerfSetup]
        public void SetUp(BenchmarkContext context)
        {
            _scheduledOpsCounter = context.GetCounter(ScheduledOps);
            _jobsScheduled = context.GetCounter(ScheduledJobs);
            _actorSystem = ActorSystem.Create("SchedulerPerformanceSpecs");
            _cancelSignal = new Cancelable(_actorSystem.Scheduler);
            _counterIncrement = () => _scheduledOpsCounter.Increment();

            _eventLoop = () =>
            {
                while (!_cancelSignal.IsCancellationRequested)
                {
                    for (var i = 0; i < SchedulePerBatch; i++)
                    {
                        _actorSystem.Scheduler.Advanced.ScheduleRepeatedly(0, 10, _counterIncrement, _cancelSignal);
                        _jobsScheduled.Increment();
                    }
                    Thread.Sleep(40); // wait a bit, then keep going
                }
            };
        }

        [PerfBenchmark(Description = "Tests to see how many concurrent jobs we can schedule and what the effects are on throughput", RunMode = RunMode.Iterations, NumberOfIterations = IterationCount)]
        [CounterMeasurement(ScheduledJobs)]
        [CounterMeasurement(ScheduledOps)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void SchedulerThroughputStressTest()
        {
            for (var i = 0; i < DegreeOfParallelism; i++)
            {
                Task.Factory.StartNew(_eventLoop);
            }
            Task.Delay(RunTime).Wait();
            _cancelSignal.Cancel(false);
        }

        [PerfCleanup]
        public void CleanUp()
        {
            _actorSystem.Terminate().Wait();
        }
    }
}

