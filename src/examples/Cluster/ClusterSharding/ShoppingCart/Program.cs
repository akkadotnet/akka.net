//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Bootstrap.Docker;
using Akka.Cluster;
using Akka.Cluster.Sharding;
using Akka.Cluster.Sharding.Delivery;
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
            var sharding = Akka.Cluster.Sharding.ClusterSharding.Get(system);
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
                        entityPropsFactory: e => 
                            // ShardingConsumerController guarantees processing of messages,
                            // even across process restarts / shutdowns or shard rebalancing
                            ShardingConsumerController.Create<Customer.ICustomerCommand>( 
                                c => Props.Create(() => new Customer(e,c)), 
                                ShardingConsumerController.Settings.Create(system)),
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
            var producerId = "ProducerId1" + MurmurHash.StringHash(Cluster.Get(system).SelfAddress.ToString());
            
            var shardingProducerController = system.ActorOf(ShardingProducerController.Create<Customer.ICustomerCommand>(
                producerId,
                shardRegionProxy,
                Option<Props>.None, 
                ShardingProducerController.Settings.Create(system)), "shardingProducerController-1");

            var producer = system.ActorOf(Props.Create(() => new Producer()), "msg-producer");
            shardingProducerController.Tell(new ShardingProducerController.Start<Customer.ICustomerCommand>(producer));
        }
        #endregion

       
    }
}
