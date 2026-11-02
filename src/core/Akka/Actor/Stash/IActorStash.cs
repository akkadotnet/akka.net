//-----------------------------------------------------------------------
// <copyright file="IActorStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;

namespace Akka.Actor
{
    /// <summary>
    /// Marker interface for adding stash support
    /// </summary>
    public interface IActorStash
    {

        /// <summary>
        /// Gets or sets the stash. This will be automatically populated by the framework AFTER the constructor has been run.
        /// Implement this as an auto property.
        /// </summary>
        /// <value>
        /// The stash.
        /// </value>
        IStash Stash { get; set; }
    }

    /// <summary>
    /// An <see cref="ActorProducerPluginBase"/> that automatically initializes and manages the
    /// <see cref="IStash"/> for actors implementing <see cref="IActorStash"/>.
    /// </summary>
    public class ActorStashPlugin : ActorProducerPluginBase
    {
        /// <summary>
        /// Stash plugin is applied to all actors implementing <see cref="IActorStash"/> interface.
        /// </summary>
        /// <param name="actorType">The actor <see cref="Type"/> to check.</param>
        /// <returns><see langword="true"/> if <paramref name="actorType"/> implements <see cref="IActorStash"/>; otherwise <see langword="false"/>.</returns>
        public override bool CanBeAppliedTo(Type actorType)
        {
            return typeof (IActorStash).IsAssignableFrom(actorType);
        }

        /// <summary>
        /// Creates a new stash for specified <paramref name="actor"/> if it has not been initialized already.
        /// </summary>
        /// <param name="actor">The actor instance being created.</param>
        /// <param name="context">The context of the actor being created.</param>
        public override void AfterIncarnated(ActorBase actor, IActorContext context)
        {
            if (actor is IActorStash stashed && stashed.Stash == null)
            {
                stashed.Stash = context.CreateStash(actor.GetType());
            }
        }

        /// <summary>
        /// Ensures, that all stashed messages inside <paramref name="actor"/> stash have been unstashed.
        /// </summary>
        /// <param name="actor">The actor instance being recycled.</param>
        /// <param name="context">The context of the actor being recycled.</param>
        public override void BeforeIncarnated(ActorBase actor, IActorContext context)
        {
            if (actor is IActorStash actorStash)
            {
                actorStash.Stash.UnstashAll();
            }
        }
    }
}

