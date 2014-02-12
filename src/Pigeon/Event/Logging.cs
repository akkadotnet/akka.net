using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Event
{
    public abstract class EventMessage
    {

    }

    public class Info : EventMessage
    {
        public Info(string logSource,Type logClass,object message)
        {
            this.LogSource = logSource;
            this.LogClass = logClass;
            this.Message = message;
        }

        public string LogSource { get;private set; }

        public Type LogClass { get; private set; }

        public object Message { get; private set; }
    }

    public class Debug : EventMessage
    {
        public Debug(string logSource, Type logClass, string message)
        {
            this.LogSource = logSource;
            this.LogClass = logClass;
            this.Message = message;
        }

        public string LogSource { get;private set; }
        public Type LogClass { get; private set; }
        public string Message { get; private set; }
    }

    public class Error : EventMessage
    {

        public Error(Exception cause, string path, Type actorType, string errorMessage)
        {
            this.Cause = cause;
            this.Path = path;
            this.ActorType = actorType;
            this.ErrorMessage = errorMessage;
        }

        public Exception Cause { get;private set; }

        public string Path { get; private set; }

        public Type ActorType { get; private set; }

        public string ErrorMessage { get; private set; }
    }

    public class UnhandledMessage : EventMessage
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
        public static LoggingAdapter GetLogger(ActorSystem system)
        {
            var actor = ActorCell.Current.Actor;
            return new LoggingAdapter();
        }
    }
}
