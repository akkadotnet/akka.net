//-----------------------------------------------------------------------
// <copyright file="ActorDslExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Dsl;

namespace Akka.TestKit
{
    public static class ActorDslExtensions
    {
        public static void Receive(this IActorDsl config, string message, Action<string, IActorContext> handler)
        {
            config.Receive<string>(m=>string.Equals(m,message,StringComparison.Ordinal), handler);
        }
    }
}

