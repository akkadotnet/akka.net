﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// This is a “marker” class which is inserted as originator class into
    /// <see cref="LogEvent"/> when the string representation was supplied directly.
    /// </summary>
    public class DummyClassForStringSources { }

    /// <summary>
    ///     Class Logging.
    /// </summary>
    public static class Logging
    {
        private const string Debug = "DEBUG";
        private const string Info = "INFO";
        private const string Warning = "WARNING";
        private const string Error = "ERROR";

        /// <summary>
        ///     The standard out logger
        /// </summary>
        public static readonly StandardOutLogger StandardOutLogger = new StandardOutLogger();

        /// <summary>
        ///     Classes for.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <returns>Type.</returns>
        /// <exception cref="System.ArgumentException">Unknown LogLevel;logLevel</exception>
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
                    throw new ArgumentException("Unknown LogLevel", "logLevel");
            }
        }

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
                default:
                    throw new ArgumentException("Unknown LogLevel", "logLevel");
            }
        }

        /// <summary>
        ///     Gets the logger.
        /// </summary>
        /// <param name="context">The cell.</param>
        /// <param name="logMessageFormatter">The log message formatter.</param>
        /// <returns>LoggingAdapter.</returns>
        public static LoggingAdapter GetLogger(IActorContext context, ILogMessageFormatter logMessageFormatter = null)
        {
            var logSource = context.Self.ToString();
            var logClass = context.Props.Type;

            return new BusLogging(context.System.EventStream, logSource, logClass, logMessageFormatter ?? new DefaultLogMessageFormatter());
        }

        /// <summary>
        ///     Gets the logger.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="logSourceObj">The log source object.</param>
        /// <param name="logMessageFormatter">The log message formatter.</param>
        /// <returns>LoggingAdapter.</returns>
        public static LoggingAdapter GetLogger(ActorSystem system, object logSourceObj, ILogMessageFormatter logMessageFormatter = null)
        {
            return GetLogger(system.EventStream, logSourceObj, logMessageFormatter);
        }

        public static LoggingAdapter GetLogger(LoggingBus loggingBus, object logSourceObj, ILogMessageFormatter logMessageFormatter = null)
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
        ///     Logs the level for.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <returns>LogLevel.</returns>
        /// <exception cref="System.ArgumentException">Unknown LogLevel;logLevel</exception>
        public static LogLevel LogLevelFor(string logLevel)
        {
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
                default:
                    throw new ArgumentException(string.Format("Unknown LogLevel: \"{0}\". Valid values are: \"{1}\", \"{2}\", \"{3}\", \"{4}\"", logLevel, Debug, Info, Warning, Error), logLevel);
            }
        }

        /// <summary>
        /// Given the type of <see cref="LogEvent"/> returns the corresponding <see cref="LogLevel"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>The <see cref="LogLevel"/> that corresponds to the specified type.</returns>
        /// <exception cref="System.ArgumentException">Thrown for unknown types, i.e. when <typeparamref name="T"/> is not
        /// <see cref="Event.Debug"/>, <see cref="Event.Info"/>, <see cref="Event.Warning"/> or<see cref="Event.Error"/></exception>
        public static LogLevel LogLevelFor<T>() where T:LogEvent
        {
            var type = typeof(T);
            if(type == typeof(Debug)) return LogLevel.DebugLevel;
            if(type == typeof(Info)) return LogLevel.InfoLevel;
            if(type == typeof(Warning)) return LogLevel.WarningLevel;
            if(type == typeof(Error)) return LogLevel.ErrorLevel;

            throw new ArgumentException(string.Format("Unknown LogEvent type: \"{0}\". Valid types are: \"{1}\", \"{2}\", \"{3}\", \"{4}\"", type.FullName, typeof(Debug).FullName, typeof(Info).FullName, typeof(Warning).FullName, typeof(Error).FullName));
            
        }
    }
}