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

            Console.WriteLine(message);
        }
    }
    public class LoggingBus : ActorEventBus<object, Type>
    {
        private IEnumerable<ActorRef> loggers;

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
                this.Subscribe(Logging.StandardOutLogger, Logging.ClassFor(logLevel));
            }
        }
  
        private static readonly LogLevel[] AllLogLevels = Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>().ToArray();

        public void SetLogLevel(LogLevel logLevel)
        {
           

            foreach(var logger in loggers)
            {
                //subscribe to given log level and above
                foreach (var level in AllLogLevels.Where(l => l >= logLevel))
                {
                    this.Subscribe(logger, Logging.ClassFor(logLevel));
                }
                //unsubscribe to all levels below loglevel
                foreach (var level in AllLogLevels.Where(l => l < logLevel))
                {
                    this.Unsubscribe(logger, Logging.ClassFor(logLevel));
                }
            }
        }
    } 

    public enum LogLevel
    {
        ErrorLevel,
        WarningLevel,
        InfoLevel,
        DebugLevel,
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

    public class Warn : LogEvent
    {
        public Warn(string logSource, Type logClass, object message)
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

    public class DefaultLogger : UntypedActor
    {

        protected override void OnReceive(object message)
        {
        }
    }

    public class LoggingAdapter
    {
        public void Debug(string text)
        {
            //TODO: implement
            //TODO: should this java api be used or replaced with tracewriter or somesuch?
            Trace.WriteLine(text);
        }

        public void Warn(string text)
        {
            //TODO: implement
            //TODO: should this java api be used or replaced with tracewriter or somesuch?
            Trace.WriteLine(text);
        }

        public void Error(string text)
        {
            //TODO: implement
            //TODO: should this java api be used or replaced with tracewriter or somesuch?
            Trace.WriteLine(text);
        }
        public void Info(string text)
        {
            //TODO: implement
            //TODO: should this java api be used or replaced with tracewriter or somesuch?
            Trace.WriteLine(text);
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
                    return typeof(Warn);
                case LogLevel.ErrorLevel:
                    return typeof(Error);
                default:
                    throw new ArgumentException("Unknown LogLevel", "logLevel");
            }
        }
        public static LoggingAdapter GetLogger(ActorSystem system)
        {
            var actor = ActorCell.Current.Actor;
            return new LoggingAdapter();
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
