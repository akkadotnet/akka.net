//-----------------------------------------------------------------------
// <copyright file="ClusterClientReceptionist.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Cluster.Tools.Client
{
    /// <summary>
    /// Extension that starts <see cref="ClusterReceptionist"/> and accompanying <see cref="DistributedPubSubMediator"/>
    /// with settings defined in config section "akka.cluster.client.receptionist".
    /// The <see cref="DistributedPubSubMediator"/> is started by the <see cref="DistributedPubSub"/> extension.
    /// </summary>
    public sealed class ClusterClientReceptionist : IExtension
    {
        /// <summary>
        /// Loads the default cluster client receptionist configuration.
        /// </summary>
        /// <returns>Default HOCON configuration from the Cluster Tools client reference resource.</returns>
        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<ClusterClient>("Akka.Cluster.Tools.Client.reference.conf");
        }

        /// <summary>
        /// Gets the cluster client receptionist extension for an actor system.
        /// </summary>
        /// <param name="system">Actor system that owns the receptionist extension.</param>
        /// <returns>The receptionist extension for the actor system.</returns>
        public static ClusterClientReceptionist Get(ActorSystem system)
        {
            return system.WithExtension<ClusterClientReceptionist, ClusterClientReceptionistExtensionProvider>();
        }
        
        private readonly ExtendedActorSystem _system;
        private readonly string _role;
        private readonly Config _config;
        private readonly IActorRef _receptionist;

        /// <summary>
        /// Initializes the extension and starts its receptionist actor when this member is eligible under the configured role.
        /// </summary>
        /// <param name="system">Actor system where the receptionist is configured.</param>
        public ClusterClientReceptionist(ExtendedActorSystem system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(DefaultConfig());
            _config = system.Settings.Config.GetConfig("akka.cluster.client.receptionist");

            _role = _config.GetString("role", null);
            if (string.IsNullOrEmpty(_role)) _role = null;

            _receptionist = CreateReceptionist();
        }

        /// <summary>
        /// Returns true if this member is not tagged with the role configured for the receptionist.
        /// </summary>
        public bool IsTerminated
        {
            get
            {
                return Cluster.Get(_system).IsTerminated || !(string.IsNullOrEmpty(_role) || Cluster.Get(_system).SelfRoles.Contains(_role));
            }
        }

        /// <summary>
        /// Register the actors that should be reachable for the clients in this <see cref="DistributedPubSubMediator"/>.
        /// </summary>
        internal IActorRef PubSubMediator
        {
            get { return DistributedPubSub.Get(_system).Mediator; }
        }

        /// <summary>
        /// Register an actor that should be reachable for the clients. The clients can send messages to this actor with
        /// <see cref="Send"/> or <see cref="SendToAll"/> using the path elements 
        /// of the <see cref="IActorRef"/>, e.g. "/user/myservice".
        /// </summary>
        /// <param name="actorRef">Actor to register as a service reachable by cluster clients.</param>
        public void RegisterService(IActorRef actorRef)
        {
            PubSubMediator.Tell(new PublishSubscribe.Put(actorRef));
        }

        /// <summary>
        /// A registered actor will be automatically unregistered when terminated, 
        /// but it can also be explicitly unregistered before termination.
        /// </summary>
        /// <param name="actorRef">Previously registered service actor to remove.</param>
        public void UnregisterService(IActorRef actorRef)
        {
            PubSubMediator.Tell(new PublishSubscribe.Remove(actorRef.Path.ToStringWithoutAddress()));
        }

        /// <summary>
        /// Register an actor that should be reachable for the clients to a named topic.
        /// Several actors can be registered to the same topic name, and all will receive
        /// published messages.
        /// The client can publish messages to this topic with <see cref="Publish"/>.
        /// </summary>
        /// <param name="topic">Topic to which the actor subscribes.</param>
        /// <param name="actorRef">Actor that will receive messages published to the topic.</param>
        public void RegisterSubscriber(string topic, IActorRef actorRef)
        {
            PubSubMediator.Tell(new PublishSubscribe.Subscribe(topic, actorRef));
        }

        /// <summary>
        /// A registered subscriber will be automatically unregistered when terminated, 
        /// but it can also be explicitly unregistered before termination.
        /// </summary>
        /// <param name="topic">Topic from which the actor should be unsubscribed.</param>
        /// <param name="actorRef">Previously registered subscriber actor to remove.</param>
        public void UnregisterSubscriber(string topic, IActorRef actorRef)
        {
            PubSubMediator.Tell(new PublishSubscribe.Unsubscribe(topic, actorRef));
        }

        private IActorRef CreateReceptionist()
        {
            if (IsTerminated)
            {
                return _system.DeadLetters;
            }
            else
            {
                var name = _config.GetString("name");
                var dispatcher = _config.GetString("use-dispatcher", null);
                if (string.IsNullOrEmpty(dispatcher)) dispatcher = Dispatchers.InternalDispatcherId;

                // important to use var mediator here to activate it outside of ClusterReceptionist constructor
                var mediator = PubSubMediator;

                return _system.SystemActorOf(ClusterReceptionist.Props(
                    mediator,
                    ClusterReceptionistSettings.Create(_config))
                        .WithDispatcher(dispatcher), name);
            }
        }

        /// <summary>
        /// Returns the underlying receptionist actor, particularly so that its
        /// events can be observed via subscribe/unsubscribe.
        /// </summary>
        public IActorRef Underlying => _receptionist;
    }

    /// <summary>
    /// Extension provider that creates a cluster client receptionist for an actor system.
    /// </summary>
    public sealed class ClusterClientReceptionistExtensionProvider : ExtensionIdProvider<ClusterClientReceptionist>
    {
        /// <summary>
        /// Creates the receptionist extension for the supplied actor system.
        /// </summary>
        /// <param name="system">Actor system that owns the extension.</param>
        /// <returns>The initialized receptionist extension.</returns>
        public override ClusterClientReceptionist CreateExtension(ExtendedActorSystem system)
        {
            return new ClusterClientReceptionist(system);
        }
    }
}
