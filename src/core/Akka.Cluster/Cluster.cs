//-----------------------------------------------------------------------
// <copyright file="Cluster.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote;
using Akka.Util;

namespace Akka.Cluster
{
    public class ClusterExtension : ExtensionIdProvider<Cluster>
    {
        public override Cluster CreateExtension(ExtendedActorSystem system)
        {
            return new Cluster((ActorSystemImpl)system);
        }
    }

    //TODO: xmldoc
    /// <summary>
    /// This module is responsible for cluster membership information. Changes to the cluster
    /// information is retrieved through <see cref="Akka.Cluster.Cluster.Subscribe"/>. Commands to operate the cluster is
    /// available through methods in this class, such as <see cref="Akka.Cluster.Cluster.Join"/>, <see cref="Akka.Cluster.Cluster.Down"/> and <see cref="Akka.Cluster.Cluster.Leave"/>.
    /// 
    /// Each cluster <see cref="Akka.Cluster.Member"/> is identified by its <see cref="Akka.Actor.Address"/>, and
    /// the cluster address of this actor system is [[#selfAddress]]. A member also has a status;
    /// initially <see cref="Akka.Cluster.MemberStatus.Joining"/> followed by <see cref="Akka.Cluster.MemberStatus.Up"/>.
    /// </summary>
    public class Cluster :IExtension
    {
        //TODO: Issue with missing overrides for Get and Lookup

        public static Cluster Get(ActorSystem system)
        {
            return system.WithExtension<Cluster, ClusterExtension>();
        }

        public static bool IsAssertInvariantsEnabled
        {
            //TODO: Consequences of this?
            get { return false; }
        }

        readonly ClusterSettings _settings;
        public ClusterSettings Settings { get { return _settings; } }
        readonly UniqueAddress _selfUniqueAddress;
        public UniqueAddress SelfUniqueAddress {get{return _selfUniqueAddress;}}
        
        public Cluster(ActorSystemImpl system)
        {
            System = system;
            _settings = new ClusterSettings(system.Settings.Config, system.Name);    

            var provider = system.Provider as ClusterActorRefProvider;
            if(provider == null)
                throw new ConfigurationException(
                    String.Format("ActorSystem {0} needs to have a 'ClusterActorRefProvider' enabled in the configuration, currently uses {1}", 
                        system, 
                        system.Provider.GetType().FullName));
            _selfUniqueAddress = new UniqueAddress(provider.Transport.DefaultAddress, AddressUidExtension.Uid(system));

            _log = Logging.GetLogger(system, "Cluster");

            LogInfo("Starting up...");

            _failureDetector = new DefaultFailureDetectorRegistry<Address>(() => FailureDetectorLoader.Load(_settings.FailureDetectorImplementationClass, _settings.FailureDetectorConfig,
                system));

            _scheduler = CreateScheduler(system);

            //create supervisor for daemons under path "/system/cluster"
            _clusterDaemons = system.SystemActorOf(Props.Create(() => new ClusterDaemon(_settings)).WithDeploy(Deploy.Local), "cluster");

            //TODO: Pretty sure this is bad and will at least throw aggregateexception possibly worse. 
            _clusterCore = GetClusterCoreRef().Result;

            _readView = new ClusterReadView(this);
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
            //TODO: add system.terminationCallback support
        }

        /// <summary>
        /// Subscribe to one or more cluster domain events.
        /// </summary>
        /// <param name="subscriber">The actor who'll receive the cluster domain events</param>
        /// <param name="to"><see cref="ClusterEvent.IClusterDomainEvent"/> subclasses</param>
        /// <remarks>A snapshot of <see cref="ClusterEvent.CurrentClusterState"/> will be sent to <paramref name="subscriber"/> as the first message</remarks>
        public void Subscribe(IActorRef subscriber, Type[] to)
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
        public void Subscribe(IActorRef subscriber, ClusterEvent.SubscriptionInitialStateMode initialStateMode, Type[] to)
        {
            var val = _clusterCore;
            _clusterCore.Tell(new InternalClusterAction.Subscribe(subscriber, initialStateMode, ImmutableHashSet.Create<Type>(to)));
        }

        /// <summary>
        /// Unsubscribe to all cluster domain events.
        /// </summary>
        public void Unsubscribe(IActorRef subscriber)
        {
            Unsubscribe(subscriber,null);
        }

        /// <summary>
        /// Unsubscribe to a specific type of cluster domain event
        /// </summary>
        public void Unsubscribe(IActorRef subscriber, Type to)
        {
            _clusterCore.Tell(new InternalClusterAction.Unsubscribe(subscriber, to));
        }

        /// <summary>
        /// Send the current (full) state of the cluster to the specified receiver.
        /// If you want this to happen periodically, you can use the <see cref="Scheduler"/> to schedule
        /// a call to this method. You can also call <see cref="State"/> directly for this information.
        /// </summary>
        public void SendCurrentClusterState(IActorRef receiver)
        {
            _clusterCore.Tell(new InternalClusterAction.SendCurrentClusterState(receiver));
        }

        /// <summary>
        /// Try to join this cluster node specified by <paramref name="address"/>.
        /// A <see cref="Join"/> command is sent to the node to join.
        /// 
        /// An actor system can only join a cluster once. Additional attempts will be ignored.
        /// When it has successfully joined it must be restarted to be able to join another
        /// cluster or to join the same cluster again.
        /// </summary>
        public void Join(Address address)
        {
            _clusterCore.Tell(new ClusterUserAction.JoinTo(address));
        }

        /// <summary>
        /// Join the specified seed nodes without defining them in config.
        /// Especially useful from tests when Addresses are unknown before startup time.
        /// 
        /// An actor system can only join a cluster once. Additional attempts will be ignored.
        /// When it has successfully joined it must be restarted to be able to join another
        /// cluster or to join the same cluster again.
        /// </summary>
        public void JoinSeedNodes(ImmutableList<Address> seedNodes)
        {
            _clusterCore.Tell(new InternalClusterAction.JoinSeedNodes(seedNodes));
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
        /// <param name="address"></param>
        public void Leave(Address address)
        {
            _clusterCore.Tell(new ClusterUserAction.Leave(address));
        }

        /// <summary>
        /// Send command to DOWN the node specified by <paramref name="address"/>.
        /// 
        /// When a member is considered by the failure detector to be unreachable the leader is not
        /// allowed to perform its duties, such as changing status of new joining members to <see cref="MemberStatus.Up"/>.
        /// The status of the unreachable member must be changed to <see cref="MemberStatus.Down"/>, which can be done with
        /// this method.
        /// </summary>
        public void Down(Address address)
        {
            _clusterCore.Tell(new ClusterUserAction.Down(address));
        }

        /// <summary>
        /// The supplied callback will be run once when the current cluster member is <see cref="MemberStatus.Up"/>.
        /// Typically used together with configuration option 'akka.cluster.min-nr-of-members' to defer some action,
        /// such as starting actors, until the cluster has reached a certain size.
        /// </summary>
        /// <param name="callback"></param>
        public void RegisterOnMemberUp(Action callback)
        {
            _clusterDaemons.Tell(new InternalClusterAction.AddOnMemberUpListener(callback));
        }

        /// <summary>
        /// The address of this cluster member.
        /// </summary>
        public Address SelfAddress
        {
            get { return _selfUniqueAddress.Address; }
        }

        /// <summary>
        /// roles that this member has
        /// </summary>
        public ImmutableHashSet<string> SelfRoles
        {
            get { return _settings.Roles; }
        }

        internal ClusterEvent.CurrentClusterState State { get { return _readView._state; } }

        readonly AtomicBoolean _isTerminated = new AtomicBoolean(false);

        public bool IsTerminated { get { return _isTerminated.Value; } }

        internal ActorSystemImpl System { get; private set; }

        readonly ILoggingAdapter _log;
        readonly ClusterReadView _readView;
        public ClusterReadView ReadView {get { return _readView; }}

        readonly DefaultFailureDetectorRegistry<Address> _failureDetector;
        public DefaultFailureDetectorRegistry<Address> FailureDetector { get { return _failureDetector; } }

        // ========================================================
        // ===================== WORK DAEMONS =====================
        // ========================================================

        readonly IScheduler _scheduler;
        internal IScheduler Scheduler { get { return _scheduler; } }

        private static IScheduler CreateScheduler(ActorSystem system)
        {
            //TODO: Whole load of stuff missing here!
            return system.Scheduler;
        }

        public void Shutdown()
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

        readonly IActorRef _clusterDaemons;
        IActorRef _clusterCore;
        public IActorRef ClusterCore { get { return _clusterCore; } }

        public void LogInfo(string message)
        {
            _log.Info("Cluster Node [{0}] - {1}", SelfAddress, message);
        }

        public void LogInfo(string template, object arg1)
        {
            _log.Info("Cluster Node [{0}] - " + template, SelfAddress, arg1);
        }

        public void LogInfo(string template, object arg1, object arg2)
        {
            _log.Info("Cluster Node [{0}] - " + template, SelfAddress, arg1, arg2);
        }
    }
}

