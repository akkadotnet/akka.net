using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract class SystemMessage
    {
    }

    public class SuperviceChild : SystemMessage
    {
        public SuperviceChild(Exception reason)
        {
            this.Reason = reason;
        }

        public Exception Reason { get;private set; }
    }

    public class ActorAction : SystemMessage
    {
        public ActorAction(Action action)
        {
            this.Action = action;
        }
        public Action Action { get;private set; }
    }

    public class Ping : SystemMessage
    {
        public DateTime LocalUtcNow { get; set; }
    }

    public class Pong : SystemMessage
    {
        public DateTime LocalUtcNow { get; set; }
        public DateTime RemoteUtcNow { get; set; }
    }

    public class Kill : SystemMessage
    {
    }

    public class Restart : SystemMessage
    {
    }

    public class Resume : SystemMessage
    {
    }

    public class Stop : SystemMessage
    {
    }

    public class Escalate : SystemMessage
    {
        public Escalate(Exception reason)
        {
            this.Reason = reason;
        }
        public Exception Reason { get;private set; }
    }

    public class UnhandledMessage : SystemMessage
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

    public class Terminated : SystemMessage
    {
    }
}
