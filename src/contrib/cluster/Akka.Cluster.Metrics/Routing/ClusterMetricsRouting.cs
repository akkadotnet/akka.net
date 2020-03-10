//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsRouting.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Annotations;
using Akka.Cluster.Metrics.Events;
using Akka.Cluster.Metrics.Serialization;
using Akka.Cluster.Routing;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Routing;
using Akka.Util;
using Akka.Util.Extensions;
using Akka.Configuration;

namespace Akka.Cluster.Metrics
{
    /// <summary>
    /// Load balancing of messages to cluster nodes based on cluster metric data.
    ///
    /// It uses random selection of routees based on probabilities derived from the remaining capacity of corresponding node.
    /// </summary>
    public sealed class AdaptiveLoadBalancingRoutingLogic : RoutingLogic
    {
        private readonly ActorSystem _system;
        private readonly IMetricsSelector _metricsSelector;
        private readonly Cluster _cluster;
        private readonly AtomicReference<Tuple<ImmutableArray<Routee>, IImmutableSet<NodeMetrics>, Option<WeightedRoutees>>> _weightedRouteesRef;

        /// <summary>
        /// Creates instance if <see cref="AdaptiveLoadBalancingRoutingLogic"/>
        /// </summary>
        /// <param name="system">The actor system hosting this router</param>
        /// <param name="metricsSelector">
        /// Decides what probability to use for selecting a routee, based on remaining capacity as indicated by the node metrics
        /// </param>
        public AdaptiveLoadBalancingRoutingLogic(ActorSystem system, IMetricsSelector metricsSelector = null)
        {
            _system = system;
            _metricsSelector = metricsSelector ?? MixMetricsSelector.Instance;
            _cluster = Cluster.Get(system);
            
            // The current weighted routees, if any. Weights are produced by the metricsSelector
            // via the metricsListener Actor. It's only updated by the actor, but accessed from
            // the threads of the sender()s.
            _weightedRouteesRef = new AtomicReference<Tuple<ImmutableArray<Routee>, IImmutableSet<NodeMetrics>, Option<WeightedRoutees>>>(
                Tuple.Create<ImmutableArray<Routee>, IImmutableSet<NodeMetrics>, Option<WeightedRoutees>>(
                    ImmutableArray<Routee>.Empty, ImmutableHashSet<NodeMetrics>.Empty, Option<WeightedRoutees>.None
                )    
            );
        }

        public void MetricsChanged(ClusterMetricsChanged @event)
        {
            var oldValue = _weightedRouteesRef.Value;
            var routees = oldValue.Item1;
            var weightedRoutees = new WeightedRoutees(routees, 
                _cluster.SelfAddress,
                _metricsSelector.Weights(@event.NodeMetrics).ToImmutableDictionary(pair => pair.Key, pair => pair.Value));
            
            // retry when CAS failure
            if (!_weightedRouteesRef.CompareAndSet(oldValue, Tuple.Create(routees, @event.NodeMetrics, weightedRoutees.AsOption())))
                MetricsChanged(@event);
        }

        /// <inheritdoc />
        public override Routee Select(object message, Routee[] routees)
        {
            if (routees.Length == 0)
                return Routee.NoRoutee;

            Option<WeightedRoutees> UpdateWeightedRoutees()
            {
                var oldValue = _weightedRouteesRef.Value;
                var (oldRoutees, oldMetrics, oldWeightedRoutees) = oldValue;

                if (oldRoutees.Equals(routees.ToImmutableArray()))
                    return oldWeightedRoutees;
                
                var weightedRoutees = new WeightedRoutees(routees.ToImmutableArray(), _cluster.SelfAddress, 
                    _metricsSelector.Weights(oldMetrics).ToImmutableDictionary(pair => pair.Key, pair => pair.Value));
                
                // ignore, don't update, in case of CAS failure
                _weightedRouteesRef.CompareAndSet(oldValue, Tuple.Create(routees.ToImmutableArray(), oldMetrics, weightedRoutees.AsOption()));
                return weightedRoutees;
            }

            var updated = UpdateWeightedRoutees();
            if (updated.HasValue)
            {
                var weighted = updated.Value;
                if (weighted.IsEmpty)
                    return Routee.NoRoutee;

                return weighted[ThreadLocalRandom.Current.Next(weighted.Total) + 1];
            }
            else
            {
                return routees[ThreadLocalRandom.Current.Next(routees.Length)];
            }
        }
    }

    /// <summary>
    /// A router pool that performs load balancing of messages to cluster nodes based on
    /// cluster metric data.
    ///
    /// It uses random selection of routees based on probabilities derived from
    /// the remaining capacity of corresponding node.
    ///
    /// The configuration parameter trumps the constructor arguments. This means that
    /// if you provide `nrOfInstances` during instantiation they will be ignored if
    /// the router is defined in the configuration file for the actor being used.
    ///
    /// <h1>Supervision Setup</h1>
    ///
    /// Any routees that are created by a router will be created as the router's children.
    /// The router is therefore also the children's supervisor.
    ///
    /// The supervision strategy of the router actor can be configured with
    /// [[#withSupervisorStrategy]]. If no strategy is provided, routers default to
    /// a strategy of â€œalways escalateâ€. This means that errors are passed up to the
    /// router's supervisor for handling.
    ///
    /// The router's supervisor will treat the error as an error with the router itself.
    /// Therefore a directive to stop or restart will cause the router itself to stop or
    /// restart. The router, in turn, will cause its children to stop and restart.
    /// </summary>
    public sealed class AdaptiveLoadBalancingPool : Pool
    {
        /// <summary>
        /// Metrics selector
        /// </summary>
        public IMetricsSelector MetricsSelector { get; }

        /// <summary>
        /// Creates instance of <see cref="AdaptiveLoadBalancingPool"/>
        /// </summary>
        /// <param name="metricsSelector">
        /// Decides what probability to use for selecting a routee, based on remaining capacity as indicated by the node metrics
        /// </param>
        /// <param name="nrOfInstances">Initial number of routees in the pool</param>
        /// <param name="supervisorStrategy">strategy for supervising the routees, see 'Supervision Setup' in class summary</param>
        /// <param name="routerDispatcher">Dispatcher to use for the router head actor, which handles supervision, death watch and router management messages</param>
        /// <param name="usePoolDispatcher"></param>
        public AdaptiveLoadBalancingPool(IMetricsSelector metricsSelector = null, int nrOfInstances = 0, SupervisorStrategy supervisorStrategy = null,
                                         string routerDispatcher = null, bool usePoolDispatcher = false) 
            : base(nrOfInstances, null, supervisorStrategy ?? DefaultSupervisorStrategy, routerDispatcher ?? Dispatchers.DefaultDispatcherId, usePoolDispatcher)
        {
            MetricsSelector =  metricsSelector ?? MixMetricsSelector.Instance;
        }

        /// <summary>
        /// Creates instance of <see cref="AdaptiveLoadBalancingPool"/> using configuration
        /// </summary>
        public AdaptiveLoadBalancingPool(Config config) 
            : this(MetricsSelectorBuilder.BuildFromConfig(config), 
                   ClusterRouterSettingsBase.GetMaxTotalNrOfInstances(config), 
                   usePoolDispatcher: config.HasPath("pool-dispatcher"))
        {
        }

        /// <inheritdoc />
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new AdaptiveLoadBalancingRoutingLogic(system, MetricsSelector));
        }

        /// <inheritdoc />
        public override Props RoutingLogicController(RoutingLogic routingLogic)
        {
            return Actor.Props.Create(() => new AdaptiveLoadBalancingMetricsListener(routingLogic as AdaptiveLoadBalancingRoutingLogic));
        }
        
        /// <inheritdoc />
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new AdaptiveLoadBalancingPoolSurrogate()
            {
                MetricsSelector = MetricsSelector,
                RouterDispatcher = RouterDispatcher,
                SupervisorStrategy = SupervisorStrategy,
                NrOfInstances = NrOfInstances,
                UsePoolDispatcher = UsePoolDispatcher
            };
        }

        /// <inheritdoc />
        public override int GetNrOfInstances(ActorSystem system) => NrOfInstances;

        /// <inheritdoc />
        public override Resizer Resizer => null;

        /// <summary>
        /// Setting the supervisor strategy to be used for the â€œheadâ€ Router actor.
        /// </summary>
        public AdaptiveLoadBalancingPool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new AdaptiveLoadBalancingPool(MetricsSelector, NrOfInstances, strategy, RouterDispatcher, UsePoolDispatcher);
        }

        /// <summary>
        /// Setting the dispatcher to be used for the router head actor,
        /// which handles supervision, death watch and router management messages.
        /// </summary>
        public AdaptiveLoadBalancingPool WithDispatcher(string dispatcherId)
        {
            return new AdaptiveLoadBalancingPool(MetricsSelector, NrOfInstances, SupervisorStrategy, dispatcherId, UsePoolDispatcher);
        }

        /// <inheritdoc />
        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            routerConfig = base.WithFallback(routerConfig);

            if (!SupervisorStrategy.Equals(DefaultSupervisorStrategy))
                return this;

            if (routerConfig is FromConfig || routerConfig is NoRouter)
                return this; // NoRouter is the default, hence â€œneutralâ€

            if (routerConfig is AdaptiveLoadBalancingPool adaptiveLoadBalancingPool)
            {
                return adaptiveLoadBalancingPool.SupervisorStrategy.Equals(DefaultSupervisorStrategy)
                    ? this
                    : WithSupervisorStrategy(adaptiveLoadBalancingPool.SupervisorStrategy);
            }
            
            throw new ArgumentException(nameof(routerConfig), $"Expected AdaptiveLoadBalancingPool, got {routerConfig}");
        }
        
        /// <summary>
        /// This class represents a surrogate of a <see cref="AdaptiveLoadBalancingPool"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class AdaptiveLoadBalancingPoolSurrogate : ISurrogate
        {
            /// <summary>
            /// Metrics selector
            /// </summary>
            public IMetricsSelector MetricsSelector { get; set; }
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
            /// The strategy to use when supervising the pool.
            /// </summary>
            public SupervisorStrategy SupervisorStrategy { get; set; }
            /// <summary>
            /// The dispatcher to use when passing messages to the routees.
            /// </summary>
            public string RouterDispatcher { get; set; }

            /// <summary>
            /// Creates a <see cref="AdaptiveLoadBalancingPool"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="AdaptiveLoadBalancingPool"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new AdaptiveLoadBalancingPool(MetricsSelector, NrOfInstances, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
            }
        }
    }

    /// <summary>
    /// A router group that performs load balancing of messages to cluster nodes based on
    /// cluster metric data.
    /// 
    /// It uses random selection of routees based on probabilities derived from
    /// the remaining capacity of corresponding node.
    /// 
    /// The configuration parameter trumps the constructor arguments. This means that
    /// if you provide `paths` during instantiation they will be ignored if
    /// the router is defined in the configuration file for the actor being used.
    /// </summary>
    public sealed class AdaptiveLoadBalancingGroup : Group
    {
        private readonly IEnumerable<string> _paths;
        private readonly IMetricsSelector _metricsSelector;

        /// <summary>
        /// Creates new instance of <see cref="AdaptiveLoadBalancingGroup"/> from provided configuration
        /// </summary>
        /// <param name="metricsSelector">
        /// Decides what probability to use for selecting a routee,
        /// based on remaining capacity as indicated by the node metrics
        /// </param>
        /// <param name="paths">
        /// String representation of the actor paths of the routees,
        /// messages are sent with <see cref="ActorSelection"/> to these paths
        /// </param>
        /// <param name="routerDispatcher">
        /// Dispatcher to use for the router head actor, which handles router management messages
        /// </param>
        public AdaptiveLoadBalancingGroup(IMetricsSelector metricsSelector = null, IEnumerable<string> paths = null, string routerDispatcher = null) 
            : base(paths, routerDispatcher ?? Dispatchers.DefaultDispatcherId)
        {
            _paths = paths;
            _metricsSelector = metricsSelector ?? MixMetricsSelector.Instance;
        }

        /// <summary>
        /// Creates new instance of <see cref="AdaptiveLoadBalancingGroup"/> from provided configuration
        /// </summary>
        public AdaptiveLoadBalancingGroup(Config config)
            : this(MetricsSelectorBuilder.BuildFromConfig(config), paths: config.GetStringList("routees.paths", new string[] { }))
        {
        }
        
        /// <inheritdoc />
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new AdaptiveLoadBalancingRoutingLogic(system, _metricsSelector));
        }

        /// <inheritdoc />
        public override Props RoutingLogicController(RoutingLogic routingLogic)
        {
            return Actor.Props.Create(() => new AdaptiveLoadBalancingMetricsListener(routingLogic as AdaptiveLoadBalancingRoutingLogic));
        }

        /// <inheritdoc />
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new AdaptiveLoadBalancingGroupSurrogate()
            {
                Paths = _paths,
                MetricsSelector = _metricsSelector,
                RouterDispatcher = RouterDispatcher
            };
        }

        /// <inheritdoc />
        public override IEnumerable<string> GetPaths(ActorSystem system) => _paths;

        /// <summary>
        /// Setting the dispatcher to be used for the router head actor, which handles router management messages
        /// </summary>
        public AdaptiveLoadBalancingGroup WithDispatcher(string dispatcherId)
        {
            return new AdaptiveLoadBalancingGroup(_metricsSelector, _paths, dispatcherId);
        }
        
        /// <summary>
        /// This class represents a surrogate of a <see cref="AdaptiveLoadBalancingGroup"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class AdaptiveLoadBalancingGroupSurrogate : ISurrogate
        {
            /// <summary>
            /// Metrics selector
            /// </summary>
            public IMetricsSelector MetricsSelector { get; set; }
            /// <summary>
            /// Retrieves the paths of all routees declared on this router.
            /// </summary>
            public IEnumerable<string> Paths { get; set; }
            /// <summary>
            /// The dispatcher to use when passing messages to the routees.
            /// </summary>
            public string RouterDispatcher { get; set; }

            /// <summary>
            /// Creates a <see cref="AdaptiveLoadBalancingGroup"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="AdaptiveLoadBalancingGroup"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new AdaptiveLoadBalancingGroup(MetricsSelector, Paths, RouterDispatcher);
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Subscribe to <see cref="IClusterMetricsEvent"/> and update routing logic depending on the events.
    /// </summary>
    [InternalApi]
    public class AdaptiveLoadBalancingMetricsListener : ActorBase
    {
        private readonly AdaptiveLoadBalancingRoutingLogic _routingLogic;
        private readonly ClusterMetrics _extension = ClusterMetrics.Get(Context.System);

        public AdaptiveLoadBalancingMetricsListener(AdaptiveLoadBalancingRoutingLogic routingLogic)
        {
            _routingLogic = routingLogic;
        }

        /// <inheritdoc />
        protected override void PreStart()
        {
            base.PreStart();
            
            _extension.Subscribe(Self);
        }

        /// <inheritdoc />
        protected override void PostStop()
        {
            base.PostStop();
            
            _extension.Unsubscribe(Self);
        }

        /// <inheritdoc />
        protected override bool Receive(object message)
        {
            if (message is ClusterMetricsChanged changed)
                _routingLogic.MetricsChanged(changed);

            return true; // Just Ignore any other message
        }
    }
}
