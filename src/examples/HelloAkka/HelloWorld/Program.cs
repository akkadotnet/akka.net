//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#region akka-hello-world-main

using Akka.Actor;

namespace HelloWorld;

class Program
{
    static async Task Main(string[] args)
    {
        // create a new actor system (a container for actors)
        ActorSystem system = ActorSystem.Create("the-universe");

        // create actor and get a reference to it.
        // this will be an "ActorRef", which is not a 
        // reference to the actual actor instance
        // but rather a client or proxy to it
        IActorRef greeter = system.ActorOf<GreetingActor>("greeter");

        // send a message to the actor
        greeter.Tell(new Greet("World"));

        //this is for demonstration purposes
        await Task.Delay(TimeSpan.FromSeconds(1));
        system.Stop(greeter);

        // prevent the application from exiting before message is handled            
        await system.WhenTerminated;
    }
}

#endregion
