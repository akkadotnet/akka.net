//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            Config config = @"
            akka {
              actor {
                provider = cluster
                serializers {
                  hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                }
                serialization-bindings {
                  ""System.Object"" = hyperion
                }
              }
              remote {
                dot-netty.tcp {
                  public-hostname = ""localhost""
                  hostname = ""localhost""
                  port = 0
                }
              }
              cluster {
                auto-down-unreachable-after = 5s
                sharding {
                  least-shard-allocation-strategy.rebalance-threshold = 3
                }
              }
              persistence {
                journal {
                  plugin = ""akka.persistence.journal.sqlite""
                  sqlite {
                    connection-string = ""Datasource=store.db""
                    auto-initialize = true
                  }
                }
                snapshot-store {
                  plugin = ""akka.persistence.snapshot-store.sqlite""
                  sqlite {
                    connection-string = ""Datasource=store.db""
                    auto-initialize = true
                  }
                }
              }
            }";

            using (var system = ActorSystem.Create("sharded-cluster-system", config.WithFallback(ClusterSingletonManager.DefaultConfig())))
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
