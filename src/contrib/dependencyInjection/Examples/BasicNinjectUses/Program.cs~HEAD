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
            WithHashPoolAndChildActors();
        }

        private static void WithHashPoolAndChildActors()
        {
            Console.WriteLine("Running With Hash Pool And Child Actors");
            var config = ConfigurationFactory.ParseString(My.HashPoolWOResizer);

            ConsistentHashMapping hashMapping = msg =>
            {
                if (msg is TypedActorMessage)
                {
                    var m2 = msg as TypedActorMessage;
                    return m2.ConsistentHashKey;
                }

                return null;
            };


            Ninject.IKernel container = new Ninject.StandardKernel();
            container.Bind<TypedWorker>().To(typeof(TypedWorker));
            container.Bind<TypedParentWorker>().To(typeof(TypedParentWorker));

            using (var system = ActorSystem.Create("MySystem"))
            {
                NinjectDependencyResolver propsResolver =
                    new NinjectDependencyResolver(container, system);


                var pool = new ConsistentHashingPool(10, null, null, null, hashMapping: hashMapping);

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
                Console.WriteLine("Hit Enter to Continue");
                Console.ReadLine();
            }


        }

        private static void WithHashPool()
        {
            Console.WriteLine("Running With Hash Pool");
            var config = ConfigurationFactory.ParseString(My.HashPoolWOResizer);

            Ninject.IKernel container = new Ninject.StandardKernel();
            container.Bind<TypedWorker>().To(typeof(TypedWorker));

            ConsistentHashMapping hashMapping = msg =>
            {
                if (msg is TypedActorMessage)
                {
                    var m2 = msg as TypedActorMessage;
                    return m2.ConsistentHashKey;
                }

                return null;
            };


            using (var system = ActorSystem.Create("MySystem"))
            {
                NinjectDependencyResolver propsResolver =
                    new NinjectDependencyResolver(container, system);


                var pool = new ConsistentHashingPool(10, null, null, null, hashMapping: hashMapping);
    
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
                Console.WriteLine("Hit Enter to Continue");
                Console.ReadLine();
            }

        }
    }
}
