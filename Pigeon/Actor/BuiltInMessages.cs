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

    public class CompleteFuture : SystemMessage
    {
        public CompleteFuture(Action action)
        {
            this.SetResult = action;
        }
        public Action SetResult { get;private set; }
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

    public class PoisonPill : SystemMessage
    {
    }
    public class Kill : SystemMessage
    {
    }

    public class Restart : SystemMessage
    {
    }

    public class RestartChild : SystemMessage
    {
        public RestartChild(LocalActorRef child)
        {
            this.Child = child;
        }
        public LocalActorRef Child { get; private set; }
    }

    public class Resume : SystemMessage
    {
    }

    public class Stop : SystemMessage
    {
    }

    public class StopChild : SystemMessage
    {
        public StopChild(LocalActorRef child)
        {
            this.Child = child;
        }
        public LocalActorRef Child { get;private set; }
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

    //request to an actor ref, to get back the identity of the underlying actors
    public class Identity : SystemMessage
    {
    }

    //response to the Identity message, get identity by Sender
    public class ActorIdentity : SystemMessage
    {
    }

    //used to start watching another actor (deathwatch)
    public class Watch : SystemMessage
    {
    }

    //used to unsubscribe to deathwatch
    public class Unwatch : SystemMessage
    {
    }
}
