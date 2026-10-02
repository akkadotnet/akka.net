// -----------------------------------------------------------------------
//  <copyright file="SerializationV2Specs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable
using System;
using Akka.Serialization;
using FluentAssertions;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Akka.Hosting.TestKit.Tests;

public sealed class SerializationV2SpecsEnabled : TestKit
{
    public SerializationV2SpecsEnabled(XunitTestOutputHelper output)
        : base(nameof(SerializationV2SpecsEnabled), output, logLevel: LogLevel.Information)
    {
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.WithSerializationV2();
    }

    [Fact(DisplayName = "Should_add_an_enabled_SerializationV2Setup_When_WithSerializationV2_is_called")]
    public void Should_add_an_enabled_SerializationV2Setup_When_WithSerializationV2_is_called()
    {
        var setup = Sys.Settings.Setup.Get<SerializationV2Setup>();
        setup.HasValue.Should().BeTrue();
        setup.Value.Enabled.Should().BeTrue();
    }
}

public sealed class SerializationV2SpecsOverridden : TestKit
{
    public SerializationV2SpecsOverridden(XunitTestOutputHelper output)
        : base(nameof(SerializationV2SpecsOverridden), output, logLevel: LogLevel.Information)
    {
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        // HOCON says on, and the last builder call says off: the setup wins over HOCON, and the last call wins
        builder.AddHocon("akka.actor.serialization-v2 = on", HoconAddMode.Prepend);
        builder.WithSerializationV2();
        builder.WithSerializationV2(false);
    }

    [Fact(DisplayName = "Should_keep_only_the_last_SerializationV2Setup_When_WithSerializationV2_is_called_twice")]
    public void Should_keep_only_the_last_SerializationV2Setup_When_WithSerializationV2_is_called_twice()
    {
        var setup = Sys.Settings.Setup.Get<SerializationV2Setup>();
        setup.HasValue.Should().BeTrue();
        setup.Value.Enabled.Should().BeFalse();
    }
}
