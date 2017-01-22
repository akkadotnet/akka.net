﻿//-----------------------------------------------------------------------
// <copyright file="Cluster.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Cluster
{
    /// <summary>
    /// TBD
    /// </summary>
    public class ClusterExtension : ExtensionIdProvider<Cluster>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override Cluster CreateExtension(ExtendedActorSystem system)
        {
            return new Cluster((ActorSystemImpl)system);
        }
    }

    //TODO: xmldoc
    /// <summary>
    /// This module is responsible for cluster membership information. Changes to the cluster
    /// information is retrieved through <see cref="InternalClusterAction.Subscribe"/>. Commands to operate the cluster is
    /// available through methods in this class, such as <see cref="Akka.Cluster.Cluster.Join"/>, <see cref="Akka.Cluster.Cluster.Down"/> and <see cref="Akka.Cluster.Cluster.Leave"/>.
    /// 
    /// Each cluster <see cref="Akka.Cluster.Member"/> is identified by its <see cref="Akka.Actor.Address"/>, and
    /// the cluster address of this actor system is [[#selfAddress]]. A member also has a status;
    /// initially <see cref="Akka.Cluster.MemberStatus.Joining"/> followed by <see cref="Akka.Cluster.MemberStatus.Up"/>.
    /// </summary>
    public class Cluster : IExtension
    {
        //TODO: Issue with missing overrides for Get and Lookup

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        public ClusterSettings Settings { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public UniqueAddress SelfUniqueAddress { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <exception cref="ConfigurationException">TBD</exception>
        public Cluster(ActorSystemImpl system)
        {
            System = system;
            Settings = new ClusterSettings(system.Settings.Config, system.Name);

            var provider = system.Provider as ClusterActorRefProvider;
            if (provider == null)
                throw new ConfigurationException(
                    String.Format("ActorSystem {0} needs to have a 'ClusterActorRefProvider' enabled in the configuration, currently uses {1}",
                        system,
                        system.Provider.GetType().FullName));
            SelfUniqueAddress = new UniqueAddress(provider.Transport.DefaultAddress, AddressUidExtension.Uid(system));

            _log = Logging.GetLogger(system, "Cluster");

            LogInfo("Starting up...");

            _failureDetector = new DefaultFailureDetectorRegistry<Address>(() => FailureDetectorLoader.Load(Settings.FailureDetectorImplementationClass, Settings.FailureDetectorConfig,
                system));

            _scheduler = CreateScheduler(system);

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

        /// <summary>
        /// Handles initialization logic for the <see cref="Cluster"/>
        /// </summary>
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
        /// If set to <see cref="ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents"/> the events corresponding to the current state
        /// will be sent to <paramref name="subscriber"/> to mimic what it would have seen if it were listening to the events when they occurred in the past.
        /// 
        /// If set to <see cref="ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot"/> 
        /// a snapshot of <see cref="ClusterEvent.CurrentClusterState"/> will be sent to <paramref name="subscriber"/> as the first message. </param>
        /// <param name="to"><see cref="ClusterEvent.IClusterDomainEvent"/> subclasses</param>
        public void Subscribe(IActorRef subscriber, ClusterEvent.SubscriptionInitialStateMode initialStateMode, params Type[] to)
        {
            if (to.Length == 0)
                throw new ArgumentException("At least one `IClusterDomainEvent` class is required");
            if (!to.All(t => typeof(ClusterEvent.IClusterDomainEvent).IsAssignableFrom(t)))
                throw new ArgumentException($"Subscribe to `IClusterDomainEvent` or subclasses, was [{string.Join(", ", to.Select(c => c.Name))}]");

            ClusterCore.Tell(new InternalClusterAction.Subscribe(subscriber, initialStateMode, ImmutableHashSet.Create(to)));
        }

        /// <summary>
        /// Unsubscribe to all cluster domain events.
        /// </summary>
        /// <param name="subscriber">TBD</param>
        public void Unsubscribe(IActorRef subscriber)
        {
            Unsubscribe(subscriber, null);
        }

        /// <summary>
        /// Unsubscribe to a specific type of cluster domain event
        /// </summary>
        /// <param name="subscriber">TBD</param>
        /// <param name="to">TBD</param>
        public void Unsubscribe(IActorRef subscriber, Type to)
        {
            ClusterCore.Tell(new InternalClusterAction.Unsubscribe(subscriber, to));
        }

        /// <summary>
        /// Send the current (full) state of the cluster to the specified receiver.
        /// If you want this to happen periodically, you can use the <see cref="Scheduler"/> to schedule
        /// a call to this method. You can also call <see cref="State"/> directly for this information.
        /// </summary>
        /// <param name="receiver">TBD</param>
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
        /// <param name="address">TBD</param>
        public void Join(Address address)
        {
            ClusterCore.Tell(new ClusterUserAction.JoinTo(FillLocal(address)));
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
        /// Join the specified seed nodes without defining them in config.
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
        /// Send command to issue state transition to LEAVING for the node specified by <paramref name="address"/>.
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
        /// <param name="address">TBD</param>
        public void Leave(Address address)
        {
            ClusterCore.Tell(new ClusterUserAction.Leave(FillLocal(address)));
        }

        /// <summary>
        /// Causes the CURRENT node, i.e. the one calling this function, to leave the cluster.
        /// 
        /// Once the returned <see cref="Task"/> completes, it means that the member has successfully been removed
        /// from the cluster.
        /// </summary>
        /// <returns>A <see cref="Task"/> that will return true upon the current node being removed from the cluster.</returns>
        public Task LeaveAsync()
        {
            var tcs = _leaveTask.Value;

            // short-circuit - check to see if we've already successfully left.
            if (tcs.Task.IsCompleted)
                return tcs.Task;

            // Register it such that our TCS is automatically completed when we're removed
            _clusterDaemons.Tell(new InternalClusterAction.AddOnMemberRemovedListener(() => tcs.TrySetResult(true)));

            // Issue the leave command
            Leave(SelfAddress);

            return tcs.Task;
        }

        /// <summary>
        /// Send command to DOWN the node specified by <paramref name="address"/>.
        /// 
        /// When a member is considered by the failure detector to be unreachable the leader is not
        /// allowed to perform its duties, such as changing status of new joining members to <see cref="MemberStatus.Up"/>.
        /// The status of the unreachable member must be changed to <see cref="MemberStatus.Down"/>, which can be done with
        /// this method.
        /// </summary>
        /// <param name="address">TBD</param>
        public void Down(Address address)
        {
            ClusterCore.Tell(new ClusterUserAction.Down(FillLocal(address)));
        }

        /// <summary>
        /// The supplied callback will be run once when the current cluster member is <see cref="MemberStatus.Up"/>.
        /// Typically used together with configuration option 'akka.cluster.min-nr-of-members' to defer some action,
        /// such as starting actors, until the cluster has reached a certain size.
        /// </summary>
        /// <param name="callback">The callback that will be run whenever the current member achieves a status of <see cref="MemberStatus.Up"/></param>
        public void RegisterOnMemberUp(Action callback)
        {
            _clusterDaemons.Tell(new InternalClusterAction.AddOnMemberUpListener(callback));
        }

        /// <summary>
        /// The supplied callback will be run once when the current cluster member is <see cref="MemberStatus.Removed"/>.
        /// 
        /// Typically used in combination with <see cref="Leave"/> and <see cref="ActorSystem.Terminate"/>.
        /// </summary>
        /// <param name="callback">The callback that will be run whenever the current member achieves a status of <see cref="MemberStatus.Down"/></param>
        public void RegisterOnMemberRemoved(Action callback)
        {
            if (IsTerminated)
                callback();
            else
                _clusterDaemons.Tell(new InternalClusterAction.AddOnMemberRemovedListener(callback));
        }

        /// <summary>
        /// Generate the remote actor path by replacing the Address in the RootActor Path for the given
        /// ActorRef with the cluster's `SelfAddress`, unless address' host is already defined
        /// </summary>
        /// <param name="actorRef">TBD</param>
        /// <returns>TBD</returns>
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
        /// roles that this member has
        /// </summary>
        public ImmutableHashSet<string> SelfRoles
        {
            get { return Settings.Roles; }
        }

        /// <summary>
        /// Current snapshot state of the cluster.
        /// </summary>
        public ClusterEvent.CurrentClusterState State { get { return _readView._state; } }

        private readonly AtomicBoolean _isTerminated = new AtomicBoolean(false);

        /// <summary>
        /// Returns true if this cluster instance has be shutdown.
        /// </summary>
        public bool IsTerminated { get { return _isTerminated.Value; } }

        /// <summary>
        /// TBD
        /// </summary>
        public ExtendedActorSystem System { get; }

        private Lazy<IDowningProvider> _downingProvider;
        private readonly ILoggingAdapter _log;
        private readonly ClusterReadView _readView;
        /// <summary>
        /// TBD
        /// </summary>
        internal ClusterReadView ReadView { get { return _readView; } }

        private readonly DefaultFailureDetectorRegistry<Address> _failureDetector;
        /// <summary>
        /// TBD
        /// </summary>
        public DefaultFailureDetectorRegistry<Address> FailureDetector { get { return _failureDetector; } }

        private Lazy<TaskCompletionSource<bool>> _leaveTask = new Lazy<TaskCompletionSource<bool>>(() => new TaskCompletionSource<bool>(), LazyThreadSafetyMode.ExecutionAndPublication);
        /// <summary>
        /// TBD
        /// </summary>
        public IDowningProvider DowningProvider => _downingProvider.Value;

        // ========================================================
        // ===================== WORK DAEMONS =====================
        // ========================================================

        readonly IScheduler _scheduler;
        /// <summary>
        /// TBD
        /// </summary>
        internal IScheduler Scheduler { get { return _scheduler; } }

        private static IScheduler CreateScheduler(ActorSystem system)
        {
            //TODO: Whole load of stuff missing here!
            return system.Scheduler;
        }

        /// <summary>
        /// INTERNAL API.
        /// 
        /// Shuts down all connections to other members, the cluster daemon and the periodic gossip and cleanup tasks.
        /// Should not called by the user. 
        /// 
        /// The user can issue a <see cref="Leave"/> command which will tell the node
        /// to go through graceful handoff process `LEAVE -> EXITING ->  REMOVED -> SHUTDOWN`.
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
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        internal void LogInfo(string message)
        {
            _log.Info("Cluster Node [{0}] - {1}", SelfAddress, message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="template">TBD</param>
        /// <param name="arg1">TBD</param>
        internal void LogInfo(string template, object arg1)
        {
            _log.Info("Cluster Node [{0}] - " + template, SelfAddress, arg1);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="template">TBD</param>
        /// <param name="arg1">TBD</param>
        /// <param name="arg2">TBD</param>
        internal void LogInfo(string template, object arg1, object arg2)
        {
            _log.Info("Cluster Node [{0}] - " + template, SelfAddress, arg1, arg2);
        }
    }
}

