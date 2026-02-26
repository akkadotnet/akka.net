// -----------------------------------------------------------------------
//  <copyright file="AotUntypedActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.AOT.App.Actors;

public class AotUntypedActor : UntypedActor
{
    protected override void OnReceive(object message)
    { 
        Sender.Tell(message);
    }
}