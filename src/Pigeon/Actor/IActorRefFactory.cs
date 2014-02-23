using System;
namespace Pigeon.Actor
{
    public interface IActorRefFactory
    {
        InternalActorRef ActorOf(Props props, string name = null);
        InternalActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase;
        ActorSelection ActorSelection(ActorPath actorPath);
        ActorSelection ActorSelection(string actorPath);
    }
}