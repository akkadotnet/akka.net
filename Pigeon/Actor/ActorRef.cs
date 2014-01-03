using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract class ActorRef
    {
        public virtual ActorPath Path { get;protected set; }

        public abstract void Tell(object message, ActorRef sender = null);        

        public static readonly ActorRef NoSender = new NoSender();
    }

    public sealed class NoSender : ActorRef
    {
        public NoSender()
        {
            this.Path = new ActorPath("NoSender");
        }

        public override void Tell(object message, ActorRef sender)
        {
            
        }
    }
}
