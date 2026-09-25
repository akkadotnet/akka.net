//-----------------------------------------------------------------------
// <copyright file="AotPipeToActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.AOT.App.Actors;

/// <summary>
/// Pipes the result of a completed <see cref="Task"/> back to the asker.
/// </summary>
public sealed class AotPipeToActor : ReceiveActor
{
    public AotPipeToActor()
    {
        Receive<string>(msg => Task.FromResult($"piped:{msg}").PipeTo(Sender));
    }
}
