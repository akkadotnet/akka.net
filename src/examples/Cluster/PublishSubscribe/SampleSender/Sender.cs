﻿// -----------------------------------------------------------------------
//  <copyright file="Sender.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#region SampleSender

using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;

namespace SampleSender;

public sealed class Sender : ReceiveActor
{
    public Sender()
    {
        // activate the extension
        var mediator = DistributedPubSub.Get(Context.System).Mediator;

        Receive<string>(str =>
        {
            var upperCase = str.ToUpper();
            mediator.Tell(new Send("/user/destination", upperCase, true));
        });
    }
}

#endregion