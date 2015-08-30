//-----------------------------------------------------------------------
// <copyright file="ClusterClientReceptionist.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Tools.PubSub;
using Akka.Configuration;

namespace Akka.Cluster.Tools.Client
{

    public class ClusterClientReceptionistExtensionProvider : ExtensionIdProvider<ClusterClientReceptionist>
    {
        public override ClusterClientReceptionist CreateExtension(ExtendedActorSystem system)
        {
            return new ClusterClientReceptionist(system);
        }
    }

    /**
     * Extension that starts [[ClusterReceptionist]] and accompanying [[akka.cluster.pubsub.DistributedPubSubMediator]]
     * with settings defined in config section `akka.cluster.client.receptionist`.
     * The [[akka.cluster.pubsub.DistributedPubSubMediator]] is started by the [[akka.cluster.pubsub.DistributedPubSub]] extension.
     */
    public class ClusterClientReceptionist : IExtension
    {
        private readonly ActorSystem _system;
        private readonly Config _config;
        private readonly string _role;

        public static ClusterClientReceptionist Get(ActorSystem system)
        {
            return system.WithExtension<ClusterClientReceptionist, ClusterClientReceptionistExtensionProvider>();
        }

        public ClusterClientReceptionist(ExtendedActorSystem system)
        {
            _system = system;
            _config = system.Settings.Config.GetConfig("akka.cluster.client.receptionist");
            _role = _config.GetString("role");
        }

        /**
         * Returns true if this member is not tagged with the role configured for the
         * receptionist.
         */

        public bool IsTerminated
        {
            get
            {
                var cluster = Cluster.Get(_system);
                return cluster.IsTerminated || !(_role == null || cluster.SelfRoles.Contains(_role));
            }
        }

        /**
         * Register the actors that should be reachable for the clients in this [[DistributedPubSubMediator]].
         */
        public IActorRef PubSubMediator
        {
            get { return DistributedPubSub.Get(_system).Mediator; }
        }

        /**
         * Register an actor that should be reachable for the clients.
         * The clients can send messages to this actor with `Send` or `SendToAll` using
         * the path elements of the `ActorRef`, e.g. `"/user/myservice"`.
         */
        public void RegisterService(IActorRef actorRef)
        {
            PubSubMediator.Tell(new DistributedPubSubMediator.Put(actorRef));
        }

        /**
         * A registered actor will be automatically unregistered when terminated,
         * but it can also be explicitly unregistered before termination.
         */
        public void UnregisterService(IActorRef actorRef)
        {
            PubSubMediator.Tell(new DistributedPubSubMediator.Remove(actorRef.Path.ToStringWithoutAddress()));
        }

        /**
         * Register an actor that should be reachable for the clients to a named topic.
         * Several actors can be registered to the same topic name, and all will receive
         * published messages.
         * The client can publish messages to this topic with `Publish`.
         */
        public void RegisterSubscriber(string topic, IActorRef actorRef)
        {
            PubSubMediator.Tell(new DistributedPubSubMediator.Subscribe(topic, actorRef));
        }

        /**
         * A registered subscriber will be automatically unregistered when terminated,
         * but it can also be explicitly unregistered before termination.
         */
        public void UnregisterSubscriber(string topic, IActorRef actorRef)
        {
            PubSubMediator.Tell(new DistributedPubSubMediator.Unsubscribe(topic, actorRef));
        }   
    }
}