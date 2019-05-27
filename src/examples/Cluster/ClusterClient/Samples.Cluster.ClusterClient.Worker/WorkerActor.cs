//-----------------------------------------------------------------------
// <copyright file="WorkerActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Samples.Cluster.ClusterClient.Messages;

namespace Samples.Cluster.ClusterClient.Worker
{
    public class WorkerActor : ReceiveActor
    {
        private readonly Akka.Cluster.Cluster _cluster = Akka.Cluster.Cluster.Get(Context.System);
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public WorkerActor()
        {
            Receive<Ping>(p =>
            {
                _log.Info("[{0}] Received [{1}] from {2}", _cluster.SelfAddress, p.Msg, Sender);
                Sender.Tell(new Pong(p.Msg, _cluster.SelfAddress));
            });
        }
    }
}
