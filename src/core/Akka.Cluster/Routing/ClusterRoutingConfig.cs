//-----------------------------------------------------------------------
// <copyright file="ClusterRoutingConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Cluster.Routing
{
    /// <summary>
    /// <see cref="ClusterRouterSettingsBase.TotalInstances"/> of cluster router must be > 0
    /// </summary>
    public sealed class ClusterRouterGroupSettings : ClusterRouterSettingsBase
    {
        [Obsolete]
        public ClusterRouterGroupSettings(int totalInstances, bool allowLocalRoutees, IEnumerable<string> routeesPaths)
            : this(totalInstances, routeesPaths, allowLocalRoutees, null)
        {

        }

        [Obsolete]
        public ClusterRouterGroupSettings(int totalInstances, bool allowLocalRoutees, string useRole, ImmutableHashSet<string> routeesPaths)
            : this(totalInstances, routeesPaths, allowLocalRoutees, useRole)
        {

        }

        public ClusterRouterGroupSettings(int totalInstances, IEnumerable<string> routeesPaths, bool allowLocalRoutees, string useRole = null) 
            : base(totalInstances, allowLocalRoutees, useRole)
        {
            if (routeesPaths == null || !routeesPaths.Any() || string.IsNullOrEmpty(routeesPaths.First()))
            {
                throw new ArgumentException("RouteesPaths must be defined", nameof(routeesPaths));
            }

            RouteesPaths = routeesPaths;

            // validate that all RouteesPaths are relative
            foreach (var path in routeesPaths)
            {
                if (RelativeActorPath.Unapply(path) == null)
                    throw new ArgumentException($"routeesPaths [{path}] is not a valid relative actor path.", nameof(routeesPaths));
            }
        }

        public IEnumerable<string> RouteesPaths { get; }

        public static ClusterRouterGroupSettings FromConfig(Config config)
        {
            return new ClusterRouterGroupSettings(
                GetMaxTotalNrOfInstances(config),
                ImmutableHashSet.Create(config.GetStringList("routees.paths").ToArray()),
                config.GetBoolean("cluster.allow-local-routees"),
                UseRoleOption(config.GetString("cluster.use-role")));
        }
    }

    /// <summary>
    /// <see cref="ClusterRouterSettingsBase.TotalInstances"/> of cluster router must be > 0
    /// <see cref="MaxInstancesPerNode"/> of cluster router must be > 0
    /// <see cref="MaxInstancesPerNode"/> of cluster router must be 1 when routeesPath is defined
    /// </summary>
    public sealed class ClusterRouterPoolSettings : ClusterRouterSettingsBase
    {
        [Obsolete]
        public ClusterRouterPoolSettings(int totalInstances, bool allowLocalRoutees, int maxInstancesPerNode)
            : this(totalInstances, maxInstancesPerNode, allowLocalRoutees)
        {
        }

        [Obsolete]
        public ClusterRouterPoolSettings(int totalInstances, bool allowLocalRoutees, string useRole, int maxInstancesPerNode) 
            : this(totalInstances, maxInstancesPerNode, allowLocalRoutees, useRole)
        {
        }

        public ClusterRouterPoolSettings(int totalInstances, int maxInstancesPerNode, bool allowLocalRoutees, string useRole = null)
            : base(totalInstances, allowLocalRoutees, useRole)
        {
            MaxInstancesPerNode = maxInstancesPerNode;

            if (MaxInstancesPerNode <= 0) throw new ArgumentOutOfRangeException(nameof(maxInstancesPerNode), "maxInstancesPerNode of cluster pool router must be > 0");
        }

        public int MaxInstancesPerNode { get; private set; }

        public static ClusterRouterPoolSettings FromConfig(Config config)
        {
            return new ClusterRouterPoolSettings(
                GetMaxTotalNrOfInstances(config),
                config.GetInt("cluster.max-nr-of-instances-per-node"),
                config.GetBoolean("cluster.allow-local-routees"),
                UseRoleOption(config.GetString("cluster.use-role")));
        }
    }

    /// <summary>
    /// Base class for defining <see cref="ClusterRouterGroupSettings"/> and <see cref="ClusterRouterPoolSettings"/>
    /// </summary>
    public abstract class ClusterRouterSettingsBase
    {
        protected ClusterRouterSettingsBase(int totalInstances, bool allowLocalRoutees, string useRole)
        {
            UseRole = useRole;
            AllowLocalRoutees = allowLocalRoutees;
            TotalInstances = totalInstances;

            if (useRole == string.Empty) throw new ArgumentOutOfRangeException(nameof(useRole), "useRole must be either null or non-empty");
            if (totalInstances <= 0) throw new ArgumentOutOfRangeException(nameof(totalInstances), "totalInstances of cluster router must be > 0");
        }

        public int TotalInstances { get; }

        public bool AllowLocalRoutees { get; }

        public string UseRole { get; }

        internal static string UseRoleOption(string role)
        {
            if (string.IsNullOrEmpty(role))
                return null;

            return role;
        }

        internal static int GetMaxTotalNrOfInstances(Config config)
        {
            int number = config.GetInt("nr-of-instances");
            if (number == 0 || number == 1)
            {
                return config.GetInt("cluster.max-nr-of-instances-per-node");
            }
            else
            {
                return number; ;
            }
        }
    }


    /// <summary>
    /// <see cref="RouterConfig"/> implementation for deployment on cluster nodes.
    /// Delegates other duties to the local <see cref="RouterConfig"/>, which makes it
    /// possible to mix this with built-in routers such as <see cref="RoundRobinGroup"/> or
    /// custom routers.
    /// </summary>
    public sealed class ClusterRouterPool : Pool
    {
        private readonly AtomicCounter _childNameCounter = new AtomicCounter(0);

        public ClusterRouterPool(Pool local, ClusterRouterPoolSettings settings)
            : base(settings.AllowLocalRoutees ? settings.MaxInstancesPerNode : 0,
            local.Resizer,
            local.SupervisorStrategy,
            local.RouterDispatcher,
            false)
        {
            if (local.Resizer != null)
                throw new ConfigurationException("Resizer can't be used together with cluster router.");
            Settings = settings;
            Local = local;
        }

        public ClusterRouterPoolSettings Settings { get; }

        public Pool Local { get; }

        internal override Routee NewRoutee(Props routeeProps, IActorContext context)
        {
            var name = "c" + _childNameCounter.IncrementAndGet();
            var actorRef = ((ActorCell)context).AttachChild(Local.EnrichWithPoolDispatcher(routeeProps, context), false, name);
            return new ActorRefRoutee(actorRef);
        }

        /// <summary>
        /// Returns the initial number of routees
        /// </summary>
        /// <param name="system">The actor system to which this router belongs.</param>
        /// <returns>The initial number of routees</returns>
        public override int GetNrOfInstances(ActorSystem system)
        {
            if (Settings.AllowLocalRoutees && !string.IsNullOrEmpty(Settings.UseRole))
            {
                return Cluster.Get(system).SelfRoles.Contains(Settings.UseRole) ? Settings.MaxInstancesPerNode : 0;
            }
            else if (Settings.AllowLocalRoutees && string.IsNullOrEmpty(Settings.UseRole))
            {
                return Settings.MaxInstancesPerNode;
            }
            else
            {
                return 0;
            }
        }

        internal override RouterActor CreateRouterActor()
        {
            return new ClusterRouterPoolActor(Local.SupervisorStrategy, Settings);
        }

        public override SupervisorStrategy SupervisorStrategy
        {
            get
            {
                return Local.SupervisorStrategy;
            }
        }

        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            var otherClusterRouterPool = routerConfig as ClusterRouterPool;

            if (otherClusterRouterPool != null && otherClusterRouterPool.Local is ClusterRouterPool)
            {
                throw new ConfigurationException("ClusterRouterPool is not allowed to wrap a ClusterRouterPool");
            }

            if (otherClusterRouterPool != null)
            {
                return Copy(Local.WithFallback(otherClusterRouterPool.Local).AsInstanceOf<Pool>());
            }

            return Copy(Local.WithFallback(routerConfig).AsInstanceOf<Pool>());
        }

        public override Router CreateRouter(ActorSystem system)
        {
            return Local.CreateRouter(system);
        }

        public override string RouterDispatcher
        {
            get
            {
                return Local.RouterDispatcher;
            }
        }

        public override bool StopRouterWhenAllRouteesRemoved
        {
            get
            {
                return false;
            }
        }

        public override Props RoutingLogicController(RoutingLogic routingLogic)
        {
            return Local.RoutingLogicController(routingLogic);
        }

        public override bool IsManagementMessage(object message)
        {
            return message is ClusterEvent.IClusterDomainEvent
                || message is ClusterEvent.CurrentClusterState
                || base.IsManagementMessage(message);
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            throw new NotImplementedException();
        }

        internal RouterConfig Copy(Pool local = null, ClusterRouterPoolSettings settings = null)
        {
            return new ClusterRouterPool(local ?? Local, settings ?? Settings);
        }
    }

    /// <summary>
    /// <see cref="RouterConfig"/> implementation for deployment on cluster nodes.
    /// Delegates other duties to the local <see cref="RouterConfig"/>, which makes it
    /// possible to mix this with built-in routers such as <see cref="RoundRobinGroup"/> or
    /// custom routers.
    /// </summary>
    public sealed class ClusterRouterGroup : Group
    {
        public ClusterRouterGroup(Group local, ClusterRouterGroupSettings settings)
            : base(settings.AllowLocalRoutees ? settings.RouteesPaths.ToArray() : Enumerable.Empty<string>(), local.RouterDispatcher)
        {
            Settings = settings;
            Local = local;
        }

        public ClusterRouterGroupSettings Settings { get; }

        public Group Local { get; }

        public override IEnumerable<string> GetPaths(ActorSystem system)
        {
            if (Settings.AllowLocalRoutees && !string.IsNullOrEmpty(Settings.UseRole))
            {
                if (Cluster.Get(system).SelfRoles.Contains(Settings.UseRole))
                {
                    return Settings.RouteesPaths;
                }
                else
                {
                    return null;
                }
            }
            else if (Settings.AllowLocalRoutees && string.IsNullOrEmpty(Settings.UseRole))
            {
                return Settings.RouteesPaths;
            }
            else return null;
        }

        internal override RouterActor CreateRouterActor()
        {
            return new ClusterRouterGroupActor(Settings);
        }

        public override Router CreateRouter(ActorSystem system)
        {
            return Local.CreateRouter(system);
        }

        public override string RouterDispatcher
        {
            get
            {
                return Local.RouterDispatcher;
            }
        }

        public override bool StopRouterWhenAllRouteesRemoved
        {
            get
            {
                return false;
            }
        }

        public override Props RoutingLogicController(RoutingLogic routingLogic)
        {
            return Local.RoutingLogicController(routingLogic);
        }

        public override bool IsManagementMessage(object message)
        {
            return message is ClusterEvent.IClusterDomainEvent
                || message is ClusterEvent.CurrentClusterState
                || base.IsManagementMessage(message);
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return Local.ToSurrogate(system);
        }

        public override RouterConfig WithFallback(RouterConfig other)
        {
            var localFallback = other as ClusterRouterGroup;
            if (localFallback != null && (localFallback.Local is ClusterRouterGroup))
            {
                throw new ConfigurationException("ClusterRouterGroup is not allowed to wrap a ClusterRouterGroup");
            }

            if (localFallback != null)
            {
                return Copy(Local.WithFallback(localFallback.Local).AsInstanceOf<Group>());
            }

            return Copy(Local.WithFallback(other).AsInstanceOf<Group>());
        }

        internal RouterConfig Copy(Group local = null, ClusterRouterGroupSettings settings = null)
        {
            return new ClusterRouterGroup(local ?? Local, settings ?? Settings);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// The router actor, subscribes to cluster events and
    /// adjusts the routees.
    /// </summary>
    internal abstract class ClusterRouterActor : RouterActor
    {
        protected ClusterRouterActor(ClusterRouterSettingsBase settings)
        {
            Settings = settings;

            if (!(Cell.RouterConfig is Pool) && !(Cell.RouterConfig is Group))
            {
                throw new ActorInitializationException(string.Format("Cluster router actor can only be used with Pool or Group, not with {0}", Cell.RouterConfig.GetType()));
            }

            Cluster = Cluster.Get(Context.System);
            Nodes = ImmutableSortedSet.Create(Member.AddressOrdering,
                    Cluster.ReadView.Members.Where(IsAvailable).Select(x => x.Address).ToArray());
        }

        public ClusterRouterSettingsBase Settings { get; protected set; }

        public Cluster Cluster { get; }

        protected override void PreStart()
        {
            Cluster.Subscribe(Self, new[]
            {
                typeof(ClusterEvent.IMemberEvent),
                typeof(ClusterEvent.IReachabilityEvent)
            });
        }

        protected override void PostStop()
        {
            Cluster.Unsubscribe(Self);
        }

        public ImmutableSortedSet<Address> Nodes { get; private set; }

        public bool IsAvailable(Member m)
        {
            return m.Status == MemberStatus.Up && SatisfiesRole(m.Roles) &&
                   (Settings.AllowLocalRoutees || m.Address != Cluster.SelfAddress);
        }

        private bool SatisfiesRole(ImmutableHashSet<string> memberRoles)
        {
            if (string.IsNullOrEmpty(Settings.UseRole)) return true;
            return memberRoles.Contains(Settings.UseRole);
        }

        public ImmutableSortedSet<Address> AvailableNodes
        {
            get
            {
                if (Nodes.IsEmpty && Settings.AllowLocalRoutees && SatisfiesRole(Cluster.SelfRoles))
                {
                    //use my own node, cluster information not updated yet
                    return ImmutableSortedSet.Create(Cluster.SelfAddress);
                }
                return Nodes;
            }
        }

        /// <summary>
        /// Fills in self address for local <see cref="IActorRef"/>
        /// </summary>
        public Address FullAddress(Routee routee)
        {
            Address a = null;
            if (routee is ActorRefRoutee)
            {
                a = ((ActorRefRoutee)routee).Actor.Path.Address;
            }
            else if (routee is ActorSelectionRoutee)
            {
                a = ((ActorSelectionRoutee)routee).Selection.Anchor.Path.Address;
            }

            if (a == null || string.IsNullOrEmpty(a.Host) || !a.Port.HasValue)
            {
                return Cluster.SelfAddress; //local address
            }

            return a;
        }

        /// <summary>
        /// Adds routees based on settings
        /// </summary>
        public abstract void AddRoutees();

        public void AddMember(Member member)
        {
            Nodes = Nodes.Add(member.Address);
            AddRoutees();
        }

        public virtual void RemoveMember(Member member)
        {
            var address = member.Address;
            Nodes = Nodes.Remove(address);

            // unregister routees that live on that node
            var affectedRoutees = Cell.Router.Routees.Where(x => FullAddress(x) == address).ToList();
            Cell.RemoveRoutees(affectedRoutees, stopChild: true);

            // addRoutees will not create more than createRoutees and maxInstancesPerNode
            // this is useful when totalInstances < upNodes.size
            AddRoutees();
        }

        protected override void OnReceive(object message)
        {
            if (message is ClusterEvent.CurrentClusterState)
            {
                var state = (ClusterEvent.CurrentClusterState)message;
                Nodes = ImmutableSortedSet.Create(Member.AddressOrdering, state.Members.Where(IsAvailable).Select(x => x.Address).ToArray());
                AddRoutees();
            }
            else if (message is ClusterEvent.IMemberEvent)
            {
                var memberEvent = (ClusterEvent.IMemberEvent)message;
                if (IsAvailable(memberEvent.Member))
                {
                    AddMember(memberEvent.Member);
                }
                else
                {
                    // other events means that it is no onger interesting, such as
                    // MemberExited, MemberRemoved
                    RemoveMember(memberEvent.Member);
                }
            }
            else if (message is ClusterEvent.UnreachableMember)
            {
                var member = (ClusterEvent.UnreachableMember)message;
                RemoveMember(member.Member);
            }
            else if (message is ClusterEvent.ReachableMember)
            {
                var member =(ClusterEvent.ReachableMember)message;
                if (IsAvailable(member.Member)) AddMember(member.Member);
            }
            else
            {
                base.OnReceive(message);
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ClusterRouterGroupActor : ClusterRouterActor
    {
        private readonly Group _group;

        public ClusterRouterGroupActor(ClusterRouterGroupSettings settings) : base(settings)
        {
            Settings = settings;
            var groupConfig = Cell.RouterConfig as Group;
            if (groupConfig != null)
            {
                _group = groupConfig;
            }
            else
            {
                throw new ActorInitializationException(string.Format("ClusterRouterGroupActor can only be used with group, not {0}", Cell.RouterConfig.GetType()));
            }

            UsedRouteePaths = Settings.AllowLocalRoutees
                ? ImmutableDictionary<Address, ImmutableHashSet<string>>.Empty.Add(Cluster.SelfAddress, settings.RouteesPaths.ToImmutableHashSet())
                : ImmutableDictionary<Address, ImmutableHashSet<string>>.Empty;
        }

        public new ClusterRouterGroupSettings Settings { get; private set; }

        public ImmutableDictionary<Address, ImmutableHashSet<string>> UsedRouteePaths { get; private set; }

        /// <summary>
        /// Adds routees based on totalInstances and maxInstancesPerNode settings
        /// </summary>
        public override void AddRoutees()
        {
            Action doAddRoutees = null;
            doAddRoutees = () =>
            {
                var deploymentTarget = SelectDeploymentTarget();
                if (deploymentTarget != null)
                {
                    var address = deploymentTarget.Item1;
                    var path = deploymentTarget.Item2;
                    var routee = _group.RouteeFor(address + path, Context);
                    UsedRouteePaths = UsedRouteePaths.SetItem(
                        address,
                        UsedRouteePaths.GetOrElse(address, ImmutableHashSet<string>.Empty).Add(path));

                    //must register each one, since registered routees are used in SelectDeploymentTarget
                    Cell.AddRoutee(routee);

                    doAddRoutees();
                }
            };

            doAddRoutees();
        }

        public Tuple<Address, string> SelectDeploymentTarget()
        {
            var currentRoutees = Cell.Router.Routees.ToList();
            var currentNodes = AvailableNodes;
            if (currentNodes.IsEmpty || currentRoutees.Count >= Settings.TotalInstances) return null;

            //find the node with the least routees
            var unusedNodes = currentNodes.Except(UsedRouteePaths.Keys);
            if (!unusedNodes.IsEmpty) //we found at least 1 totally unused node
            {
                return new Tuple<Address, string>(unusedNodes.First(), Settings.RouteesPaths.First());
            }
            else
            {
                //find the node with the fewest routees
                var minNode = UsedRouteePaths
                    .Select(x => new { Address = x.Key, Used = x.Value })
                    .OrderBy(x => x.Used.Count)
                    .First();

                // pick next of unused paths
                var minPath = Settings.RouteesPaths.FirstOrDefault(p => !minNode.Used.Contains(p));
                return minPath == null ? null : new Tuple<Address, string>(minNode.Address, minPath);
            }
        }

        public override void RemoveMember(Member member)
        {
            UsedRouteePaths = UsedRouteePaths.Remove(member.Address);
            base.RemoveMember(member);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ClusterRouterPoolActor : ClusterRouterActor
    {
        protected Pool Pool;
        private readonly SupervisorStrategy _supervisorStrategy;

        public ClusterRouterPoolActor(SupervisorStrategy supervisorStrategy, ClusterRouterPoolSettings settings) : base(settings)
        {
            _supervisorStrategy = supervisorStrategy;
            Settings = settings;

            var pool = Cell.RouterConfig as Pool;
            if (pool != null)
            {
                Pool = pool;
            }
            else
            {
                throw new ActorInitializationException("RouterPoolActor can only be used with Pool, not " +
                                                       Cell.RouterConfig.GetType());
            }
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return _supervisorStrategy;
        }

        public new ClusterRouterPoolSettings Settings { get; private set; }

        public override void AddRoutees()
        {
            var deploymentTarget = SelectDeploymentTarget();
            while (deploymentTarget != null)
            {
                var routeeProps = Cell.RouteeProps;
                var deploy = new Deploy(
                    path: string.Empty,
                    config: ConfigurationFactory.Empty,
                    routerConfig: routeeProps.RouterConfig,
                    scope: new RemoteScope(deploymentTarget),
                    dispatcher: Deploy.NoDispatcherGiven);

                var routee = Pool.NewRoutee(routeeProps.WithDeploy(deploy), Context);

                //must register each one, since registered routees are used in SelectDeploymentTarget
                Cell.AddRoutee(routee);

                deploymentTarget = SelectDeploymentTarget();
            }
        }

        public Address SelectDeploymentTarget()
        {
            var currentRoutees = Cell.Router.Routees.ToList();
            var currentNodes = AvailableNodes;
            if (currentNodes.IsEmpty || currentRoutees.Count >= Settings.TotalInstances) return null;

            //find the node with the least routees
            var numberOfRouteesPerNode = currentNodes.ToDictionary(x => x,
                routee => currentRoutees.Count(y => routee == FullAddress(y)));

            var target = numberOfRouteesPerNode.Aggregate(
                        (curMin, x) =>
                            (x.Value < curMin.Value)
                                ? x
                                : curMin);
            if (target.Value < Settings.MaxInstancesPerNode) return target.Key;
            return null;
        }

        protected override void OnReceive(object message)
        {
            // Moved from RouterPoolActor
            var poolSize = message as AdjustPoolSize;
            if (poolSize != null)
            {
                if (poolSize.Change > 0)
                {
                    var newRoutees = Vector.Fill<Routee>(poolSize.Change)(() => Pool.NewRoutee(Cell.RouteeProps, Context));
                    Cell.AddRoutees(newRoutees);
                }
                else if (poolSize.Change < 0)
                {
                    var currentRoutees = Cell.Router.Routees.ToArray();

                    var abandon = currentRoutees
                        .Skip(currentRoutees.Length + poolSize.Change)
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
