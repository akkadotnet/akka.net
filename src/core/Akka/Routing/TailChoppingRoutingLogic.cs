using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Util;
using Akka.Actor;
using System.Threading;
using Akka.Configuration;
using Akka.Util.Internal;

namespace Akka.Routing
{
    /// <summary>
    /// The routing logic for the TailChoppingRouter. This router will send a message to a randomly chosen
    /// routee, and after a delay, send to a different randomly chosen routee. The first response is forwarded,
    /// and all other responses are discarded.
    /// </summary>
    public sealed class TailChoppingRoutingLogic : RoutingLogic
    {
        /// <summary>
        /// The amount of time to wait for a response.
        /// </summary>
        private readonly TimeSpan within;

        /// <summary>
        /// The interval to delay between choosing a new random routee.
        /// </summary>
        private readonly TimeSpan interval;

        /// <summary>
        /// An instance of the actor system scheduler.
        /// </summary>
        private readonly Scheduler scheduler;

        /// <summary>
        /// Creates an instance of the TailChoopingRoutingLogic.
        /// </summary>
        /// <param name="within">The time within which at least one response is expected.</param>
        /// <param name="interval">The duration after which the next routee will be picked.</param>
        public TailChoppingRoutingLogic(TimeSpan within, TimeSpan interval, Scheduler scheduler)
        {
            this.within = within;
            this.interval = interval;
            this.scheduler = scheduler;
        }

        /// <summary>
        /// Selects all routees and creates a TailChoppingRoutee.
        /// </summary>
        /// <param name="message">The message to use.</param>
        /// <param name="routees">The routees to select from.</param>
        /// <returns>A TailChoppingRoutee to handle the tail chopping routing.</returns>
        public override Routee Select(object message, Routee[] routees)
        {
            if(routees.IsNullOrEmpty())
            {
                return Routee.NoRoutee;
            }
            return new TailChoppingRoutee(routees, within, interval, scheduler);
        }
    }

    /// <summary>
    /// A single point routee that routes to randomly chosen routees at a given interval. Accepts the first response.
    /// </summary>
    internal sealed class TailChoppingRoutee : Routee
    {
        /// <summary>
        /// The collection of possible routees to send messages to.
        /// </summary>
        private readonly Routee[] routees;

        /// <summary>
        /// The amount of time to wait for a response.
        /// </summary>
        private readonly TimeSpan within;

        /// <summary>
        /// The interval to wait before sending to the next routee.
        /// </summary>
        private readonly TimeSpan interval;

        /// <summary>
        /// An instance of the actor system scheduler.
        /// </summary>
        private readonly Scheduler scheduler;

        /// <summary>
        /// Creates an instance of the TailChoppingRoutee.
        /// </summary>
        /// <param name="routees">The routees to route to.</param>
        /// <param name="within">The time within which at least one response is expected.</param>
        /// <param name="interval">The duration after which the next routee will be picked.</param>
        public TailChoppingRoutee(Routee[] routees, TimeSpan within, TimeSpan interval, Scheduler scheduler)
        {
            this.routees = routees;
            this.within = within;
            this.interval = interval;
            this.scheduler = scheduler;
        }

        /// <summary>
        /// Sends a message to the tail chopping router's collection of routees.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender of the message.</param>
        public override void Send(object message, ActorRef sender)
        {
            routees.Shuffle();
            var routeeIndex = new AtomicCounter(0);

            var completion = new TaskCompletionSource<object>();
            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;

            var scheduledSends = scheduler.Schedule(TimeSpan.Zero, interval, async () => 
            {
                var currentIndex = routeeIndex.GetAndIncrement();
                if(currentIndex < routees.Length)
                {
                    completion.TrySetResult(await ((Task<object>)routees[currentIndex].Ask(message, null)));
                }
            }, token);

            var withinTimeout = scheduler.ScheduleOnce(within, () => 
            {
                completion.TrySetException(new TimeoutException(String.Format("Ask timed out on {0} after {1}", sender, within)));
            }, token);

            var request = completion.Task;
            completion.Task.ContinueWith((task) => 
            {
                tokenSource.Cancel(false);
            });

            request.PipeTo(sender);
        }
    }

    /// <summary>
    /// A router pool that selects a random routee, then waits an interval before sending to a
    /// different routee. The first response is used and the remaining discarded.
    /// </summary>
    public sealed class TailChoppingPool : Pool
    {
        /// <summary>
        /// The amount of time to wait for a response.
        /// </summary>
        private readonly TimeSpan within;

        /// <summary>
        /// The interval to wait before sending to the next routee.
        /// </summary>
        private readonly TimeSpan interval;

        /// <summary>
        /// Creates an instance of the TailChoppingPool.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="resizer">The resizer to use with this instance.</param>
        /// <param name="supervisorStrategy">The supervision strategy to use with this pool.</param>
        /// <param name="routerDispatcher">The router dispatcher to use with this instance.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        /// <param name="interval">The interval to wait before sending to the next routee.</param>
        /// <param name="usePoolDispatcher">Whether or not to use the pool dispatcher.</param>
        public TailChoppingPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy,
            string routerDispatcher, TimeSpan within, TimeSpan interval, bool usePoolDispatcher = false)
            :base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
            this.within = within;
            this.interval = interval;
        }

        /// <summary>
        /// Creates an instance of the TailChoppingPool.
        /// </summary>
        /// <param name="config">The configuration to use with this instance.</param>
        public TailChoppingPool(Config config)
        {
            NrOfInstances = config.GetInt("nr-of-instances");
            within = config.GetMillisDuration("within");
            interval = config.GetMillisDuration("tail-chopping-router.interval");
            Resizer = DefaultResizer.FromConfig(config);
            UsePoolDispatcher = config.HasPath("pool-dispatcher");
        }

        /// <summary>
        /// Creates an instance of the TailChoppingPool.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        /// <param name="interval">The interval to wait before sending to the next routee.</param>
        public TailChoppingPool(int nrOfInstances, TimeSpan within, TimeSpan interval)
        {
            NrOfInstances = nrOfInstances;
            this.within = within;
            this.interval = interval;
        }

        /// <summary>
        /// Sets the supervisor strategy to use for the pool.
        /// </summary>
        /// <param name="strategy">The strategy to use.</param>
        /// <returns>The tail chopping pool.</returns>
        public TailChoppingPool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            SupervisorStrategy = strategy;
            return this;
        }

        /// <summary>
        /// Sets the resizer to use for the pool.
        /// </summary>
        /// <param name="strategy">The resizer to use.</param>
        /// <returns>The tail chopping pool.</returns>
        public TailChoppingPool WithResizer(Resizer resizer)
        {
            Resizer = resizer;
            return this;
        }

        /// <summary>
        /// Sets the router dispatcher to use for the pool.
        /// </summary>
        /// <param name="strategy">The router dispatcher to use.</param>
        /// <returns>The tail chopping pool.</returns>
        public TailChoppingPool WithDispatcher(string dispatcherId)
        {
            RouterDispatcher = dispatcherId;
            return this;
        }

        /// <summary>
        /// Creates a tail chopping router.
        /// </summary>
        /// <param name="system">The actor system to use to create this router.</param>
        /// <returns>The created router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new TailChoppingRoutingLogic(within, interval, system.Scheduler));
        }
    }

    /// <summary>
    /// A router group that selects a random routee, then waits an interval before sending to a
    /// different routee. The first response is used and the remaining discarded.
    /// </summary>
    public sealed class TailChoppingGroup : Group
    {
        /// <summary>
        /// The amount of time to wait for a response.
        /// </summary>
        private readonly TimeSpan within;

        /// <summary>
        /// The interval to wait before sending to the next routee.
        /// </summary>
        private readonly TimeSpan interval;
        
        /// <summary>
        /// Creates an instance of the TailChoppingGroup.
        /// </summary>
        /// <param name="config">The configuration to use with this instance.</param>
        public TailChoppingGroup(Config config)
        {
            Paths = config.GetStringList("routees.paths").ToArray();
            within = config.GetMillisDuration("within");
            interval = config.GetMillisDuration("tail-chopping-router.interval");
        }

        /// <summary>
        /// Creates an instance of the TailChoppingGroup.
        /// </summary>
        /// <param name="routeePaths">The configured routee paths to use with this instance.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        /// <param name="interval">The interval to wait before sending to the next routee.</param>
        public TailChoppingGroup(string[] routeePaths, TimeSpan within, TimeSpan interval)
        {
            Paths = routeePaths;
            this.within = within;
            this.interval = interval;
        }

        /// <summary>
        /// Creates a tail chopping router.
        /// </summary>
        /// <param name="system">The actor system to use to create this router.</param>
        /// <returns>The created router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new TailChoppingRoutingLogic(within, interval, system.Scheduler));
        }
    }
}
