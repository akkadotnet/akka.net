//-----------------------------------------------------------------------
// <copyright file="AotPipeToActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.AOT.App.Actors;

/// <summary>
/// Pipes the result of a Task that completes on a real async continuation back to the asker -
/// Task.FromResult would complete synchronously and never exercise PipeTo's continuation path.
/// </summary>
public sealed class AotPipeToActor : ReceiveActor
{
    public AotPipeToActor()
    {
        Receive<string>(msg => Task.Delay(10).ContinueWith(_ => $"piped:{msg}").PipeTo(Sender));
    }
}
