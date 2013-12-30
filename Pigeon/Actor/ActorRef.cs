using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract class ActorRef
    {
        public string Path { get;protected set; }

        public ActorRef Owner { get; set; }

        public void Tell(IMessage message)
        {
            if (Owner == null)
                throw new ArgumentNullException("Owner");

            this.Tell(message, Owner);
        }
        public abstract void Tell(IMessage message, ActorRef sender);

        public static readonly ActorRef NoSender = new NoSender();
    }

    public sealed class NoSender : ActorRef
    {
        public NoSender()
        {
            this.Path = "NoSender";
        }

        public override void Tell(IMessage message, ActorRef sender)
        {
            
        }
    }
}
