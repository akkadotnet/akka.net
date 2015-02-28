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
        private readonly TimeSpan _within;

        /// <summary>
        /// The interval to delay between choosing a new random routee.
        /// </summary>
        private readonly TimeSpan _interval;

        /// <summary>
        /// An instance of the actor system scheduler.
        /// </summary>
        private readonly Scheduler _scheduler;

        /// <summary>
        /// Creates an instance of the TailChoppingRoutingLogic.
        /// </summary>
        /// <param name="within">The time within which at least one response is expected.</param>
        /// <param name="interval">The duration after which the next routee will be picked.</param>
        public TailChoppingRoutingLogic(TimeSpan within, TimeSpan interval, Scheduler scheduler)
        {
            _within = within;
            _interval = interval;
            _scheduler = scheduler;
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
            return new TailChoppingRoutee(routees, _within, _interval, _scheduler);
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
        private readonly Routee[] _routees;

        /// <summary>
        /// The amount of time to wait for a response.
        /// </summary>
        private readonly TimeSpan _within;

        /// <summary>
        /// The interval to wait before sending to the next routee.
        /// </summary>
        private readonly TimeSpan _interval;

        /// <summary>
        /// An instance of the actor system scheduler.
        /// </summary>
        private readonly Scheduler _scheduler;

        /// <summary>
        /// Creates an instance of the TailChoppingRoutee.
        /// </summary>
        /// <param name="routees">The routees to route to.</param>
        /// <param name="within">The time within which at least one response is expected.</param>
        /// <param name="interval">The duration after which the next routee will be picked.</param>
        /// <param name="scheduler">Access to a <see cref="Scheduler"/> instance, used to force deadlines.</param>
        public TailChoppingRoutee(Routee[] routees, TimeSpan within, TimeSpan interval, Scheduler scheduler)
        {
            _routees = routees;
            _within = within;
            _interval = interval;
            _scheduler = scheduler;
        }

        /// <summary>
        /// Sends a message to the tail chopping router's collection of routees.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender of the message.</param>
        public override void Send(object message, ActorRef sender)
        {
            _routees.Shuffle();
            var routeeIndex = new AtomicCounter(0);

            var completion = new TaskCompletionSource<object>();
            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;

            var scheduledSends = _scheduler.Schedule(TimeSpan.Zero, _interval, async () => 
            {
                var currentIndex = routeeIndex.GetAndIncrement();
                if(currentIndex < _routees.Length)
                {
                    completion.TrySetResult(await ((Task<object>)_routees[currentIndex].Ask(message, null)));
                }
            }, token);

            var withinTimeout = _scheduler.ScheduleOnce(_within, () => 
            {
                completion.TrySetException(new TimeoutException(String.Format("Ask timed out on {0} after {1}", sender, _within)));
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
        public class TailChoppingPoolSurrogate : ISurrogate
        {
            public object FromSurrogate(ActorSystem system)
            {
                return new TailChoppingPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, Within, Interval, UsePoolDispatcher);
            }

            public TimeSpan Interval { get; set; }
            public TimeSpan Within { get; set; }
            public int NrOfInstances { get; set; }
            public bool UsePoolDispatcher { get; set; }
            public Resizer Resizer { get; set; }
            public SupervisorStrategy SupervisorStrategy { get; set; }
            public string RouterDispatcher { get; set; }
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new TailChoppingPoolSurrogate
            {
                Interval = _interval,
                Within = _within,
                NrOfInstances = NrOfInstances,
                UsePoolDispatcher = UsePoolDispatcher,
                Resizer = Resizer,
                SupervisorStrategy = SupervisorStrategy,
                RouterDispatcher = RouterDispatcher,
            };
        }

        /// <summary>
        /// The amount of time to wait for a response.
        /// </summary>
        private readonly TimeSpan _within;

        /// <summary>
        /// The interval to wait before sending to the next routee.
        /// </summary>
        private readonly TimeSpan _interval;

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
            _within = within;
            _interval = interval;
        }

        /// <summary>
        /// Creates an instance of the TailChoppingPool.
        /// </summary>
        /// <param name="config">The configuration to use with this instance.</param>
        public TailChoppingPool(Config config)
        {
            NrOfInstances = config.GetInt("nr-of-instances");
            _within = config.GetTimeSpan("within");
            _interval = config.GetTimeSpan("tail-chopping-router.interval");
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
            _within = within;
            _interval = interval;
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
        /// <param name="resizer">The resizer to use.</param>
        /// <returns>The tail chopping pool.</returns>
        public TailChoppingPool WithResizer(Resizer resizer)
        {
            Resizer = resizer;
            return this;
        }

        /// <summary>
        /// Sets the router dispatcher to use for the pool.
        /// </summary>
        /// <param name="dispatcherId">The router dispatcher to use.</param>
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
            return new Router(new TailChoppingRoutingLogic(_within, _interval, system.Scheduler));
        }
    }

    /// <summary>
    /// A router group that selects a random routee, then waits an interval before sending to a
    /// different routee. The first response is used and the remaining discarded.
    /// </summary>
    public sealed class TailChoppingGroup : Group
    {
        public class TailChoppingGroupSurrogate : ISurrogate
        {
            public object FromSurrogate(ActorSystem system)
            {
                return new TailChoppingGroup(Paths, Within,Interval);
            }

            public TimeSpan Within { get; set; }
            public string[] Paths { get; set; }
            public TimeSpan Interval { get; set; }
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new TailChoppingGroupSurrogate
            {
                Paths = Paths,
                Within = _within,
                Interval = _interval,
            };
        }

        /// <summary>
        /// The amount of time to wait for a response.
        /// </summary>
        private readonly TimeSpan _within;

        /// <summary>
        /// The interval to wait before sending to the next routee.
        /// </summary>
        private readonly TimeSpan _interval;
        
        /// <summary>
        /// Creates an instance of the TailChoppingGroup.
        /// </summary>
        /// <param name="config">The configuration to use with this instance.</param>
        public TailChoppingGroup(Config config)
        {
            Paths = config.GetStringList("routees.paths").ToArray();
            _within = config.GetTimeSpan("within");
            _interval = config.GetTimeSpan("tail-chopping-router.interval");
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
            _within = within;
            _interval = interval;
        }

        /// <summary>
        /// Creates a tail chopping router.
        /// </summary>
        /// <param name="system">The actor system to use to create this router.</param>
        /// <returns>The created router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new TailChoppingRoutingLogic(_within, _interval, system.Scheduler));
        }
    }
}
