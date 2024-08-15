//-----------------------------------------------------------------------
// <copyright file="WorkLoadCounter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;

namespace ClusterToolsExample.Shared;

public class WorkLoadCounter : ReceiveActor
{
    public WorkLoadCounter()
    {
        var counts = new Dictionary<IActorRef, int>();

        Receive<Result>(_ =>
        {
            if (counts.TryGetValue(Sender, out var count))
                counts[Sender] = ++count;
            else
                counts.Add(Sender, 1);
        });

        Receive<SendReport>(_ => Sender.Tell(new Report(counts)));
    }
}
