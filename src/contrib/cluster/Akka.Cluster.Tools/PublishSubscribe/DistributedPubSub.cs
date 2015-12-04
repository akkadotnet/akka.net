//-----------------------------------------------------------------------
// <copyright file="DistributedPubSub.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Cluster.Tools.PublishSubscribe
{
    /// <summary>
    /// Marker trait for remote messages with special serializer.
    /// </summary>
    public interface IDistributedPubSubMessage { }

    public class DistributedPubSubExtensionProvider : ExtensionIdProvider<DistributedPubSub>
    {
        public override DistributedPubSub CreateExtension(ExtendedActorSystem system)
        {
            return new DistributedPubSub(system);
        }
    }

    /// <summary>
    /// Extension that starts a <see cref="DistributedPubSubMediator"/> actor with settings 
    /// defined in config section `akka.cluster.pub-sub`.
    /// </summary>
    public class DistributedPubSub : IExtension
    {
        private readonly ExtendedActorSystem _system;
        private readonly DistributedPubSubSettings _settings;
        private readonly Cluster _cluster;
        private readonly IActorRef _mediatorRef;

        public static DistributedPubSub Get(ActorSystem system)
        {
            return system.WithExtension<DistributedPubSub, DistributedPubSubExtensionProvider>();
        }

        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<DistributedPubSub>("Akka.Cluster.Tools.PublishSubscribe.reference.conf");
        }

        public DistributedPubSub(ExtendedActorSystem system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(DefaultConfig());
            _settings = DistributedPubSubSettings.Create(system);
            _cluster = Cluster.Get(_system);
            _mediatorRef = CreateMediator();
        }

        /// <summary>
        /// Returns true if this member is not tagged with the role configured for the mediator.
        /// </summary>
        public bool IsTerminated
        {
            get
            {
                return _cluster.IsTerminated || !(string.IsNullOrEmpty(_settings.Role) || _cluster.SelfRoles.Contains(_settings.Role));
            }
        }

        /// <summary>
        /// The <see cref="DistributedPubSubMediator"/> actor reference.
        /// </summary>
        public IActorRef Mediator
        {
            get
            {
                return IsTerminated ? _system.DeadLetters : _mediatorRef;
            }
        }

        private IActorRef CreateMediator()
        {
            var name = _system.Settings.Config.GetString("akka.cluster.pub-sub.name");
            var dispatcher = _system.Settings.Config.GetString("akka.cluster.pub-sub.use-dispatcher");
            if (string.IsNullOrEmpty(dispatcher))
                dispatcher = Dispatchers.DefaultDispatcherId;

            return _system.SystemActorOf(
                Props.Create(() => new DistributedPubSubMediator(_settings))
                    .WithDeploy(Deploy.Local)
                    .WithDispatcher(dispatcher),
                name);
        }
    }
}