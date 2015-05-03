//-----------------------------------------------------------------------
// <copyright file="SmallestMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;

namespace Akka.Routing
{
    public class SmallestMailboxRoutingLogic : RoutingLogic
    {

        public SmallestMailboxRoutingLogic() {}

        public SmallestMailboxRoutingLogic(int next)
        {
            _next = next;
        }

        private int _next;

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
            var winner = routees[(Interlocked.Increment(ref _next) & int.MaxValue) %  routees.Length];

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

        private ICell TryGetActorCell(Routee routee)
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
        public class SmallestMailboxPoolSurrogate : ISurrogate
        {
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new SmallestMailboxPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
            }

            public int NrOfInstances { get; set; }
            public bool UsePoolDispatcher { get; set; }
            public Resizer Resizer { get; set; }
            public SupervisorStrategy SupervisorStrategy { get; set; }
            public string RouterDispatcher { get; set; }
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new SmallestMailboxPoolSurrogate
            {
                NrOfInstances = NrOfInstances,
                UsePoolDispatcher = UsePoolDispatcher,
                Resizer = Resizer,
                SupervisorStrategy = SupervisorStrategy,
                RouterDispatcher = RouterDispatcher,
            };
        }

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

        public SmallestMailboxPool(int nrOfInstances) : base(nrOfInstances, null, Pool.DefaultStrategy, null) { }

        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new SmallestMailboxRoutingLogic());
        }

        public override Pool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new SmallestMailboxPool(NrOfInstances, Resizer, strategy, RouterDispatcher, UsePoolDispatcher);
        }

        public override Pool WithResizer(Resizer resizer)
        {
            return new SmallestMailboxPool(NrOfInstances, resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
        }

        public override Pool WithDispatcher(string dispatcher)
        {
            return new SmallestMailboxPool(NrOfInstances, Resizer, SupervisorStrategy, dispatcher, UsePoolDispatcher);
        }

        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return OverrideUnsetConfig(routerConfig);
        }
    }
}

