//-----------------------------------------------------------------------
// <copyright file="Worker.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;

namespace ClusterToolsExample.Shared
{
    public class Worker : ReceiveActor
    {
        public Worker(IActorRef counter)
        {
            var log = Context.GetLogger();

            Receive<Work>(work =>
            {
                var result = new Result(work.Id);
                log.Info("Worker {0} - result: {1}", Self.Path.Name, result.Id);
                Sender.Tell(result);
                counter.Tell(result);
            });
        }
    }
}
