//-----------------------------------------------------------------------
// <copyright file="ActorRefFactoryExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    public static class ActorRefFactoryExtensions
    {
        public static IActorRef ActorOf<TActor>(this IActorRefFactory factory, string name = null) where TActor : ActorBase, new()
        {
            return factory.ActorOf(Props.Create<TActor>(), name: name);
        }

    }
}
