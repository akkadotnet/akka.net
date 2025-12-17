//-----------------------------------------------------------------------
// <copyright file="WorkerManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Event;
using Akka.Routing;

namespace ClusterToolsExample.Shared;

public class WorkerManager : ReceiveActor, IWithTimers
{
    private const string BatchKey = nameof(BatchKey);
    private const string ReportKey = nameof(ReportKey);
    
    private readonly Random _random = new();
    
    public WorkerManager()
    {
        var log = Context.GetLogger();
        var counter = Context.ActorOf<WorkLoadCounter>(name: "workload-counter");
        var workerRouter = GetWorkerRouter(counter);

        Receive<Batch>(batch =>
        {
            log.Info("Generating a batch of size {0}", batch.Size);
            for (var i = 0; i < batch.Size; i++)
                workerRouter.Tell(new Work(i));

            Timers.StartSingleTimer(BatchKey, GetBatch(), TimeSpan.FromSeconds(10));
        });

        Receive<Report>(report =>
        {
            foreach (var (actor, count) in report.Counts)
            {
                var name = (string.IsNullOrEmpty(actor.Path.Address.Host) 
                    ? "local/" 
                    : actor.Path.Address.Host + ":" + actor.Path.Address.Port + "/") + actor.Path.Name;

                log.Info("{0} -> {1}", name, count);
            }
        });

        Receive<SendReport>(msg =>
        {
            counter.Tell(msg);
        });

        Timers.StartSingleTimer(BatchKey, GetBatch(), TimeSpan.FromSeconds(3));
        Timers.StartPeriodicTimer(ReportKey, SendReport.Instance, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
    }

    public ITimerScheduler Timers { get; set; }

    private IActorRef GetWorkerRouter(IActorRef counter)
    {
        return Context.ActorOf(new ClusterRouterPool(
                local: new RoundRobinPool(10),
                settings: new ClusterRouterPoolSettings(30, 10, true, null))
            .Props(Props.Create(() => new Worker(counter))));
    }

    private Batch GetBatch()
    {
        return new Batch(_random.Next(5) + 5);
    }
}
