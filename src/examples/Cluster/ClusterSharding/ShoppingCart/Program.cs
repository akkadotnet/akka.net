//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Bootstrap.Docker;
using Akka.Cluster;
using Akka.Cluster.Sharding;
using Akka.Configuration;
using Akka.Util;

namespace ShoppingCart
{
    public static class Program
    {
        private const string FrontEndRole = "frontend";
        private const string BackEndRole = "backend";
        
        public static async Task<int> Main(string[] args)
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

            // We will create a single node with the role "frontend" and three nodes with the role "backend"
            // using docker containers and docker-compose
            // Frontend will periodically send a purchase order to the backend nodes

            #region RoleSetup
            var role = Environment.GetEnvironmentVariable("IS_FRONTEND") == "true" 
                ? FrontEndRole : BackEndRole;
            
            var config = ConfigurationFactory.ParseString(@$"
                    # We need to tell Akka to provide us cluster enabled actors
                    akka.actor.provider = cluster

                    # This tells Akka which role this node belongs to
                    akka.cluster.roles=[{role}]

                    # This tells Akka to wait for at least 4 nodes joining the cluster 
                    # before signaling that it is up and ready
                    akka.cluster.min-nr-of-members = 4")
                .BootstrapFromDocker();

            var system = ActorSystem.Create("shopping-cart", config);
            #endregion
            
            #region StartSharding
            // Starts and initialize cluster sharding
            var sharding = ClusterSharding.Get(system);
            #endregion

            #region StartShardRegion
            switch (role)
            {
                case FrontEndRole:
                    // Depending on the role, we will start a shard or a shard proxy
                    var shardRegionProxy = await sharding.StartProxyAsync(
                        typeName: "customer",
                        role: BackEndRole,
                        messageExtractor: new MessageExtractor(10));
                
                    // Register a callback for the "cluster is up" event
                    var cluster = Cluster.Get(system);
                    cluster.RegisterOnMemberUp(() =>
                    {
                        ProduceMessages(system, shardRegionProxy);
                    });
                    break;
                
                case BackEndRole:
                    // Depending on the role, we will start a shard or a shard proxy
                    await sharding.StartAsync(
                        typeName: "customer",
                        entityPropsFactory: e => Props.Create(() => new Customer(e)),
                        // .WithRole is important because we're dedicating a specific node role for
                        // the actors to be instantiated in; in this case, we're instantiating only
                        // in the "backend" roled nodes.
                        settings: ClusterShardingSettings.Create(system).WithRole(BackEndRole), 
                        messageExtractor: new MessageExtractor(10));
                    break;
            }
            #endregion

            exitEvent.WaitOne();
            await system.Terminate();
            return 0;
        }

        #region StartSendingMessage
        private static void ProduceMessages(ActorSystem system, IActorRef shardRegionProxy)
        {
            var customers = new[]
            {
                "Yoda", "Obi-Wan", "Darth Vader", "Princess Leia", 
                "Luke Skywalker", "R2D2", "Han Solo", "Chewbacca", "Jabba"
            };
            var items = new[]
            {
                "Yoghurt", "Fruits", "Lightsaber", "Fluffy toy", "Dreamcatcher", 
                "Candies", "Cigars", "Chicken nuggets", "French fries"
            };

            // Start a timer that periodically sends messages to the shard
            system.Scheduler.Advanced
                .ScheduleRepeatedly(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3), () =>
                {
                    var customer = PickRandom(customers);
                    var item = PickRandom(items);
                
                    // A shard message needs to be wrapped inside an envelope so the system knows which
                    // shard and actor it should route the message to.
                    var message = new ShardEnvelope(customer, new Customer.PurchaseItem(item));

                    shardRegionProxy.Tell(message);
                });
        }
        #endregion

        private static T PickRandom<T>(T[] items) => items[ThreadLocalRandom.Current.Next(items.Length)];
    }
}
