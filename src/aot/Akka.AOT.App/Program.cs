using Akka.Actor;
using Akka.AOT.App.Actors;

namespace Akka.AOT.App;

class Program
{
    static async Task Main(string[] args)
    {
        var system = ActorSystem.Create("MySystem");
        
        // create AotUntypedActor
        var untypedActorProps = Props.Create(() => new AotUntypedActor());
        var untypedActor = system.ActorOf(untypedActorProps, "untyped-actor");
        
        // create AotReceiveActor
        var receiveActorProps = Props.Create(() => new AotReceiveActor());
        var receiveActor = system.ActorOf(receiveActorProps, "receive-actor");
        
        // send a message to both actors
        Console.WriteLine(await untypedActor.Ask("Hello, untyped actor!"));
        Console.WriteLine(await receiveActor.Ask("Hello, receive actor!"));
        
        // terminate the actor system
        await system.Terminate();
    }
}