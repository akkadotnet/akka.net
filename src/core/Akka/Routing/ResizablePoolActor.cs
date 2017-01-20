﻿//-----------------------------------------------------------------------
// <copyright file="ResizablePoolActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        /// TBD
        /// </summary>
        /// <param name="supervisorStrategy">TBD</param>
        public ResizablePoolActor(SupervisorStrategy supervisorStrategy) : base(supervisorStrategy)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="ActorInitializationException">TBD</exception>
        protected ResizablePoolCell ResizerCell
        {
            get
            {
                var resizablePoolCell = Context as ResizablePoolCell;
                if (resizablePoolCell != null)
                    return resizablePoolCell;
                else 
                    throw new ActorInitializationException($"Resizable router actor can only be used when resizer is defined, not in {Context.GetType()}");
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
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

