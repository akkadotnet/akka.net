//-----------------------------------------------------------------------
// <copyright file="AotTimerActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.AOT.App.Actors;

/// <summary>
/// Starts a single one-shot timer on request and replies once it fires.
/// </summary>
public sealed class AotTimerActor : ReceiveActor, IWithTimers
{
    private sealed record Tick(IActorRef ReplyTo);

    public ITimerScheduler Timers { get; set; } = null!;

    public AotTimerActor()
    {
        Receive<string>(_ => Timers.StartSingleTimer("tick", new Tick(Sender), TimeSpan.FromMilliseconds(50)));
        Receive<Tick>(t => t.ReplyTo.Tell("ticked"));
    }
}
