using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.Util.Internal;

namespace Akka.Cluster.Routing
{
    /// <summary>
    /// `TotalInstances` of cluster router must be > 0
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

            //todo add relative actor path validation
        }

        public ImmutableHashSet<string> RouteesPaths { get; private set; }

        public static ClusterRouterGroupSettings FromConfig(Config config)
        {
            return new ClusterRouterGroupSettings(config.GetInt("nr-of-instances"), config.GetBoolean("cluster.allow-local-routees"), config.GetString("cluster.use-role"), ImmutableHashSet.Create(config.GetStringList("routees.paths").ToArray()));
        }
    }

    /// <summary>
    /// `totalInstances` of cluster router must be > 0
    /// `maxInstancesPerNode` of cluster router must be > 0
    /// `maxInstancesPerNode` of cluster router must be 1 when routeesPath is defined
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
    public sealed class ClusterRouterGroup : Group, IClusterRouterConfigBase
    {
        public ClusterRouterGroup(Group local, ClusterRouterGroupSettings settings)
        {
            Settings = settings;
            Local = local;
            Paths = settings.AllowLocalRoutees ? settings.RouteesPaths.ToArray() : null;
            RouterDispatcher = local.RouterDispatcher;
        }

        public RouterConfig Local { get; private set; }
        public ClusterRouterSettingsBase Settings { get; private set; }

        public override Router CreateRouter(ActorSystem system)
        {
            return Local.CreateRouter(system);
        }

        public override RouterActor CreateRouterActor()
        {
            return new ClusterRouterGroupActor((ClusterRouterGroupSettings) Settings);
        }

        public override bool IsManagementMessage(object message)
        {
            return message is IClusterMessage || message is ClusterEvent.CurrentClusterState || base.IsManagementMessage(message);
        }

        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            var localFallback = (ClusterRouterGroup) routerConfig;
            if (localFallback != null && (localFallback.Local is ClusterRouterGroup)) throw new ConfigurationException("ClusterRouterGroup is not allowed to wrap a ClusterRouterGroup");
            if (localFallback != null) return Copy(Local.WithFallback(localFallback.Local).AsInstanceOf<Group>());
            return Copy(Local.WithFallback(routerConfig).AsInstanceOf<Group>());
        }

        public RouterConfig Copy(Group local = null, ClusterRouterGroupSettings settings = null)
        {
            return new ClusterRouterGroup(local ?? (Group)Local, settings ?? (ClusterRouterGroupSettings)Settings);
        }
    }

    /// <summary>
    /// <see cref="RouterConfig"/> implementation for deployment on cluster nodes.
    /// Delegates other duties to the local <see cref="RouterConfig"/>, which makes it
    /// possible to mix this with built-in routers such as <see cref="RoundRobinGroup"/> or
    /// custom routers.
    /// </summary>
    public sealed class ClusterRouterPool : Pool, IClusterRouterConfigBase
    {
        public ClusterRouterPool(Pool local, ClusterRouterPoolSettings settings)
        {
            Settings = settings;
            Local = local;
            RouterDispatcher = local.RouterDispatcher;

            if(local.Resizer != null) throw new ConfigurationException("Resizer can't be used together with cluster router.");
            NrOfInstances = Settings.AllowLocalRoutees ? settings.MaxInstancesPerNode : 0;
            Resizer = local.Resizer;
            SupervisorStrategy = local.SupervisorStrategy;
        }

        private readonly AtomicCounter _childNameCounter = new AtomicCounter(0);

        public RouterConfig Local { get; private set; }
        public ClusterRouterSettingsBase Settings { get; private set; }

        public override Router CreateRouter(ActorSystem system)
        {
            return Local.CreateRouter(system);
        }

        public override RouterActor CreateRouterActor()
        {
            return new ClusterRouterPoolActor(((Pool) Local).SupervisorStrategy, (ClusterRouterPoolSettings) Settings);
        }


        public override bool IsManagementMessage(object message)
        {
            return message is IClusterMessage || message is ClusterEvent.CurrentClusterState || base.IsManagementMessage(message);
        }

        public override Routee NewRoutee(Props routeeProps, IActorContext context)
        {
            var name = "c" + _childNameCounter.GetAndIncrement();
            var actorRef = context.ActorOf(EnrichWithPoolDispatcher(routeeProps, context), name);
            return new ActorRefRoutee(actorRef);
        }

        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            var otherClusterRouterPool = (ClusterRouterPool) routerConfig;
            if(otherClusterRouterPool != null && otherClusterRouterPool.Local is ClusterRouterPool) throw new ConfigurationException("ClusterRouterPool is not allowed to wrap a ClusterRouterPool");
            if (otherClusterRouterPool != null)
                return Copy(Local.WithFallback(otherClusterRouterPool.Local).AsInstanceOf<Pool>());
            return Copy(Local.WithFallback(routerConfig).AsInstanceOf<Pool>());
        }

        public RouterConfig Copy(Pool local = null, ClusterRouterPoolSettings settings = null)
        {
            return new ClusterRouterPool(local ?? (Pool)Local, settings ?? (ClusterRouterPoolSettings)Settings);
        }
       
    }


    /// <summary>
    /// INTERNAL API
    /// 
    /// Have to implement this as an interface rather than a base class, so we can continue to inherit from <see cref="Group"/> and <see cref="Pool"/>
    /// on the concrete cluster router implementations.
    /// </summary>
    public interface IClusterRouterConfigBase
    {
        RouterConfig Local { get; }

        ClusterRouterSettingsBase Settings { get; }
    }

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
        /// Fills in self address for local <see cref="ActorRef"/>
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
            message.Match()
                .With<ClusterEvent.CurrentClusterState>(state =>
                {
                    Nodes = ImmutableSortedSet.Create(Member.AddressOrdering,
                        state.Members.Where(IsAvailable).Select(x => x.Address).ToArray());
                    AddRoutees();
                })
                .With<ClusterEvent.IMemberEvent>(@event =>
                {
                    if (IsAvailable(@event.Member))
                        AddMember(@event.Member);
                    else
                    {
                        // other events means that it is no onger interesting, such as
                        // MemberExited, MemberRemoved
                        RemoveMember(@event.Member);
                    }
                })
                .With<ClusterEvent.UnreachableMember>(member => RemoveMember(member.Member))
                .With<ClusterEvent.ReachableMember>(member =>
                {
                    if (IsAvailable(member.Member)) AddMember(member.Member);
                })
                .Default(msg => base.OnReceive(msg));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ClusterRouterGroupActor : ClusterRouterActor
    {
        public ClusterRouterGroupActor(ClusterRouterGroupSettings settings) : base(settings)
        {
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
            Settings = settings;
        }

        public new ClusterRouterGroupSettings Settings { get; private set; }

        private readonly Group _group;

        public ImmutableDictionary<Address, ImmutableHashSet<string>> UsedRouteePaths { get; private set; }

        /// <summary>
        /// Adds routees based on totalInstances and maxInstancesPerNode settings
        /// </summary>
        public override void AddRoutees()
        {
            var deploymentTarget = SelectDeploymentTarget();
            while (deploymentTarget != null)
            {
                var address = deploymentTarget.Item1;
                var path = deploymentTarget.Item2;
                var routee = _group.RouteeFor(address + path, Context);
                UsedRouteePaths = UsedRouteePaths.SetItem(address,
                    UsedRouteePaths.GetOrElse(address, ImmutableHashSet<string>.Empty).Add(path));

                //must register each one, since registered routees are used in SelectDeploymentTarget
                Cell.AddRoutee(routee);

                deploymentTarget = SelectDeploymentTarget();
            }
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
                var minNode =
                    UsedRouteePaths.Aggregate(
                        (curMin, x) =>
                            (curMin.Value == ImmutableHashSet<string>.Empty || x.Value.Count < curMin.Value.Count)
                                ? x
                                : curMin);

                // pick next of unused paths
                var minPath = Settings.RouteesPaths.First(p => !minNode.Value.Contains(p));
                return new Tuple<Address, string>(minNode.Key, minPath);
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
            var numberOfRouteesPerNode = currentRoutees.ToDictionary(FullAddress,
                routee => currentNodes.Count(y => y == FullAddress(routee)));

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
