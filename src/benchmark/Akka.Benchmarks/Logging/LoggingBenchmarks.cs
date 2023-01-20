//-----------------------------------------------------------------------
// <copyright file="LoggingBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Event;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class LoggingBenchmarks
    {
        private sealed class BenchmarkLogAdapter : LoggingAdapterBase
        {
            public int CurrentLogs { get; private set; }
            public LogEvent[] AllLogs { get; private set; }

            private readonly string _logSource;
            private readonly Type _logClass;

            public BenchmarkLogAdapter(int capacity) : base(DefaultLogMessageFormatter.Instance)
            {
                AllLogs = new LogEvent[capacity];
                _logSource = LogSource.Create(this).Source;
                _logClass = typeof(BenchmarkLogAdapter);
            }

            public override bool IsDebugEnabled { get; } = true;
            public override bool IsInfoEnabled { get; } = true;
            public override bool IsWarningEnabled { get; } = true;
            public override bool IsErrorEnabled { get; } = true;

            private void AddLogMessage(LogEvent m)
            {
                AllLogs[CurrentLogs++] = m;
            }

            protected override void NotifyError(object message)
            {
               AddLogMessage(new Error(null, _logSource, _logClass, message));
            }

            /// <summary>
            /// Publishes the error message and exception onto the LoggingBus.
            /// </summary>
            /// <param name="cause">The exception that caused this error.</param>
            /// <param name="message">The error message.</param>
            protected override void NotifyError(Exception cause, object message)
            {
                AddLogMessage(new Error(cause, _logSource, _logClass, message));
            }

            /// <summary>
            /// Publishes the warning message onto the LoggingBus.
            /// </summary>
            /// <param name="message">The warning message.</param>
            protected override void NotifyWarning(object message)
            {
                AddLogMessage(new Warning(_logSource, _logClass, message));
            }

            protected override void NotifyWarning(Exception cause, object message)
            {
                AddLogMessage(new Warning(cause, _logSource, _logClass, message));
            }

            /// <summary>
            /// Publishes the info message onto the LoggingBus.
            /// </summary>
            /// <param name="message">The info message.</param>
            protected override void NotifyInfo(object message)
            {
                AddLogMessage(new Info(_logSource, _logClass, message));
            }

            protected override void NotifyInfo(Exception cause, object message)
            {
                AddLogMessage(new Info(cause, _logSource, _logClass, message));
            }

            /// <summary>
            /// Publishes the debug message onto the LoggingBus.
            /// </summary>
            /// <param name="message">The debug message.</param>
            protected override void NotifyDebug(object message)
            {
                AddLogMessage(new Debug(_logSource, _logClass, message));
            }

            protected override void NotifyDebug(Exception cause, object message)
            {
                AddLogMessage(new Debug(cause, _logSource, _logClass, message));
            }
        }

        public const int NumOperations = 1_000_000;

        private BenchmarkLogAdapter _log;

        [IterationSetup]
        public void InitLogger()
        {
            _log = new BenchmarkLogAdapter(NumOperations);
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = NumOperations)]
        public LogEvent[] LogInfoNoParams()
        {
            for(var i = 0; i < NumOperations; i++)
                _log.Info("foo");

            return _log.AllLogs;
        }
        
        [Benchmark( OperationsPerInvoke = NumOperations)]
        public LogEvent[] LogInfo1Params()
        {
            for(var i = 0; i < NumOperations; i++)
                _log.Info("foo {0}", i);

            return _log.AllLogs;
        }
        
        [Benchmark(OperationsPerInvoke = NumOperations)]
        public LogEvent[] LogInfo2Params()
        {
            for(var i = 0; i < NumOperations; i++)
                _log.Info("foo {0} and {1}", i, i-1);

            return _log.AllLogs;
        }
        
        [Benchmark(OperationsPerInvoke = NumOperations)]
        public LogEvent[] LogInfoWithException()
        {
            var ex = new ApplicationException();
            for(var i = 0; i < NumOperations; i++)
                _log.Info(ex, "foo");

            return _log.AllLogs;
        }
        
        [Benchmark(OperationsPerInvoke = NumOperations)]
        public LogEvent[] LogInfoWithException1Params()
        {
            var ex = new ApplicationException();
            for(var i = 0; i < NumOperations; i++)
                _log.Info(ex, "foo {0}", i);

            return _log.AllLogs;
        }
        
        [Benchmark(OperationsPerInvoke = NumOperations)]
        public LogEvent[] LogInfoWithException2Params()
        {
            var ex = new ApplicationException();
            for(var i = 0; i < NumOperations; i++)
                _log.Info(ex, "foo {0} and {1}", i, i-1);

            return _log.AllLogs;
        }
    }
}