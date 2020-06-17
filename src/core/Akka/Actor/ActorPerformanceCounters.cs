using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Akka.Actor
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class ActorPerformanceCounters
    {
        private const string CategoryName = "Akka.NET";
        private static readonly string _processName = Process.GetCurrentProcess().ProcessName;
        private static string _version;

        private static bool _initialized;

        private static PerformanceCounter _mailboxRun;
        private static PerformanceCounter _schedulerRun;
        private static PerformanceCounter _schedulerWaitingTick;
        private static PerformanceCounter _schedulerSchedule;
        private static PerformanceCounter _schedulerUnschedule;
        private static PerformanceCounter _schedulerSleep;
        private static PerformanceCounter _dedicatedPoolCount;
        private static PerformanceCounter _dedicatedPoolWorkerCount;

        private static TimeSpan _loggingDelay;
        private static bool _logging;
        private static Stopwatch _stopwatch;
        private static PerformanceCounter _totalCpu;
        private static PerformanceCounter _processCpu;
        private static PerformanceCounter _processThreads;

        private static ICancelable _logTask;

        private static void LoggingProc()
        {
            Console.WriteLine("Timestamp,Total CPU,Process CPU");
            var comma = ',';
            var sb = new StringBuilder();
            while (_logging)
            {
                sb.Clear();
                sb.Append(_stopwatch.Elapsed)
                  .Append(comma)
                  .Append(_totalCpu.NextValue())
                  .Append(comma)
                  .Append(_processCpu.NextValue());
                Console.WriteLine(sb.ToString());
                Thread.Sleep(_loggingDelay);
            }
        }

        public static bool Start(IScheduler scheduler, bool useConsoleOutput = false, TimeSpan? delay = null)
        {
            _version = Assembly.GetAssembly(typeof(ReceiveActor)).GetName().Version.ToString();

            // If the category does not exist, create the category and exit.
            // Performance counters should not be created and immediately used.
            // There is a latency time to enable the counters, they should be created
            // prior to executing the application that uses the counters.
            // Execute this sample a second time to use the category.
            if (SetupCategory())
                return true;

            CreateCounters();
            Reset();
            _initialized = true;

            if (!useConsoleOutput)
                return false;

            if (_stopwatch is null) _stopwatch = Stopwatch.StartNew();
            if (!_stopwatch.IsRunning) _stopwatch.Start();

            if (!delay.HasValue || delay.Value <= TimeSpan.Zero)
                delay = TimeSpan.FromSeconds(10);
            _loggingDelay = delay.Value;

            _logTask = scheduler.Advanced.ScheduleRepeatedlyCancelable(_loggingDelay, _loggingDelay, LoggingProc);
            _logging = true;

            return false;
        }

        public static void Stop()
        {
            if (!_logging) return;

            _logging = false;
            _logTask?.Cancel();
        }

        public static void MailboxRun()
        {
            if (_initialized) _mailboxRun.Increment();
        }

        public static void SchedulerRun()
        {
            if (_initialized) _schedulerRun.Increment();
        }

        public static void SchedulerWaiting()
        {
            if (_initialized) _schedulerWaitingTick.Increment();
        }

        public static void SchedulerSchedule()
        {
            if (_initialized) _schedulerSchedule.Increment();
        }

        public static void SchedulerUnschedule()
        {
            if (_initialized) _schedulerUnschedule.Increment();
        }

        public static void SchedulerSleep()
        {
            if (_initialized) _schedulerSleep.Increment();
        }

        public static void ThreadPoolInc()
        {
            if (_initialized) _dedicatedPoolCount.Increment();
        }

        public static void ThreadPoolDec()
        {
            if (_initialized) _dedicatedPoolCount.Decrement();
        }

        public static void ThreadWorkerInc()
        {
            if (_initialized) _dedicatedPoolWorkerCount.Increment();
        }

        public static void ThreadWorkerDec()
        {
            if (_initialized) _dedicatedPoolWorkerCount.Decrement();
        }

        private static bool SetupCategory()
        {
            //PerformanceCounterCategory.Delete(_categoryName);
            //return true;

            if (!PerformanceCounterCategory.Exists(CategoryName))
            {

                var counterDataCollection = new CounterCreationDataCollection();

                // Add the counters.
                var data = new CounterCreationData
                {
                    CounterType = PerformanceCounterType.RateOfCountsPerSecond32,
                    CounterName = "Mailbox.Run",
                    CounterHelp = "Mailbox.Run() function calls/second"
                };
                counterDataCollection.Add(data);

                data = new CounterCreationData
                {
                    CounterType = PerformanceCounterType.RateOfCountsPerSecond32,
                    CounterName = "HashedWheelTimerScheduler.Run",
                    CounterHelp = "HashedWheelTimerScheduler.Run() function calls/second"
                };
                counterDataCollection.Add(data);

                data = new CounterCreationData
                {
                    CounterType = PerformanceCounterType.RateOfCountsPerSecond32,
                    CounterName = "HashedWheelTimerScheduler.WaitForNextTick",
                    CounterHelp = "HashedWheelTimerScheduler.WaitForNextTick() function calls/second"
                };
                counterDataCollection.Add(data);

                data = new CounterCreationData
                {
                    CounterType = PerformanceCounterType.RateOfCountsPerSecond32,
                    CounterName = "HashedWheelTimerScheduler.InternalSchedule",
                    CounterHelp = "HashedWheelTimerScheduler.InternalSchedule() function calls/second"
                };
                counterDataCollection.Add(data);

                data = new CounterCreationData
                {
                    CounterType = PerformanceCounterType.RateOfCountsPerSecond32,
                    CounterName = "HashedWheelTimerScheduler.UnSchedule",
                    CounterHelp = "HashedWheelTimerScheduler.UnSchedule() function calls/second"
                };
                counterDataCollection.Add(data);

                data = new CounterCreationData
                {
                    CounterType = PerformanceCounterType.RateOfCountsPerSecond32,
                    CounterName = "HashedWheelTimerScheduler.Thread.Sleep",
                    CounterHelp = "HashedWheelTimerScheduler.Thread.Sleep() function calls/second"
                };
                counterDataCollection.Add(data);

                data = new CounterCreationData
                {
                    CounterType = PerformanceCounterType.NumberOfItems32,
                    CounterName = "DedicatedThreadPool",
                    CounterHelp = "Helios.Concurrency.DedicatedThreadPool instance count"
                };
                counterDataCollection.Add(data);

                data = new CounterCreationData
                {
                    CounterType = PerformanceCounterType.NumberOfItems32,
                    CounterName = "DedicatedThreadPool.Workers",
                    CounterHelp = "Helios.Concurrency.DedicatedThreadPool workers count"
                };
                counterDataCollection.Add(data);

                // Create the category.
                PerformanceCounterCategory.Create(CategoryName,
                    "Akka.NET Performance Counters",
                    PerformanceCounterCategoryType.MultiInstance, counterDataCollection);

                return (true);
            }

            Console.WriteLine($"Category exists - {CategoryName}");
            Console.WriteLine($"Process name: {_processName}");
            return (false);
        }

        private static void CreateCounters()
        {
            _mailboxRun = new PerformanceCounter(CategoryName, "Mailbox.Run", $"{_processName} ({_version})", false);
            _schedulerRun = new PerformanceCounter(CategoryName, "HashedWheelTimerScheduler.Run", $"{_processName} ({_version})", false);
            _schedulerWaitingTick = new PerformanceCounter(CategoryName, "HashedWheelTimerScheduler.WaitForNextTick", $"{_processName} ({_version})", false);
            _schedulerSchedule = new PerformanceCounter(CategoryName, "HashedWheelTimerScheduler.InternalSchedule", $"{_processName} ({_version})", false);
            _schedulerUnschedule = new PerformanceCounter(CategoryName, "HashedWheelTimerScheduler.UnSchedule", $"{_processName} ({_version})", false);
            _schedulerSleep = new PerformanceCounter(CategoryName, "HashedWheelTimerScheduler.Thread.Sleep", $"{_processName} ({_version})", false);
            _dedicatedPoolCount = new PerformanceCounter(CategoryName, "DedicatedThreadPool", $"{_processName} ({_version})", false);
            _dedicatedPoolWorkerCount = new PerformanceCounter(CategoryName, "DedicatedThreadPool.Workers", $"{_processName} ({_version})", false);

            _totalCpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _processCpu = new PerformanceCounter("Process", "% Processor Time", _processName, true);
            _processThreads = new PerformanceCounter("Process", "Thread Count", _processName, true);

        }

        public static void Reset()
        {
            _mailboxRun.RawValue = 0;
            _schedulerRun.RawValue = 0;
            _schedulerWaitingTick.RawValue = 0;
            _schedulerSchedule.RawValue = 0;
            _schedulerSleep.RawValue = 0;
            _dedicatedPoolCount.RawValue = 0;
            _dedicatedPoolWorkerCount.RawValue = 0;
        }
    }
}
