//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Util;
using ClusterSharding.Node.AutomaticJoin;

namespace ClusterSharding.Node
{
    using ClusterSharding = Akka.Cluster.Sharding.ClusterSharding;

    class Program
    {
        static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("sharded-cluster-system", ConfigurationFactory.Load().WithFallback(ClusterSingletonManager.DefaultConfig())))
            {
                var automaticCluster = new AutomaticCluster(system);
                try
                {
                    automaticCluster.Join();

                    RunExample(system);

                    Console.ReadLine();
                }
                finally
                {
                    //WARNING: you may need to remove SQLite database file from bin/Debug or bin/Release in case when unexpected crash happened
                    automaticCluster.Leave();
                }
            }
        }

        private static void RunExample(ActorSystem system)
        {
            var sharding = ClusterSharding.Get(system);
            var shardRegion = sharding.Start(
                typeName: "customer",
                entityProps: Props.Create<Customer>(),
                settings: ClusterShardingSettings.Create(system),
                messageExtractor: new MessageExtractor(10));

            Thread.Sleep(5000);
            Console.Write("Press ENTER to start producing messages...");
            Console.ReadLine();

            ProduceMessages(system, shardRegion);
        }

        private static void ProduceMessages(ActorSystem system, IActorRef shardRegion)
        {
            var customers = new[] { "Yoda", "Obi-Wan", "Darth Vader", "Princess Leia", "Luke Skywalker", "Jar Jar Binks", "R2D2", "Han Solo", "Chewbacca", "Jabba" };
            var items = new[] { "Yoghurt", "Fruits", "Lightsaber", "Fluffy toy", "Dreamcatcher", "Candies", "Cigars", "Chicken nuggets", "French fries" };

            system.Scheduler.Advanced.ScheduleRepeatedly(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3), () =>
            {
                var customer = PickRandom(customers);
                var item = PickRandom(items);
                var message = new ShardEnvelope(customer, new Customer.PurchaseItem(item));

                shardRegion.Tell(message);
            });
        }

        private static T PickRandom<T>(T[] items) => items[ThreadLocalRandom.Current.Next(items.Length)];
    }
}
