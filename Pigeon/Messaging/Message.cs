using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Messaging
{
    public class Message
    {
        public ActorRef Sender { get; set; }
        public IMessage Payload { get; set; }

        public LocalActorRef Target { get; set; }
    }
}
