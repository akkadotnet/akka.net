using Pigeon.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class DeadLettersActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            Context.System.EventStream.Publish(new DeadLetter(message, Sender, Self));
        }
    }

    public class EventStreamActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
        }
    }

    public class GuardianActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            Unhandled(message);
        }
    }
}
