//-----------------------------------------------------------------------
// <copyright file="SmallestMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Util;

namespace Akka.Routing
{
    /// <summary>
    /// This class contains logic used by a <see cref="Router"/> to route a message to a <see cref="Routee"/>
    /// determined using smallest-mailbox. This process has the router select a routee based on the fewest number
    /// of messages in its routees' mailbox. The selection is done in the following order:
    /// 
    /// <ul>
    /// <li>Pick any routee with an empty mailbox.</li>
    /// <li>Pick a routee with the fewest pending messages in its mailbox.</li>
    /// <li>Pick any remaining routees.</li>
    /// </ul>
    /// <note>
    /// Remote routees are consider lowest priority, since their mailbox size is unknown.
    /// </note>
    /// <note>
    /// For the case, when all routees are of unpredictable size, the selection process fails back to round-robin.
    /// </note>
    /// </summary>
    public sealed class SmallestMailboxRoutingLogic : RoutingLogic
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SmallestMailboxRoutingLogic"/> class.
        /// </summary>
        public SmallestMailboxRoutingLogic() {}

        /// <summary>
        /// Initializes a new instance of the <see cref="SmallestMailboxRoutingLogic"/> class.
        /// </summary>
        /// <param name="next">Seed value used in the fallback selection process.</param>
        public SmallestMailboxRoutingLogic(int next)
        {
            _next = next;
        }

        private int _next;

        /// <summary>
        /// Picks a <see cref="Routee" /> to receive the <paramref name="message" />.
        /// </summary>
        /// <param name="message">The message that is being routed</param>
        /// <param name="routees">A collection of routees to choose from when receiving the <paramref name="message" />.</param>
        /// <returns>A <see cref="Routee" /> that receives the <paramref name="message" />.</returns>
        public override Routee Select(object message, Routee[] routees)
        {
            return routees == null || routees.Length == 0
                ? Routee.NoRoutee
                : SelectNext(routees);
        }

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
            if (routee is ActorRefRoutee refRoutee && refRoutee.Actor is ActorRefWithCell actorRef)
            {
                return actorRef.Underlying;
            }
            return null;
        }
    }

    /// <summary>
    /// This class represents a <see cref="Pool"/> router that sends messages to a <see cref="Routee"/> determined using smallest-mailbox.
    /// Please refer to <see cref="SmallestMailboxRoutingLogic"/> for more information on the selection process.
    /// </summary>
    public sealed class SmallestMailboxPool : Pool
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SmallestMailboxPool"/> class.
        /// </summary>
        /// <param name="config">The configuration used to configure the pool.</param>
        public SmallestMailboxPool(Config config) 
            : this(
                  nrOfInstances: config.GetInt("nr-of-instances", 0),
                  resizer: Resizer.FromConfig(config),
                  supervisorStrategy: Pool.DefaultSupervisorStrategy,
                  routerDispatcher: Dispatchers.DefaultDispatcherId,
                  usePoolDispatcher: config.HasPath("pool-dispatcher"))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SmallestMailboxPool"/> class.
        /// <note>
        /// A <see cref="SmallestMailboxPool"/> configured in this way uses the <see cref="Pool.DefaultSupervisorStrategy"/> supervisor strategy.
        /// </note>
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        public SmallestMailboxPool(int nrOfInstances) 
            : this(nrOfInstances, null, Pool.DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId) { }

        /// <summary>
        /// Initializes a new instance of the <see cref=" SmallestMailboxPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="resizer">The resizer to use when dynamically allocating routees to the pool.</param>
        /// <param name="supervisorStrategy">The strategy to use when supervising the pool.</param>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to the routees.</param>
        /// <param name="usePoolDispatcher"><c>true</c> to use the pool dispatcher; otherwise <c>false</c>.</param>
        public SmallestMailboxPool(
            int nrOfInstances,
            Resizer resizer,
            SupervisorStrategy supervisorStrategy,
            string routerDispatcher,
            bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new SmallestMailboxRoutingLogic());
        }

        /// <summary>
        /// Used by the <see cref="RoutedActorCell" /> to determine the initial number of routees.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The number of routees associated with this pool.</returns>
        public override int GetNrOfInstances(ActorSystem system)
        {
            return NrOfInstances;
        }

        /// <summary>
        /// Creates a new <see cref="SmallestMailboxPool" /> router with a given <see cref="SupervisorStrategy" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="strategy">The <see cref="SupervisorStrategy" /> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="strategy" />.</returns>
        public SmallestMailboxPool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new SmallestMailboxPool(NrOfInstances, Resizer, strategy, RouterDispatcher, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a new <see cref="SmallestMailboxPool" /> router with a given <see cref="Routing.Resizer" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="resizer">The <see cref="Routing.Resizer" /> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="resizer" />.</returns>
        public SmallestMailboxPool WithResizer(Resizer resizer)
        {
            return new SmallestMailboxPool(NrOfInstances, resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a new <see cref="SmallestMailboxPool" /> router with a given dispatcher id.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public SmallestMailboxPool WithDispatcher(string dispatcher)
        {
            return new SmallestMailboxPool(NrOfInstances, Resizer, SupervisorStrategy, dispatcher, UsePoolDispatcher);
        }

        /// <summary>
        /// Configure the current router with an auxiliary router for routes that it does not know how to handle.
        /// </summary>
        /// <param name="routerConfig">The router to use as an auxiliary source.</param>
        /// <returns>The router configured with the auxiliary information. </returns>
        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return OverrideUnsetConfig(routerConfig);
        }

        private RouterConfig OverrideUnsetConfig(RouterConfig other)
        {
            if (other is Pool pool)
            {
                SmallestMailboxPool wssConf;

                if (SupervisorStrategy != null
                    && SupervisorStrategy.Equals(DefaultSupervisorStrategy)
                    && !pool.SupervisorStrategy.Equals(DefaultSupervisorStrategy))
                {
                    wssConf = WithSupervisorStrategy(pool.SupervisorStrategy);
                }
                else
                {
                    wssConf = this;
                }

                if (wssConf.Resizer == null && pool.Resizer != null)
                    return wssConf.WithResizer(pool.Resizer);

                return wssConf;
            }

            return this;
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="SmallestMailboxPool"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="SmallestMailboxPool"/>.</returns>
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
        /// This class represents a surrogate of a <see cref="SmallestMailboxPool"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class SmallestMailboxPoolSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="SmallestMailboxPool"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="SmallestMailboxPool"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new SmallestMailboxPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
            }

            /// <summary>
            /// The number of routees associated with this pool.
            /// </summary>
            public int NrOfInstances { get; set; }
            /// <summary>
            /// Determine whether or not to use the pool dispatcher. The dispatcher is defined in the
            /// 'pool-dispatcher' configuration property in the deployment section of the router.
            /// </summary>
            public bool UsePoolDispatcher { get; set; }
            /// <summary>
            /// The resizer to use when dynamically allocating routees to the pool.
            /// </summary>
            public Resizer Resizer { get; set; }
            /// <summary>
            /// The strategy to use when supervising the pool.
            /// </summary>
            public SupervisorStrategy SupervisorStrategy { get; set; }
            /// <summary>
            /// The dispatcher to use when passing messages to the routees.
            /// </summary>
            public string RouterDispatcher { get; set; }
        }
    }
}
