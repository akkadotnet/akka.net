//-----------------------------------------------------------------------
// <copyright file="ActorEventBus.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// Class ActorEventBus.
    /// </summary>
    /// <typeparam name="TEvent">The type of the t event.</typeparam>
    /// <typeparam name="TClassifier">The type of the t classifier.</typeparam>
    public abstract class ActorEventBus<TEvent, TClassifier> : EventBus<TEvent, TClassifier, IActorRef>
    {
    }
}
