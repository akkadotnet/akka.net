//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
#if DNXCORE50
using Akka.Configuration;
#endif

namespace HelloAkka
{
    class Program
    {
        static void Main(string[] args)
        {
            // create a new actor system (a container for actors)
#if DNXCORE50
            var system = ActorSystem.Create("MySystem", ConfigurationFactory.Default());
#else
            var system = ActorSystem.Create("MySystem");
#endif

            // create actor and get a reference to it.
            // this will be an "ActorRef", which is not a 
            // reference to the actual actor instance
            // but rather a client or proxy to it
            var greeter = system.ActorOf<GreetingActor>("greeter");

            // send a message to the actor
            greeter.Tell(new Greet("World"));

            // prevent the application from exiting before message is handled
            Console.ReadLine();
        }
    }
}

