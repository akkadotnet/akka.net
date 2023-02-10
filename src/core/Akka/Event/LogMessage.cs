﻿//-----------------------------------------------------------------------
// <copyright file="LogMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;

namespace Akka.Event
{
    public interface ILogContents
    {
        public LogLevel LogLevel { get; }

        public Exception Exception { get; }
        
        /// <summary>
        /// The timestamp that this event occurred.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// The thread where this event occurred.
        /// </summary>
        public int ThreadId { get; }

        /// <summary>
        /// The source that generated this event.
        /// </summary>
        public LogSource LogSource { get; }

        
        /// <summary>
        /// Renders an underlying <see cref="LogEntry{TState}"/> in a non-generic fashion.
        /// </summary>
        /// <returns></returns>
        string Format();
    }
    
    /// <summary>
    /// Raw data payload produced from any logging call.
    /// </summary>
    /// <typeparam name="TState">The state for the specified log message.</typeparam>
    public readonly struct LogEntry<TState> : ILogContents
    {
        public LogEntry(LogLevel logLevel, TState message, Func<TState, Exception, string> formatter, 
            LogSource source, int threadId, DateTime timestamp, Exception exception = null)
        {
            LogLevel = logLevel;
            Message = message;
            Formatter = formatter;
            LogSource = source;
            ThreadId = threadId;
            Timestamp = timestamp;
            Exception = exception;
        }

        public LogLevel LogLevel { get; }

        public TState Message {get;}
        
        public Exception Exception { get; }
        
        /// <summary>
        /// The timestamp that this event occurred.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// The thread where this event occurred.
        /// </summary>
        public int ThreadId { get; }

        /// <summary>
        /// The source that generated this event.
        /// </summary>
        public LogSource LogSource { get; }

        public Func<TState, Exception, string> Formatter { get; }

        public string Format()
        {
            return Formatter(Message, Exception);
        }
    }

    internal static class LogEntryExtensions
    {
        public static LogEntry<string> CreateLogEntryFromString(LogLevel level, string source, Type sourceType,
            string msg, Exception ex = null)
        {
            return new LogEntry<string>(level, msg, LoggingAdapterExtensions.StringOnlyFormatter, new LogSource(source, sourceType), Thread.CurrentThread.ManagedThreadId, DateTime.UtcNow, ex);
        }
    }
    

    /// <summary>
    /// Used for the original <c>params object[]</c> methods.
    /// </summary>
    internal readonly struct UntypedLogEntryState
    {
        public UntypedLogEntryState(string format, params object[] args)
        {
            Format = format;
            Args = args;
        }

        /// <summary>
        /// Gets the format string of this log message.
        /// </summary>
        public string Format { get; }

        /// <summary>
        /// Gets the format args of this log message.
        /// </summary>
        public object[] Args { get; }

    }
}

