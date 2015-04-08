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