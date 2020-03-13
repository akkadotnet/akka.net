//-----------------------------------------------------------------------
// <copyright file="TailChopping.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
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
    public sealed class TailChopping : RoutingLogic
    {
        private readonly TimeSpan _within;

        private readonly TimeSpan _interval;

        private readonly IScheduler _scheduler;

        /// <summary>
        /// Initializes a new instance of the <see cref="TailChopping"/> class.
        /// </summary>
        /// <param name="within">The time within which at least one response is expected.</param>
        /// <param name="interval">The duration after which the next routee will be picked.</param>
        /// <param name="scheduler">The <see cref="IScheduler"/> used to force deadlines.</param>
        public TailChopping(IScheduler scheduler, TimeSpan within, TimeSpan interval)
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
            if (routees.IsNullOrEmpty())
            {
                return Routee.NoRoutee;
            }

            return new TailChoppingRoutee(_scheduler, routees, _within, _interval);
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
        public TailChoppingRoutee(IScheduler scheduler, Routee[] routees, TimeSpan within, TimeSpan interval)
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

                        completion.TrySetResult(await (_routees[currentIndex].Ask(message, _within)).ConfigureAwait(false));
                    }
                    catch (TaskCanceledException)
                    {
                        completion.TrySetResult(
                            new Status.Failure(
                                new AskTimeoutException($"Ask timed out on {sender} after {_within}")));
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
        /// Initializes a new instance of the <see cref="TailChoppingPool"/> class.
        /// <note>
        /// 'nr-of-instances', 'within', and 'tail-chopping-router.interval'
        /// must be defined in the provided configuration.
        /// </note>
        /// </summary>
        /// <param name="config">The configuration used to configure the pool.</param>
        public TailChoppingPool(Config config)
            : this(
                  config.GetInt("nr-of-instances", 0),
                  Resizer.FromConfig(config),
                  Pool.DefaultSupervisorStrategy,
                  Dispatchers.DefaultDispatcherId,
                  config.GetTimeSpan("within", null), config.GetTimeSpan("tail-chopping-router.interval", null), config.HasPath("pool-dispatcher"))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TailChoppingPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        /// <param name="interval">The interval to wait before sending to the next routee.</param>
        public TailChoppingPool(int nrOfInstances, TimeSpan within, TimeSpan interval)
            : this(nrOfInstances, null, Pool.DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId, within, interval)
        {

        }

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
        public TailChoppingPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy, string routerDispatcher, TimeSpan within, TimeSpan interval, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
            Within = within;
            Interval = interval;
        }

        /// <summary>
        /// The amount of time to wait for a response.
        /// </summary>
        public TimeSpan Within { get; }

        /// <summary>
        /// The amount of time to wait before sending to the next routee.
        /// </summary>
        public TimeSpan Interval { get; }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new TailChopping(system.Scheduler, Within, Interval));
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
        /// Creates a new <see cref="TailChoppingPool"/> router with a given <see cref="SupervisorStrategy"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="strategy">The <see cref="SupervisorStrategy"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="strategy"/>.</returns>
        public TailChoppingPool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new TailChoppingPool(NrOfInstances, Resizer, strategy, RouterDispatcher, Within, Interval, UsePoolDispatcher);
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
        public TailChoppingPool WithResizer(Resizer resizer)
        {
            return new TailChoppingPool(NrOfInstances, resizer, SupervisorStrategy, RouterDispatcher, Within, Interval, UsePoolDispatcher);
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
        public TailChoppingPool WithDispatcher(string dispatcher)
        {
            return new TailChoppingPool(NrOfInstances, Resizer, SupervisorStrategy, dispatcher, Within, Interval, UsePoolDispatcher);
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

        private RouterConfig OverrideUnsetConfig(RouterConfig other)
        {
            if (other is Pool pool)
            {
                TailChoppingPool wssConf;

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
        /// Creates a surrogate representation of the current <see cref="TailChoppingPool"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="TailChoppingPool"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new TailChoppingPoolSurrogate
            {
                Interval = Interval,
                Within = Within,
                NrOfInstances = NrOfInstances,
                UsePoolDispatcher = UsePoolDispatcher,
                Resizer = Resizer,
                SupervisorStrategy = SupervisorStrategy,
                RouterDispatcher = RouterDispatcher,
            };
        }

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
        /// Initializes a new instance of the <see cref="TailChoppingGroup"/> class.
        /// </summary>
        /// <param name="config">
        /// The configuration to use to lookup paths used by the group router.
        /// <note>
        /// If 'routees.path' is defined in the provided configuration then those paths will be used by the router.
        /// If 'within' is defined in the provided configuration then that will be used as the timeout.
        /// If 'tail-chopping-router.interval' is defined in the provided configuration then that will be used as the interval.
        /// </note>
        /// </param>
        public TailChoppingGroup(Config config)
            : this(
                  config.GetStringList("routees.paths", new string[] { }),
                  config.GetTimeSpan("within", null),
                  config.GetTimeSpan("tail-chopping-router.interval", null),
                  Dispatchers.DefaultDispatcherId)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TailChoppingGroup"/> class.
        /// </summary>
        /// <param name="routeePaths">The actor paths used by this router during routee selection.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        /// <param name="interval">The interval to wait before sending to the next routee.</param>
        public TailChoppingGroup(IEnumerable<string> routeePaths, TimeSpan within, TimeSpan interval)
            : this(routeePaths, within, interval, Dispatchers.DefaultDispatcherId)
        {
            Within = within;
            Interval = interval;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TailChoppingGroup"/> class.
        /// </summary>
        /// <param name="routeePaths">The actor paths used by this router during routee selection.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        /// <param name="interval">The interval to wait before sending to the next routee.</param>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to the routees.</param>
        public TailChoppingGroup(
            IEnumerable<string> routeePaths,
            TimeSpan within,
            TimeSpan interval,
            string routerDispatcher) 
            : base(routeePaths, routerDispatcher)
        {
            Within = within;
            Interval = interval;
        }

        /// <summary>
        /// The amount of time to wait for a response.
        /// </summary>
        public TimeSpan Within { get; }

        /// <summary>
        /// The amount of time to wait before sending to the next routee.
        /// </summary>
        public TimeSpan Interval { get; }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            
            return new Router(new TailChopping(system.Scheduler, Within, Interval));
        }

        /// <summary>
        /// Retrieves the actor paths used by this router during routee selection.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>An enumeration of actor paths used during routee selection</returns>
        public override IEnumerable<string> GetPaths(ActorSystem system)
        {
            return InternalPaths;
        }

        /// <summary>
        /// Creates a new <see cref="TailChoppingGroup" /> router with a given dispatcher id.
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public TailChoppingGroup WithDispatcher(string dispatcher)
        {
            return new TailChoppingGroup(InternalPaths, Within, Interval, dispatcher);
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
                Paths = InternalPaths,
                Within = Within,
                Interval = Interval,
                RouterDispatcher = RouterDispatcher
            };
        }

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
                return new TailChoppingGroup(Paths, Within, Interval, Dispatchers.DefaultDispatcherId);
            }

            /// <summary>
            /// The actor paths used by this router during routee selection.
            /// </summary>
            public IEnumerable<string> Paths { get; set; }

            /// <summary>
            /// The amount of time to wait for a response.
            /// </summary>
            public TimeSpan Within { get; set; }

            /// <summary>
            /// The interval to wait before sending to the next routee.
            /// </summary>
            public TimeSpan Interval { get; set; }

            /// <summary>
            /// The dispatcher to use when passing messages to the routees.
            /// </summary>
            public string RouterDispatcher { get; set; }
        }
    }
}
