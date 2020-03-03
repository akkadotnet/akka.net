//-----------------------------------------------------------------------
// <copyright file="ActorEventBus.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// This class represents an <see cref="EventBus{TEvent,TClassifier,TSubscriber}"/> where the subscriber type is an <see cref="IActorRef"/>.
    /// </summary>
    /// <typeparam name="TEvent">The type of event published to the bus.</typeparam>
    /// <typeparam name="TClassifier">The type of classifier used to classify events.</typeparam>
    public abstract class ActorEventBus<TEvent, TClassifier> : EventBus<TEvent, TClassifier, IActorRef>
    {
    }
}
