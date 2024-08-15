﻿// -----------------------------------------------------------------------
//  <copyright file="Extension.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Remote.TestKit.Internals;
using Akka.Util.Internal;

namespace Akka.Remote.TestKit;

/// <summary>
///     Access to the <see cref="TestConductor" /> extension:
///     {{{
///     var tc = TestConductor(system)
///     tc.StartController(numPlayers)
///     OR
///     tc.StartClient(conductorPort)
///     }}}
/// </summary>
public class TestConductorExtension : ExtensionIdProvider<TestConductor>
{
    //TODO:
    //override def lookup = TestConductor

    public override TestConductor CreateExtension(ExtendedActorSystem system)
    {
        return new TestConductor(system);
    }

    //TODO:
    //override def get(system: ActorSystem): TestConductorExt = super.get(system)
    //def apply()(implicit ctx: ActorContext): TestConductorExt = apply(ctx.system)
}

/// <summary>
///     This binds together the Conductor and Player in an extension
///     ====Note====
///     This extension requires the `akka.actor.provider`
///     to be a <see cref="Akka.Remote.RemoteActorRefProvider" />.
///     To use ``blackhole``, ``passThrough``, and ``throttle`` you must activate the
///     failure injector and throttler transport adapters by specifying `testTransport(on = true)`
///     in your MultiNodeConfig.
/// </summary>
public partial class TestConductor : IExtension
{
    private readonly ActorSystem _system;

    public TestConductor(ActorSystem system)
    {
        Settings = new TestConductorSettings(system.Settings.Config.WithFallback(TestConductorConfigFactory.Default())
            .GetConfig("akka.testconductor"));
        Transport = system.AsInstanceOf<ExtendedActorSystem>().Provider.AsInstanceOf<IRemoteActorRefProvider>()
            .Transport;
        Address = Transport.DefaultAddress;
        _system = system;
    }

    public TestConductorSettings Settings { get; }

    /// <summary>
    ///     Remote transport used by the actor ref provider.
    /// </summary>
    public RemoteTransport Transport { get; }

    /// <summary>
    ///     Transport address of this Helios-like remote transport.
    /// </summary>
    public Address Address { get; }

    public static TestConductor Get(ActorSystem system)
    {
        return system.WithExtension<TestConductor, TestConductorExtension>();
    }
}

/// <summary>
///     Settings used to operate the <see cref="TestConductor" />.
/// </summary>
public class TestConductorSettings
{
    public TestConductorSettings(Config config)
    {
        if (config.IsNullOrEmpty())
            throw ConfigurationException.NullOrEmptyConfig<TestConductorSettings>();

        ConnectTimeout = config.GetTimeSpan("connect-timeout");
        ClientReconnects = config.GetInt("client-reconnects");
        ReconnectBackoff = config.GetTimeSpan("reconnect-backoff");
        BarrierTimeout = config.GetTimeSpan("barrier-timeout");
        QueryTimeout = config.GetTimeSpan("query-timeout");
        PacketSplitThreshold = config.GetTimeSpan("packet-split-threshold");
        ServerSocketWorkerPoolSize = ComputeWps(config.GetConfig("helios.server-socket-worker-pool"));
        ClientSocketWorkerPoolSize = ComputeWps(config.GetConfig("helios.client-socket-worker-pool"));
    }

    public TimeSpan ConnectTimeout { get; }

    public int ClientReconnects { get; }

    public TimeSpan ReconnectBackoff { get; }

    public TimeSpan BarrierTimeout { get; }

    public TimeSpan QueryTimeout { get; }

    public TimeSpan PacketSplitThreshold { get; }

    public int ServerSocketWorkerPoolSize { get; }

    public int ClientSocketWorkerPoolSize { get; }

    public int ComputeWps(Config config)
    {
        if (config.IsNullOrEmpty())
            return ThreadPoolConfig.ScaledPoolSize(2, 1.0, 2);

        return ThreadPoolConfig.ScaledPoolSize(
            config.GetInt("pool-size-min"),
            config.GetDouble("pool-size-factor"),
            config.GetInt("pool-size-max"));
    }
}