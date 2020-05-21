//-----------------------------------------------------------------------
// <copyright file="LoggingBus.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;

namespace Akka.Event
{
    /// <summary>
    /// This class represents an event bus which subscribes loggers to system <see cref="LogEvent">LogEvents</see>.
    /// </summary>
    public class LoggingBus : ActorEventBus<object, Type>
    {
        private static readonly LogLevel[] AllLogLevels = Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>().ToArray();

        private static int _loggerId;
        private readonly List<IActorRef> _loggers = new List<IActorRef>();

        /// <summary>
        /// The minimum log level that this bus will subscribe to, any <see cref="LogEvent">LogEvents</see> with a log level below will not be subscribed to.
        /// </summary>
        public LogLevel LogLevel { get; private set; }

        /// <summary>
        /// Determines whether a specified classifier, <paramref name="child" />, is a subclass of another classifier, <paramref name="parent" />.
        /// </summary>
        /// <param name="parent">The potential parent of the classifier that is being checked.</param>
        /// <param name="child">The classifier that is being checked.</param>
        /// <returns><c>true</c> if the <paramref name="child" /> classifier is a subclass of <paramref name="parent" />; otherwise <c>false</c>.</returns>
        protected override bool IsSubClassification(Type parent, Type child)
        {
            return parent.IsAssignableFrom(child);
        }

        /// <summary>
        /// Publishes the specified event directly to the specified subscriber.
        /// </summary>
        /// <param name="event">The event that is being published.</param>
        /// <param name="subscriber">The subscriber that receives the event.</param>
        protected override void Publish(object @event, IActorRef subscriber)
        {
            subscriber.Tell(@event);
        }

        /// <summary>
        /// Classifies the specified event using the specified classifier.
        /// </summary>
        /// <param name="event">The event that is being classified.</param>
        /// <param name="classifier">The classifier used to classify the event.</param>
        /// <returns><c>true</c> if the classification succeeds; otherwise <c>false</c>.</returns>
        protected override bool Classify(object @event, Type classifier)
        {
            return classifier.IsAssignableFrom(GetClassifier(@event));
        }

        /// <summary>
        /// Retrieves the classifier used to classify the specified event.
        /// </summary>
        /// <param name="event">The event for which to retrieve the classifier.</param>
        /// <returns>The classifier used to classify the event.</returns>
        protected override Type GetClassifier(object @event)
        {
            return @event.GetType();
        }

        /// <summary>
        /// Starts the loggers defined in the system configuration.
        /// </summary>
        /// <param name="system">The system that the loggers need to start monitoring.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown if the logger specified in the <paramref name="system"/> configuration could not be found or loaded.
        /// </exception>
        /// <exception cref="LoggerInitializationException">
        /// This exception is thrown if the logger doesn't respond with a <see cref="LoggerInitialized"/> message when initialized.
        /// </exception>
        internal void StartDefaultLoggers(ActorSystemImpl system)
        {
            var logName = SimpleName(this) + "(" + system.Name + ")";
            var logLevel = Logging.LogLevelFor(system.Settings.LogLevel);
            var loggerTypes = system.Settings.Loggers;
            var timeout = system.Settings.LoggerStartTimeout;
            var asyncStart = system.Settings.LoggerAsyncStart;
            var shouldRemoveStandardOutLogger = true;

            foreach (var strLoggerType in loggerTypes)
            {
                var loggerType = Type.GetType(strLoggerType);
                if (loggerType == null)
                {
                    throw new ConfigurationException($@"Logger specified in config cannot be found: ""{strLoggerType}""");
                }

                if (loggerType == typeof(StandardOutLogger))
                {
                    shouldRemoveStandardOutLogger = false;
                    continue;
                }

                if (asyncStart)
                {
                    // Not awaiting for result, and not depending on current thread context
                    Task.Run(() => AddLogger(system, loggerType, logLevel, logName, timeout))
                        .ContinueWith(t =>
                        {
                            if (t.Exception != null)
                            {
                                Console.WriteLine($"Logger [{strLoggerType}] specified in config cannot be loaded: {t.Exception}");
                            }
                        });
                }
                else
                {
                    try
                    {
                        AddLogger(system, loggerType, logLevel, logName, timeout);
                    }
                    catch (Exception ex)
                    {
                        throw new ConfigurationException($"Logger [{strLoggerType}] specified in config cannot be loaded: {ex}", ex);
                    }
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

        /// <summary>
        /// Stops the loggers defined in the system configuration.
        /// </summary>
        /// <param name="system">The system that the loggers need to stop monitoring.</param>
        internal void StopDefaultLoggers(ActorSystem system)
        {
            var level = LogLevel; // volatile access before reading loggers
            if (!_loggers.Any(c => c is StandardOutLogger))
            {
                SetUpStdoutLogger(system.Settings);
                Publish(new Debug(SimpleName(this), GetType(), "Shutting down: StandardOutLogger started"));
            }

            foreach (var logger in _loggers)
            {
                if (!(logger is StandardOutLogger))
                {
                    Unsubscribe(logger);
                    var internalActorRef = logger as IInternalActorRef;
                    if (internalActorRef != null)
                    {
                        internalActorRef.Stop();
                    }
                }
            }

            Publish(new Debug(SimpleName(this), GetType(), "All default loggers stopped"));
        }

        private void AddLogger(ActorSystemImpl system, Type loggerType, LogLevel logLevel, string loggingBusName, TimeSpan timeout)
        {
            var loggerName = CreateLoggerName(loggerType);
            var logger = system.SystemActorOf(Props.Create(loggerType).WithDispatcher(system.Settings.LoggersDispatcher), loggerName);
            var askTask = logger.Ask(new InitializeLogger(this), timeout);

            object response = null;
            try
            {
                response = askTask.Result;
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is AskTimeoutException)
            {
                Publish(new Warning(loggingBusName, GetType(),
                     string.Format("Logger {0} [{2}] did not respond within {1} to InitializeLogger(bus)", loggerName, timeout, loggerType.FullName)));
            }
                
            if (!(response is LoggerInitialized))
                throw new LoggerInitializationException($"Logger {loggerName} [{loggerType.FullName}] did not respond with LoggerInitialized, sent instead {response}");
            

            _loggers.Add(logger);
            SubscribeLogLevelAndAbove(logLevel, logger);
            Publish(new Debug(loggingBusName, GetType(), $"Logger {loggerName} [{loggerType.Name}] started"));
            
        }

        private string CreateLoggerName(Type actorClass)
        {
            var id = Interlocked.Increment(ref _loggerId);
            var name = "log" + id + "-" + SimpleName(actorClass);
            return name;
        }

        /// <summary>
        /// Starts the <see cref="StandardOutLogger"/> logger.
        /// </summary>
        /// <param name="config">The configuration used to configure the <see cref="StandardOutLogger"/>.</param>
        public void StartStdoutLogger(Settings config)
        {
            SetUpStdoutLogger(config);
            Publish(new Debug(SimpleName(this), GetType(), "StandardOutLogger started"));
        }

        private void SetUpStdoutLogger(Settings config)
        {
            var logLevel = Logging.LogLevelFor(config.StdoutLogLevel);
            SubscribeLogLevelAndAbove(logLevel, Logging.StandardOutLogger);
        }

        /// <summary>
        /// Sets the minimum log level for this bus, any <see cref="LogEvent">LogEvents</see> below this level are ignored.
        /// </summary>
        /// <param name="logLevel">The new log level in which to listen.</param>
        public void SetLogLevel(LogLevel logLevel)
        {
            LogLevel = logLevel;

            foreach (var logger in _loggers)
            {
                //subscribe to given log level and above
                SubscribeLogLevelAndAbove(logLevel, logger);

                //unsubscribe to all levels below log level
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
                if (!(message is UnhandledMessage msg)) 
                    return false;

                Context.System.EventStream.Publish(ToDebug(msg));
                return true;
            }

            private static Debug ToDebug(UnhandledMessage message)
            {
                // avoid NREs when we have ActorRefs.NoSender
                var sender = Equals(message.Sender, ActorRefs.NoSender) ? "NoSender" : message.Sender.Path.ToString();

                var msg = string.Format(
                    CultureInfo.InvariantCulture, "Unhandled message from {0} : {1}",
                    sender,
                    message.Message
                    );

                return new Debug(message.Recipient.Path.ToString(), message.Recipient.GetType(), msg);
            }
        }
    }
}

