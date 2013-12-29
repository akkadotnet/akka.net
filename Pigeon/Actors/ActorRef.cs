using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon
{
    public abstract class ActorRef
    {
        public string Name { get;protected set; }

        public abstract void Tell(ActorRef sender, IMessage message);

        public static readonly ActorRef NoSender = new NoSender();
    }

    public sealed class NoSender : ActorRef
    {
        public NoSender()
        {
            this.Name = "NoSender";
        }

        public override void Tell(ActorRef sender, IMessage message)
        {
            
        }
    }
}
