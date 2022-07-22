//-----------------------------------------------------------------------
// <copyright file="LoggingBus.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        private sealed class LoggerStartInfo
        {
            private readonly string _name;
            
            public LoggerStartInfo(string loggerName, Type loggerType, IActorRef actorRef)
            {
                _name = $"{loggerName} [{loggerType.FullName}]";
                ActorRef = actorRef;
            }

            public IActorRef ActorRef { get; }
            
            public override string ToString() => _name;
        }
        
        private static readonly LogLevel[] AllLogLevels = Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>().ToArray();

        private static int _loggerId;
        private readonly List<IActorRef> _loggers = new List<IActorRef>();
        
        // Async load support
        private readonly Dictionary<Task, LoggerStartInfo> _startupState = new Dictionary<Task, LoggerStartInfo>();
        private readonly CancellationTokenSource _shutdownCts = new CancellationTokenSource();

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
            var loggerTypes = system.Settings.Loggers;
            var timeout = system.Settings.LoggerStartTimeout;
            var shouldRemoveStandardOutLogger = true;

            LogLevel = Logging.LogLevelFor(system.Settings.LogLevel);

            foreach (var strLoggerType in loggerTypes)
            {
                var loggerType = Type.GetType(strLoggerType);
                if (loggerType == null)
                {
                    throw new ConfigurationException($@"Logger specified in config cannot be found: ""{strLoggerType}""");
                }

                if (typeof(MinimalLogger).IsAssignableFrom(loggerType))
                {
                    shouldRemoveStandardOutLogger = false;
                    continue;
                }

                AddLogger(system, loggerType, logName);
            }

            if (!Task.WaitAll(_startupState.Keys.ToArray(), timeout))
            {
                foreach (var kvp in _startupState)
                {
                    if (!kvp.Key.IsCompleted)
                    {
                        Publish(new Warning(logName, GetType(),
                            $"Logger {kvp.Value} did not respond within {timeout} to InitializeLogger(bus), " +
                            $"{nameof(LoggingBus)} will try and wait until it is ready. Since it start up is delayed, " +
                            "this logger may not capture all startup events correctly."));
                    }
                }
            }

            if (system.Settings.DebugUnhandledMessage)
            {
                var forwarder = system.SystemActorOf(Props.Create(typeof(UnhandledMessageForwarder)), "UnhandledMessageForwarder");
                Subscribe(forwarder, typeof(UnhandledMessage));
            }

            if (shouldRemoveStandardOutLogger)
            {
                var stdOutLogger = system.Settings.StdoutLogger;
                Publish(new Debug(logName, GetType(), $"{Logging.SimpleName(stdOutLogger)} being removed"));
                Unsubscribe(stdOutLogger);
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
            if (!_loggers.Any(c => c is MinimalLogger))
            {
                SetUpStdoutLogger(system.Settings);
                Publish(new Debug(SimpleName(this), GetType(), $"Shutting down: {Logging.SimpleName(system.Settings.StdoutLogger)} started"));
            }

            // Cancel all pending logger initialization tasks
            _shutdownCts.Cancel(false);
            _shutdownCts.Dispose();
            
            // Stop all currently running loggers
            foreach (var logger in _loggers)
            {
                RemoveLogger(logger);
            }

            Publish(new Debug(SimpleName(this), GetType(), "All default loggers stopped"));
        }

        private void RemoveLogger(IActorRef logger)
        {
            if (!(logger is MinimalLogger))
            {
                Unsubscribe(logger);
                if (logger is IInternalActorRef internalActorRef)
                {
                    internalActorRef.Stop();
                }
            }
        }

        private void AddLogger(ActorSystemImpl system, Type loggerType, string loggingBusName)
        {
            var loggerName = CreateLoggerName(loggerType);
            var logger = system.SystemActorOf(Props.Create(loggerType).WithDispatcher(system.Settings.LoggersDispatcher), loggerName);
            var askTask = logger.Ask(new InitializeLogger(this), Timeout.InfiniteTimeSpan, _shutdownCts.Token);
            
            _startupState[askTask] = new LoggerStartInfo(loggerName, loggerType, logger);
            askTask.ContinueWith(t =>
                {
                    var info = _startupState[t];
                    _startupState.Remove(t);
                    
                    // _shutdownCts was cancelled while this logger is still loading
                    if (t.IsCanceled)
                    {
                        Publish(new Warning(loggingBusName, GetType(),
                            $"Logger {info} startup have been cancelled because of system shutdown. Stopping logger."));
                        RemoveLogger(info.ActorRef);
                        return;
                    }
                    
                    // Task ran to completion successfully
                    var response = t.Result;
                    if (!(response is LoggerInitialized))
                    {
                        // Malformed logger, logger did not send a proper ack.
                        Publish(new Error(null, loggingBusName, GetType(),
                            $"Logger {info} did not respond with {nameof(LoggerInitialized)}, sent instead {response.GetType()}. Stopping logger."));
                        RemoveLogger(info.ActorRef);
                        return;
                    }

                    // Logger initialized successfully
                    _loggers.Add(info.ActorRef);
                    SubscribeLogLevelAndAbove(LogLevel, info.ActorRef);
                    Publish(new Debug(loggingBusName, GetType(), $"Logger {info} started"));
                });
        }

        private string CreateLoggerName(Type actorClass)
        {
            var id = Interlocked.Increment(ref _loggerId);
            var name = "log" + id + "-" + SimpleName(actorClass);
            return name;
        }

        /// <summary>
        /// Starts the <see cref="MinimalLogger"/> logger.
        /// </summary>
        /// <param name="config">The configuration used to configure the <see cref="MinimalLogger"/>.</param>
        public void StartStdoutLogger(Settings config)
        {
            SetUpStdoutLogger(config);
            Publish(new Debug(SimpleName(this), GetType(), "StandardOutLogger started"));
        }

        private void SetUpStdoutLogger(Settings config)
        {
            var logLevel = Logging.LogLevelFor(config.StdoutLogLevel);
            SubscribeLogLevelAndAbove(logLevel, config.StdoutLogger);
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
                SubscribeLogLevelAndAbove(LogLevel, logger);

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

