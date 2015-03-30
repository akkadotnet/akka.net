using Akka.Actor;
using Akka.Configuration;
using Akka.DI.Ninject;
using Akka.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using My = BasicNinjectUses.Properties.Resources;

namespace BasicNinjectUses
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

            Ninject.IKernel container = new Ninject.StandardKernel();
            container.Bind<TypedWorker>().To(typeof(TypedWorker));


            using (var system = ActorSystem.Create("MySystem"))
            {
                NinjectDependencyResolver propsResolver =
                    new NinjectDependencyResolver(container, system);


                var pool = new ConsistentHashingPool(config);
                pool.NrOfInstances = 10;


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
