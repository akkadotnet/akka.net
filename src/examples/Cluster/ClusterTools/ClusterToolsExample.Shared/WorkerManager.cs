//-----------------------------------------------------------------------
// <copyright file="WorkerManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Event;
using Akka.Routing;

namespace ClusterToolsExample.Shared
{
    public class WorkerManager : ReceiveActor
    {
        private readonly Random random = new Random();
        public WorkerManager()
        {
            var log = Context.GetLogger();
            var counter = Context.ActorOf<WorkLoadCounter>(name: "workload-counter");
            var workerRouter = GetWorkerRouter(counter);

            Receive<Batch>(batch =>
            {
                log.Info("Generating a batch of size {0}", batch.Size);
                for (int i = 0; i < batch.Size; i++)
                    workerRouter.Tell(new Work(i));

                Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(10), Self, GetBatch(), Self);
            });

            Receive<Report>(report =>
            {
                foreach (var entry in report.Counts)
                {
                    var key = entry.Key;
                    var value = entry.Value;
                    var name = (string.IsNullOrEmpty(key.Path.Address.Host) ? "local/" : key.Path.Address.Host + ":" + key.Path.Address.Port + "/") + key.Path.Name;

                    log.Info("{0} -> {1}", name, value);
                }
            });

            Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(3), Self, GetBatch(), Self);
            Context.System.Scheduler.ScheduleTellRepeatedly(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), counter, SendReport.Instance, Self);
        }

        private IActorRef GetWorkerRouter(IActorRef counter)
        {
            return Context.ActorOf(new ClusterRouterPool(
                local: new RoundRobinPool(10),
                settings: new ClusterRouterPoolSettings(30, 10, true, null))
                .Props(Props.Create(() => new Worker(counter))));
        }

        private Batch GetBatch()
        {
            return new Batch(random.Next(5) + 5);
        }
    }
}
