using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Event
{
    public class StandardOutLogger : MinimalActorRef
    {
        public StandardOutLogger()
        {
            Path = new RootActorPath(Address.AllSystems, "/StandardOutLogger");
        }

        public override ActorRefProvider Provider
        {
            get { throw new Exception("StandardOutLogged does not provide"); }
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            if (message == null)
                throw new Exception("message is null");
            ConsoleColor tmp = Console.ForegroundColor;
            //    Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            //    Console.ForegroundColor = ConsoleColor.White;
        }
    }

    public class LoggingBus : ActorEventBus<object, Type>
    {
        private static readonly LogLevel[] AllLogLevels = Enum.GetValues(typeof (LogLevel)).Cast<LogLevel>().ToArray();
        private readonly List<ActorRef> loggers = new List<ActorRef>();
        public LogLevel LogLevel { get; private set; }

        protected override bool IsSubClassification(Type parent, Type child)
        {
            return parent.IsAssignableFrom(child);
        }

        protected override void Publish(object @event, ActorRef subscriber)
        {
            subscriber.Tell(@event);
        }

        protected override bool Classify(object @event, Type classifier)
        {
            return classifier.IsAssignableFrom(GetClassifier(@event));
        }

        protected override Type GetClassifier(object @event)
        {
            return @event.GetType();
        }

        public async void StartDefaultLoggers(ActorSystem system)
        {
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

        private async Task AddLogger(ActorSystem system, Type actorClass, LogLevel logLevel, string logName)
        {
            //TODO: remove the newguid stuff
            string name = "log" + system.Name + "-" + SimpleName(actorClass);
            ActorRef actor = null;
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

        public void StartStdoutLogger(Settings config)
        {
            SetUpStdoutLogger(config);
            Publish(new Debug(SimpleName(this), GetType(), "StandardOutLogger started"));
        }

        private void SetUpStdoutLogger(Settings config)
        {
            LogLevel logLevel = Logging.LogLevelFor(config.StdoutLogLevel);
            foreach (LogLevel level in AllLogLevels.Where(l => l >= logLevel))
            {
                Subscribe(Logging.StandardOutLogger, Logging.ClassFor(level));
            }
        }

        public void SetLogLevel(LogLevel logLevel)
        {
            LogLevel = LogLevel;
            foreach (ActorRef logger in loggers)
            {
                //subscribe to given log level and above
                foreach (LogLevel level in AllLogLevels.Where(l => l >= logLevel))
                {
                    Subscribe(logger, Logging.ClassFor(level));
                }
                //unsubscribe to all levels below loglevel
                foreach (LogLevel level in AllLogLevels.Where(l => l < logLevel))
                {
                    Unsubscribe(logger, Logging.ClassFor(level));
                }
            }
        }
    }

    public enum LogLevel
    {
        DebugLevel,
        InfoLevel,
        WarningLevel,
        ErrorLevel,
    }

    public abstract class LogEvent : NoSerializationVerificationNeeded
    {
        public LogEvent()
        {
            Timestamp = DateTime.Now;
            Thread = Thread.CurrentThread;
        }

        public DateTime Timestamp { get; private set; }
        public Thread Thread { get; private set; }
        public string LogSource { get; protected set; }
        public Type LogClass { get; protected set; }
        public object Message { get; protected set; }
        public abstract LogLevel LogLevel();

        public override string ToString()
        {
            return string.Format("{0} {1} {2} - {3} [Thread {4}]", Timestamp, LogLevel(), LogSource, Message,
                Thread.ManagedThreadId);
        }
    }

    public class Info : LogEvent
    {
        public Info(string logSource, Type logClass, object message)
        {
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        public override LogLevel LogLevel()
        {
            return Event.LogLevel.InfoLevel;
        }
    }

    public class Debug : LogEvent
    {
        public Debug(string logSource, Type logClass, object message)
        {
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        public override LogLevel LogLevel()
        {
            return Event.LogLevel.DebugLevel;
        }
    }

    public class Warning : LogEvent
    {
        public Warning(string logSource, Type logClass, object message)
        {
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        public override LogLevel LogLevel()
        {
            return Event.LogLevel.WarningLevel;
        }
    }

    public class Error : LogEvent
    {
        public Error(Exception cause, string logSource, Type logClass, object message)
        {
            Cause = cause;
            LogSource = logSource;
            LogClass = logClass;
            Message = message;
        }

        public Exception Cause { get; private set; }

        public override LogLevel LogLevel()
        {
            return Event.LogLevel.ErrorLevel;
        }
    }

    public class UnhandledMessage
    {
        internal UnhandledMessage(object message, ActorRef sender, ActorRef recipient)
        {
            Message = message;
            Sender = sender;
            Recipient = recipient;
        }

        internal object Message { get; private set; }
        internal ActorRef Sender { get; private set; }
        internal ActorRef Recipient { get; private set; }
    }

    public class InitializeLogger : NoSerializationVerificationNeeded
    {
        public InitializeLogger(LoggingBus loggingBus)
        {
            LoggingBus = loggingBus;
        }

        public LoggingBus LoggingBus { get; private set; }
    }

    public class LoggerInitialized : NoSerializationVerificationNeeded
    {
    }

    public class DefaultLogger : UntypedActor
    {
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

    public abstract class LoggingAdapter
    {
        protected bool isDebugEnabled;
        protected bool isErrorEnabled;
        protected bool isInfoEnabled;
        protected bool isWarningEnabled;
        protected abstract void NotifyError(string message);
        protected abstract void NotifyError(Exception cause, string message);
        protected abstract void NotifyWarning(string message);
        protected abstract void NotifyInfo(string message);
        protected abstract void NotifyDebug(string message);

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

        public void Debug(string message)
        {
            if (isDebugEnabled)
                NotifyDebug(message);
        }

        public void Warn(string message)
        {
            if (isWarningEnabled)
                NotifyWarning(message);
        }

        public void Error(Exception cause, string message)
        {
            if (isErrorEnabled)
                NotifyError(cause, message);
        }

        public void Error(string message)
        {
            if (isErrorEnabled)
                NotifyError(message);
        }

        public void Info(string message)
        {
            if (isInfoEnabled)
                NotifyInfo(message);
        }


        public void Debug(string format, params object[] args)
        {
            if (isDebugEnabled)
                NotifyDebug(string.Format(format, args));
        }

        public void Warn(string format, params object[] args)
        {
            if (isWarningEnabled)
                NotifyWarning(string.Format(format, args));
        }

        public void Error(Exception cause, string format, params object[] args)
        {
            if (isErrorEnabled)
                NotifyError(cause, string.Format(format, args));
        }

        public void Error(string format, params object[] args)
        {
            if (isErrorEnabled)
                NotifyError(string.Format(format, args));
        }

        public void Info(string format, params object[] args)
        {
            if (isInfoEnabled)
                NotifyInfo(string.Format(format, args));
        }

        public void Log(LogLevel logLevel, string message)
        {
            NotifyLog(logLevel, message);
        }

        public void Log(LogLevel logLevel, string format, params object[] args)
        {
            NotifyLog(logLevel, string.Format(format, args));
        }
    }

    public class BusLogging : LoggingAdapter
    {
        private readonly LoggingBus bus;
        private readonly Type logClass;
        private readonly string logSource;

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

        protected override void NotifyError(string message)
        {
            bus.Publish(new Error(null, logSource, logClass, message));
        }

        protected override void NotifyError(Exception cause, string message)
        {
            bus.Publish(new Error(cause, logSource, logClass, message));
        }

        protected override void NotifyWarning(string message)
        {
            bus.Publish(new Warning(logSource, logClass, message));
        }

        protected override void NotifyInfo(string message)
        {
            bus.Publish(new Info(logSource, logClass, message));
        }

        protected override void NotifyDebug(string message)
        {
            bus.Publish(new Debug(logSource, logClass, message));
        }
    }

    public static class Logging
    {
        public static readonly StandardOutLogger StandardOutLogger = new StandardOutLogger();

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

        public static LoggingAdapter GetLogger(IActorContext cell)
        {
            string logSource = cell.Self.ToString();
            Type logClass = cell.Props.Type;

            return new BusLogging(cell.System.EventStream, logSource, logClass);
        }

        public static LoggingAdapter GetLogger(ActorSystem system, object logSourceObj)
        {
            //TODO: refine this
            string logSource = logSourceObj.ToString();
            Type logClass = null;
            if (logSourceObj is Type)
                logClass = (Type) logSourceObj;
            else
                logClass = logSourceObj.GetType();

            return new BusLogging(system.EventStream, logSource, logClass);
        }

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
                    throw new ArgumentException("Unknown LogLevel", "logLevel");
            }
        }
    }
}