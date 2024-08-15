﻿// -----------------------------------------------------------------------
//  <copyright file="Follower.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;

namespace DocsExamples.Actor.UntypedActorAPI;

#region UntypedActor

public class Follower : UntypedActor
{
    private readonly string identifyId = "1";

    public Follower()
    {
        Context.ActorSelection("/user/another").Tell(new Identify(identifyId));
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case ActorIdentity a when a.MessageId.Equals(identifyId) && a.Subject != null:
                Context.Watch(a.Subject);
                Context.Become(Active(a.Subject));
                break;
            case ActorIdentity a when a.MessageId.Equals(identifyId) && a.Subject == null:
                Context.Stop(Self);
                break;
        }
    }

    public UntypedReceive Active(IActorRef another)
    {
        return message =>
        {
            if (message is Terminated t && t.ActorRef.Equals(another)) Context.Stop(Self);
        };
    }
}

#endregion