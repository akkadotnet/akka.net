using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon
{
    public class LocalActorRef : ActorRef
    {
        private ActorBase _actor;
        public LocalActorRef(ActorBase actor)
        {
            _actor = actor;
        }

        public override void Tell(ActorRef sender, IMessage message)
        {
            _actor.Tell(sender,message);
        }
    }
}
