using System;
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
        public ClusterRouterGroupSettings(int totalInstances, bool allowLocalRoutees, ImmutableHashSet<string> routeesPaths) : this(totalInstances, allowLocalRoutees, null, routeesPaths)
        {
        }

        public ClusterRouterGroupSettings(int totalInstances, bool allowLocalRoutees, string useRole, ImmutableHashSet<string> routeesPaths) : base(totalInstances, allowLocalRoutees, useRole)
        {
            RouteesPaths = routeesPaths;
            if(routeesPaths == null || routeesPaths.IsEmpty || string.IsNullOrEmpty(routeesPaths.First())) throw new ArgumentException("routeesPaths must be defined", "routeesPaths");

            //validate that all routeesPaths are relative
            foreach (var path in routeesPaths)
            {
                if(RelativeActorPath.Unapply(path) == null)
                    throw new ArgumentException(string.Format("routeesPaths [{0}] is not a valid relative actor path.", path), "routeesPaths");
            }
        }

        public ImmutableHashSet<string> RouteesPaths { get; private set; }

        public static ClusterRouterGroupSettings FromConfig(Config config)
        {
            return new ClusterRouterGroupSettings(config.GetInt("nr-of-instances"), config.GetBoolean("cluster.allow-local-routees"), config.GetString("cluster.use-role"), ImmutableHashSet.Create(config.GetStringList("routees.paths").ToArray()));
        }
    }

    /// <summary>
    /// <see cref="ClusterRouterSettingsBase.TotalInstances"/> of cluster router must be > 0
    /// <see cref="MaxInstancesPerNode"/> of cluster router must be > 0
    /// <see cref="MaxInstancesPerNode"/> of cluster router must be 1 when routeesPath is defined
    /// </summary>
    public sealed class ClusterRouterPoolSettings : ClusterRouterSettingsBase
    {
        public ClusterRouterPoolSettings(int totalInstances, bool allowLocalRoutees, int maxInstancesPerNode) : this(totalInstances, allowLocalRoutees, null, maxInstancesPerNode)
        {
        }

        public ClusterRouterPoolSettings(int totalInstances, bool allowLocalRoutees, string useRole, int maxInstancesPerNode) : base(totalInstances, allowLocalRoutees, useRole)
        {
            MaxInstancesPerNode = maxInstancesPerNode;
            if(MaxInstancesPerNode <= 0) throw new ArgumentOutOfRangeException("maxInstancesPerNode", "maxInstancesPerNode of cluster pool router must be > 0");
        }

        public int MaxInstancesPerNode { get; private set; }

        public static ClusterRouterPoolSettings FromConfig(Config config)
        {
            return new ClusterRouterPoolSettings(config.GetInt("nr-of-instances"), config.GetBoolean("cluster.allow-local-routees"), config.GetString("cluster.use-role"), config.GetInt("cluster.max-nr-of-instances-per-node"));
        }
    }

    /// <summary>
    /// Base class for defining <see cref="ClusterRouterGroupSettings"/> and <see cref="ClusterRouterPoolSettings"/>
    /// </summary>
    public abstract class ClusterRouterSettingsBase
    {
        protected ClusterRouterSettingsBase(int totalInstances, bool allowLocalRoutees) : this(totalInstances, allowLocalRoutees, null)
        {
        }

        protected ClusterRouterSettingsBase(int totalInstances, bool allowLocalRoutees, string useRole)
        {
            UseRole = useRole;
            AllowLocalRoutees = allowLocalRoutees;
            TotalInstances = totalInstances;

            if(TotalInstances <= 0) throw new ArgumentOutOfRangeException("totalInstances", "totalInstances of cluster router must be > 0");
        }

        public int TotalInstances { get; private set; }

        public bool AllowLocalRoutees { get; private set; }

        public string UseRole { get; private set; }
    }

    /// <summary>
    /// <see cref="RouterConfig"/> implementation for deployment on cluster nodes.
    /// Delegates other duties to the local <see cref="RouterConfig"/>, which makes it
    /// possible to mix this with built-in routers such as <see cref="RoundRobinGroup"/> or
    /// custom routers.
    /// </summary>
    public sealed class ClusterRouterGroup : Group, IClusterRouterConfigBase<Group, ClusterRouterGroupSettings>
    {
        private readonly Group _local;
        private readonly ClusterRouterGroupSettings _settings;

        public ClusterRouterGroup(Group local, ClusterRouterGroupSettings settings)
            : base(settings.AllowLocalRoutees ? settings.RouteesPaths.ToArray() : Enumerable.Empty<string>(),local.RouterDispatcher)
        {
            _settings = settings;
            _local = local;
        }

        public Group Local
        {
            get { return _local; }
        }

        public ClusterRouterGroupSettings Settings
        {
            get { return _settings; }
        }

        public override Router CreateRouter(ActorSystem system)
        {
            return Local.CreateRouter(system);
        }

        internal override RouterActor CreateRouterActor()
        {
            return new ClusterRouterGroupActor(Settings);
        }

        public override bool IsManagementMessage(object message)
        {
            return message is ClusterEvent.IClusterDomainEvent || message is ClusterEvent.CurrentClusterState || base.IsManagementMessage(message);
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            throw new NotImplementedException();
        }

        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            var localFallback = routerConfig as ClusterRouterGroup;
            if (localFallback != null && (localFallback.Local is ClusterRouterGroup)) throw new ConfigurationException("ClusterRouterGroup is not allowed to wrap a ClusterRouterGroup");
            if (localFallback != null) return Copy(Local.WithFallback(localFallback.Local).AsInstanceOf<Group>());
            return Copy(Local.WithFallback(routerConfig).AsInstanceOf<Group>());
        }

        public RouterConfig Copy(Group local = null, ClusterRouterGroupSettings settings = null)
        {
            return new ClusterRouterGroup(local ?? Local, settings ?? Settings);
        }
    }

    /// <summary>
    /// <see cref="RouterConfig"/> implementation for deployment on cluster nodes.
    /// Delegates other duties to the local <see cref="RouterConfig"/>, which makes it
    /// possible to mix this with built-in routers such as <see cref="RoundRobinGroup"/> or
    /// custom routers.
    /// </summary>
    public sealed class ClusterRouterPool : Pool, IClusterRouterConfigBase<Pool, ClusterRouterPoolSettings>
    {
        public ClusterRouterPool(Pool local, ClusterRouterPoolSettings settings)
            : base(settings.AllowLocalRoutees ? settings.MaxInstancesPerNode : 0,
            local.Resizer,
            local.SupervisorStrategy,
             local.RouterDispatcher,false
            )
        {
            if (local.Resizer != null) 
                throw new ConfigurationException("Resizer can't be used together with cluster router.");
            _settings = settings;
            _local = local;
            Guard.Assert(local.Resizer == null, "Resizer can't be used together with cluster router.");
        }

        private readonly AtomicCounter _childNameCounter = new AtomicCounter(0);
        private readonly Pool _local;
        private readonly ClusterRouterPoolSettings _settings;

        public Pool Local
        {
            get { return _local; }
        }

        public ClusterRouterPoolSettings Settings
        {
            get { return _settings; }
        }

        public override SupervisorStrategy SupervisorStrategy
        {
            get { return _local.SupervisorStrategy; }
        }

        public override Resizer Resizer { get { return Local.Resizer; } }


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

        public override int NrOfInstances
        {
            get
            {
                return Local.NrOfInstances;
            }
        }

        public override string RouterDispatcher
        {
            get { return Local.RouterDispatcher; }
        }

        public override Router CreateRouter(ActorSystem system)
        {
            return Local.CreateRouter(system);
        }

        internal override RouterActor CreateRouterActor()
        {
            return new ClusterRouterPoolActor(Local.SupervisorStrategy, Settings);
        }

        

        public override Pool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new ClusterRouterPool(Local.WithSupervisorStrategy(strategy), Settings);
        }

        public override Pool WithResizer(Resizer resizer)
        {
            return new ClusterRouterPool(Local.WithResizer(resizer), Settings);
        }


        public override bool IsManagementMessage(object message)
        {
            return message is ClusterEvent.IClusterDomainEvent || message is ClusterEvent.CurrentClusterState || base.IsManagementMessage(message);
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            throw new NotImplementedException();
        }

        public override Routee NewRoutee(Props routeeProps, IActorContext context)
        {
            var name = "c" + _childNameCounter.GetAndIncrement();
            var actorRef = context.ActorOf(EnrichWithPoolDispatcher(routeeProps, context), name);
            return new ActorRefRoutee(actorRef);
        }

        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            var otherClusterRouterPool = routerConfig as ClusterRouterPool;
            if(otherClusterRouterPool != null && otherClusterRouterPool.Local is ClusterRouterPool) throw new ConfigurationException("ClusterRouterPool is not allowed to wrap a ClusterRouterPool");
            if (otherClusterRouterPool != null)
                return Copy(Local.WithFallback(otherClusterRouterPool.Local).AsInstanceOf<Pool>());
            return Copy(Local.WithFallback(routerConfig).AsInstanceOf<Pool>());
        }

        public RouterConfig Copy(Pool local = null, ClusterRouterPoolSettings settings = null)
        {
            return new ClusterRouterPool(local ?? Local, settings ?? Settings);
        }
       
    }


    /// <summary>
    /// INTERNAL API
    /// 
    /// Have to implement this as an interface rather than a base class, so we can continue to inherit from <see cref="Group"/> and <see cref="Pool"/>
    /// on the concrete cluster router implementations.
    /// </summary>
    public interface IClusterRouterConfigBase<out TR, out TC> where TR:RouterConfig
                                                        where TC:ClusterRouterSettingsBase
    {
        TR Local { get; }

        TC Settings { get; }
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
            var routedActorCell = (RoutedActorCell) Context;
            if (routedActorCell == null)
            {
                throw new NotSupportedException("Current Context must be of type RouterActorContext");
            }
            
            if(!(routedActorCell.RouterConfig is Pool) && !(routedActorCell.RouterConfig is Group))
                throw new NotSupportedException(string.Format("Cluster router actor can only be used with Pool or Group, now with {0}", routedActorCell.RouterConfig.GetType()));

            Nodes = ImmutableSortedSet.Create(Member.AddressOrdering, Cluster.ReadView.Members.Where(IsAvailable).Select(x => x.Address).ToArray());
        }

        private readonly Cluster _cluster = Cluster.Get(Context.System);
        public Cluster Cluster { get { return _cluster; } }

        public ClusterRouterSettingsBase Settings { get; protected set; }

        public ImmutableSortedSet<Address> Nodes { get; private set; }

        public ImmutableSortedSet<Address> AvailableNodes
        {
            get
            {
                var currentNodes = Nodes;
                if (currentNodes.IsEmpty && Settings.AllowLocalRoutees && SatisfiesRole(Cluster.SelfRoles))
                {
                    //use my own node, cluster information not updated yet
                    return ImmutableSortedSet.Create(Cluster.SelfAddress);
                }
                return currentNodes;
            }
        }

        public bool IsAvailable(Member m)
        {
            return m.Status == MemberStatus.Up &&
                   SatisfiesRole(m.Roles) &&
                   (Settings.AllowLocalRoutees || m.Address != Cluster.SelfAddress);
        }

        private bool SatisfiesRole(ImmutableHashSet<string> memberRoles)
        {
            if (string.IsNullOrEmpty(Settings.UseRole)) return true;
            return memberRoles.Contains(Settings.UseRole);
        }

        /// <summary>
        /// Fills in self address for local <see cref="IActorRef"/>
        /// </summary>
        public Address FullAddress(Routee routee)
        {
            Address a = null;
            if (routee is ActorRefRoutee) { a = ((ActorRefRoutee)routee).Actor.Path.Address; }
            else if (routee is ActorSelectionRoutee) { a = ((ActorSelectionRoutee)routee).Selection.Anchor.Path.Address; }

            if (a == null || string.IsNullOrEmpty(a.Host) || !a.Port.HasValue) return Cluster.SelfAddress; //local address
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
            Cell.RemoveRoutees(affectedRoutees, true);

            // addRoutees will not create more than createRoutees and maxInstancesPerNode
            // this is useful when totalInstances < upNodes.size
            AddRoutees();
        }

        protected override void PreStart()
        {
            Cluster.Subscribe(Self, new []{ typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.IReachabilityEvent) });
        }

        protected override void PostStop()
        {
            Cluster.Unsubscribe(Self);
        }

        protected override void OnReceive(object message)
        {
            if (message is ClusterEvent.CurrentClusterState)
            {
                var state = message as ClusterEvent.CurrentClusterState;
                Nodes = ImmutableSortedSet.Create(Member.AddressOrdering,
                      state.Members.Where(IsAvailable).Select(x => x.Address).ToArray());
                AddRoutees();
            }
            else if (message is ClusterEvent.IMemberEvent)
            {
                var @event = message as ClusterEvent.IMemberEvent;
                if (IsAvailable(@event.Member))
                    AddMember(@event.Member);
                else
                {
                    // other events means that it is no onger interesting, such as
                    // MemberExited, MemberRemoved
                    RemoveMember(@event.Member);
                }
            }
            else if (message is ClusterEvent.UnreachableMember)
            {
                var member = message as ClusterEvent.UnreachableMember;
                RemoveMember(member.Member);
            }
            else if (message is ClusterEvent.ReachableMember)
            {
                var member = message as ClusterEvent.ReachableMember;
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
                ? ImmutableDictionary<Address, ImmutableHashSet<string>>.Empty.Add(Cluster.SelfAddress,
                    settings.RouteesPaths)
                : ImmutableDictionary<Address, ImmutableHashSet<string>>.Empty;
        }

        public new ClusterRouterGroupSettings Settings { get; private set; }

        private readonly Group _group;

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
                    UsedRouteePaths = UsedRouteePaths.SetItem(address,
                        UsedRouteePaths.GetOrElse(address, ImmutableHashSet<string>.Empty).Add(path));

                    var currentRoutees = Cell.Router.Routees.ToList();

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
                var minNode =
                    UsedRouteePaths.Select(x => new{ Address = x.Key, Used = x.Value }).OrderBy(x => x.Used.Count).First();

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
        public ClusterRouterPoolActor(SupervisorStrategy supervisorStrategy, ClusterRouterPoolSettings settings) : base(settings)
        {
            Settings = settings;
            _supervisorStrategy = supervisorStrategy;
            _pool = (Pool)Cell.RouterConfig;
        }

        private readonly Pool _pool;

        private readonly SupervisorStrategy _supervisorStrategy;

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
                var deploy = new Deploy(routeeProps.RouterConfig, new RemoteScope(deploymentTarget));
                
                var routee = _pool.NewRoutee(routeeProps.WithDeploy(deploy), Context);

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
    }

}
