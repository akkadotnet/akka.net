﻿//-----------------------------------------------------------------------
// <copyright file="LoggingBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            
            private LogEvent CreateLogEvent(LogLevel logLevel, object message, Exception cause = null)
            {
                return logLevel switch
                {
                    LogLevel.DebugLevel => new Debug(cause, _logSource, _logClass, message),
                    LogLevel.InfoLevel => new Info(cause, _logSource, _logClass, message),
                    LogLevel.WarningLevel => new Warning(cause, _logSource, _logClass, message),
                    LogLevel.ErrorLevel => new Error(cause, _logSource, _logClass, message),
                    _ => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null)
                };
            }
            protected override void NotifyLog(LogLevel logLevel, object message, Exception cause = null)
            {
                AddLogMessage(CreateLogEvent(logLevel, message, cause));
            }

            public override bool IsErrorEnabled { get; } = true;

            private void AddLogMessage(LogEvent m)
            {
                AllLogs[CurrentLogs++] = m;
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
