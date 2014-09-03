using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor.Internals;

namespace Akka.Event
{
    /// <summary>
    ///     Class LoggingBus.
    /// </summary>
    public class LoggingBus : ActorEventBus<object, Type>
    {
        /// <summary>
        ///     All log levels
        /// </summary>
        private static readonly LogLevel[] _allLogLevels = Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>().ToArray();

        /// <summary>
        ///     The loggers
        /// </summary>
        private readonly List<ActorRef> _loggers = new List<ActorRef>();

        private LogLevel _logLevel;

        /// <summary>
        ///     Gets the log level.
        /// </summary>
        /// <value>The log level.</value>
        public LogLevel LogLevel { get { return _logLevel; }}

        /// <summary>
        ///     Determines whether [is sub classification] [the specified parent].
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="child">The child.</param>
        /// <returns><c>true</c> if [is sub classification] [the specified parent]; otherwise, <c>false</c>.</returns>
        protected override bool IsSubClassification(Type parent, Type child)
        {
            return parent.IsAssignableFrom(child);
        }

        /// <summary>
        ///     Publishes the specified event.
        /// </summary>
        /// <param name="event">The event.</param>
        /// <param name="subscriber">The subscriber.</param>
        protected override void Publish(object @event, ActorRef subscriber)
        {
            subscriber.Tell(@event);
        }

        /// <summary>
        ///     Classifies the specified event.
        /// </summary>
        /// <param name="event">The event.</param>
        /// <param name="classifier">The classifier.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        protected override bool Classify(object @event, Type classifier)
        {
            return classifier.IsAssignableFrom(GetClassifier(@event));
        }

        /// <summary>
        ///     Gets the classifier.
        /// </summary>
        /// <param name="event">The event.</param>
        /// <returns>Type.</returns>
        protected override Type GetClassifier(object @event)
        {
            return @event.GetType();
        }

        /// <summary>
        ///     Starts the default loggers.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <exception cref="System.Exception">Can not use logger of type: + loggerType</exception>
        public async void StartDefaultLoggers(ActorSystemImpl system)
        {
            //TODO: find out why we have logName and in AddLogger, "name"
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
                    //TODO: create real exceptions and refine error messages
                    throw new Exception("Logger specified in config cannot be found: \"" + strLoggerType+"\"");
                }
                if(loggerType == typeof(StandardOutLogger))
                {
                    shouldRemoveStandardOutLogger = false;
                    continue;
                }
                var addLoggerTask = AddLogger(system, loggerType, logLevel, logName);

                if(!addLoggerTask.Wait(timeout))
                {
                    Publish(new Warning(logName, GetType(),
                        "Logger " + logName + " did not respond within " + timeout + " to InitializeLogger(bus)"));
                }
                else
                {
                    var actorRef = addLoggerTask.Result;
                    _loggers.Add(actorRef);
                    SubscribeLogLevelAndAbove(logLevel, actorRef);
                    Publish(new Debug(logName, GetType(), "Logger " + actorRef.Path.Name + " started"));
                }
            }
            _logLevel = logLevel;
            if(shouldRemoveStandardOutLogger)
            {
                Publish(new Debug(logName, GetType(), "StandardOutLogger being removed"));
                Unsubscribe(Logging.StandardOutLogger);
            }
            Publish(new Debug(logName, GetType(), "Default Loggers started"));

        }

        /// <summary>
        ///     Adds the logger.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="actorClass">The actor class.</param>
        /// <param name="logLevel">The log level.</param>
        /// <param name="logName">Name of the log.</param>
        /// <returns>Task.</returns>
        private async Task<ActorRef> AddLogger(ActorSystemImpl system, Type actorClass, LogLevel logLevel, string logName)
        {
            //TODO: remove the newguid stuff
            string name = "log" + system.Name + "-" + SimpleName(actorClass);
            ActorRef actor;
            try
            {
                actor = system.SystemActorOf(Props.Create(actorClass), name);
            }
            catch
            {
                //HACK: the EventStreamSpec tries to start up loggers for a new EventBus
                //when doing so, this logger is already started and the name reserved.
                //we need to examine how this is dealt with in akka.
                name = name + Guid.NewGuid();
                actor = system.SystemActorOf(Props.Create(actorClass), name);
            }
            await actor.Ask(new InitializeLogger(this));
            return actor;
        }

        /// <summary>
        ///     Starts the stdout logger.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public void StartStdoutLogger(Settings config)
        {
            SetUpStdoutLogger(config);
            Publish(new Debug(SimpleName(this), GetType(), "StandardOutLogger started"));
        }

        /// <summary>
        ///     Sets up stdout logger.
        /// </summary>
        /// <param name="config">The configuration.</param>
        private void SetUpStdoutLogger(Settings config)
        {
            var logLevel = Logging.LogLevelFor(config.StdoutLogLevel);
            SubscribeLogLevelAndAbove(logLevel, Logging.StandardOutLogger);
        }

        /// <summary>
        ///     Sets the log level.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        public void SetLogLevel(LogLevel logLevel)
        {
            _logLevel = logLevel;
            foreach (ActorRef logger in _loggers)
            {
                //subscribe to given log level and above
                SubscribeLogLevelAndAbove(logLevel, logger);

                //unsubscribe to all levels below loglevel
                foreach (LogLevel level in _allLogLevels.Where(l => l < logLevel))
                {
                    Unsubscribe(logger, Logging.ClassFor(level));
                }
            }
        }

        private void SubscribeLogLevelAndAbove(LogLevel logLevel, ActorRef logger)
        {
            //subscribe to given log level and above
            foreach(LogLevel level in _allLogLevels.Where(l => l >= logLevel))
            {
                Subscribe(logger, Logging.ClassFor(level));
            }
        }
    }
}
