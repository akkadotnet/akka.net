//-----------------------------------------------------------------------
// <copyright file="ReplyActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;

namespace Akka.Hosting.TestKit.Tests.TestActorRefTests;

public class ReplyActor : TActorBase
{
    private IActorRef? _replyTo;

    protected override bool ReceiveMessage(object message)
    {
        var strMessage = message as string;
        switch(strMessage)
        {
            case "complexRequest":
                _replyTo = Sender;
                var worker = new TestActorRef<WorkerActor>(System, Props.Create<WorkerActor>());
                worker.Tell("work");
                return true;
            case "complexRequest2":
                var worker2 = new TestActorRef<WorkerActor>(System, Props.Create<WorkerActor>());
                worker2.Tell(Sender, Self);
                return true;
            case "workDone":
                if (_replyTo is null)
                    throw new NullReferenceException("_replyTo is null, make sure that \"complexRequest\" is sent first");
                
                _replyTo.Tell("complexReply", Self);
                return true;
            case "simpleRequest":
                Sender.Tell("simpleReply", Self);
                return true;
        }
        return false;
    }
}