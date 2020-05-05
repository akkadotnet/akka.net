//-----------------------------------------------------------------------
// <copyright file="BackendActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Samples.Cluster.ConsistentHashRouting
{
    public class BackendActor : UntypedActor
    {
        protected Akka.Cluster.Cluster Cluster = Akka.Cluster.Cluster.Get(Context.System);

        protected override void OnReceive(object message)
        {
            if (message is FrontendCommand)
            {
                var command = message as FrontendCommand;
                Console.WriteLine("Backend [{0}]: Received command {1} for job {2} from {3}", Cluster.SelfAddress, command.Message, command.JobId, Sender);
                Sender.Tell(new CommandComplete());
            }
            else
            {
                Unhandled(message);
            }
        }
    }
}

