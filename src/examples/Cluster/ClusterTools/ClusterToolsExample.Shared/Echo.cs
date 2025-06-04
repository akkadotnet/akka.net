//-----------------------------------------------------------------------
// <copyright file="Echo.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Tools.PublishSubscribe;

namespace ClusterToolsExample.Shared;

public sealed class Echo
{
    public readonly string Message;

    public Echo(string message)
    {
        Message = message;
    }
}

public class EchoReceiver : ReceiveActor
{
    private const string Topic = "echo";

    private readonly Cluster _cluster = Cluster.Get(Context.System);
    private readonly IActorRef _mediator = DistributedPubSub.Get(Context.System).Mediator;

    public EchoReceiver()
    {
        Receive<Echo>(echo => Console.WriteLine(echo.Message));
        Receive<SubscribeAck>(ack =>
            Console.WriteLine("Actor [{0}] has subscribed to topic [{1}]", ack.Subscribe.Ref, ack.Subscribe.Topic));
    }

    protected override void PreStart()
    {
        base.PreStart();
        _mediator.Tell(new Subscribe(Topic, Self));
    }

    protected override void PostStop()
    {
        _mediator.Tell(new Unsubscribe(Topic, Self));
        base.PostStop();
    }
}
