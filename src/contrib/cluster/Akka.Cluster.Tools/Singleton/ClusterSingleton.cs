//-----------------------------------------------------------------------
// <copyright file="ClusterSingleton.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Util;

namespace Akka.Cluster.Tools.Singleton
{
    /// <summary>
    /// This class is not intended for user extension other than for test purposes (e.g. stub implementation). 
    /// More methods may be added in the future and that may break such implementations.
    /// </summary>
    [DoNotInherit]
    public class ClusterSingleton : IExtension
    {
        private readonly ActorSystem _system;
        private readonly Lazy<Cluster> _cluster;
        
        /// <summary>
        /// Returns default HOCON configuration for the cluster singleton.
        /// </summary>
        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<ClusterSingleton>(
                "Akka.Cluster.Tools.Singleton.reference.conf");
        }
        
        // Cache for singleton proxies, remove in v1.6
        private readonly ConcurrentDictionary<string, IActorRef> _proxies = new();

        public static ClusterSingleton Get(ActorSystem system) =>
            system.WithExtension<ClusterSingleton, ClusterSingletonProvider>();

        public ClusterSingleton(ExtendedActorSystem system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(DefaultConfig());
            _cluster = new Lazy<Cluster>(() => Cluster.Get(system));
        }

        /// <summary>
        /// Start if needed and provide a proxy to a named singleton.
        /// 
        /// <para>If there already is a manager running for the given `singletonName` on this node, no additional manager is started.</para>
        /// <para>If there already is a proxy running for the given `singletonName` on this node, an <see cref="IActorRef"/> to that is returned.</para>
        /// </summary>
        /// <returns>A proxy actor that can be used to communicate with the singleton in the cluster</returns>
        [Obsolete("This convenience method is deprecated and will be removed in v1.6, " +
                  "please use ClusterSingletonManager.Props and ClusterSingletonProxy.Props directly instead. " +
                  "See https://getakka.net/community/whats-new/akkadotnet-v1.5-upgrade-advisories.html#upgrading-to-akkanet-v1532. " +
                  "Since 1.5.32.")]
        public IActorRef Init(SingletonActor singleton)
        {
            var settings = singleton.Settings.GetOrElse(ClusterSingletonSettings.Create(_system));
            if (settings.ShouldRunManager(_cluster.Value))
            {
                var managerName = ManagerNameFor(singleton.Name);
                try
                {
                    _system.ActorOf(ClusterSingletonManager.Props(
                        singletonProps: singleton.Props,
                        terminationMessage: singleton.StopMessage.GetOrElse(PoisonPill.Instance),
                        settings: settings.ToManagerSettings(singleton.Name)),
                        managerName);
                }
                catch (InvalidActorNameException ex) when (ex.Message.EndsWith("is not unique!"))
                {
                    // This is fine. We just wanted to make sure it is running and it already is
                }
            }

            return GetProxy(singleton.Name, settings);
        }

        [Obsolete("Deprecated, remove in v1.6")]
        private IActorRef GetProxy(string name, ClusterSingletonSettings settings)
        {
            IActorRef ProxyCreator()
            {
                var proxyName = $"singletonProxy{name}";
                return _system.ActorOf(
                    props: ClusterSingletonProxy.Props(
                        singletonManagerPath: $"/user/{ManagerNameFor(name)}",
                        settings: settings.ToProxySettings(name)),
                    name: proxyName);
            }

            return _proxies.GetOrAdd(name, _ => ProxyCreator());
        }

        [Obsolete("Deprecated, remove in v1.6")]
        private string ManagerNameFor(string singletonName) => $"singletonManager{singletonName}";
    }

    public class ClusterSingletonProvider : ExtensionIdProvider<ClusterSingleton>
    {
        public override ClusterSingleton CreateExtension(ExtendedActorSystem system) => new(system);
    }

    [Obsolete("This setting class is deprecated and will be removed in v1.6, " +
              "please use ClusterSingletonManager.Props and ClusterSingletonProxy.Props directly instead. " +
              "See https://getakka.net/community/whats-new/akkadotnet-v1.5-upgrade-advisories.html#upgrading-to-akkanet-v1532. " +
              "Since 1.5.32.")]
    public class SingletonActor
    {
        public string Name { get; }

        public Props Props { get; }

        public Option<object> StopMessage { get; }

        public Option<ClusterSingletonSettings> Settings { get; }

        public static SingletonActor Create(Props props, string name) => new(name, props, Option<object>.None, Option<ClusterSingletonSettings>.None);

        private SingletonActor(string name, Props props, Option<object> stopMessage, Option<ClusterSingletonSettings> settings)
        {
            Name = name;
            Props = props;
            StopMessage = stopMessage;
            Settings = settings;
        }

        /// <summary>
        /// <see cref="Props"/> of the singleton actor, such as dispatcher settings.
        /// </summary>
        public SingletonActor WithProps(Props props) => Copy(props: props);

        /// <summary>
        /// Message sent to the singleton to tell it to stop, e.g. when being migrated. 
        /// If this is not defined, a <see cref="PoisonPill"/> will be used instead. 
        /// It can be useful to define a custom stop message if the singleton needs to 
        /// perform some asynchronous cleanup or interactions before stopping.
        /// </summary>
        public SingletonActor WithStopMessage(object stopMessage) => Copy(stopMessage: stopMessage);

        /// <summary>
        /// Additional settings, typically loaded from configuration.
        /// </summary>
        public SingletonActor WithSettings(ClusterSingletonSettings settings) => Copy(settings: settings);

        private SingletonActor Copy(string name = null, Props props = null, Option<object> stopMessage = default, Option<ClusterSingletonSettings> settings = default) =>
            new(name ?? Name, props ?? Props, stopMessage.HasValue ? stopMessage : StopMessage, settings.HasValue ? settings : Settings);
    }
}
