//-----------------------------------------------------------------------
// <copyright file="ClusterClientReceptionist.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Cluster.Tools.Client
{

    public class ClusterClientReceptionistExtensionProvider : ExtensionIdProvider<ClusterClientReceptionist>
    {
        public override ClusterClientReceptionist CreateExtension(ExtendedActorSystem system)
        {
            return new ClusterClientReceptionist(system);
        }
    }

    /// <summary>
    /// Extension that starts <see cref="ClusterReceptionist"/> and accompanying <see cref="DistributedPubSubMediator"/>
    /// with settings defined in config section "akka.cluster.client.receptionist".
    /// The <see cref="DistributedPubSubMediator"/> is started by the <see cref="DistributedPubSub"/> extension.
    /// </summary>
    public class ClusterClientReceptionist : IExtension
    {
        private readonly ExtendedActorSystem _system;
        private readonly string _role;

        private readonly IActorRef _receptionist;
        private readonly Cluster _cluster;

        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<ClusterClient>("Akka.Cluster.Tools.Client.reference.conf");
        }

        public static ClusterClientReceptionist Get(ActorSystem system)
        {
            return system.WithExtension<ClusterClientReceptionist, ClusterClientReceptionistExtensionProvider>();
        }

        public ClusterClientReceptionist(ExtendedActorSystem system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(DefaultConfig());
            _cluster = Cluster.Get(_system);
            var config = system.Settings.Config.GetConfig("akka.cluster.client.receptionist");
            _role = config.GetString("role");
            _receptionist = CreateReceptionist(config);
        }

        /// <summary>
        /// Returns true if this member is not tagged with the role configured for the receptionist.
        /// </summary>
        public bool IsTerminated
        {
            get
            {
                return _cluster.IsTerminated || !(string.IsNullOrEmpty(_role) || _cluster.SelfRoles.Contains(_role));
            }
        }

        /// <summary>
        /// Register the actors that should be reachable for the clients in this <see cref="DistributedPubSubMediator"/>.
        /// </summary>
        public IActorRef PubSubMediator
        {
            get { return DistributedPubSub.Get(_system).Mediator; }
        }

        /// <summary>
        /// Register an actor that should be reachable for the clients. The clients can send messages to this actor with
        /// <see cref="Send"/> or <see cref="SendToAll"/> using the path elements 
        /// of the <see cref="IActorRef"/>, e.g. "/user/myservice".
        /// </summary>
        public void RegisterService(IActorRef actorRef)
        {
            PubSubMediator.Tell(new Put(actorRef));
        }

        /// <summary>
        /// A registered actor will be automatically unregistered when terminated, 
        /// but it can also be explicitly unregistered before termination.
        /// </summary>
        public void UnregisterService(IActorRef actorRef)
        {
            PubSubMediator.Tell(new Remove(actorRef.Path.ToStringWithoutAddress()));
        }

        /// <summary>
        /// Register an actor that should be reachable for the clients to a named topic.
        /// Several actors can be registered to the same topic name, and all will receive
        /// published messages.
        /// The client can publish messages to this topic with <see cref="Publish"/>.
        /// </summary>
        public void RegisterSubscriber(string topic, IActorRef actorRef)
        {
            PubSubMediator.Tell(new Subscribe(topic, actorRef));
        }

        /// <summary>
        /// A registered subscriber will be automatically unregistered when terminated, 
        /// but it can also be explicitly unregistered before termination.
        /// </summary>
        public void UnregisterSubscriber(string topic, IActorRef actorRef)
        {
            PubSubMediator.Tell(new Unsubscribe(topic, actorRef));
        }

        private IActorRef CreateReceptionist(Config config)
        {
            if (IsTerminated) return _system.DeadLetters;
            else
            {
                var name = config.GetString("name");
                var dispatcher = config.GetString("use-dispatcher");
                if (string.IsNullOrEmpty(dispatcher)) dispatcher = Dispatchers.DefaultDispatcherId;

                // important to use val mediator here to activate it outside of ClusterReceptionist constructor
                var mediator = PubSubMediator;
                return _system.SystemActorOf(ClusterReceptionist.Props(mediator, ClusterReceptionistSettings.Create(config)).WithDispatcher(dispatcher), name);
            }
        }
    }
}