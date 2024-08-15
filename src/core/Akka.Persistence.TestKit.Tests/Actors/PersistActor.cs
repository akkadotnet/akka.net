// -----------------------------------------------------------------------
//  <copyright file="PersistActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Persistence.TestKit.Tests;

public class PersistActor : UntypedPersistentActor
{
    private readonly IActorRef _probe;

    public PersistActor(IActorRef probe)
    {
        _probe = probe;
    }

    public override string PersistenceId => "foo";

    protected override void OnCommand(object message)
    {
        switch (message)
        {
            case WriteMessage msg:
                Persist(msg.Data, _ => { _probe.Tell("ack"); });

                break;

            default:
                return;
        }
    }

    protected override void OnRecover(object message)
    {
        _probe.Tell(message);
    }

    protected override void OnPersistFailure(Exception cause, object @event, long sequenceNr)
    {
        _probe.Tell("failure");

        base.OnPersistFailure(cause, @event, sequenceNr);
    }

    protected override void OnPersistRejected(Exception cause, object @event, long sequenceNr)
    {
        _probe.Tell("rejected");

        base.OnPersistRejected(cause, @event, sequenceNr);
    }

    public class WriteMessage
    {
        public WriteMessage(string data)
        {
            Data = data;
        }

        public string Data { get; }
    }
}