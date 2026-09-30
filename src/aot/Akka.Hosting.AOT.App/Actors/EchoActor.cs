//-----------------------------------------------------------------------
// <copyright file="EchoActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Hosting.AOT.App.Actors;

/// <summary>
/// Registered into the <see cref="ActorRegistry"/> from <c>WithActors</c> and resolved back out
/// through <see cref="IRequiredActor{TActor}"/> from DI - the common "register once, inject
/// everywhere" Hosting idiom.
/// </summary>
internal sealed class EchoActor : ReceiveActor
{
    public EchoActor()
    {
        Receive<string>(msg => Sender.Tell($"echo:{msg}"));
    }
}
