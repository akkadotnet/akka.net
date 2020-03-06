//-----------------------------------------------------------------------
// <copyright file="Cluster.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Event;
using Akka.Remote;
using Akka.Util;
using Akka.Util.Internal;
using Akka.Configuration;

namespace Akka.Cluster
{
    /// <summary>
    /// This class represents an <see cref="ActorSystem"/> provider used to create the cluster extension.
    /// </summary>
    public class ClusterExtension : ExtensionIdProvider<Cluster>
    {
        /// <summary>
        /// Creates the cluster extension using a given actor system.
        /// </summary>
        /// <param name="system">The actor system to use when creating the extension.</param>
        /// <returns>The extension created using the given actor system.</returns>
        public override Cluster CreateExtension(ExtendedActorSystem system)
        {
            return new Cluster((ActorSystemImpl)system);
        }
    }

    /// <summary>
    /// <para>
    /// This class represents an <see cref="ActorSystem"/> extension used to create, monitor and manage
    /// a cluster of member nodes hosted within the actor system.
    /// </para>
    /// <para>
    /// Each cluster <see cref="Akka.Cluster.Member"/> is identified by its <see cref="Akka.Actor.Address"/>
    /// and the cluster address of this actor system is <see cref="SelfAddress"/>. A member also has a
    /// <see cref="Akka.Cluster.MemberStatus">status</see>; initially <see cref="Akka.Cluster.MemberStatus.Joining"/>
    /// followed by <see cref="Akka.Cluster.MemberStatus.Up"/>.
    /// </para>
    /// </summary>
    public class Cluster : IExtension
    {
        /// <summary>
        /// Retrieves the extension from the specified actor system.
        /// </summary>
        /// <param name="system">The actor system from which to retrieve the extension.</param>
        /// <returns>The extension retrieved from the given actor system.</returns>
        public static Cluster Get(ActorSystem system)
        {
            return system.WithExtension<Cluster, ClusterExtension>();
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal static bool IsAssertInvariantsEnabled
        {
            //TODO: Consequences of this?
            get { return false; }
        }

        /// <summary>
        /// The settings for the cluster.
        /// </summary>
        public ClusterSettings Settings { get; }

        /// <summary>
        /// The current unique address for the cluster, which includes the UID.
        /// </summary>
        public UniqueAddress SelfUniqueAddress { get; }

        /// <summary>
        /// Used to retain the <see cref="InfoLogger"/> instance that decorates the cluster.
        /// </summary>
        internal InfoLogger CurrentInfoLogger { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cluster"/> class.
        /// </summary>
        /// <param name="system">The actor system that hosts the cluster.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown if the <paramref name="system"/> does not have a <see cref="ClusterActorRefProvider"/> enabled in the configuration.
        /// </exception>
        public Cluster(ActorSystemImpl system)
        {
            System = system;
            Settings = new ClusterSettings(system.Settings.Config, system.Name);

            if (!(system.Provider is IClusterActorRefProvider provider))
                throw new ConfigurationException(
                    $"ActorSystem {system} needs to have a 'IClusterActorRefProvider' enabled in the configuration, currently uses {system.Provider.GetType().FullName}");
            SelfUniqueAddress = new UniqueAddress(provider.Transport.DefaultAddress, AddressUidExtension.Uid(system));

            _log = Logging.GetLogger(system, "Cluster");

            CurrentInfoLogger = new InfoLogger(_log, Settings, SelfAddress);

            LogInfo("Starting up...");

            FailureDetector = new DefaultFailureDetectorRegistry<Address>(() => FailureDetectorLoader.Load(Settings.FailureDetectorImplementationClass, Settings.FailureDetectorConfig,
                system));

            Scheduler = CreateScheduler(system);

            // it has to be lazy - otherwise if downing provider will init a cluster itself, it will deadlock
            _downingProvider = new Lazy<IDowningProvider>(() => Akka.Cluster.DowningProvider.Load(Settings.DowningProviderType, system), LazyThreadSafetyMode.ExecutionAndPublication);

            //create supervisor for daemons under path "/system/cluster"
            _clusterDaemons = system.SystemActorOf(Props.Create(() => new ClusterDaemon(Settings)).WithDeploy(Deploy.Local), "cluster");

            _readView = new ClusterReadView(this);

            // force the underlying system to start
            _clusterCore = GetClusterCoreRef().Result;

            system.RegisterOnTermination(Shutdown);

            LogInfo("Started up successfully");
        }

        private async Task<IActorRef> GetClusterCoreRef()
        {
            var timeout = System.Settings.CreationTimeout;
            try
            {
                return await _clusterDaemons.Ask<IActorRef>(InternalClusterAction.GetClusterCoreRef.Instance, timeout).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to startup Cluster. You can try to increase 'akka.actor.creation-timeout'.");
                Shutdown();
                System.DeadLetters.Tell(ex); //don't re-throw the error. Just log it.
                return System.DeadLetters;
            }
        }

        /// <summary>
        /// Subscribe to one or more cluster domain events.
        /// </summary>
        /// <param name="subscriber">The actor who'll receive the cluster domain events</param>
        /// <param name="to"><see cref="ClusterEvent.IClusterDomainEvent"/> subclasses</param>
        /// <remarks>A snapshot of <see cref="ClusterEvent.CurrentClusterState"/> will be sent to <paramref name="subscriber"/> as the first message</remarks>
        public void Subscribe(IActorRef subscriber, params Type[] to)
        {
            Subscribe(subscriber, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, to);
        }

        /// <summary>
        /// Subscribe to one or more cluster domain events.
        /// </summary>
        /// <param name="subscriber">The actor who'll receive the cluster domain events</param>
        /// <param name="initialStateMode">
        /// If set to <see cref="ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents"/>, then the events corresponding to the current state
        /// are sent to <paramref name="subscriber"/> to mimic what it would have seen if it were listening to the events when they occurred in the past.
        /// 
        /// If set to <see cref="ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot"/>, then a snapshot of
        /// <see cref="ClusterEvent.CurrentClusterState"/> will be sent to <paramref name="subscriber"/> as the first message.
        /// </param>
        /// <param name="to">An array of event types that the actor receives.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the array of supplied types, <paramref name="to"/>, is empty
        /// or contains types that do not implement <see cref="ClusterEvent.IClusterDomainEvent"/>.
        /// </exception>
        public void Subscribe(IActorRef subscriber, ClusterEvent.SubscriptionInitialStateMode initialStateMode, params Type[] to)
        {
            if (to.Length == 0)
                throw new ArgumentException("At least one `IClusterDomainEvent` class is required", nameof(to));
            if (!to.All(t => typeof(ClusterEvent.IClusterDomainEvent).IsAssignableFrom(t)))
                throw new ArgumentException($"Subscribe to `IClusterDomainEvent` or subclasses, was [{string.Join(", ", to.Select(c => c.Name))}]", nameof(to));

            ClusterCore.Tell(new InternalClusterAction.Subscribe(subscriber, initialStateMode, ImmutableHashSet.Create(to)));
        }

        /// <summary>
        /// Stops the specific actor from receiving all types of cluster domain events.
        /// </summary>
        /// <param name="subscriber">The actor that no longer receives cluster domain events.</param>
        public void Unsubscribe(IActorRef subscriber)
        {
            Unsubscribe(subscriber, null);
        }

        /// <summary>
        /// Stops the specific actor from receiving a specific type of cluster domain event.
        /// </summary>
        /// <param name="subscriber">The actor that no longer receives cluster domain events.</param>
        /// <param name="to">The event type that the actor no longer receives.</param>
        public void Unsubscribe(IActorRef subscriber, Type to)
        {
            ClusterCore.Tell(new InternalClusterAction.Unsubscribe(subscriber, to));
        }

        /// <summary>
        /// Sends the current (full) state of the cluster to the specified actor.
        /// If you want this to happen periodically, you can use the <see cref="Scheduler"/> to schedule
        /// a call to this method. You can also call <see cref="State"/> directly for this information.
        /// </summary>
        /// <param name="receiver">The actor that receives the current cluster state.</param>
        public void SendCurrentClusterState(IActorRef receiver)
        {
            ClusterCore.Tell(new InternalClusterAction.SendCurrentClusterState(receiver));
        }

        /// <summary>
        /// Try to join this cluster node specified by <paramref name="address"/>.
        /// A <see cref="Join"/> command is sent to the node to join.
        /// 
        /// An actor system can only join a cluster once. Additional attempts will be ignored.
        /// When it has successfully joined it must be restarted to be able to join another
        /// cluster or to join the same cluster again.
        /// </summary>
        /// <param name="address">The address of the node we want to join.</param>
        public void Join(Address address)
        {
            ClusterCore.Tell(new ClusterUserAction.JoinTo(FillLocal(address)));
        }

        /// <summary>
        /// Try to asynchronously join this cluster node specified by <paramref name="address"/>.
        /// A <see cref="Join"/> command is sent to the node to join. Returned task will be completed
        /// once current cluster node will be moved into <see cref="MemberStatus.Up"/> state,
        /// or cancelled when provided <paramref name="token"/> cancellation triggers. Cancelling this 
        /// token doesn't prevent current node from joining the cluster, therefore a manuall 
        /// call to <see cref="Leave"/>/<see cref="LeaveAsync()"/> may still be required in order to
        /// leave the cluster gracefully.
        /// 
        /// An actor system can only join a cluster once. Additional attempts will be ignored.
        /// When it has successfully joined it must be restarted to be able to join another
        /// cluster or to join the same cluster again.
        /// 
        /// Once cluster has been shutdown, <see cref="JoinAsync"/> will always fail until an entire 
        /// actor system is manually restarted.
        /// </summary>
        /// <param name="address">The address of the node we want to join.</param>
        /// <param name="token">An optional cancellation token used to cancel returned task before it completes.</param>
        /// <returns>Task which completes, once current cluster node reaches <see cref="MemberStatus.Up"/> state.</returns>
        public Task JoinAsync(Address address, CancellationToken token = default(CancellationToken))
        {
            var completion = new TaskCompletionSource<NotUsed>();
            this.RegisterOnMemberUp(() => completion.TrySetResult(NotUsed.Instance));
            this.RegisterOnMemberRemoved(() => completion.TrySetException(
                new ClusterJoinFailedException($"Node has not managed to join the cluster using provided address: {address}")));

            Join(address);

            return completion.Task.WithCancellation(token);
        }

        private Address FillLocal(Address address)
        {
            // local address might be used if grabbed from IActorRef.Path.Address
            if (address.HasLocalScope && address.System == SelfAddress.System)
            {
                return SelfAddress;
            }
            else
            {
                return address;
            }
        }

        /// <summary>
        /// Joins the specified seed nodes without defining them in config.
        /// Especially useful from tests when Addresses are unknown before startup time.
        /// 
        /// An actor system can only join a cluster once. Additional attempts will be ignored.
        /// When it has successfully joined it must be restarted to be able to join another
        /// cluster or to join the same cluster again.
        /// </summary>
        /// <param name="seedNodes">TBD</param>
        public void JoinSeedNodes(IEnumerable<Address> seedNodes)
        {
            ClusterCore.Tell(
                new InternalClusterAction.JoinSeedNodes(seedNodes.Select(FillLocal).ToImmutableList()));
        }

        /// <summary>
        /// Joins the specified seed nodes without defining them in config.
        /// Especially useful from tests when Addresses are unknown before startup time.
        /// Returns a task, which completes once current cluster node has successfully joined the cluster
        /// or which cancels, when a cancellation <paramref name="token"/> has been cancelled. Cancelling this 
        /// token doesn't prevent current node from joining the cluster, therefore a manuall 
        /// call to <see cref="Leave"/>/<see cref="LeaveAsync()"/> may still be required in order to
        /// leave the cluster gracefully.
        /// 
        /// An actor system can only join a cluster once. Additional attempts will be ignored.
        /// When it has successfully joined it must be restarted to be able to join another
        /// cluster or to join the same cluster again.
        /// 
        /// Once cluster has been shutdown, <see cref="JoinSeedNodesAsync"/> will always fail until an entire 
        /// actor system is manually restarted.
        /// </summary>
        /// <param name="seedNodes">TBD</param>
        /// <param name="token">TBD</param>
        public Task JoinSeedNodesAsync(IEnumerable<Address> seedNodes, CancellationToken token = default(CancellationToken))
        {
            var completion = new TaskCompletionSource<NotUsed>();
            this.RegisterOnMemberUp(() => completion.TrySetResult(NotUsed.Instance));
            this.RegisterOnMemberRemoved(() => completion.TrySetException(
                new ClusterJoinFailedException($"Node has not managed to join the cluster using provided seed node addresses: {string.Join(", ", seedNodes)}.")));

            JoinSeedNodes(seedNodes);

            return completion.Task.WithCancellation(token);
        }

        /// <summary>
        /// Sends a command to issue state transition to LEAVING for the node specified by <paramref name="address"/>.
        /// The member will go through the status changes <see cref="MemberStatus.Leaving"/> (not published to 
        /// subscribers) followed by <see cref="MemberStatus.Exiting"/> and finally <see cref="MemberStatus.Removed"/>.
        /// 
        /// Note that this command can be issued to any member in the cluster, not necessarily the
        /// one that is leaving. The cluster extension, but not the actor system, of the leaving member will be shutdown after
        /// the leader has changed status of the member to <see cref="MemberStatus.Exiting"/>. Thereafter the member will be
        /// removed from the cluster. Normally this is handled automatically, but in case of network failures during
        /// this process it might still be necessary to set the node's status to <see cref="MemberStatus.Down"/> in order
        /// to complete the removal.
        /// </summary>
        /// <param name="address">The address of the node leaving the cluster.</param>
        public void Leave(Address address)
        {
            if (FillLocal(address) == SelfAddress)
            {
                LeaveSelf();
            }
            else
                ClusterCore.Tell(new ClusterUserAction.Leave(FillLocal(address)));
        }

        /// <summary>
        /// Causes the CURRENT node, i.e. the one calling this function, to leave the cluster.
        /// 
        /// Once the returned <see cref="Task"/> completes, it means that the member has successfully been removed
        /// from the cluster.
        /// </summary>
        /// <returns>A <see cref="Task"/> that will return upon the current node being removed from the cluster.</returns>
        public Task LeaveAsync()
        {
            return LeaveSelf();
        }

        /// <summary>
        /// Causes the CURRENT node, i.e. the one calling this function, to leave the cluster.
        /// 
        /// Once the returned <see cref="Task"/> completes in completed or cancelled state, it means that the member has successfully been removed
        /// from the cluster or cancellation token cancelled the task.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token to cancel awaiting.</param>
        /// <returns>A <see cref="Task"/> that will return upon the current node being removed from the cluster, or if await was cancelled.</returns>
        /// <remarks>
        /// The cancellation token doesn't cancel leave from the cluster, it only lets to give up on awaiting (by timeout for example).
        /// </remarks>
        public Task LeaveAsync(CancellationToken cancellationToken)
        {
            return LeaveSelf().WithCancellation(cancellationToken);
        }

        private Task _leaveTask;

        private Task LeaveSelf()
        {
            var tcs = new TaskCompletionSource<object>();
            var leaveTask = Interlocked.CompareExchange(ref _leaveTask, tcs.Task, null);

            // It's assumed here that once the member left the cluster, it won't get back again.
            // So, the member removal event being memoized in TaskCompletionSource and never reset.
            if (leaveTask != null)
                return leaveTask;

            // Subscribe to MemberRemoved events
            _clusterDaemons.Tell(new InternalClusterAction.AddOnMemberRemovedListener(() => tcs.TrySetResult(null)));

            // Send leave message
            ClusterCore.Tell(new ClusterUserAction.Leave(SelfAddress));

            return tcs.Task;
        }

        /// <summary>
        /// Sends a command to DOWN the node specified by <paramref name="address"/>.
        /// 
        /// When a member is considered by the failure detector to be unreachable the leader is not
        /// allowed to perform its duties, such as changing status of new joining members to <see cref="MemberStatus.Up"/>.
        /// The status of the unreachable member must be changed to <see cref="MemberStatus.Down"/>, which can be done with
        /// this method.
        /// </summary>
        /// <param name="address">The address of the node we're going to mark as <see cref="MemberStatus.Down"/></param>
        public void Down(Address address)
        {
            ClusterCore.Tell(new ClusterUserAction.Down(FillLocal(address)));
        }

        /// <summary>
        /// Registers the supplied callback to run once when the current cluster member is <see cref="MemberStatus.Up"/>.
        /// Typically used together with configuration option 'akka.cluster.min-nr-of-members' to defer some action,
        /// such as starting actors, until the cluster has reached a certain size.
        /// </summary>
        /// <param name="callback">The callback that is run whenever the current member achieves a status of <see cref="MemberStatus.Up"/></param>
        public void RegisterOnMemberUp(Action callback)
        {
            _clusterDaemons.Tell(new InternalClusterAction.AddOnMemberUpListener(callback));
        }

        /// <summary>
        /// Registers the supplied callback to run once when the current cluster member is <see cref="MemberStatus.Removed"/>.
        /// 
        /// Typically used in combination with <see cref="Leave"/> and <see cref="ActorSystem.Terminate"/>.
        /// </summary>
        /// <param name="callback">The callback that is run whenever the current member achieves a status of <see cref="MemberStatus.Down"/></param>
        public void RegisterOnMemberRemoved(Action callback)
        {
            if (IsTerminated)
                callback();
            else
                _clusterDaemons.Tell(new InternalClusterAction.AddOnMemberRemovedListener(callback));
        }

        /// <summary>
        /// Generates the remote actor path by replacing the <see cref="ActorPath.Address"/> in the RootActorPath for the given
        /// ActorRef with the cluster's <see cref="SelfAddress"/>, unless address' host is already defined
        /// </summary>
        /// <param name="actorRef">An <see cref="IActorRef"/> belonging to the current node.</param>
        /// <returns>The absolute remote <see cref="ActorPath"/> of <paramref name="actorRef"/>.</returns>
        public ActorPath RemotePathOf(IActorRef actorRef)
        {
            var path = actorRef.Path;
            if (!string.IsNullOrEmpty(path.Address.Host))
            {
                return path;
            }
            else
            {
                return (new RootActorPath(path.Root.Address
                    .WithProtocol(SelfAddress.Protocol)
                    .WithSystem(SelfAddress.System)
                    .WithHost(SelfAddress.Host)
                    .WithPort(SelfAddress.Port)) / string.Join("/", path.Elements)).WithUid(path.Uid);
            }
        }

        /// <summary>
        /// The address of this cluster member.
        /// </summary>
        public Address SelfAddress
        {
            get { return SelfUniqueAddress.Address; }
        }

        /// <summary>
        /// The roles that this cluster member is currently a part.
        /// </summary>
        public ImmutableHashSet<string> SelfRoles
        {
            get { return Settings.Roles; }
        }

        /// <summary>
        /// The current snapshot state of the cluster.
        /// </summary>
        public ClusterEvent.CurrentClusterState State { get { return _readView._state; } }

        /// <summary>
        /// Access to the current member info for this node.
        /// </summary>
        public Member SelfMember => _readView.Self;

        private readonly AtomicBoolean _isTerminated = new AtomicBoolean(false);

        /// <summary>
        /// Determine whether or not this cluster instance has been shutdown.
        /// </summary>
        public bool IsTerminated { get { return _isTerminated.Value; } }

        /// <summary>
        /// The underlying <see cref="ActorSystem"/> supported by this plugin.
        /// </summary>
        public ExtendedActorSystem System { get; }

        private readonly Lazy<IDowningProvider> _downingProvider;
        private readonly ILoggingAdapter _log;
        private readonly ClusterReadView _readView;

        /// <summary>
        /// TBD
        /// </summary>
        internal ClusterReadView ReadView { get { return _readView; } }

        /// <summary>
        /// The set of failure detectors used for monitoring one or more nodes in the cluster.
        /// </summary>
        public DefaultFailureDetectorRegistry<Address> FailureDetector { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IDowningProvider DowningProvider => _downingProvider.Value;

        // ========================================================
        // ===================== WORK DAEMONS =====================
        // ========================================================

        /// <summary>
        /// TBD
        /// </summary>
        internal IScheduler Scheduler { get; }

        private static IScheduler CreateScheduler(ActorSystem system)
        {
            //TODO: Whole load of stuff missing here!
            return system.Scheduler;
        }

        /// <summary>
        /// INTERNAL API.
        /// 
        /// Shuts down all connections to other members, the cluster daemon and the periodic gossip and cleanup tasks.
        /// This should not be called directly by the user
        /// 
        /// The user can issue a <see cref="Leave"/> command which will tell the node
        /// to go through graceful handoff process <c>LEAVE -> EXITING ->  REMOVED -> SHUTDOWN</c>.
        /// </summary>
        internal void Shutdown()
        {
            if (_isTerminated.CompareAndSet(false, true))
            {
                LogInfo("Shutting down...");
                System.Stop(_clusterDaemons);

                if (_readView != null)
                {
                    _readView.Dispose();
                }

                LogInfo("Successfully shut down");
            }
        }

        private readonly IActorRef _clusterDaemons;
        private IActorRef _clusterCore;

        /// <summary>
        /// TBD
        /// </summary>
        internal IActorRef ClusterCore
        {
            get
            {
                if (_clusterCore == null)
                {
                    _clusterCore = GetClusterCoreRef().Result;
                }
                return _clusterCore;
            }
        }

        /// <summary>
        /// INTERNAL API.
        ///
        /// Used for logging important messages with the cluster's address appended.
        /// </summary>
        internal class InfoLogger
        {
            private readonly ILoggingAdapter _log;
            private readonly ClusterSettings _settings;
            private readonly Address _selfAddress;

            public InfoLogger(ILoggingAdapter log, ClusterSettings settings, Address selfAddress)
            {
                _log = log;
                _settings = settings;
                _selfAddress = selfAddress;
            }

            /// <summary>
            /// Creates an <see cref="Akka.Event.LogLevel.InfoLevel"/> log entry with the specific message.
            /// </summary>
            /// <param name="message">The message being logged.</param>
            internal void LogInfo(string message)
            {
                if(_settings.LogInfo)
                    _log.Info("Cluster Node [{0}] - {1}", _selfAddress, message);
            }

            /// <summary>
            /// Creates an <see cref="Akka.Event.LogLevel.InfoLevel"/> log entry with the specific template and arguments.
            /// </summary>
            /// <param name="template">The template being rendered and logged.</param>
            /// <param name="arg1">The argument that fills in the template placeholder.</param>
            internal void LogInfo(string template, object arg1)
            {
                if (_settings.LogInfo)
                    _log.Info("Cluster Node [{1}] - " + template, arg1, _selfAddress);
            }

            /// <summary>
            /// Creates an <see cref="Akka.Event.LogLevel.InfoLevel"/> log entry with the specific template and arguments.
            /// </summary>
            /// <param name="template">The template being rendered and logged.</param>
            /// <param name="arg1">The first argument that fills in the corresponding template placeholder.</param>
            /// <param name="arg2">The second argument that fills in the corresponding template placeholder.</param>
            internal void LogInfo(string template, object arg1, object arg2)
            {
                if (_settings.LogInfo)
                    _log.Info("Cluster Node [{2}] - " + template, arg1, arg2, _selfAddress);
            }
        }

        /// <summary>
        /// Creates an <see cref="Akka.Event.LogLevel.InfoLevel"/> log entry with the specific message.
        /// </summary>
        /// <param name="message">The message being logged.</param>
        internal void LogInfo(string message)
        {
            CurrentInfoLogger.LogInfo(message);
        }

        /// <summary>
        /// Creates an <see cref="Akka.Event.LogLevel.InfoLevel"/> log entry with the specific template and arguments.
        /// </summary>
        /// <param name="template">The template being rendered and logged.</param>
        /// <param name="arg1">The argument that fills in the template placeholder.</param>
        internal void LogInfo(string template, object arg1)
        {
            CurrentInfoLogger.LogInfo(template, arg1);
        }

        /// <summary>
        /// Creates an <see cref="Akka.Event.LogLevel.InfoLevel"/> log entry with the specific template and arguments.
        /// </summary>
        /// <param name="template">The template being rendered and logged.</param>
        /// <param name="arg1">The first argument that fills in the corresponding template placeholder.</param>
        /// <param name="arg2">The second argument that fills in the corresponding template placeholder.</param>
        internal void LogInfo(string template, object arg1, object arg2)
        {
            CurrentInfoLogger.LogInfo(template, arg1, arg2);
        }
    }

    /// <summary>
    /// Exception thrown, when <see cref="Cluster.JoinAsync"/> or <see cref="Cluster.JoinSeedNodesAsync"/> fails to succeed.
    /// </summary>
    public class ClusterJoinFailedException : AkkaException
    {
        public ClusterJoinFailedException(string message) : base(message)
        {
        }
    }
}

