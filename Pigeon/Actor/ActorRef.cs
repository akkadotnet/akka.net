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

        public void Tell(object message, ActorRef sender = null)
        {
            if (sender != null)
            {
            }
            else if (ActorContext.Current != null)
            {
                sender = ActorContext.Current.Self;
            }
            else
            {
                sender = ActorRef.NoSender;
            }

            TellInternal(message, sender);
        }

        protected abstract void TellInternal(object message,ActorRef sender);     

        public static readonly ActorRef NoSender = new NoSender();
    }

    public sealed class NoSender : ActorRef
    {
        public NoSender()
        {
            this.Path = new ActorPath("NoSender");
        }

        protected override void TellInternal(object message, ActorRef sender)
        {

        }
    }
}
