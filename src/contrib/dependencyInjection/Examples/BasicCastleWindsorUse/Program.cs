using Akka.Actor;
using Akka.Configuration;
using Akka.DI.CastleWindsor;
using Akka.Routing;
using Castle.MicroKernel.Registration;
using Castle.Windsor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using My = BasicCastleWindsorUses.Properties.Resources;

namespace BasicCastleWindsorUse
{
    class Program
    {
        static void Main(string[] args)
        {
            WithHashPool();
        }

        private static void WithHashPool()
        {
            var config = ConfigurationFactory.ParseString(My.HashPoolWOResizer);

            using (var system = ActorSystem.Create("MySystem", config))
            {
                IWindsorContainer container = new WindsorContainer();
                container.Register(Component.For<TypedWorker>().Named("TypedWorker").LifestyleTransient());


                var pool = new ConsistentHashingPool(config);
                pool.NrOfInstances = 10;


                WindsorDependencyResolver propsResolver =
                    new WindsorDependencyResolver(container, system);

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
            }

            Console.ReadLine();
        }
    }
}
