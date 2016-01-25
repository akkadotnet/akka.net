//-----------------------------------------------------------------------
// <copyright file="RouterPoolActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Routing
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Actor implementation for <see cref="Pool"/> routers.
    /// </summary>
    internal class RouterPoolActor : RouterActor
    {
        private readonly SupervisorStrategy _supervisorStrategy;

        protected Pool Pool
        {
            get
            {
                if (Cell.RouterConfig is Pool)
                {
                    return Cell.RouterConfig as Pool;
                }
                else
                {
                    throw new ActorInitializationException("RouterPoolActor can only be used with Pool, not " +
                                                           Cell.RouterConfig.GetType());
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RouterPoolActor"/> class.
        /// </summary>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        public RouterPoolActor(SupervisorStrategy supervisorStrategy)
        {
            _supervisorStrategy = supervisorStrategy;
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return _supervisorStrategy;
        }

        /// <summary>
        /// Called when [receive].
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void OnReceive(object message)
        {
            var terminated = message as Terminated;
            if (terminated != null)
            {
                var t = terminated;
                Cell.RemoveRoutee(new ActorRefRoutee(t.ActorRef), false);
                StopIfAllRouteesRemoved();
            }
            else if (message is AdjustPoolSize)
            {
                var poolSize = message as AdjustPoolSize;
                if (poolSize.Change > 0)
                {
                    var newRoutees = Enumerable.Repeat(Pool.NewRoutee(Cell.RouteeProps, Context), poolSize.Change).ToArray();
                    Cell.AddRoutees(newRoutees);
                }
                else if (poolSize.Change < 0)
                {
                    var currentRoutees = Cell.Router.Routees.ToArray();

                    var abandon = currentRoutees
                        .Drop(currentRoutees.Length + poolSize.Change)
                        .ToList();

                    Cell.RemoveRoutees(abandon, true);
                }
            }
            else
            {
                base.OnReceive(message);
            }
        }


    }
}

