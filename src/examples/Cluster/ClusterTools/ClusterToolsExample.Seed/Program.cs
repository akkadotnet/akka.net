//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.Singleton;
using ClusterToolsExample.Shared;

namespace ClusterToolsExample.Seed
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Seed node";
            using (var system = ActorSystem.Create("singleton-cluster-system"))
            {
                //RunClusterSingletonSeed(system);
                RunDistributedPubSubSeed(system);
                //RunClusterClientSeed(system);

                Console.ReadLine();
            }
        }

        /// <summary>
        /// Initializes cluster singleton of the <see cref="WorkerManager"/> actor.
        /// </summary>
        /// <param name="system"></param>
        static void RunClusterSingletonSeed(ActorSystem system)
        {
            var aref = system.ActorOf(ClusterSingletonManager.Props(
                singletonProps: Props.Create(() => new WorkerManager()),
                terminationMessage: PoisonPill.Instance,
                settings: ClusterSingletonManagerSettings.Create(system)),
                name: "manager");
        }

        /// <summary>
        /// Starts a job, which publishes <see cref="Echo"/> message to distributed cluster pub sub in 5 sec periods.
        /// </summary>
        static void RunDistributedPubSubSeed(ActorSystem system)
        {
            var mediator = DistributedPubSub.Get(system).Mediator;

            system.Scheduler.ScheduleTellRepeatedly(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), mediator,
                new Publish("echo", new Echo("hello world")), ActorRefs.NoSender);
        }

        /// <summary>
        /// Starts a job, which establishes cluster client receptionist for target <see cref="EchoReceiver"/> actor,
        /// making it visible from outside of the cluster.
        /// </summary>
        static void RunClusterClientSeed(ActorSystem system)
        {
            var receptionist = ClusterClientReceptionist.Get(system);
            receptionist.RegisterService(system.ActorOf(Props.Create<EchoReceiver>(), "my-service"));
        }
    }
}
