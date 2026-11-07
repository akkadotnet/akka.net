//-----------------------------------------------------------------------
// <copyright file="ClusterRoutingConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterRouterGroupSettings"/> class.
        /// </summary>
        /// <param name="totalInstances">The total number of routees. Defaults to 10000.</param>
        /// <param name="routeesPaths">The actor selection paths to use for each routee.</param>
        /// <param name="allowLocalRoutees">When <c>true</c>, allows routees to be deployed locally 
        /// on the node doing the deploying so long as that node also 
        /// satisfies the useRole setting when used.</param>
        /// <param name="useRole">The role of the node upon which we are able to create routees.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the specified <paramref name="routeesPaths"/> is undefined
        /// or a path defined in the specified <paramref name="routeesPaths"/> is an invalid relative actor path.
        /// </exception>
        public ClusterRouterGroupSettings(int totalInstances, IEnumerable<string> routeesPaths, bool allowLocalRoutees, string useRole = null) 
            : base(totalInstances, allowLocalRoutees, useRole)
        {
            if (string.IsNullOrEmpty(routeesPaths?.FirstOrDefault()))
                throw new ArgumentException("RouteesPaths must be defined", nameof(routeesPaths));

            RouteesPaths = routeesPaths;

            // validate that all RouteesPaths are relative
            foreach (var path in routeesPaths)
            {
                if (RelativeActorPath.Unapply(path) == null)
                    throw new ArgumentException($"routeesPaths [{path}] is not a valid relative actor path.", nameof(routeesPaths));
            }
        }

        /// <summary>
        /// The paths of the routees to use on each qualified node.
        /// </summary>
        public IEnumerable<string> RouteesPaths { get; }

        /// <summary>
        /// Creates a new <see cref="ClusterRouterGroupSettings"/> from the specified configuration.
        /// </summary>
        /// <param name="config">The configuration used to configure the settings.</param>
        /// <returns>New settings based on the specified <paramref name="config"/></returns>
        public static ClusterRouterGroupSettings FromConfig(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterRouterGroupSettings>();

            return new ClusterRouterGroupSettings(
                GetMaxTotalNrOfInstances(config),
                ImmutableHashSet.CreateRange(config.GetStringList("routees.paths")),
                config.GetBoolean("cluster.allow-local-routees", false),
                UseRoleOption(config.GetString("cluster.use-role", null)));
        }
    }

    /// <summary>
    /// <see cref="ClusterRouterSettingsBase.TotalInstances"/> of cluster router must be > 0
    /// <see cref="MaxInstancesPerNode"/> of cluster router must be > 0
    /// <see cref="MaxInstancesPerNode"/> of cluster router must be 1 when routeesPath is defined
    /// </summary>
    public sealed class ClusterRouterPoolSettings : ClusterRouterSettingsBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterRouterPoolSettings"/> class.
        /// </summary>
        /// <param name="totalInstances">The target total number of routees across the cluster.</param>
        /// <param name="maxInstancesPerNode">The maximum number of routees deployed on one eligible node.</param>
        /// <param name="allowLocalRoutees">Whether the router may deploy routees on its own node.</param>
        /// <param name="useRole">The role required on a node before it can host routees, or <c>null</c> for no role requirement.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// This exception is thrown when the specified <paramref name="maxInstancesPerNode"/> is less than or equal to zero.
        /// </exception>
        public ClusterRouterPoolSettings(int totalInstances, int maxInstancesPerNode, bool allowLocalRoutees, string useRole = null)
            : base(totalInstances, allowLocalRoutees, useRole)
        {
            MaxInstancesPerNode = maxInstancesPerNode;

            if (MaxInstancesPerNode <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxInstancesPerNode), "maxInstancesPerNode of cluster pool router must be > 0");
        }

        /// <summary>
        /// The maximum number of routee actors that can be deployed per valid node.
        /// </summary>
        public int MaxInstancesPerNode { get; }

        /// <summary>
        /// Creates a new <see cref="ClusterRouterPoolSettings"/> from the specified configuration.
        /// </summary>
        /// <param name="config">The configuration used to configure the settings.</param>
        /// <returns>New settings based on the specified <paramref name="config"/></returns>
        public static ClusterRouterPoolSettings FromConfig(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterRouterPoolSettings>();

            return new ClusterRouterPoolSettings(
                GetMaxTotalNrOfInstances(config),
                config.GetInt("cluster.max-nr-of-instances-per-node", 0),
                config.GetBoolean("cluster.allow-local-routees", false),
                UseRoleOption(config.GetString("cluster.use-role", null)));
        }

        private bool Equals(ClusterRouterPoolSettings other)
        {
            return MaxInstancesPerNode == other.MaxInstancesPerNode
                && TotalInstances == other.TotalInstances 
                && AllowLocalRoutees == other.AllowLocalRoutees 
                && string.Equals(UseRole, other.UseRole);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((ClusterRouterPoolSettings)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = MaxInstancesPerNode;
                hashCode = (hashCode * 397) ^ TotalInstances.GetHashCode();
                hashCode = (hashCode * 397) ^ AllowLocalRoutees.GetHashCode();
                hashCode = (hashCode * 397) ^ (UseRole?.GetHashCode() ?? 0);
                return hashCode;
            }
        }
    }

    /// <summary>
    /// Base class for defining <see cref="ClusterRouterGroupSettings"/> and <see cref="ClusterRouterPoolSettings"/>
    /// </summary>
    public abstract class ClusterRouterSettingsBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterRouterSettingsBase"/> class.
        /// </summary>
        /// <param name="totalInstances">The target total number of routees across the cluster.</param>
        /// <param name="allowLocalRoutees">Whether the router may deploy routees on its own node.</param>
        /// <param name="useRole">The role required on a node before it can host routees, or <c>null</c> for no role requirement.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// This exception is thrown when the specified <paramref name="useRole"/> is undefined
        /// or the specified <paramref name="totalInstances"/> is less than or equal to zero.
        /// </exception>
        protected ClusterRouterSettingsBase(int totalInstances, bool allowLocalRoutees, string useRole)
        {
            UseRole = useRole;
            AllowLocalRoutees = allowLocalRoutees;
            TotalInstances = totalInstances;

            if (useRole == string.Empty) throw new ArgumentOutOfRangeException(nameof(useRole), "useRole must be either null or non-empty");
            if (totalInstances <= 0) throw new ArgumentOutOfRangeException(nameof(totalInstances), "totalInstances of cluster router must be > 0");
        }

        /// <summary>
        /// The requested total number of routees across the cluster.
        /// </summary>
        public int TotalInstances { get; }

        /// <summary>
        /// Whether this router may create routees on its own node.
        /// </summary>
        public bool AllowLocalRoutees { get; }

        /// <summary>
        /// The role a node must have to host routees, or <c>null</c> when no role is required.
        /// </summary>
        public string UseRole { get; }

        /// <summary>
        /// Returns the role value used by settings, converting a null or empty string to <c>null</c>.
        /// </summary>
        /// <param name="role">The configured role name.</param>
        /// <returns>The non-empty role name, or <c>null</c> when the value is null or empty.</returns>
        internal static string UseRoleOption(string role) => !string.IsNullOrEmpty(role) ? role : null;

        /// <summary>
        /// For backwards compatibility reasons, nr-of-instances
        /// has the same purpose as max-total-nr-of-instances for cluster
        /// aware routers and nr-of-instances (if defined by user) takes
        /// precedence over max-total-nr-of-instances.
        /// </summary>
        internal static int GetMaxTotalNrOfInstances(Config config)
        {
            int number = config.GetInt("nr-of-instances", 0);
            if (number is 0 or 1)
            {
                return config.GetInt("cluster.max-nr-of-instances-per-node");
            }
            else
            {
                return number;
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
        private readonly AtomicCounter _childNameCounter = new(0);

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterRouterPool"/> class.
        /// </summary>
        /// <param name="local">The local pool configuration used for routee creation and router behavior.</param>
        /// <param name="settings">The cluster-wide and per-node routee settings.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when the resizer in the specified pool <paramref name="local"/> is defined.
        /// A resizer cannot be used in conjunction with a cluster router.
        /// </exception>
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

        /// <summary>
        /// The cluster-router settings used to place routees across member nodes.
        /// </summary>
        public ClusterRouterPoolSettings Settings { get; }

        /// <summary>
        /// The local pool configuration delegated to for router behavior.
        /// </summary>
        public Pool Local { get; }

        /// <summary>
        /// Creates a local child routee using the pool's dispatcher settings.
        /// </summary>
        /// <param name="routeeProps">The actor properties used to create the routee.</param>
        /// <param name="context">The router actor context that owns the child.</param>
        /// <returns>A routee wrapping the created child actor.</returns>
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

        /// <inheritdoc cref="RouterConfig.CreateRouterActor"/>
        public override ActorBase CreateRouterActor()
        {
#pragma warning disable AK1000
            return new ClusterRouterPoolActor(Local.SupervisorStrategy, Settings);
#pragma warning restore AK1000
        }

        /// <summary>
        /// Retrieve the strategy to use when supervising the pool.
        /// </summary>
        public override SupervisorStrategy SupervisorStrategy => Local.SupervisorStrategy;

        /// <summary>
        /// Configure the current router with an auxiliary router for routes that it does not know how to handle.
        /// </summary>
        /// <param name="routerConfig">The router to use as an auxiliary source.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when the specified router is another <see cref="ClusterRouterPool"/>.
        /// This configuration is not allowed.
        /// </exception>
        /// <returns>The router configured with the auxiliary information.</returns>
        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            switch(routerConfig)
            {
                case ClusterRouterPool otherClusterRouterPool when otherClusterRouterPool.Local is ClusterRouterPool:
                    throw new ConfigurationException("ClusterRouterPool is not allowed to wrap a ClusterRouterPool");
                case ClusterRouterPool otherClusterRouterPool:
                    return Copy(Local.WithFallback(otherClusterRouterPool.Local).AsInstanceOf<Pool>());
                default:
                    return Copy(Local.WithFallback(routerConfig).AsInstanceOf<Pool>());
            }
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The ActorSystem this router belongs to.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return Local.CreateRouter(system);
        }

        /// <summary>
        /// Dispatcher ID to use for running the "head" actor, which handles supervision, death watch and router management messages.
        /// </summary>
        public override string RouterDispatcher => Local.RouterDispatcher;

        /// <summary>
        /// Specify that this router should stop itself when all routees have terminated (been removed).
        /// </summary>
        public override bool StopRouterWhenAllRouteesRemoved => false;

        /// <summary>
        /// Delegates routing-logic controller creation to the configured local pool.
        /// </summary>
        /// <param name="routingLogic">The routing logic for which to create a controller.</param>
        /// <returns>The controller properties produced by the local pool.</returns>
        public override Props RoutingLogicController(RoutingLogic routingLogic)
        {
            return Local.RoutingLogicController(routingLogic);
        }

        /// <summary>
        /// Determines whether a provided message is handled by the router.
        /// </summary>
        /// <param name="message">The message to inspect.</param>
        /// <returns><c>true</c> if this message is handled by the router; otherwise <c>false</c>.</returns>
        public override bool IsManagementMessage(object message)
        {
            return message is ClusterEvent.IClusterDomainEvent
                || message is ClusterEvent.CurrentClusterState
                || base.IsManagementMessage(message);
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="system">N/A</param>
        /// <exception cref="NotImplementedException">
        /// This exception is thrown automatically since surrogates aren't supported by this router.
        /// </exception>
        /// <returns>N/A</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a copy using replacement local-pool or settings values where supplied.
        /// </summary>
        /// <param name="local">The replacement local pool, or <c>null</c> to retain the current pool.</param>
        /// <param name="settings">The replacement settings, or <c>null</c> to retain the current settings.</param>
        /// <returns>A cluster router pool using the supplied replacements and retained values.</returns>
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
        /// <summary>
        /// Creates a cluster router group that selects routee paths from cluster settings and uses the local group to create router behavior.
        /// </summary>
        /// <param name="local">The local group configuration used to create the router behavior.</param>
        /// <param name="settings">The cluster-wide routee paths and placement settings.</param>
        public ClusterRouterGroup(Group local, ClusterRouterGroupSettings settings)
            : base(settings.AllowLocalRoutees ? settings.RouteesPaths.ToArray() : Enumerable.Empty<string>(), local.RouterDispatcher)
        {
            Settings = settings;
            Local = local;
        }

        /// <summary>
        /// The cluster-router settings used to select routee paths across member nodes.
        /// </summary>
        public ClusterRouterGroupSettings Settings { get; }

        /// <summary>
        /// The local group configuration used to create the router behavior.
        /// </summary>
        public Group Local { get; }

        /// <summary>
        /// Retrieves the actor paths used by this router during routee selection.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>An enumeration of actor paths used during routee selection</returns>
        public override IEnumerable<string> GetPaths(ActorSystem system)
        {
            if (!Settings.AllowLocalRoutees)
                return null;

            if (!string.IsNullOrEmpty(Settings.UseRole)
                && !Cluster.Get(system).SelfRoles.Contains(Settings.UseRole))
                return null;

            return Settings.RouteesPaths;
        }

        /// <inheritdoc cref="RouterConfig.CreateRouterActor"/>
        public override ActorBase CreateRouterActor()
        {
#pragma warning disable AK1000
            return new ClusterRouterGroupActor(Settings);
#pragma warning restore AK1000
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The ActorSystem this router belongs to.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return Local.CreateRouter(system);
        }

        /// <summary>
        /// Dispatcher ID to use for running the "head" actor, which handles supervision, death watch and router management messages.
        /// </summary>
        public override string RouterDispatcher => Local.RouterDispatcher;

        /// <summary>
        /// Specify that this router should stop itself when all routees have terminated (been removed).
        /// By Default it is `true`, unless a `resizer` is used.
        /// </summary>
        public override bool StopRouterWhenAllRouteesRemoved => false;

        /// <summary>
        /// Delegates routing-logic controller creation to the configured local group.
        /// </summary>
        /// <param name="routingLogic">The routing logic for which to create a controller.</param>
        /// <returns>The controller properties produced by the local group.</returns>
        public override Props RoutingLogicController(RoutingLogic routingLogic)
        {
            return Local.RoutingLogicController(routingLogic);
        }

        /// <summary>
        /// Determines whether a provided message is handled by the router.
        /// </summary>
        /// <param name="message">The message to inspect.</param>
        /// <returns><c>true</c> if this message is handled by the router; otherwise <c>false</c>.</returns>
        public override bool IsManagementMessage(object message)
        {
            return message is ClusterEvent.IClusterDomainEvent
                || message is ClusterEvent.CurrentClusterState
                || base.IsManagementMessage(message);
        }

        /// <summary>
        /// Creates a surrogate representation of the current router.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current router.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return Local.ToSurrogate(system);
        }

        /// <summary>
        /// Configure the current router with an auxiliary router for routes that it does not know how to handle.
        /// </summary>
        /// <param name="routerConfig">The router to use as an auxiliary source.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when the specified router is another <see cref="ClusterRouterGroup"/>.
        /// This configuration is not allowed.
        /// </exception>
        /// <returns>The router configured with the auxiliary information.</returns>
        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            switch(routerConfig)
            {
                case ClusterRouterGroup localFallback when localFallback.Local is ClusterRouterGroup:
                    throw new ConfigurationException("ClusterRouterGroup is not allowed to wrap a ClusterRouterGroup");
                case ClusterRouterGroup localFallback:
                    return Copy(Local.WithFallback(localFallback.Local).AsInstanceOf<Group>());
                default:
                    return Copy(Local.WithFallback(routerConfig).AsInstanceOf<Group>());
            }
        }

        /// <summary>
        /// Creates a copy using replacement local-group or settings values where supplied.
        /// </summary>
        /// <param name="local">The replacement local group, or <c>null</c> to retain the current group.</param>
        /// <param name="settings">The replacement settings, or <c>null</c> to retain the current settings.</param>
        /// <returns>A cluster router group using the supplied replacements and retained values.</returns>
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
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterRouterActor"/> class.
        /// </summary>
        /// <param name="settings">The settings used to configure the router.</param>
        /// <exception cref="ActorInitializationException">
        /// This exception is thrown when this actor is configured as something other than a <see cref="Pool"/> router or <see cref="Group"/> router.
        /// </exception>
        protected ClusterRouterActor(ClusterRouterSettingsBase settings)
        {
            Settings = settings;

            if (!(Cell.RouterConfig is Pool) && !(Cell.RouterConfig is Group))
            {
                throw new ActorInitializationException(
                    $"Cluster router actor can only be used with Pool or Group, not with {Cell.RouterConfig.GetType()}");
            }

            Cluster = Cluster.Get(Context.System);
            Nodes = ImmutableSortedSet.CreateRange(Member.AddressOrdering,
                    Cluster.ReadView.Members.Where(IsAvailable).Select(x => x.Address));
        }

        /// <summary>
        /// The cluster router settings used to determine routee placement.
        /// </summary>
        public ClusterRouterSettingsBase Settings { get; protected set; }

        /// <summary>
        /// The cluster extension whose membership and roles determine routee placement.
        /// </summary>
        public Cluster Cluster { get; }

        /// <summary>
        /// Subscribes to membership and reachability events, then creates routees for eligible nodes.
        /// </summary>
        protected override void PreStart()
        {
            Cluster.Subscribe(Self, new[]
            {
                typeof(ClusterEvent.IMemberEvent),
                typeof(ClusterEvent.IReachabilityEvent)
            });
            AddRoutees();
        }

        /// <summary>
        /// Unsubscribes from cluster events when the router actor stops.
        /// </summary>
        protected override void PostStop()
        {
            Cluster.Unsubscribe(Self);
        }

        /// <summary>
        /// The current eligible cluster-node addresses, ordered by member address.
        /// </summary>
        public ImmutableSortedSet<Address> Nodes { get; private set; }

        /// <summary>
        /// Whether the member's status, role, and local-routee setting allow it to host routees.
        /// </summary>
        /// <param name="member">The cluster member to evaluate.</param>
        /// <returns><c>true</c> when the member is Up or WeaklyUp, satisfies the configured role, and is local only when local routees are allowed.</returns>
        public bool IsAvailable(Member member)
        {
            return member.Status is MemberStatus.Up or MemberStatus.WeaklyUp && 
                   SatisfiesRole(member.Roles) &&
                   (Settings.AllowLocalRoutees || member.Address != Cluster.SelfAddress);
        }

        private bool SatisfiesRole(ImmutableHashSet<string> memberRoles)
        {
            return string.IsNullOrEmpty(Settings.UseRole) || memberRoles.Contains(Settings.UseRole);
        }

        /// <summary>
        /// Eligible cluster nodes, with the local node used as a temporary fallback before membership information arrives when local routees are allowed and its role matches.
        /// </summary>
        public ImmutableSortedSet<Address> AvailableNodes
        {
            get
            {
                if (Nodes.IsEmpty && Settings.AllowLocalRoutees && SatisfiesRole(Cluster.SelfRoles))
                {
                    //use my own node, cluster information not updated yet
                    return ImmutableSortedSet.Create(Member.AddressOrdering, Cluster.SelfAddress);
                }
                return Nodes;
            }
        }

        /// <summary>
        /// Fills in self address for local <see cref="IActorRef"/>
        /// </summary>
        /// <param name="routee">The routee whose node address is requested.</param>
        /// <returns>The routee's remote address, or the local cluster address for a local routee or an unsupported routee type.</returns>
        public Address FullAddress(Routee routee)
        {
            Address a;
            switch(routee)
            {
                case ActorRefRoutee r:
                    a = r.Actor.Path.Address;
                    break;
                case ActorSelectionRoutee r:
                    a = r.Selection.Anchor.Path.Address;
                    break;
                default:
                    a = null;
                    break;
            }

            if (string.IsNullOrEmpty(a?.Host) || !a.Port.HasValue)
                return Cluster.SelfAddress; //local address

            return a;
        }

        /// <summary>
        /// Adds routees based on settings
        /// </summary>
        public abstract void AddRoutees();

        /// <summary>
        /// Adds the member address to the eligible node set and creates any routees needed by the settings.
        /// </summary>
        /// <param name="member">The eligible member to add.</param>
        public void AddMember(Member member)
        {
            Nodes = Nodes.Add(member.Address);
            AddRoutees();
        }

        /// <summary>
        /// Removes the member and its routees, then creates routees on the remaining eligible nodes as needed.
        /// </summary>
        /// <param name="member">The member whose node and routees are removed.</param>
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

        /// <summary>
        /// Updates eligible nodes and routees in response to cluster state, membership, or reachability events.
        /// </summary>
        /// <param name="message">A cluster state or membership/reachability event.</param>
        protected override void OnReceive(object message)
        {
            switch(message)
            {
                case ClusterEvent.CurrentClusterState state:
                    Nodes = ImmutableSortedSet.CreateRange(Member.AddressOrdering, state.Members.Where(IsAvailable).Select(x => x.Address));
                    AddRoutees();
                    break;
                case ClusterEvent.IMemberEvent memberEvent when IsAvailable(memberEvent.Member):
                    AddMember(memberEvent.Member);
                    break;
                case ClusterEvent.IMemberEvent memberEvent:
                    // other events means that it is no onger interesting, such as
                    // MemberExited, MemberRemoved
                    RemoveMember(memberEvent.Member);
                    break;
                case ClusterEvent.UnreachableMember member:
                    RemoveMember(member.Member);
                    break;
                case ClusterEvent.ReachableMember member when IsAvailable(member.Member):
                    AddMember(member.Member);
                    break;
                case ClusterEvent.ReachableMember member:
                    //ignore
                    break;
                default:
                    base.OnReceive(message);
                    break;
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ClusterRouterGroupActor : ClusterRouterActor
    {
        private readonly Group _group;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterRouterGroupActor"/> class.
        /// </summary>
        /// <param name="settings">The settings used to configure the router.</param>
        /// <exception cref="ActorInitializationException">
        /// This exception is thrown when this actor is configured as something other than a <see cref="Group"/> router.
        /// </exception>
        public ClusterRouterGroupActor(ClusterRouterGroupSettings settings) : base(settings)
        {
            Settings = settings;

            _group = Cell.RouterConfig as Group 
                ?? throw new ActorInitializationException(
                    $"ClusterRouterGroupActor can only be used with group, not {Cell.RouterConfig.GetType()}"); ;

            if (Settings.AllowLocalRoutees)
                UsedRouteePaths = UsedRouteePaths.Add(Cluster.SelfAddress, settings.RouteesPaths.ToImmutableHashSet());
        }

        /// <summary>
        /// The cluster-router settings used by this group router actor.
        /// </summary>
        public new ClusterRouterGroupSettings Settings { get; private set; }

        /// <summary>
        /// Routee paths already assigned to each cluster node by this router.
        /// </summary>
        public ImmutableDictionary<Address, ImmutableHashSet<string>> UsedRouteePaths { get; private set; } = ImmutableDictionary<Address, ImmutableHashSet<string>>.Empty;

        /// <summary>
        /// Adds routees based on totalInstances and maxInstancesPerNode settings
        /// </summary>
        public override void AddRoutees()
        {
            var deploymentTarget = SelectDeploymentTarget();
            while (deploymentTarget != null)
            {
                var address = deploymentTarget.Value.Item1;
                var path = deploymentTarget.Value.Item2;
                var routee = _group.RouteeFor(address + path, Context);
                UsedRouteePaths = UsedRouteePaths.SetItem(
                    address,
                    UsedRouteePaths.GetOrElse(address, ImmutableHashSet<string>.Empty).Add(path));

                //must register each one, since registered routees are used in SelectDeploymentTarget
                Cell.AddRoutee(routee);

                deploymentTarget = SelectDeploymentTarget();
            }
        }

        /// <summary>
        /// Selects an eligible node and routee path while the configured total has not been reached.
        /// </summary>
        /// <returns>A node and unused routee path, or <c>null</c> when no eligible placement remains.</returns>
        public (Address, string)? SelectDeploymentTarget()
        {
            var currentRoutees = Cell.Router.Routees.ToList();
            var currentNodes = AvailableNodes;
            if (currentNodes.IsEmpty || currentRoutees.Count >= Settings.TotalInstances)
                return null;

            //find the node with the least routees
            var unusedNodes = currentNodes.Except(UsedRouteePaths.Keys);
            if (!unusedNodes.IsEmpty) //we found at least 1 totally unused node
            {
                return (unusedNodes.First(), Settings.RouteesPaths.First());
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
                return minPath == null ? ((Address, string)?)null : (minNode.Address, minPath);
            }
        }

        /// <summary>
        /// Removes the node's used-path tracking, then removes its routees through the base router actor.
        /// </summary>
        /// <param name="member">The member whose routees and path assignments are removed.</param>
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
        /// <summary>
        /// The local pool configuration used to create and supervise routees.
        /// </summary>
        protected Pool Pool;
        private readonly SupervisorStrategy _supervisorStrategy;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterRouterPoolActor"/> class.
        /// </summary>
        /// <param name="supervisorStrategy">The strategy used to supervise the pool.</param>
        /// <param name="settings">The settings used to configure the router.</param>
        /// <exception cref="ActorInitializationException">
        /// This exception is thrown when this actor is configured as something other than a <see cref="Akka.Routing.Pool"/> router.
        /// </exception>
        public ClusterRouterPoolActor(SupervisorStrategy supervisorStrategy, ClusterRouterPoolSettings settings) : base(settings)
        {
            _supervisorStrategy = supervisorStrategy;
            Settings = settings;

            Pool = Cell.RouterConfig as Pool
                ?? throw new ActorInitializationException(
                    $"RouterPoolActor can only be used with Pool, not {Cell.RouterConfig.GetType()}");
        }

        /// <summary>
        /// Retrieve the strategy used when supervising the pool.
        /// </summary>
        /// <returns>The strategy used when supervising the pool</returns>
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return _supervisorStrategy;
        }

        /// <summary>
        /// The cluster-router pool settings used by this pool router actor.
        /// </summary>
        public new ClusterRouterPoolSettings Settings { get; private set; }

        /// <summary>
        /// Creates routees on eligible nodes while the total and per-node limits allow placement.
        /// </summary>
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

        /// <summary>
        /// Selects an eligible node with the fewest current routees when the total and per-node limits permit another routee.
        /// </summary>
        /// <returns>The selected node address, or <c>null</c> when no eligible node can accept another routee.</returns>
        public Address SelectDeploymentTarget()
        {
            var currentRoutees = Cell.Router.Routees.ToList();
            var currentNodes = AvailableNodes;
            if (currentNodes.IsEmpty || currentRoutees.Count >= Settings.TotalInstances)
                return null;

            //find the node with the least routees
            var numberOfRouteesPerNode = currentNodes.ToDictionary(x => x,
                routee => currentRoutees.Count(y => routee == FullAddress(y)));

            var target = numberOfRouteesPerNode.Aggregate(
                        (curMin, x) =>
                            (x.Value < curMin.Value)
                                ? x
                                : curMin);

            return target.Value < Settings.MaxInstancesPerNode ? target.Key : null;
        }

        /// <summary>
        /// Applies pool-size adjustment messages and delegates other messages to the base router actor.
        /// </summary>
        /// <param name="message">A pool-size adjustment or another router message.</param>
        protected override void OnReceive(object message)
        {
            switch(message)
            {
                // Moved from RouterPoolActor
                case AdjustPoolSize poolSize when poolSize.Change > 0:
                    var newRoutees = Vector.Fill<Routee>(poolSize.Change)(() => Pool.NewRoutee(Cell.RouteeProps, Context));
                    Cell.AddRoutees(newRoutees);
                    break;
                case AdjustPoolSize poolSize when poolSize.Change < 0:
                    var currentRoutees = Cell.Router.Routees.ToArray();

                    var abandon = currentRoutees
                        .Skip(currentRoutees.Length + poolSize.Change)
                        .ToList();

                    Cell.RemoveRoutees(abandon, true);
                    break;
                case AdjustPoolSize poolSize:
                    //ignore
                    break;
                default:
                    base.OnReceive(message);
                    break;
            }
        }
    }
}
