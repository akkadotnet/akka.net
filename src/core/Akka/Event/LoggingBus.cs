//-----------------------------------------------------------------------
// <copyright file="LoggingBus.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
#if DNXCORE50
using System.Reflection;
#endif
using System.Threading;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;

namespace Akka.Event
{
    /// <summary>
    /// Represents a logging bus which subscribes loggers to the system LogEvents for the desired minimum level.
    /// </summary>
    public class LoggingBus : ActorEventBus<object, Type>
    {
        private static readonly LogLevel[] AllLogLevels = Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>().ToArray();

        private static int _loggerId;
        private readonly List<IActorRef> _loggers = new List<IActorRef>();

        /// <summary>
        /// Gets the minimum log level that this LoggingBus will subscribe to, any LogEvents with a log level below will not be subscribed to.
        /// </summary>
        /// <value>The log level.</value>
        public LogLevel LogLevel { get; private set; }

        /// <summary>
        /// Determines whether [is sub classification] [the specified parent].
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="child">The child.</param>
        /// <returns><c>true</c> if [is sub classification] [the specified parent]; otherwise, <c>false</c>.</returns>
        protected override bool IsSubClassification(Type parent, Type child)
        {
            return parent.IsAssignableFrom(child);
        }

        /// <summary>
        /// Publishes the specified event.
        /// </summary>
        /// <param name="event">The event.</param>
        /// <param name="subscriber">The subscriber.</param>
        protected override void Publish(object @event, IActorRef subscriber)
        {
            subscriber.Tell(@event);
        }

        /// <summary>
        /// Classifies the specified event.
        /// </summary>
        /// <param name="event">The event.</param>
        /// <param name="classifier">The classifier.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        protected override bool Classify(object @event, Type classifier)
        {
            return classifier.IsAssignableFrom(GetClassifier(@event));
        }

        /// <summary>
        /// Gets the classifier for the LogEvent.
        /// </summary>
        /// <param name="event">The event.</param>
        /// <returns>Type.</returns>
        protected override Type GetClassifier(object @event)
        {
            return @event.GetType();
        }

        /// <summary>
        /// Starts the default loggers.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <exception cref="System.Exception">Can not use logger of type: + loggerType</exception>
        internal void StartDefaultLoggers(ActorSystemImpl system)
        {
            var logName = SimpleName(this) + "(" + system.Name + ")";
            var logLevel = Logging.LogLevelFor(system.Settings.LogLevel);
            var loggerTypes = system.Settings.Loggers;
            var timeout = system.Settings.LoggerStartTimeout;
            var shouldRemoveStandardOutLogger = true;

            foreach (var strLoggerType in loggerTypes)
            {
                var loggerType = Type.GetType(strLoggerType);
                if (loggerType == null)
                {
                    throw new ConfigurationException("Logger specified in config cannot be found: \"" + strLoggerType + "\"");
                }

                if (loggerType == typeof(StandardOutLogger))
                {
                    shouldRemoveStandardOutLogger = false;
                    continue;
                }
                
                try
                {
                    AddLogger(system, loggerType, logLevel, logName, timeout);
                }
                catch (Exception e)
                {
                    throw new ConfigurationException(string.Format("Logger [{0}] specified in config cannot be loaded: {1}", strLoggerType, e),e);
                }
            }

            LogLevel = logLevel;

            if (system.Settings.DebugUnhandledMessage)
            {
                var forwarder = system.SystemActorOf(Props.Create(typeof(UnhandledMessageForwarder)), "UnhandledMessageForwarder");
                Subscribe(forwarder, typeof(UnhandledMessage));
            }

            if (shouldRemoveStandardOutLogger)
            {
                Publish(new Debug(logName, GetType(), "StandardOutLogger being removed"));
                Unsubscribe(Logging.StandardOutLogger);
            }

            Publish(new Debug(logName, GetType(), "Default Loggers started"));
        }

        internal void StopDefaultLoggers(ActorSystem system)
        {
            //TODO: Implement stopping loggers
        }

        private void AddLogger(ActorSystemImpl system, Type loggerType, LogLevel logLevel, string loggingBusName, TimeSpan timeout)
        {
            var loggerName = CreateLoggerName(loggerType);
            var logger = system.SystemActorOf(Props.Create(loggerType), loggerName);
            var askTask = logger.Ask(new InitializeLogger(this));

            if (!askTask.Wait(timeout))
            {
                Publish(new Warning(loggingBusName, GetType(),
                    string.Format("Logger {0} [{2}] did not respond within {1} to InitializeLogger(bus)", loggerName, timeout, loggerType.FullName)));
            }
            else
            {
                var response = askTask.Result;
                if (!(response is LoggerInitialized))
                {
                    throw new LoggerInitializationException(string.Format("Logger {0} [{2}] did not respond with LoggerInitialized, sent instead {1}", loggerName, response, loggerType.FullName));
                }

                _loggers.Add(logger);
                SubscribeLogLevelAndAbove(logLevel, logger);
                Publish(new Debug(loggingBusName, GetType(), string.Format("Logger {0} [{1}] started", loggerName, loggerType.Name)));
            }
        }

        private string CreateLoggerName(Type actorClass)
        {
            var id = Interlocked.Increment(ref _loggerId);
            var name = "log" + id + "-" + SimpleName(actorClass);
            return name;
        }

        /// <summary>
        /// Starts the StandardOutLogger logger.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public void StartStdoutLogger(Settings config)
        {
            SetUpStdoutLogger(config);
            Publish(new Debug(SimpleName(this), GetType(), "StandardOutLogger started"));
        }

        /// <summary>
        /// Sets up StandardOutLogger logger.
        /// </summary>
        /// <param name="config">The configuration.</param>
        private void SetUpStdoutLogger(Settings config)
        {
            var logLevel = Logging.LogLevelFor(config.StdoutLogLevel);
            SubscribeLogLevelAndAbove(logLevel, Logging.StandardOutLogger);
        }

        /// <summary>
        /// Sets the minimum log level for the LoggingBus, any LogEvents below this level will not be listened to.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        public void SetLogLevel(LogLevel logLevel)
        {
            LogLevel = logLevel;

            foreach (var logger in _loggers)
            {
                //subscribe to given log level and above
                SubscribeLogLevelAndAbove(logLevel, logger);

                //unsubscribe to all levels below loglevel
                foreach (var level in AllLogLevels.Where(l => l < logLevel))
                {
                    Unsubscribe(logger, level.ClassFor());
                }
            }
        }

        private void SubscribeLogLevelAndAbove(LogLevel logLevel, IActorRef logger)
        {
            //subscribe to given log level and above
            foreach (var level in AllLogLevels.Where(l => l >= logLevel))
            {
                Subscribe(logger, level.ClassFor());
            }
        }

        private class UnhandledMessageForwarder : ActorBase
        {
            protected override bool Receive(object message)
            {
                var msg = message as UnhandledMessage;
                if (msg == null) 
                    return false;

                Context.System.EventStream.Publish(ToDebug(msg));
                return true;
            }

            private static Debug ToDebug(UnhandledMessage message)
            {
                var msg = string.Format(
                    CultureInfo.InvariantCulture, "Unhandled message from {0} : {1}",
                    message.Sender.Path,
                    message.Message
                    );

                return new Debug(message.Recipient.Path.ToString(), message.Recipient.GetType(), msg);
            }
        }
    }
}

