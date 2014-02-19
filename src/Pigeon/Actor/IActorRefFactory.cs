using System;
namespace Pigeon.Actor
{
    public interface IActorRefFactory
    {
        LocalActorRef ActorOf(Props props, string name = null);
        LocalActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase;
        BrokenActorSelection ActorSelection(ActorPath actorPath);
        BrokenActorSelection ActorSelection(string actorPath);
    }
}