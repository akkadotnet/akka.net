using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Event
{
    public class DeadLetter : EventMessage
    {
        public DeadLetter(object message,ActorRef sender,ActorRef recipient)
        {
            this.Message = message;
            this.Sender = sender;
            this.Recipient = recipient;
        }

        public object Message { get;private set; }

        public ActorRef Recipient { get; private set; }

        public ActorRef Sender { get; private set; }
    }
}
