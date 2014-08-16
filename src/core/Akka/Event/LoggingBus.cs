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
        private static readonly LogLevel[] allLogLevels = Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>().ToArray();

        /// <summary>
        ///     The loggers
        /// </summary>
        private readonly List<ActorRef> loggers = new List<ActorRef>();

        /// <summary>
        ///     Gets the log level.
        /// </summary>
        /// <value>The log level.</value>
        public LogLevel LogLevel { get; private set; }

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
            string logName = SimpleName(this) + "(" + system.Name + ")";
            LogLevel logLevel = Logging.LogLevelFor(system.Settings.LogLevel);
            IList<string> loggerTypes = system.Settings.Loggers;
            foreach (string loggerType in loggerTypes)
            {
                Type actorClass = Type.GetType(loggerType);
                if (actorClass == null)
                {
                    //TODO: create real exceptions and refine error messages
                    throw new Exception("Can not use logger of type:" + loggerType);
                }
                TimeSpan timeout = system.Settings.LoggerStartTimeout;
                Task task = AddLogger(system, actorClass, logLevel, logName);
                if (await Task.WhenAny(task, Task.Delay(timeout)) == task)
                {
                }
                else
                {
                    Publish(new Warning(logName, GetType(),
                        "Logger " + logName + " did not respond within " + timeout + " to InitializeLogger(bus)"));
                }
            }
            Publish(new Debug(logName, GetType(), "Default Loggers started"));
            SetLogLevel(logLevel);
        }

        /// <summary>
        ///     Adds the logger.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="actorClass">The actor class.</param>
        /// <param name="logLevel">The log level.</param>
        /// <param name="logName">Name of the log.</param>
        /// <returns>Task.</returns>
        private async Task AddLogger(ActorSystemImpl system, Type actorClass, LogLevel logLevel, string logName)
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
            loggers.Add(actor);
            await actor.Ask(new InitializeLogger(this));
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
            LogLevel logLevel = Logging.LogLevelFor(config.StdoutLogLevel);
            foreach (LogLevel level in allLogLevels.Where(l => l >= logLevel))
            {
                Subscribe(Logging.StandardOutLogger, Logging.ClassFor(level));
            }
        }

        /// <summary>
        ///     Sets the log level.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        public void SetLogLevel(LogLevel logLevel)
        {
            LogLevel = LogLevel;
            foreach (ActorRef logger in loggers)
            {
                //subscribe to given log level and above
                foreach (LogLevel level in allLogLevels.Where(l => l >= logLevel))
                {
                    Subscribe(logger, Logging.ClassFor(level));
                }
                //unsubscribe to all levels below loglevel
                foreach (LogLevel level in allLogLevels.Where(l => l < logLevel))
                {
                    Unsubscribe(logger, Logging.ClassFor(level));
                }
            }
        }
    }
}
