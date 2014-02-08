using Pigeon.Actor;
using Pigeon.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Routing
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("MySystem"))
            {
                system.ActorOf<Worker>("Worker1");
                system.ActorOf<Worker>("Worker2");
                system.ActorOf<Worker>("Worker3");
                system.ActorOf<Worker>("Worker4");

                var actor = system.ActorOf(new Props().WithRouter(new RoundRobinGroup("user/Worker1", "user/Worker2", "user/Worker3", "user/Worker4")));

                Console.WriteLine("Why is the order so strange if we use round robin?");
                Console.WriteLine("This is because of the 'Throughput' setting of the MessageDispatcher");
                Console.WriteLine("it lets each actor process X message per scheduled run");
                Console.WriteLine();
                for (int i = 0; i < 20; i++)
                {
                    actor.Tell(i);
                }
                Console.ReadLine();
            }
        }
    }

    public class Worker : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            Console.WriteLine("{0} received {1}",Self.Path.Name ,message);
        }
    }
}
