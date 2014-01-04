using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract class BuiltInMessage
    {
    }

    public class SuperviceChild : BuiltInMessage
    {
        public SuperviceChild(Exception reason)
        {
            this.Reason = reason;
        }

        public Exception Reason { get;private set; }
    }

    public class ActorAction : BuiltInMessage
    {
        public ActorAction(Action action)
        {
            this.Action = action;
        }
        public Action Action { get;private set; }
    }

    public class Ping : BuiltInMessage
    {
        public DateTime LocalUtcNow { get; set; }
    }

    public class Pong : BuiltInMessage
    {
        public DateTime LocalUtcNow { get; set; }
        public DateTime RemoteUtcNow { get; set; }
    }

    public class Kill : BuiltInMessage
    {
    }

    public class Restart : BuiltInMessage
    {
    }

    public class Resume : BuiltInMessage
    {
    }

    public class Stop : BuiltInMessage
    {
    }

    public class Escalate : BuiltInMessage
    {
        public Escalate(Exception reason)
        {
            this.Reason = reason;
        }
        public Exception Reason { get;private set; }
    }

    public class UnhandledMessage : BuiltInMessage
    {
        public UnhandledMessage(object message, ActorRef sender, ActorRef recipient)
        {
            this.Message = message;
            this.Sender = sender;
            this.Recipient = recipient;
        }

        public object Message { get;private set; }
        public ActorRef Sender { get;private set; }
        public ActorRef Recipient { get;private set; }
    }
}
