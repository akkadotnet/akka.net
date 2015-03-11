using Akka.Actor;
using Akka.Routing;
using System;
using System.Threading.Tasks;
using Autofac;
using Akka.DI.AutoFac;

namespace BasicAutoFacUses
{
    class Program
    {
        static void Main(string[] args)
        {
            WithHashPool();
        }

        private static void WithHashPool()
        {
            var builder = new ContainerBuilder();
            builder.RegisterType<TypedWorker>();

            var container = builder.Build();


            using (var system = ActorSystem.Create("MySystem"))
            {
                var propsResolver =
                    new AutoFacDependencyResolver(container, system);

                var router = system.ActorOf(propsResolver.Create<TypedWorker>().WithRouter(FromConfig.Instance), "router1");

                Task.Delay(500).Wait();
                Console.WriteLine("Sending Messages");

                for (var i = 0; i < 5; i++)
                {
                    for (var j = 0; j < 7; j++)
                    {

                        var msg = new TypedActorMessage { Id = j, Name = Guid.NewGuid().ToString() };
                        var ms = new AnotherMessage { Id = j, Name = msg.Name };

                        var envelope = new ConsistentHashableEnvelope(ms, msg.Id);

                        router.Tell(msg);
                        router.Tell(envelope);

                    }
                }
            }

            Console.ReadLine();
        }
    }
}
