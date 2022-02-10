//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Bootstrap.Docker;
using Akka.Cluster;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Persistence.Sqlite;
using Akka.Util;

namespace ClusterSharding.Node
{
    using ClusterSharding = Akka.Cluster.Sharding.ClusterSharding;

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            #region Console shutdown setup
            
            var exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                exitEvent.Set();
            };
            AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
            {
                exitEvent.Set();
            };
            
            #endregion
            
            var config = ConfigurationFactory.ParseString(await File.ReadAllTextAsync("app.conf"))
                .BootstrapFromDocker()
                .WithFallback(ClusterSingletonManager.DefaultConfig())
                .WithFallback(SqlitePersistence.DefaultConfiguration());

            var system = ActorSystem.Create("sharded-cluster-system", config);
            
            var sharding = ClusterSharding.Get(system);
            var shardRegion = await sharding.StartAsync(
                typeName: "customer",
                entityPropsFactory: e => Props.Create(() => new Customer(e)),
                settings: ClusterShardingSettings.Create(system),
                messageExtractor: new MessageExtractor(10));

            var cluster = Cluster.Get(system);
            cluster.RegisterOnMemberUp(() =>
            {
                ProduceMessages(system, shardRegion);
            });

            exitEvent.WaitOne();
            await system.Terminate();
        }

        private static void ProduceMessages(ActorSystem system, IActorRef shardRegion)
        {
            var customers = new[] { "Yoda", "Obi-Wan", "Darth Vader", "Princess Leia", "Luke Skywalker", "R2D2", "Han Solo", "Chewbacca", "Jabba" };
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
