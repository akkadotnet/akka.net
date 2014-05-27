using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    ///     Class StandardOutLogger.
    /// </summary>
    public class StandardOutLogger : MinimalActorRef
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="StandardOutLogger" /> class.
        /// </summary>
        public StandardOutLogger()
        {
            Path = new RootActorPath(Address.AllSystems, "/StandardOutLogger");
        }

        /// <summary>
        ///     Gets the provider.
        /// </summary>
        /// <value>The provider.</value>
        /// <exception cref="System.Exception">StandardOutLogged does not provide</exception>
        public override ActorRefProvider Provider
        {
            get { throw new Exception("StandardOutLogged does not provide"); }
        }

        /// <summary>
        ///     Tells the internal.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <exception cref="System.ArgumentNullException">message</exception>
        protected override void TellInternal(object message, ActorRef sender)
        {
            if (message == null)
                throw new ArgumentNullException("message");
            Console.WriteLine(message);
        }
    }

    /// <summary>
    ///     Class LoggingBus.
    /// </summary>
    public class LoggingBus : ActorEventBus<object, Type>
    {
        /// <summary>
        ///     All log levels
        /// </summary>
        private static readonly LogLevel[] allLogLevels = Enum.GetValues(typeof (LogLevel)).Cast<LogLevel>().ToArray();

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
        public async void StartDefaultLoggers(ActorSystem system)
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
        private async Task AddLogger(ActorSystem system, Type actorClass, LogLevel logLevel, string logName)
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

    /// <summary>
    ///     Enum LogLevel
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        ///     The debug level
        /// </summary>
        DebugLevel,

        /// <summary>
        ///     The information level
        /// </summary>
        InfoLevel,

        /// <summary>
        ///     The warning level
        /// </summary>
        WarningLevel,

        /// <summary>
        ///     The error level
        /// </summary>
        ErrorLevel,
    }

    /// <summary>
    ///     Class LogEvent.
    /// </summary>
    public abstract class LogEvent : NoSerializationVerificationNeeded
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="LogEvent" /> class.
        /// </summary>
        public LogEvent()
        {
            Timestamp = DateTime.Now;
            Thread = Thread.CurrentThread;
        }

        /// <summary>
        ///     Gets the timestamp.
        /// </summary>
        /// <value>The timestamp.</value>
        public DateTime Timestamp { get; private set; }

        /// <summary>
        ///     Gets the thread.
        /// </summary>
        /// <value>The thread.</value>
        public Thread Thread { get; private set; }

        /// <summary>
        ///     Gets or sets the log source.
        /// </summary>
        /// <value>The log source.</value>
        public string LogSource { get; protected set; }

        /// <summary>
        ///     Gets or sets the log class.
        /// </summary>
        /// <value>The log class.</value>
        public Type LogClass { get; protected set; }

        /// <summary>
        ///     Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; protected set; }

        /// <summary>
        ///     Logs the level.
        /// </summary>
        /// <returns>LogLevel.</returns>
        public abstract LogLevel LogLevel();

        /// <summary>
        ///     Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString()
        {
            return string.Format("[{0}][{1}][Thread {2}][{3}] {4}", LogLevel().ToString().Replace("Level", "").ToUpperInvariant(), Timestamp, Thread.ManagedThreadId.ToString().PadLeft(4,'0'), LogSource, Message);
        }
    }

    /// <summary>
    ///     Class Info.
    /// </summary>
    public class Info : LogEvent
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Info" /> class.
        /// </summary>
        /// <param name="logSource">The log source.</param>
        /// <param name="logClass">The log class.</param>
        /// <param name="message">The message.</param>
        public Info(string logSource, Type logClass, object message)
        {
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        /// <summary>
        ///     Logs the level.
        /// </summary>
        /// <returns>LogLevel.</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.InfoLevel;
        }
    }

    /// <summary>
    ///     Class Debug.
    /// </summary>
    public class Debug : LogEvent
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Debug" /> class.
        /// </summary>
        /// <param name="logSource">The log source.</param>
        /// <param name="logClass">The log class.</param>
        /// <param name="message">The message.</param>
        public Debug(string logSource, Type logClass, object message)
        {
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        /// <summary>
        ///     Logs the level.
        /// </summary>
        /// <returns>LogLevel.</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.DebugLevel;
        }
    }

    /// <summary>
    ///     Class Warning.
    /// </summary>
    public class Warning : LogEvent
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Warning" /> class.
        /// </summary>
        /// <param name="logSource">The log source.</param>
        /// <param name="logClass">The log class.</param>
        /// <param name="message">The message.</param>
        public Warning(string logSource, Type logClass, object message)
        {
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        /// <summary>
        ///     Logs the level.
        /// </summary>
        /// <returns>LogLevel.</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.WarningLevel;
        }
    }

    /// <summary>
    ///     Class Error.
    /// </summary>
    public class Error : LogEvent
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Error" /> class.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="logSource">The log source.</param>
        /// <param name="logClass">The log class.</param>
        /// <param name="message">The message.</param>
        public Error(Exception cause, string logSource, Type logClass, object message)
        {
            Cause = cause;
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        /// <summary>
        ///     Gets the cause.
        /// </summary>
        /// <value>The cause.</value>
        public Exception Cause { get; private set; }

        /// <summary>
        ///     Logs the level.
        /// </summary>
        /// <returns>LogLevel.</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.ErrorLevel;
        }

        /// <summary>
        /// Modifies the <see cref="LogEvent"/> printable error stream to also include
        /// the details of the <see cref="Cause"/> object itself.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var errorStr = string.Format("[{0}][{1}][Thread {2}][{3}] {4}", LogLevel().ToString().Replace("Level", "").ToUpperInvariant(), Timestamp, Thread.ManagedThreadId.ToString().PadLeft(4,'0'), LogSource, Message);
            errorStr += Environment.NewLine + string.Format("Cause: {0}", Cause != null ? Cause.Message : "Unknown");
            if (Cause != null)
                errorStr += Environment.NewLine + string.Format("StackTrace: {0}", Cause.StackTrace);
            return errorStr;
        }
    }

    /// <summary>
    ///     Class UnhandledMessage.
    /// </summary>
    public class UnhandledMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="UnhandledMessage" /> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <param name="recipient">The recipient.</param>
        internal UnhandledMessage(object message, ActorRef sender, ActorRef recipient)
        {
            Message = message;
            Sender = sender;
            Recipient = recipient;
        }

        /// <summary>
        ///     Gets the message.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; private set; }

        /// <summary>
        ///     Gets the sender.
        /// </summary>
        /// <value>The sender.</value>
        public ActorRef Sender { get; private set; }

        /// <summary>
        ///     Gets the recipient.
        /// </summary>
        /// <value>The recipient.</value>
        public ActorRef Recipient { get; private set; }
    }

    /// <summary>
    ///     Class InitializeLogger.
    /// </summary> 
    public class InitializeLogger : NoSerializationVerificationNeeded
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="InitializeLogger" /> class.
        /// </summary>
        /// <param name="loggingBus">The logging bus.</param>
        public InitializeLogger(LoggingBus loggingBus)
        {
            LoggingBus = loggingBus;
        }

        /// <summary>
        ///     Gets the logging bus.
        /// </summary>
        /// <value>The logging bus.</value>
        public LoggingBus LoggingBus { get; private set; }
    }

    /// <summary>
    ///     Class LoggerInitialized.
    /// </summary>
    public class LoggerInitialized : NoSerializationVerificationNeeded
    {
    }

    /// <summary>
    ///     Class DefaultLogger.
    /// </summary>
    public class DefaultLogger : UntypedActor
    {
        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void OnReceive(object message)
        {
            message
                .Match()
                .With<InitializeLogger>(m => Sender.Tell(new LoggerInitialized()))
                .With<LogEvent>(m =>
                    Console.WriteLine(m))
                .Default(Unhandled);
        }
    }

    /// <summary>
    ///     Class LoggingAdapter.
    /// </summary>
    public abstract class LoggingAdapter
    {
        /// <summary>
        ///     The is debug enabled
        /// </summary>
        protected bool isDebugEnabled;

        /// <summary>
        ///     The is error enabled
        /// </summary>
        protected bool isErrorEnabled;

        /// <summary>
        ///     The is information enabled
        /// </summary>
        protected bool isInfoEnabled;

        /// <summary>
        ///     The is warning enabled
        /// </summary>
        protected bool isWarningEnabled;

        /// <summary>
        ///     Notifies the error.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void NotifyError(string message);

        /// <summary>
        ///     Notifies the error.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="message">The message.</param>
        protected abstract void NotifyError(Exception cause, string message);

        /// <summary>
        ///     Notifies the warning.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void NotifyWarning(string message);

        /// <summary>
        ///     Notifies the information.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void NotifyInfo(string message);

        /// <summary>
        ///     Notifies the debug.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void NotifyDebug(string message);

        /// <summary>
        ///     Determines whether the specified log level is enabled.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <returns><c>true</c> if the specified log level is enabled; otherwise, <c>false</c>.</returns>
        /// <exception cref="System.NotSupportedException">Unknown LogLevel  + logLevel</exception>
        protected bool IsEnabled(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.DebugLevel:
                    return isDebugEnabled;
                case LogLevel.InfoLevel:
                    return isInfoEnabled;
                case LogLevel.WarningLevel:
                    return isWarningEnabled;
                case LogLevel.ErrorLevel:
                    return isErrorEnabled;
                default:
                    throw new NotSupportedException("Unknown LogLevel " + logLevel);
            }
        }

        /// <summary>
        ///     Notifies the log.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="message">The message.</param>
        /// <exception cref="System.NotSupportedException">Unknown LogLevel  + logLevel</exception>
        protected void NotifyLog(LogLevel logLevel, string message)
        {
            switch (logLevel)
            {
                case LogLevel.DebugLevel:
                    if (isDebugEnabled) NotifyDebug(message);
                    break;
                case LogLevel.InfoLevel:
                    if (isInfoEnabled) NotifyInfo(message);
                    break;
                case LogLevel.WarningLevel:
                    if (isWarningEnabled) NotifyWarning(message);
                    break;
                case LogLevel.ErrorLevel:
                    if (isErrorEnabled) NotifyError(message);
                    break;
                default:
                    throw new NotSupportedException("Unknown LogLevel " + logLevel);
            }
        }

        /// <summary>
        ///     Debugs the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Debug(string message)
        {
            if (isDebugEnabled)
                NotifyDebug(message);
        }

        /// <summary>
        ///     Warns the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Warn(string message)
        {
            if (isWarningEnabled)
                NotifyWarning(message);
        }

        /// <summary>
        ///     Errors the specified cause.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="message">The message.</param>
        public void Error(Exception cause, string message)
        {
            if (isErrorEnabled)
                NotifyError(cause, message);
        }

        /// <summary>
        ///     Errors the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Error(string message)
        {
            if (isErrorEnabled)
                NotifyError(message);
        }

        /// <summary>
        ///     Informations the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Info(string message)
        {
            if (isInfoEnabled)
                NotifyInfo(message);
        }


        /// <summary>
        ///     Debugs the specified format.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Debug(string format, params object[] args)
        {
            if (isDebugEnabled)
                NotifyDebug(string.Format(format, args));
        }

        /// <summary>
        ///     Warns the specified format.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Warn(string format, params object[] args)
        {
            if (isWarningEnabled)
                NotifyWarning(string.Format(format, args));
        }

        /// <summary>
        ///     Errors the specified cause.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Error(Exception cause, string format, params object[] args)
        {
            if (isErrorEnabled)
                NotifyError(cause, string.Format(format, args));
        }

        /// <summary>
        ///     Errors the specified format.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Error(string format, params object[] args)
        {
            if (isErrorEnabled)
                NotifyError(string.Format(format, args));
        }

        /// <summary>
        ///     Informations the specified format.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Info(string format, params object[] args)
        {
            if (isInfoEnabled)
                NotifyInfo(string.Format(format, args));
        }

        /// <summary>
        ///     Logs the specified log level.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="message">The message.</param>
        public void Log(LogLevel logLevel, string message)
        {
            NotifyLog(logLevel, message);
        }

        /// <summary>
        ///     Logs the specified log level.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        public void Log(LogLevel logLevel, string format, params object[] args)
        {
            NotifyLog(logLevel, string.Format(format, args));
        }
    }

    /// <summary>
    ///     Class BusLogging.
    /// </summary>
    public class BusLogging : LoggingAdapter
    {
        /// <summary>
        ///     The bus
        /// </summary>
        private readonly LoggingBus bus;

        /// <summary>
        ///     The log class
        /// </summary>
        private readonly Type logClass;

        /// <summary>
        ///     The log source
        /// </summary>
        private readonly string logSource;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BusLogging" /> class.
        /// </summary>
        /// <param name="bus">The bus.</param>
        /// <param name="logSource">The log source.</param>
        /// <param name="logClass">The log class.</param>
        public BusLogging(LoggingBus bus, string logSource, Type logClass)
        {
            this.bus = bus;
            this.logSource = logSource;
            this.logClass = logClass;

            isErrorEnabled = bus.LogLevel <= LogLevel.ErrorLevel;
            isWarningEnabled = bus.LogLevel <= LogLevel.WarningLevel;
            isInfoEnabled = bus.LogLevel <= LogLevel.InfoLevel;
            isDebugEnabled = bus.LogLevel <= LogLevel.DebugLevel;
        }

        /// <summary>
        ///     Notifies the error.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void NotifyError(string message)
        {
            bus.Publish(new Error(null, logSource, logClass, message));
        }

        /// <summary>
        ///     Notifies the error.
        /// </summary>
        /// <param name="cause">The cause.</param>
        /// <param name="message">The message.</param>
        protected override void NotifyError(Exception cause, string message)
        {
            bus.Publish(new Error(cause, logSource, logClass, message));
        }

        /// <summary>
        ///     Notifies the warning.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void NotifyWarning(string message)
        {
            bus.Publish(new Warning(logSource, logClass, message));
        }

        /// <summary>
        ///     Notifies the information.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void NotifyInfo(string message)
        {
            bus.Publish(new Info(logSource, logClass, message));
        }

        /// <summary>
        ///     Notifies the debug.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void NotifyDebug(string message)
        {
            bus.Publish(new Debug(logSource, logClass, message));
        }
    }

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
            //TODO: refine this
            string logSource = logSourceObj.ToString();
            Type logClass;
            if (logSourceObj is Type)
                logClass = (Type) logSourceObj;
            else
                logClass = logSourceObj.GetType();

            return new BusLogging(system.EventStream, logSource, logClass);
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