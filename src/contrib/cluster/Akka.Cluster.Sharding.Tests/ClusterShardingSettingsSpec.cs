﻿// -----------------------------------------------------------------------
//  <copyright file="ClusterShardingSettingsSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests;

public class ClusterShardingSettingsSpec : AkkaSpec
{
    public ClusterShardingSettingsSpec(ITestOutputHelper helper)
        : base(SpecConfig, helper)
    {
    }

    private static Config SpecConfig =>
        ConfigurationFactory.ParseString(@"akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0")
            .WithFallback(ClusterSharding.DefaultConfig())
            .WithFallback(ClusterSingletonManager.DefaultConfig());


    [Fact]
    public void
        ClusterShardingSettingsSpec_must_passivate_idle_entities_if_RememberEntities_and_PassivateIdleEntityAfter_are_the_defaults()
    {
        ClusterShardingSettings.Create(Sys)
            .ShouldPassivateIdleEntities.ShouldBe(true);
    }

    [Fact]
    public void ClusterShardingSettingsSpec_should_disable_passivation_if_RememberEntities_is_enabled()
    {
        ClusterShardingSettings.Create(Sys)
            .WithRememberEntities(true)
            .ShouldPassivateIdleEntities.ShouldBe(false);
    }

    [Fact]
    public void
        ClusterShardingSettingsSpec_should_disable_passivation_if_RememberEntities_is_enabled_and_PassivateIdleEntityAfter_is_0_or_off()
    {
        ClusterShardingSettings.Create(Sys)
            .WithRememberEntities(true)
            .WithPassivateIdleAfter(TimeSpan.Zero)
            .ShouldPassivateIdleEntities.ShouldBe(false);
    }

    [Fact]
    public void
        ClusterShardingSettingsSpec_should_disable_passivation_if_RememberEntities_is_the_default_and_PassivateIdleEntityAfter_is_0_or_off()
    {
        ClusterShardingSettings.Create(Sys)
            .WithPassivateIdleAfter(TimeSpan.Zero)
            .ShouldPassivateIdleEntities.ShouldBe(false);
    }
}