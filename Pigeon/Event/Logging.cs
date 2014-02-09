using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Events
{
    public abstract class Event
    {

    }

    public class Error : Event
    {

        public Error(Exception cause, string path, Type actorType, string errorMessage)
        {
        }
    }

    public class UnhandledMessage : Event
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
}
