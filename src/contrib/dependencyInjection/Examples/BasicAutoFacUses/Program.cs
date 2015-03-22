using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Akka.DI.AutoFac;
using My = BasicAutoFacUses.Properties.Resources;

namespace BasicAutoFacUses
{
    class Program
    {
        static void Main(string[] args)
        {
            WithHashPool();
            WithHashPoolAndChildActors();
        }

        private static void WithHashPool()
        {
            Console.WriteLine("With Has Pool");

            var config = ConfigurationFactory.ParseString(My.HashPoolWOResizer);
            var builder = new ContainerBuilder();
            builder.RegisterType<TypedWorker>();
            
            Autofac.IContainer container = builder.Build();


            using (var system = ActorSystem.Create("MySystem"))
            {
                AutoFacDependencyResolver propsResolver =
                    new AutoFacDependencyResolver(container, system);
                
                var pool = new ConsistentHashingPool(config);

                var router = system.ActorOf(propsResolver.Create<TypedWorker>().WithRouter(pool));

                Task.Delay(500).Wait();
                Console.WriteLine("Sending Messages");

                for (var i = 0; i < 5; i++)
                {
                    for (var j = 0; j < 7; j++)
                    {

                        TypedActorMessage msg = new TypedActorMessage { Id = j, Name = Guid.NewGuid().ToString() };
                        AnotherMessage ms = new AnotherMessage { Id = j, Name = msg.Name };

                        var envelope = new ConsistentHashableEnvelope(ms, msg.Id);

                        router.Tell(msg);
                        router.Tell(envelope);

                    }
                }
                Console.WriteLine("Hit Enter to continue");
                Console.ReadLine();
            }
           
        }
        private static void WithHashPoolAndChildActors()
        {
            Console.WriteLine("With Has Pool and Child Actors");
            var config = ConfigurationFactory.ParseString(My.HashPoolWOResizer);
            var builder = new ContainerBuilder();
            builder.RegisterType<TypedWorker>();
            builder.RegisterType<TypedParentWorker>();

            Autofac.IContainer container = builder.Build();


            using (var system = ActorSystem.Create("MySystem"))
            {
                AutoFacDependencyResolver propsResolver =
                    new AutoFacDependencyResolver(container, system);

                var pool = new ConsistentHashingPool(config);

                var router = system.ActorOf(propsResolver.Create<TypedParentWorker>().WithRouter(pool));

                Task.Delay(500).Wait();
                Console.WriteLine("Sending Messages");

                for (var i = 0; i < 5; i++)
                {
                    for (var j = 0; j < 7; j++)
                    {

                        TypedActorMessage msg = new TypedActorMessage { Id = j, Name = Guid.NewGuid().ToString() };
                        AnotherMessage ms = new AnotherMessage { Id = j, Name = msg.Name };

                        var envelope = new ConsistentHashableEnvelope(ms, msg.Id);

                        router.Tell(msg);
                        router.Tell(envelope);

                    }
                }
                Console.WriteLine("Hit Enter to continue");
                Console.ReadLine();
            }

            
        }
    }
}
