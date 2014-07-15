﻿using System;
using System.Collections.Generic;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Routing
{
    public class SmallestMailboxRoutingLogic : RoutingLogic
    {
        private int next = -1;

        public override Routee Select(object message, Routee[] routees)
        {
            return routees == null || routees.Length == 0
                ? Routee.NoRoutee
                : SelectNext(routees);
        }

        /// <summary>
        /// Select a next best route to handle a message, based on priority of interests:
        /// 1. Actors without any messages.
        /// 2. Actors of known messages count, lower is better.
        /// 4. Actors of unknown message count.
        /// For the case, when all routees are of unpredictable size, it falls back to round robin.
        /// </summary>
        private Routee SelectNext(Routee[] routees)
        {
            var winningScore = long.MaxValue;

            // round robin fallback
            var winner = routees[Interlocked.Increment(ref next) % routees.Length];

            for (int i = 0; i < routees.Length; i++)
            {
                var routee = routees[i];
                var cell = TryGetActorCell(routee);
                if (cell != null)
                {
                    // routee can be reasoned about it's mailbox size
                    var score = cell.NumberOfMessages;
                    if (score == 0)
                    {
                        // no messages => instant win    
                        return routee;
                    }

                    if (winningScore > score)
                    {
                        winningScore = score;
                        winner = routee;
                    }
                }
            }

            return winner;
        }

        private Cell TryGetActorCell(Routee routee)
        {
            var refRoutee = routee as ActorRefRoutee;
            if (refRoutee != null)
            {
                var actorRef = refRoutee.Actor as ActorRefWithCell;
                if (actorRef != null)
                {
                    return actorRef.Underlying;
                }
            }
            return null;
        }
    }

    public class SmallestMailboxPool : Pool
    {
        /// <summary>
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="resizer">The resizer.</param>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        /// <param name="routerDispatcher">The router dispatcher.</param>
        /// <param name="usePoolDispatcher">if set to <c>true</c> [use pool dispatcher].</param>
        public SmallestMailboxPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy,
            string routerDispatcher, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
        }
        
        public SmallestMailboxPool(Config config) : base(config)
        {
            
        }

        [Obsolete("for serialization only",true)]
        public SmallestMailboxPool()
        {
            
        }

        public SmallestMailboxPool(int nrOfInstances) : base(nrOfInstances, null, Pool.DefaultStrategy, null) { }

        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new SmallestMailboxRoutingLogic());
        }
    }
}