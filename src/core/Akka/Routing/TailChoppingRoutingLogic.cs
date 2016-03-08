//-----------------------------------------------------------------------
// <copyright file="TailChoppingRoutingLogic.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Routing
{
    /// <summary>
    /// This class contains logic used by a <see cref="Router"/> to route a message to a <see cref="Routee"/> determined using tail-chopping.
    /// This process has the router select a random routee, then waits an interval before sending to a different randomly chosen routee.
    /// The first response is used and the remaining are discarded. If the none of the routees respond within a specified time limit,
    /// a timeout failure occurs.
    /// </summary>
    public sealed class TailChoppingRoutingLogic : RoutingLogic
    {
        private readonly TimeSpan _within;

        private readonly TimeSpan _interval;

        private readonly IScheduler _scheduler;

        /// <summary>
        /// Initializes a new instance of the <see cref="TailChoppingRoutingLogic"/> class.
        /// </summary>
        /// <param name="within">The time within which at least one response is expected.</param>
        /// <param name="interval">The duration after which the next routee will be picked.</param>
        /// <param name="scheduler">The <see cref="IScheduler"/> used to force deadlines.</param>
        public TailChoppingRoutingLogic(TimeSpan within, TimeSpan interval, IScheduler scheduler)
        {
            _within = within;
            _interval = interval;
            _scheduler = scheduler;
        }

        /// <summary>
        /// Picks all of the provided <paramref name="routees"/> to receive the <paramref name="message" />.
        /// </summary>
        /// <param name="message">The message that is being routed</param>
        /// <param name="routees">A collection of routees used when receiving the <paramref name="message" />.</param>
        /// <returns>A <see cref="TailChoppingRoutee" /> that receives the <paramref name="message" />.</returns>
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
    /// This class represents a single point <see cref="Routee"/> that sends messages to a <see cref="Routee"/> determined using tail-chopping.
    /// This process has the routee select a random routee, then waits an interval before sending to a different randomly chosen routee.
    /// The first response is used and the remaining are discarded. If the none of the routees respond within a specified time limit,
    /// a timeout failure occurs.
    /// </summary>
    internal sealed class TailChoppingRoutee : Routee
    {
        private readonly Routee[] _routees;

        private readonly TimeSpan _within;

        private readonly TimeSpan _interval;

        private readonly IScheduler _scheduler;

        /// <summary>
        /// Initializes a new instance of the <see cref="TailChoppingRoutee"/> class.
        /// </summary>
        /// <param name="routees">The list of routees that the router uses to send messages.</param>
        /// <param name="within">The time within which at least one response is expected.</param>
        /// <param name="interval">The duration after which the next routee will be picked.</param>
        /// <param name="scheduler">The <see cref="IScheduler"/> used to force deadlines.</param>
        public TailChoppingRoutee(Routee[] routees, TimeSpan within, TimeSpan interval, IScheduler scheduler)
        {
            _routees = routees;
            _within = within;
            _interval = interval;
            _scheduler = scheduler;
        }

        /// <summary>
        /// Sends a message to the collection of routees.
        /// </summary>
        /// <param name="message">The message that is being sent.</param>
        /// <param name="sender">The actor sending the message.</param>
        public override void Send(object message, IActorRef sender)
        {
            _routees.Shuffle();
            var routeeIndex = new AtomicCounter(0);

            var completion = new TaskCompletionSource<object>();
            var cancelable = new Cancelable(_scheduler);

            completion.Task
                .ContinueWith(task => cancelable.Cancel(false));

            if (_routees.Length == 0)
            {
                completion.TrySetResult(NoRoutee);
            }
            else
            {
                _scheduler.Advanced.ScheduleRepeatedly(TimeSpan.Zero, _interval, async () =>
                {
                    var currentIndex = routeeIndex.GetAndIncrement();
                    if (currentIndex >= _routees.Length) 
                        return;

                    try
                    {

                        completion.TrySetResult(await ((Task<object>)_routees[currentIndex].Ask(message, _within)));
                    }
                    catch (TaskCanceledException)
                    {
                        completion.TrySetResult(
                            new Status.Failure(
                                new TimeoutException(String.Format("Ask timed out on {0} after {1}", sender, _within))));
                    }
                }, cancelable);
            }

            completion.Task.PipeTo(sender);
        }
    }

    /// <summary>
    /// This class represents a <see cref="Pool"/> router that sends messages to a <see cref="Routee"/> determined using tail-chopping.
    /// This process has the router select a random routee, then waits an interval before sending to a different randomly chosen routee.
    /// The first response is used and the remaining are discarded. If the none of the routees respond within a specified time limit,
    /// a timeout failure occurs.
    /// </summary>
    public sealed class TailChoppingPool : Pool
    {
        /// <summary>
        /// This class represents a surrogate of a <see cref="TailChoppingPool"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class TailChoppingPoolSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="TailChoppingPool"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="TailChoppingPool"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new TailChoppingPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, Within, Interval, UsePoolDispatcher);
            }

            /// The interval to wait before sending to the next routee.
            public TimeSpan Interval { get; set; }
            /// The amount of time to wait for a response.
            public TimeSpan Within { get; set; }
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

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="TailChoppingPool"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="TailChoppingPool"/>.</returns>
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

        private readonly TimeSpan _within;

        private readonly TimeSpan _interval;

        /// <summary>
        /// Initializes a new instance of the <see cref="TailChoppingPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="resizer">The resizer to use when dynamically allocating routees to the pool.</param>
        /// <param name="supervisorStrategy">The strategy to use when supervising the pool.</param>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to the routees.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        /// <param name="interval">The interval to wait before sending to the next routee.</param>
        /// <param name="usePoolDispatcher"><c>true</c> to use the pool dispatcher; otherwise <c>false</c>.</param>
        public TailChoppingPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy,
            string routerDispatcher, TimeSpan within, TimeSpan interval, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
            _within = within;
            _interval = interval;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TailChoppingPool"/> class.
        /// 
        /// <note>
        /// 'nr-of-instances', 'within', and 'tail-chopping-router.interval'
        /// must be defined in the provided configuration.
        /// </note>
        /// </summary>
        /// <param name="config">The configuration used to configure the pool.</param>
        public TailChoppingPool(Config config)
            : this(config.GetInt("nr-of-instances"),
                DefaultResizer.FromConfig(config),
                null,
                null,   //TODO: what are our defaults? null?
                config.GetTimeSpan("within"),
                config.GetTimeSpan("tail-chopping-router.interval"),
                config.HasPath("pool-dispatcher")
                )
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TailChoppingPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        /// <param name="interval">The interval to wait before sending to the next routee.</param>
        public TailChoppingPool(int nrOfInstances, TimeSpan within, TimeSpan interval)
            : this(nrOfInstances, null, null, null, within, interval)
        {
            //TODO: what are our defaults? null?
        }

        /// <summary>
        /// Creates a new <see cref="TailChoppingPool"/> router with a given <see cref="SupervisorStrategy"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="strategy">The <see cref="SupervisorStrategy"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="strategy"/>.</returns>
        public override Pool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new TailChoppingPool(NrOfInstances, Resizer, strategy, RouterDispatcher, _within, _interval, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a new <see cref="TailChoppingPool"/> router with a given <see cref="Resizer"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="resizer">The <see cref="Resizer"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="resizer"/>.</returns>
        public override Pool WithResizer(Resizer resizer)
        {
            return new TailChoppingPool(NrOfInstances, resizer, SupervisorStrategy, RouterDispatcher, _within, _interval, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a new <see cref="TailChoppingPool"/> router with a given dispatcher id.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public override Pool WithDispatcher(string dispatcher)
        {
            return new TailChoppingPool(NrOfInstances, Resizer, SupervisorStrategy, dispatcher, _within, _interval, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new TailChoppingRoutingLogic(_within, _interval, system.Scheduler));
        }

        /// <summary>
        /// Configure the current router with an auxiliary router for routes that it does not know how to handle.
        /// </summary>
        /// <param name="routerConfig">The router to use as an auxiliary source.</param>
        /// <returns>The router configured with the auxiliary information.</returns>
        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return OverrideUnsetConfig(routerConfig);
        }
    }

    /// <summary>
    /// This class represents a <see cref="Group"/> router that sends messages to a <see cref="Routee"/> determined using tail-chopping.
    /// This process has the router select a random routee, then waits an interval before sending to a different randomly chosen routee.
    /// The first response is used and the remaining are discarded. If the none of the routees respond within a specified time limit,
    /// a timeout failure occurs.
    /// </summary>
    public sealed class TailChoppingGroup : Group
    {
        /// <summary>
        /// This class represents a surrogate of a <see cref="TailChoppingGroup"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class TailChoppingGroupSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="TailChoppingGroup"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="TailChoppingGroup"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new TailChoppingGroup(Paths, Within,Interval);
            }

            /// The amount of time to wait for a response.
            public TimeSpan Within { get; set; }
            /// The actor paths used by this router during routee selection.
            public string[] Paths { get; set; }
            /// The interval to wait before sending to the next routee.
            public TimeSpan Interval { get; set; }
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="TailChoppingGroup"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="TailChoppingGroup"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new TailChoppingGroupSurrogate
            {
                Paths = Paths,
                Within = _within,
                Interval = _interval,
            };
        }

        private readonly TimeSpan _within;

        private readonly TimeSpan _interval;

        /// <summary>
        /// Initializes a new instance of the <see cref="TailChoppingGroup"/> class.
        /// </summary>
        /// <param name="config">
        /// The configuration to use to lookup paths used by the group router.
        /// 
        /// <note>
        /// If 'routees.path' is defined in the provided configuration then those paths will be used by the router.
        /// If 'within' is defined in the provided configuration then that will be used as the timeout.
        /// If 'tail-chopping-router.interval' is defined in the provided configuration then that will be used as the interval.
        /// </note>
        /// </param>
        public TailChoppingGroup(Config config)
            : base(config.GetStringList("routees.paths").ToArray())
        {
            _within = config.GetTimeSpan("within");
            _interval = config.GetTimeSpan("tail-chopping-router.interval");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TailChoppingGroup"/> class.
        /// </summary>
        /// <param name="routeePaths">The actor paths used by this router during routee selection.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        /// <param name="interval">The interval to wait before sending to the next routee.</param>
        public TailChoppingGroup(string[] routeePaths, TimeSpan within, TimeSpan interval) : base(routeePaths)
        {
            _within = within;
            _interval = interval;
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new TailChoppingRoutingLogic(_within, _interval, system.Scheduler));
        }

        /// <summary>
        /// Creates a new <see cref="TailChoppingGroup" /> router with a given dispatcher id.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public override Group WithDispatcher(string dispatcher)
        {
            return new TailChoppingGroup(Paths, _within, _interval){ RouterDispatcher = dispatcher};
        }
    }
}
