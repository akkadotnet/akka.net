//-----------------------------------------------------------------------
// <copyright file="Publisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#region SamplePublisher
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;

namespace SamplePublisher
{
    public sealed class Publisher: ReceiveActor
    {
        public Publisher()
        {
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            Receive<string>(input => mediator.Tell(new Publish("content", input.ToUpperInvariant())));
        }
    }
}
#endregion
