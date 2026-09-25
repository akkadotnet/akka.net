//-----------------------------------------------------------------------
// <copyright file="AotWatcherActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.AOT.App.Actors;

/// <summary>
/// Watches the actor it is handed, stops it, and replies once <see cref="Terminated"/> arrives.
/// </summary>
public sealed class AotWatcherActor : ReceiveActor
{
    private IActorRef _replyTo = ActorRefs.Nobody;

    public AotWatcherActor()
    {
        Receive<IActorRef>(target =>
        {
            _replyTo = Sender;
            Context.Watch(target);
            Context.Stop(target);
        });
        Receive<Terminated>(t => _replyTo.Tell($"terminated:{t.ActorRef.Path.Name}"));
    }
}
