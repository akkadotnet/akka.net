using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HelloAkka
{
    class Program
    {
        static void Main(string[] args)
        {
            // create a new actor system (a container for actors)
            var system = ActorSystem.Create("MySystem");

            // create actor and get a reference to it.
            // this will be an "ActorRef", which is not a 
            // reference to the actual actor instance
            // but rather a client or proxy to it
            var greeter = system.ActorOf<GreetingActor>("greeter");

            // send a message to the actor
            greeter.Tell(new Greet("World"));

            // prevent the application from exiting befor message is handled
            Console.ReadLine();
        }
    }
}
