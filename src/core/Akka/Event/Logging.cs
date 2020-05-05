//-----------------------------------------------------------------------
// <copyright file="Logging.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Event
{
    /// <summary>
    /// This class represents a marker which is inserted as originator class into
    /// <see cref="LogEvent"/> when the string representation was supplied directly.
    /// </summary>
    public class DummyClassForStringSources { }

    /// <summary>
    /// This object holds predefined formatting rules for log sources.
    ///
    /// In case an <see cref="ActorSystem"/> is provided, the following apply:
    /// * <see cref="ActorBase"/> and <see cref="IActorRef"/> will be represented by their absolute path.
    /// * providing a <see cref="string"/> as source will append "(ActorSystem address)" and use the result.
    /// * providing a <see cref="Type"/> will extract its simple name,  append "(ActorSystem address)", and use the result.
    /// </summary>
    public struct LogSource
    {
        private LogSource(string source, Type type)
        {
            Source = source;
            Type = type;
        }

        public string Source { get; }

        public Type Type { get; }

        public static LogSource Create(object o)
        {
            switch (o)
            {
                case IActorContext ab:
                    return new LogSource(ab.Self.Path.ToString(), SourceType(o));
                case IActorRef actorRef:
                    return new LogSource(actorRef.Path.ToString(), SourceType(actorRef));
                case string str:
                    return new LogSource(str, SourceType(str));
                case System.Type t:
                    return new LogSource(Logging.SimpleName(t), t);
                default:
                    return new LogSource(Logging.SimpleName(o), SourceType(o));
            }
        }

        public static LogSource Create(object o, ActorSystem system)
        {
            switch (o)
            {
                case IActorContext ab:
                    return new LogSource(FromActor(ab, system), SourceType(o));
                case IActorRef actorRef:
                    return new LogSource(FromActorRef(actorRef, system), SourceType(actorRef));
                case string str:
                    return new LogSource(FromString(str, system), SourceType(str));
                case System.Type t:
                    return new LogSource(FromType(t, system), t);
                default:
                    return new LogSource(FromType(o.GetType(), system), SourceType(o));
            }
        }

        public static Type SourceType(object o)
        {
            switch (o)
            {
                case System.Type t:
                    return t;
                case IActorContext context:
                    return context.Props.Type;
                case IActorRef actorRef:
                    return actorRef.GetType();
                case string str:
                    return typeof(DummyClassForStringSources);
                default:
                    return o.GetType();
            }
        }

        public static string FromType(Type t, ActorSystem system)
        {
            return $"{Logging.SimpleName(t)} ({system})";
        }

        public static string FromString(string source, ActorSystem system)
        {
            return $"{source} ({system})";
        }

        public static string FromActor(IActorContext actor, ActorSystem system)
        {
            return FromActorRef(actor.Self, system);
        }

        public static string FromActorRef(IActorRef a, ActorSystem system)
        {
            try
            {
                return a.Path.ToStringWithAddress(system.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress);
            }
            catch // can fail if the ActorSystem (remoting) is not completely started yet
            {
                return a.Path.ToString();
            }
        }
    }

    /// <summary>
    /// This class provides the functionality for creating logger instances and helpers for converting to/from <see cref="LogLevel"/> values.
    /// </summary>
    public static class Logging
    {
        /// <summary>
        /// Returns a "safe" LogSource name for the provided object's type.
        /// </summary>
        /// <returns>The simple name of the given object's Type.</returns>
        public static string SimpleName(object o)
        {
            return SimpleName(o.GetType());
        }

        /// <summary>
        /// Returns a "safe" LogSource for the provided type.
        /// </summary>
        /// <returns>A usable simple LogSource name.</returns>
        public static string SimpleName(Type t)
        {
            var n = t.Name;
            return n;
        }

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
            var logSource = LogSource.Create(context, context.System);
            return new BusLogging(context.System.EventStream, logSource.Source, logSource.Type, logMessageFormatter ?? new DefaultLogMessageFormatter());
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
            var logSource = LogSource.Create(logSourceObj, system);
            return new BusLogging(system.EventStream, logSource.Source, logSource.Type, logMessageFormatter ?? new DefaultLogMessageFormatter());
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
            var logSource = LogSource.Create(logSourceObj);
            return new BusLogging(loggingBus, logSource.Source, logSource.Type, logMessageFormatter ?? new DefaultLogMessageFormatter());
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
        /// <exception cref="ArgumentException">The exception is thrown if the given <typeparamref name="T">log event</typeparamref> is unknown.</exception>
        /// <returns>The log level associated with the specified <see cref="LogEvent"/> type.</returns>
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
