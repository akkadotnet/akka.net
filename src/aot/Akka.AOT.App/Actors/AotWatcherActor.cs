//-----------------------------------------------------------------------
// <copyright file="AotWatcherActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.AOT.App.Actors;

/// <summary>
/// Watches the actor it is handed, replies "watching" once <see cref="Context.Watch"/> has run, then
/// replies again once <see cref="Terminated"/> arrives. The caller stops the watched actor itself,
/// only after seeing "watching", so the test always exercises the live-watch path rather than racing
/// a stop against the watch registration.
/// </summary>
public sealed class AotWatcherActor : ReceiveActor
{
    private IActorRef _replyTo = ActorRefs.Nobody;

    public AotWatcherActor()
    {
        Receive<IActorRef>(target =>
        {
            Context.Watch(target);
            Sender.Tell("watching");
        });
        Receive<string>(_ => _replyTo = Sender);
        Receive<Terminated>(t => _replyTo.Tell($"terminated:{t.ActorRef.Path.Name}"));
    }
}
