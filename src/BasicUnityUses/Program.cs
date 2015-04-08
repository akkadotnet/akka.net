﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DI.Unity;
using Akka.Routing;
using Microsoft.Practices.Unity;

namespace BasicUnityUses
{
	class Program
	{
		static void Main(string[] args)
		{
			WithHashPool();
		}

		private static void WithHashPool()
        {
            IUnityContainer container = new UnityContainer();
            container.RegisterType<TypedWorker>();


            using (var system = ActorSystem.Create("MySystem"))
            {
                var propsResolver =
                    new UnityDependencyResolver(container, system);

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
