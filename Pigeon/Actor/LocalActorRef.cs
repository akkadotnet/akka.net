using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Actor
{
    public class LocalActorRef : ActorRef
    {
        private ActorBase _actor;


        public LocalActorRef(ActorPath path)
        {
            this.Path = path;
        }

        public void SetActor(ActorBase actor)
        {
            this._actor = actor;
        }

        public override void Tell(IMessage message, ActorRef sender)
        {
            _actor.Post(sender,message);
        }
    }
}
