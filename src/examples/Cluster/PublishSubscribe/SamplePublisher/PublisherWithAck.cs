// -----------------------------------------------------------------------
//  <copyright file="PublisherWithAck.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#region SamplePublisherWithAck
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;

namespace SamplePublisher;

public class PublisherWithAck: ReceiveActor
{
    public PublisherWithAck()
    {
        var log = Context.GetLogger();
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        
        Receive<string>(input => mediator.Tell(
            new PublishWithAck("content", input.ToUpperInvariant(), TimeSpan.FromSeconds(30))));
        
        Receive<PublishSucceeded>(success => log.Info(
            "Published {0} to topic {1}.", success.Message.Message, success.Message.Topic));
        
        Receive<PublishFailed>(fail => log.Error(
            "Failed to publish {0} to topic {1}. Reason: {2}", fail.Message.Message, fail.Message.Topic, fail.Reason));
    }
}
#endregion