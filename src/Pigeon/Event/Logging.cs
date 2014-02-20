using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Event
{
    public class StandardOutLogger : MinimalActorRef
    {
        public StandardOutLogger()
        {
            Path = new RootActorPath(Address.AllSystems, "StandardOutLogger");
        }

        public override ActorRefProvider Provider
        {
            get { throw new Exception("StandardOutLogged does not provide"); }
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            if (message == null)
                throw new Exception("message is null");
            var tmp = Console.ForegroundColor;
        //    Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
        //    Console.ForegroundColor = ConsoleColor.White;
        }
    }
    public class LoggingBus : ActorEventBus<object, Type>
    {
        private List<ActorRef> loggers = new List<ActorRef>();

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
            var logName = SimpleName(this) + "(" + system.Name + ")";
            var logLevel = Logging.LogLevelFor(system.Settings.LogLevel);
            var loggerTypes = system.Settings.Loggers;
            foreach(var loggerType in loggerTypes)
            {
                var actorClass = Type.GetType(loggerType);
                if (actorClass == null)
                {
                    //TODO: create real exceptions and refine error messages
                    throw new Exception("Can not use logger of type:" + loggerType);
                }
                var timeout = system.Settings.LoggerStartTimeout;
                var task = AddLogger(system, actorClass, logLevel, logName);
                if (await Task.WhenAny(task, Task.Delay(timeout)) == task)
                {

                }
                else
                {
                     Publish(new Warning(logName, this.GetType(), "Logger " + logName + " did not respond within " + timeout + " to InitializeLogger(bus)")); 
                }
            }
            Publish(new Debug(logName, this.GetType(), "Default Loggers started"));
            this.SetLogLevel(logLevel);
        }

        private async Task AddLogger(ActorSystem system, Type actorClass, LogLevel logLevel, string logName)
        {
            var name = "log" + system.Name + "-" + SimpleName(actorClass);
            var actor = system.SystemGuardian.Cell.ActorOf(Props.Create(actorClass), name);
            loggers.Add(actor);
            await actor.Ask(new InitializeLogger(this), system);
        }

        public void StartStdoutLogger(Settings config) 
        {
            SetUpStdoutLogger(config);
            Publish(new Debug(SimpleName(this), this.GetType(), "StandardOutLogger started"));
        }

        private void SetUpStdoutLogger(Settings config)
        {
            var logLevel = Logging.LogLevelFor(config.StdoutLogLevel);
            foreach (var level in AllLogLevels.Where(l => l >= logLevel))
            {
                this.Subscribe(Logging.StandardOutLogger, Logging.ClassFor(level));
            }
        }
  
        private static readonly LogLevel[] AllLogLevels = Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>().ToArray();

        public LogLevel LogLevel { get; private set; }
        public void SetLogLevel(LogLevel logLevel)
        {
            this.LogLevel = LogLevel;
            foreach(var logger in loggers)
            {
                //subscribe to given log level and above
                foreach (var level in AllLogLevels.Where(l => l >= logLevel))
                {
                    this.Subscribe(logger, Logging.ClassFor(level));
                }
                //unsubscribe to all levels below loglevel
                foreach (var level in AllLogLevels.Where(l => l < logLevel))
                {
                    this.Unsubscribe(logger, Logging.ClassFor(level));
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
            this.Timestamp = DateTime.Now;
            this.Thread = System.Threading.Thread.CurrentThread;
        }
        public abstract LogLevel LogLevel();
        public DateTime Timestamp { get; private set; }
        public System.Threading.Thread Thread { get;private set; }
        public string LogSource { get; protected set; }
        public Type LogClass { get; protected set; }
        public object Message { get; protected set; }

        public override string ToString()
        {
            return string.Format("{0} {1} {2} - {3} [Thread {4}]", Timestamp, LogLevel(), LogSource, Message, Thread.ManagedThreadId);
        }
    }

    public class Info : LogEvent
    {
        public Info(string logSource,Type logClass,object message)
        {
            this.LogSource = logSource;
            this.LogClass = logClass;
            this.Message = message;
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
            this.LogSource = logSource;
            this.LogClass = logClass;
            this.Message = message;
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
            this.LogSource = logSource;
            this.LogClass = logClass;
            this.Message = message;
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
            this.Cause = cause;
            this.LogSource = logSource;
            this.LogClass = logClass;
            this.Message = message;
        }

        public Exception Cause { get;private set; }

        public override LogLevel LogLevel()
        {
            return Event.LogLevel.ErrorLevel;
        }
    }

    public class UnhandledMessage 
    {
        internal UnhandledMessage(object message, ActorRef sender, ActorRef recipient)
        {
            this.Message = message;
            this.Sender = sender;
            this.Recipient = recipient;
        }

        internal object Message { get; private set; }
        internal ActorRef Sender { get; private set; }
        internal ActorRef Recipient { get; private set; }
    }

    public class InitializeLogger : NoSerializationVerificationNeeded
    {
        public InitializeLogger(LoggingBus loggingBus)
        {
            this.LoggingBus = loggingBus;
        }

        public LoggingBus LoggingBus { get;private set; }
    }

    public class LoggerInitialized : NoSerializationVerificationNeeded
    {
    }

    public class DefaultLogger : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            ReceiveBuilder.Match(message)
                .With<InitializeLogger>(m => Sender.Tell(new LoggerInitialized()))
                .With<LogEvent>(m => 
                    Console.WriteLine(m))
                .Default(Unhandled);
        }
    }

    public abstract class LoggingAdapter
    {
        protected abstract void NotifyError(string message);
        protected abstract void NotifyError(Exception cause,string message);
        protected abstract void NotifyWarning(string message);
        protected abstract void NotifyInfo(string message);
        protected abstract void NotifyDebug(string message);

        protected bool isErrorEnabled;
        protected bool isWarningEnabled;
        protected bool isInfoEnabled;
        protected bool isDebugEnabled;

        protected bool IsEnabled(LogLevel logLevel)
        {
            switch(logLevel)
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

        protected void NotifyLog(LogLevel logLevel,string message)
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


        public void Debug(string format,params object[] args)
        {
            if (isDebugEnabled)
                NotifyDebug(string.Format(format,args));
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
        private LoggingBus bus;
        private string logSource;
        private Type logClass;
        public BusLogging (LoggingBus bus, string logSource, Type logClass)
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
                    return typeof(Debug);
                case LogLevel.InfoLevel:
                    return typeof(Info);
                case LogLevel.WarningLevel:
                    return typeof(Warning);
                case LogLevel.ErrorLevel:
                    return typeof(Error);
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
        public static LoggingAdapter GetLogger(ActorSystem system,object logSourceObj)
        {
            //TODO: refine this
            string logSource = logSourceObj.ToString() ;
            Type logClass = null;
            if (logSourceObj is Type)
                logClass = (Type)logSourceObj;
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
