//-----------------------------------------------------------------------
// <copyright file="DistributedPubSub.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Cluster.Tools.PubSub
{
    /**
     * Marker trait for remote messages with special serializer.
     */
    public interface IDistributedPubSubMessage { }

    public class DistributedPubSubExtensionProvider : ExtensionIdProvider<DistributedPubSub>
    {
        public override DistributedPubSub CreateExtension(ExtendedActorSystem system)
        {
            return new DistributedPubSub(system);
        }
    }

    /**
     * Extension that starts a [[DistributedPubSubMediator]] actor
     * with settings defined in config section `akka.cluster.pub-sub`.
     */
    public class DistributedPubSub :  IExtension
    {
        private readonly ExtendedActorSystem _system;
        private readonly DistributedPubSubSettings _settings;
        private readonly Cluster _cluster;

        public static DistributedPubSub Get(ActorSystem system)
        {
            return system.WithExtension<DistributedPubSub, DistributedPubSubExtensionProvider>();
        }

        public DistributedPubSub(ExtendedActorSystem system)
        {
            _system = system;
            _settings = DistributedPubSubSettings.Create(system);
            _cluster = Cluster.Get(_system);
        }

        /**
         * Returns true if this member is not tagged with the role configured for the
         * mediator.
         */
        public bool IsTerminated
        {
            get
            {
                return _cluster.IsTerminated || !(string.IsNullOrEmpty(_settings.Role) || _cluster.SelfRoles.Contains(_settings.Role));
            }
        }

        /**
         * The [[DistributedPubSubMediator]]
         */
        public IActorRef Mediator
        {
            get
            {
                if (IsTerminated) return _system.DeadLetters;
                else
                {
                    var name = _system.Settings.Config.GetString("akka.cluster.pub-sub.name");
                    return _system.ActorOf(Props.Create(() => new DistributedPubSubMediator(_settings)).WithDeploy(Deploy.Local));
                }
            }
        }
    }
}