//-----------------------------------------------------------------------
// <copyright file="ActorEventBus.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// Represents an EventBus where the Subscriber type is ActorRef.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <typeparam name="TClassifier">The classifier type.</typeparam>
    public abstract class ActorEventBus<TEvent, TClassifier> : EventBus<TEvent, TClassifier, IActorRef>
    {
    }
}

