using System;
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
        public static Type ClassFor(LogLevel logLevel)
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

        /// <summary>
        ///     Gets the logger.
        /// </summary>
        /// <param name="cell">The cell.</param>
        /// <returns>LoggingAdapter.</returns>
        public static LoggingAdapter GetLogger(IActorContext cell)
        {
            string logSource = cell.Self.ToString();
            Type logClass = cell.Props.Type;

            return new BusLogging(cell.System.EventStream, logSource, logClass);
        }

        /// <summary>
        ///     Gets the logger.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="logSourceObj">The log source object.</param>
        /// <returns>LoggingAdapter.</returns>
        public static LoggingAdapter GetLogger(ActorSystem system, object logSourceObj)
        {
            return GetLogger(system.EventStream, logSourceObj);
        }

        public static LoggingAdapter GetLogger(LoggingBus loggingBus, object logSourceObj)
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
            return new BusLogging(loggingBus, logSource, logClass);
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
                case "DEBUG":
                    return LogLevel.DebugLevel;
                case "INFO":
                    return LogLevel.InfoLevel;
                case "WARNING":
                    return LogLevel.WarningLevel;
                case "ERROR":
                    return LogLevel.ErrorLevel;
                default:
                    throw new ArgumentException("Unknown LogLevel", logLevel);
            }
        }
    }
}