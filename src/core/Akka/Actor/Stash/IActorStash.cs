//-----------------------------------------------------------------------
// <copyright file="IActorStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
#if DNXCORE50
using System.Reflection;
#endif

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

    public class ActorStashPlugin : ActorProducerPluginBase
    {
        /// <summary>
        /// Stash plugin is applied to all actors implementing <see cref="IActorStash"/> interface.
        /// </summary>
        public override bool CanBeAppliedTo(Type actorType)
        {
            return typeof (IActorStash).IsAssignableFrom(actorType);
        }

        /// <summary>
        /// Creates a new stash for specified <paramref name="actor"/> if it has not been initialized already.
        /// </summary>
        public override void AfterIncarnated(ActorBase actor, IActorContext context)
        {
            var stashed = actor as IActorStash;
            if (stashed != null && stashed.Stash == null)
            {
                stashed.Stash = context.CreateStash(actor.GetType());
            }
        }

        /// <summary>
        /// Ensures, that all stashed messages inside <paramref name="actor"/> stash have been unstashed.
        /// </summary>
        public override void BeforeIncarnated(ActorBase actor, IActorContext context)
        {
            var actorStash = actor as IActorStash;
            if (actorStash != null)
            {
                actorStash.Stash.UnstashAll();
            }
        }
    }
}

