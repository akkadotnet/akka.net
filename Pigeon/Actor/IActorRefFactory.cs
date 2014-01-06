using System;
namespace Pigeon.Actor
{
    public interface IActorRefFactory
    {
        LocalActorRef ActorOf(Props props, string name = null);
        LocalActorRef ActorOf<TActor>(string name = null);
        ActorRef ActorSelection(string remoteActorPath);
        
        ActorSystem System { get; set; }
    }
}
