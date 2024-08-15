// -----------------------------------------------------------------------
//  <copyright file="ClusterSingleton.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Annotations;
using Akka.Util;

namespace Akka.Cluster.Tools.Singleton;

/// <summary>
///     This class is not intended for user extension other than for test purposes (e.g. stub implementation).
///     More methods may be added in the future and that may break such implementations.
/// </summary>
[DoNotInherit]
public class ClusterSingleton : IExtension
{
    private readonly Lazy<Cluster> _cluster;
    private readonly ConcurrentDictionary<string, IActorRef> _proxies = new();
    private readonly ActorSystem _system;

    public ClusterSingleton(ExtendedActorSystem system)
    {
        _system = system;
        _cluster = new Lazy<Cluster>(() => Cluster.Get(system));
    }

    public static ClusterSingleton Get(ActorSystem system)
    {
        return system.WithExtension<ClusterSingleton, ClusterSingletonProvider>();
    }

    /// <summary>
    ///     Start if needed and provide a proxy to a named singleton.
    ///     <para>
    ///         If there already is a manager running for the given `singletonName` on this node, no additional manager is
    ///         started.
    ///     </para>
    ///     <para>
    ///         If there already is a proxy running for the given `singletonName` on this node, an <see cref="IActorRef" />
    ///         to that is returned.
    ///     </para>
    /// </summary>
    /// <returns>A proxy actor that can be used to communicate with the singleton in the cluster</returns>
    public IActorRef Init(SingletonActor singleton)
    {
        var settings = singleton.Settings.GetOrElse(ClusterSingletonSettings.Create(_system));
        if (settings.ShouldRunManager(_cluster.Value))
        {
            var managerName = ManagerNameFor(singleton.Name);
            try
            {
                _system.ActorOf(ClusterSingletonManager.Props(
                        singleton.Props,
                        singleton.StopMessage.GetOrElse(PoisonPill.Instance),
                        settings.ToManagerSettings(singleton.Name)),
                    managerName);
            }
            catch (InvalidActorNameException ex) when (ex.Message.EndsWith("is not unique!"))
            {
                // This is fine. We just wanted to make sure it is running and it already is
            }
        }

        return GetProxy(singleton.Name, settings);
    }

    private IActorRef GetProxy(string name, ClusterSingletonSettings settings)
    {
        IActorRef ProxyCreator()
        {
            var proxyName = $"singletonProxy{name}";
            return _system.ActorOf(ClusterSingletonProxy.Props(
                    $"/user/{ManagerNameFor(name)}",
                    settings.ToProxySettings(name)),
                proxyName);
        }

        return _proxies.GetOrAdd(name, _ => ProxyCreator());
    }


    private string ManagerNameFor(string singletonName)
    {
        return $"singletonManager{singletonName}";
    }
}

public class ClusterSingletonProvider : ExtensionIdProvider<ClusterSingleton>
{
    public override ClusterSingleton CreateExtension(ExtendedActorSystem system)
    {
        return new ClusterSingleton(system);
    }
}

public class SingletonActor
{
    private SingletonActor(string name, Props props, Option<object> stopMessage,
        Option<ClusterSingletonSettings> settings)
    {
        Name = name;
        Props = props;
        StopMessage = stopMessage;
        Settings = settings;
    }

    public string Name { get; }

    public Props Props { get; }

    public Option<object> StopMessage { get; }

    public Option<ClusterSingletonSettings> Settings { get; }

    public static SingletonActor Create(Props props, string name)
    {
        return new SingletonActor(name, props, Option<object>.None, Option<ClusterSingletonSettings>.None);
    }

    /// <summary>
    ///     <see cref="Props" /> of the singleton actor, such as dispatcher settings.
    /// </summary>
    public SingletonActor WithProps(Props props)
    {
        return Copy(props: props);
    }

    /// <summary>
    ///     Message sent to the singleton to tell it to stop, e.g. when being migrated.
    ///     If this is not defined, a <see cref="PoisonPill" /> will be used instead.
    ///     It can be useful to define a custom stop message if the singleton needs to
    ///     perform some asynchronous cleanup or interactions before stopping.
    /// </summary>
    public SingletonActor WithStopMessage(object stopMessage)
    {
        return Copy(stopMessage: stopMessage);
    }

    /// <summary>
    ///     Additional settings, typically loaded from configuration.
    /// </summary>
    public SingletonActor WithSettings(ClusterSingletonSettings settings)
    {
        return Copy(settings: settings);
    }

    private SingletonActor Copy(string name = null, Props props = null, Option<object> stopMessage = default,
        Option<ClusterSingletonSettings> settings = default)
    {
        return new SingletonActor(name ?? Name, props ?? Props, stopMessage.HasValue ? stopMessage : StopMessage,
            settings.HasValue ? settings : Settings);
    }
}