//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.Singleton;
using ClusterToolsExample.Shared;

namespace ClusterToolsExample.Seed;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.Title = "Seed node";
        var config = await File.ReadAllTextAsync("akka.conf");
        using var system = ActorSystem.Create("singleton-cluster-system", config);

        //RunClusterSingletonSeed(system);
        RunDistributedPubSubSeed(system);
        //RunClusterClientSeed(system);

        Console.ReadLine();
    }

    /// <summary>
    /// Initializes cluster singleton of the <see cref="WorkerManager"/> actor.
    /// </summary>
    /// <param name="system"></param>
    private static void RunClusterSingletonSeed(ActorSystem system)
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
    private static void RunDistributedPubSubSeed(ActorSystem system)
    {
        var mediator = DistributedPubSub.Get(system).Mediator;

        system.Scheduler.ScheduleTellRepeatedly(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), mediator,
            new Publish("echo", new Echo("hello world")), ActorRefs.NoSender);
    }

    /// <summary>
    /// Starts a job, which establishes cluster client receptionist for target <see cref="EchoReceiver"/> actor,
    /// making it visible from outside of the cluster.
    /// </summary>
    private static void RunClusterClientSeed(ActorSystem system)
    {
        var receptionist = ClusterClientReceptionist.Get(system);
        receptionist.RegisterService(system.ActorOf(Props.Create<EchoReceiver>(), "my-service"));
    }
}
