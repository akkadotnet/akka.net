using Pigeon.Messaging;
using System;
using System.Collections.Generic;
namespace Pigeon.Actor
{
    public interface IActorContext : IActorRefFactory
    {
        ActorSelection ActorSelection(ActorPath actorPath);

        ActorRef Sender { get; }
        void Become(Receive receive);
        void Unbecome();

        LocalActorRef Child(string name);

        IEnumerable<LocalActorRef> GetChildren();

        void Watch(ActorRef subject);

        void Unwatch(ActorRef subject);
    }
}
