//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;

namespace Routing
{
    public class HashableMessage : IConsistentHashable
    {
        public string Name { get; set; }
        public int Id { get; set; }

        public object ConsistentHashKey
        {
            get { return Id; }
        }

        public override string ToString()
        {
            return string.Format("{0} {1}", Id, Name);
        }
    }

    internal class Program
    {
        private static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("MySystem"))
            {
                system.ActorOf<Worker>("Worker1");
                system.ActorOf<Worker>("Worker2");
                system.ActorOf<Worker>("Worker3");
                system.ActorOf<Worker>("Worker4");

                var config = ConfigurationFactory.ParseString(@"
routees.paths = [
    ""akka://MySystem/user/Worker1"" #testing full path
    user/Worker2
    user/Worker3
    user/Worker4
]");

                var roundRobinGroup = system.ActorOf(Props.Empty.WithRouter(new RoundRobinGroup(config)));
                //or: var actor = system.ActorOf(new Props().WithRouter(new RoundRobinGroup("user/Worker1", "user/Worker2", "user/Worker3", "user/Worker4")));

                Console.WriteLine("Why is the order so strange if we use round robin?");
                Console.WriteLine("This is because of the 'Throughput' setting of the MessageDispatcher");
                Console.WriteLine("it lets each actor process X message per scheduled run");
                Console.WriteLine();
                for (var i = 0; i < 20; i++)
                {
                    roundRobinGroup.Tell(i);
                }

                Console.WriteLine("-----");

                var hashGroup = system.ActorOf(Props.Empty.WithRouter(new ConsistentHashingGroup(config)));
                Task.Delay(500).Wait();
                Console.WriteLine();
                for (var i = 0; i < 5; i++)
                {
                    for (var j = 0; j < 7; j++)
                    {
                        var message = new HashableMessage
                        {
                            Name = Guid.NewGuid().ToString(),
                            Id = j,
                        };

                        hashGroup.Tell(message);
                    }
                }

                var roundRobinPool = system.ActorOf(new RoundRobinPool(5, null, null, null,
                    usePoolDispatcher: false).Props(Props.Create<Worker>()));
                //or: var actor = system.ActorOf(new Props().WithRouter(new RoundRobinGroup("user/Worker1", "user/Worker2", "user/Worker3", "user/Worker4")));

                Console.WriteLine("Why is the order so strange if we use round robin?");
                Console.WriteLine("This is because of the 'Throughput' setting of the MessageDispatcher");
                Console.WriteLine("it lets each actor process X message per scheduled run");
                Console.WriteLine();
                for (var i = 0; i < 20; i++)
                {
                    roundRobinPool.Tell(i);
                }

                var scatterGatherGroup = system.ActorOf(new ScatterGatherFirstCompletedPool(5).Props(Props.Create<ReplyWorker>()));

                var reply = scatterGatherGroup.Ask<string>("test");
                reply.Wait();
                Console.ReadLine();
            }
        }
    }

    public class Worker : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            Console.WriteLine("{0} received {1}", Self.Path.Name, message);
        }
    }

    public class ReplyWorker : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            Sender.Tell(string.Format("{0} received {1}", Self.Path.Name, message), Self);
        }
    }
}

