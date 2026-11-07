//-----------------------------------------------------------------------
// <copyright file="AotFsmActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.AOT.App.Actors;

public enum AotFsmState
{
    Idle,
    Active
}

/// <summary>
/// One transition: Idle -&gt; Active on "go".
/// </summary>
public sealed class AotFsmActor : FSM<AotFsmState, NotUsed>
{
    public AotFsmActor()
    {
        StartWith(AotFsmState.Idle, NotUsed.Instance);

        When(AotFsmState.Idle, e => e.FsmEvent is "go"
            ? GoTo(AotFsmState.Active).Replying("started")
            : Stay());

        When(AotFsmState.Active, e => Stay().Replying($"active:{e.FsmEvent}"));

        Initialize();
    }
}
