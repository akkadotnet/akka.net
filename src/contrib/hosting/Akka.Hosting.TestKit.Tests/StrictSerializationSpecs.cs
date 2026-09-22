// -----------------------------------------------------------------------
//  <copyright file="StrictSerializationSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using FluentAssertions;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Akka.Hosting.TestKit.Tests;

public class StrictSerializationSpecsEnabled : TestKit
{
    public StrictSerializationSpecsEnabled(XunitTestOutputHelper output)
        : base(nameof(StrictSerializationSpecsEnabled), output, logLevel: LogLevel.Information)
    {
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.WithStrictSerialization();
    }

    [Fact(DisplayName = "WithStrictSerialization() should set allow-unregistered-types to off")]
    public void ShouldSetAllowUnregisteredTypesOff()
    {
        var hocon = Sys.Settings.Config.GetConfig("akka.actor.serialization-settings");
        var value = hocon.GetString("allow-unregistered-types");
        value.Should().Be("off");
    }
}

public class StrictSerializationSpecsDisabled : TestKit
{
    public StrictSerializationSpecsDisabled(XunitTestOutputHelper output)
        : base(nameof(StrictSerializationSpecsDisabled), output, logLevel: LogLevel.Information)
    {
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.WithStrictSerialization(false);
    }

    [Fact(DisplayName = "WithStrictSerialization(false) should set allow-unregistered-types to on")]
    public void ShouldSetAllowUnregisteredTypesOn()
    {
        var hocon = Sys.Settings.Config.GetConfig("akka.actor.serialization-settings");
        var value = hocon.GetString("allow-unregistered-types");
        value.Should().Be("on");
    }
}
