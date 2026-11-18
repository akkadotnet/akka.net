//-----------------------------------------------------------------------
// <copyright file="ResizablePoolActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Routing
{
    /// <summary>
    /// INTERNAL API.
    /// 
    /// Defines <see cref="Pool"/> routers who can resize the number of routees
    /// they use based on a defined <see cref="Resizer"/>
    /// </summary>
    internal class ResizablePoolActor : RouterPoolActor
    {
        /// <summary>
        /// Creates a resizable pool router actor with the supplied supervision strategy.
        /// </summary>
        /// <param name="supervisorStrategy">The strategy used to supervise routees.</param>
        public ResizablePoolActor(SupervisorStrategy supervisorStrategy) : base(supervisorStrategy)
        {
        }

        /// <summary>
        /// The resizable pool cell hosting this actor.
        /// </summary>
        /// <exception cref="ActorInitializationException">The actor is not running inside a resizable pool cell.</exception>
        protected ResizablePoolCell ResizerCell
        {
            get
            {
                return Context is ResizablePoolCell resizablePoolCell
                    ? resizablePoolCell : throw new ActorInitializationException($"Resizable router actor can only be used when resizer is defined, not in {Context.GetType()}");
            }
        }

        /// <summary>
        /// Processes resize commands and delegates other messages to the pool router actor.
        /// </summary>
        /// <param name="message">The resize command or other message to handle.</param>
        protected override void OnReceive(object message)
        {
            if (message is Resize && ResizerCell != null)
            {
                ResizerCell.Resize(false);
            }
            else
            {
                base.OnReceive(message);
            }
        }

        /// <summary>
        /// Keeps the resizable router alive when all current routees are removed.
        /// </summary>
        protected override void StopIfAllRouteesRemoved()
        {
            //we don't care if routees are removed
        }
    }

    /// <summary>
    /// Command used to resize a <see cref="ResizablePoolActor"/>
    /// </summary>
    public class Resize : RouterManagementMessage
    {
    }
}
