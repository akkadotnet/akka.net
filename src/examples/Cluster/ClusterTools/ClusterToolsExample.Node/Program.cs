//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.Singleton;
using ClusterToolsExample.Shared;

namespace ClusterToolsExample.Node
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Attached node";
            using (var system = ActorSystem.Create("singleton-cluster-system"))
            {
                //RunClusterSingletonClient(system);
                RunDistributedPubSubClient(system);
                //RunClusterClient(system);

                Console.ReadLine();
            }
        }

        /// <summary>
        /// Creates a proxy to communicate with cluster singleton initialized by the seed.
        /// </summary>
        static void RunClusterSingletonClient(ActorSystem system)
        {
            var proxyRef = system.ActorOf(ClusterSingletonProxy.Props(
                singletonManagerPath: "/user/manager",
                settings: ClusterSingletonProxySettings.Create(system).WithRole("worker")),
                name: "managerProxy");
        }

        /// <summary>
        /// Creates an <see cref="EchoReceiver"/> actor which subscribes to the distributed pub/sub topic.
        /// This topic is filled with messages from the cluster seed job.
        /// </summary>
        static void RunDistributedPubSubClient(ActorSystem system)
        {
            var echo = system.ActorOf(Props.Create(() => new EchoReceiver()));
            echo.Tell(new object());
        }

        /// <summary>
        /// Creates a cluster client, that allows to connect to cluster even thou current actor system is not part of it.
        /// </summary>
        static void RunClusterClient(ActorSystem system)
        {
            //NOTE: to properly run cluster client set up actor ref provider for nodes on `provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"`
            system.Settings.InjectTopLevelFallback(ClusterClientReceptionist.DefaultConfig());
            var clusterClient = system.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(system)));
            clusterClient.Tell(new ClusterClient.Send("/user/my-service", new Echo("hello from cluster client")));
        }
    }
}
