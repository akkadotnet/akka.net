//-----------------------------------------------------------------------
// <copyright file="Logging.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// This class represents a marker which is inserted as originator class into
    /// <see cref="LogEvent"/> when the string representation was supplied directly.
    /// </summary>
    public class DummyClassForStringSources { }

    /// <summary>
    /// This class provides the functionality for creating logger instances and helpers for converting to/from <see cref="LogLevel"/> values.
    /// </summary>
    public static class Logging
    {
        private const string Debug = "DEBUG";
        private const string Info = "INFO";
        private const string Warning = "WARNING";
        private const string Error = "ERROR";
        private const string Off = "OFF";
        private const LogLevel OffLogLevel = (LogLevel) int.MaxValue;

        /// <summary>
        /// Returns a singleton instance of the standard out logger.
        /// </summary>
        public static readonly StandardOutLogger StandardOutLogger = new StandardOutLogger();

        /// <summary>
        /// Retrieves the log event class associated with the specified log level.
        /// </summary>
        /// <param name="logLevel">The log level used to lookup the associated class.</param>
        /// <exception cref="ArgumentException">The exception is thrown if the given <paramref name="logLevel"/> is unknown.</exception>
        /// <returns>The log event class associated with the specified log level.</returns>
        public static Type ClassFor(this LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.DebugLevel:
                    return typeof (Debug);
                case LogLevel.InfoLevel:
                    return typeof (Info);
                case LogLevel.WarningLevel:
                    return typeof (Warning);
                case LogLevel.ErrorLevel:
                    return typeof (Error);
                default:
                    throw new ArgumentException("Unknown LogLevel", nameof(logLevel));
            }
        }

        /// <summary>
        /// Retrieves the log event class name associated with the specified log level.
        /// </summary>
        /// <param name="logLevel">The log level used to lookup the associated class.</param>
        /// <exception cref="ArgumentException">The exception is thrown if the given <paramref name="logLevel"/> is unknown.</exception>
        /// <returns>The log event class name associated with the specified log level.</returns>
        public static string StringFor(this LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.DebugLevel:
                    return Debug;
                case LogLevel.InfoLevel:
                    return Info;
                case LogLevel.WarningLevel:
                    return Warning;
                case LogLevel.ErrorLevel:
                    return Error;
                case OffLogLevel:
                    return Off;
                default:
                    throw new ArgumentException("Unknown LogLevel", nameof(logLevel));
            }
        }

        /// <summary>
        /// Creates a new logging adapter using the specified context's event stream.
        /// </summary>
        /// <param name="context">The context used to configure the logging adapter.</param>
        /// <param name="logMessageFormatter">The formatter used to format log messages.</param>
        /// <returns>The newly created logging adapter.</returns>
        public static ILoggingAdapter GetLogger(this IActorContext context, ILogMessageFormatter logMessageFormatter = null)
        {
            var logSource = context.Self.ToString();
            var logClass = context.Props.Type;

            return new BusLogging(context.System.EventStream, logSource, logClass, logMessageFormatter ?? new DefaultLogMessageFormatter());
        }

        /// <summary>
        /// Creates a new logging adapter using the specified system's event stream.
        /// </summary>
        /// <param name="system">The system used to configure the logging adapter.</param>
        /// <param name="logSourceObj">The source that produces the log events.</param>
        /// <param name="logMessageFormatter">The formatter used to format log messages.</param>
        /// <returns>The newly created logging adapter.</returns>
        public static ILoggingAdapter GetLogger(ActorSystem system, object logSourceObj, ILogMessageFormatter logMessageFormatter = null)
        {
            return GetLogger(system.EventStream, logSourceObj, logMessageFormatter);
        }

        /// <summary>
        /// Creates a new logging adapter that writes to the specified logging bus.
        /// </summary>
        /// <param name="loggingBus">The bus on which this logger writes.</param>
        /// <param name="logSourceObj">The source that produces the log events.</param>
        /// <param name="logMessageFormatter">The formatter used to format log messages.</param>
        /// <returns>The newly created logging adapter.</returns>
        public static ILoggingAdapter GetLogger(LoggingBus loggingBus, object logSourceObj, ILogMessageFormatter logMessageFormatter = null)
        {
            //TODO: refine this
            string logSource;
            Type logClass;
            if(logSourceObj is string)
            {
                logSource = (string) logSourceObj;
                logClass = typeof(DummyClassForStringSources);
            }
            else
            {
                logSource = logSourceObj.ToString();
                if(logSourceObj is Type)
                    logClass = (Type) logSourceObj;
                else
                    logClass = logSourceObj.GetType();
            }
            return new BusLogging(loggingBus, logSource, logClass, logMessageFormatter ?? new DefaultLogMessageFormatter());
        }

        /// <summary>
        /// Retrieves the log level from the specified string.
        /// </summary>
        /// <param name="logLevel">The string representation of the log level to lookup.</param>
        /// <exception cref="ArgumentException">The exception is thrown if the given <paramref name="logLevel"/> is unknown.</exception>
        /// <returns>The log level that matches the specified string.</returns>
        public static LogLevel LogLevelFor(string logLevel)
        {
            if (!string.IsNullOrEmpty(logLevel))
            {
                logLevel = logLevel.ToUpper();
            }

            switch (logLevel)
            {
                case Debug:
                    return LogLevel.DebugLevel;
                case Info:
                    return LogLevel.InfoLevel;
                case Warning:
                    return LogLevel.WarningLevel;
                case Error:
                    return LogLevel.ErrorLevel;
                case Off:
                    return OffLogLevel;
                default:
                    throw new ArgumentException($@"Unknown LogLevel: ""{logLevel}"". Valid values are: ""{Debug}"", ""{Info}"", ""{Warning}"", ""{Error}""", nameof(logLevel));
            }
        }

        /// <summary>
        /// Retrieves the log level associated with the specified <typeparamref name="T">log event</typeparamref>.
        /// </summary>
        /// <typeparam name="T">The type of the log event.</typeparam>
        /// <exception cref="ArgumentException">The exception is thrown if the given <paramref name="logLevel"/> is unknown.</exception>
        /// <returns>The log level associated with the specified type.</returns>
        public static LogLevel LogLevelFor<T>() where T:LogEvent
        {
            var type = typeof(T);
            if(type == typeof(Debug)) return LogLevel.DebugLevel;
            if(type == typeof(Info)) return LogLevel.InfoLevel;
            if(type == typeof(Warning)) return LogLevel.WarningLevel;
            if(type == typeof(Error)) return LogLevel.ErrorLevel;

            throw new ArgumentException($@"Unknown LogEvent type: ""{type.FullName}"". Valid types are: ""{typeof(Debug).FullName}"", ""{typeof(Info).FullName}"", ""{typeof(Warning).FullName}"", ""{typeof(Error).FullName}""");
            
        }
    }
}
