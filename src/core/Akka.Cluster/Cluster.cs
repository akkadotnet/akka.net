using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Actor.Internals;
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
    /// This module is responsible cluster membership information. Changes to the cluster
    /// information is retrieved through [[#subscribe]]. Commands to operate the cluster is
    /// available through methods in this class, such as [[#join]], [[#down]] and [[#leave]].
    /// 
    /// Each cluster [[Member]] is identified by its [[akka.actor.Address]], and
    /// the cluster address of this actor system is [[#selfAddress]]. A member also has a status;
    /// initially [[MemberStatus.Joining]] followed by [[MemberStatus.Up]].
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
            _settings = new ClusterSettings(system.Settings.Config, system.Name);    

            //TODO: Akka exception?
            var provider = system.Provider as ClusterActorRefProvider;
            if(provider == null)
                throw new ConfigurationException(
                    String.Format("ActorSystem {0} needs to have a 'ClusterActorRefProvider' enabled in the configuration, currently uses {1}", 
                        system, 
                        system.Provider.GetType().FullName));
            _selfUniqueAddress = new UniqueAddress(provider.Transport.DefaultAddress, AddressUidExtension.Uid(system));

            _log = Logging.GetLogger(system, "Cluster");

            LogInfo("Starting up...");

            _failureDetector = new DefaultFailureDetectorRegistry<Address>(() =>
            {
                return FailureDetectorLoader.Load(_settings.FailureDetectorImplementationClass, _settings.FailureDetectorConfig,
                    system);
            });

            _scheduler = CreateScheduler(system);

            //TODO: Not passing settings here;
            _clusterDaemons = system.ActorOf(Props.Create(typeof (ClusterDaemon)).WithDeploy(Deploy.Local), "cluster");

            //TODO: 
            throw new NotImplementedException();            
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

        readonly AtomicBoolean _isTerminated = new AtomicBoolean(false);

        readonly LoggingAdapter _log;
        //TODO: Jmx

        readonly DefaultFailureDetectorRegistry<Address> _failureDetector;
        public DefaultFailureDetectorRegistry<Address> FailureDetector { get { return _failureDetector; } }

        // ========================================================
        // ===================== WORK DAEMONS =====================
        // ========================================================

        readonly Scheduler _scheduler;
        internal Scheduler Scheduler { get { return _scheduler; }}

        private static Scheduler CreateScheduler(ActorSystem system)
        {
            //TODO: Whole load of stuff missing here!
            return system.Scheduler;
        }

        public void Shutdown()
        {
            throw new NotImplementedException();
        }

        private ActorRef _clusterDaemons;

        private void LogInfo(string message)
        {
            _log.Info("Cluster Node [{}] - {}", SelfAddress, message);
        }

        private void LogInfo(string template, object arg1)
        {
            _log.Info(String.Format("Cluster Node [{0}] - " + template, SelfAddress, arg1));
        }

        private void LogInfo(string template, object arg1, object arg2)
        {
            _log.Info(String.Format("Cluster Node [{0}] - " + template, SelfAddress, arg1, arg2));
        }
    }
}
