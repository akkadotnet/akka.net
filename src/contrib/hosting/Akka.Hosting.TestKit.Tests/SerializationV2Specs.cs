// -----------------------------------------------------------------------
//  <copyright file="SerializationV2Specs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
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

    [Fact(DisplayName = "Should_set_the_serialization_v2_HOCON_key_to_true_When_WithSerializationV2_is_called")]
    public async Task Should_set_the_serialization_v2_HOCON_key_to_true_When_WithSerializationV2_is_called()
    {
        await Task.Yield();
        Sys.Settings.Config.GetBoolean("akka.actor.serialization-v2", false).Should().BeTrue();
    }
}

public sealed class SerializationV2SpecsDisabled : TestKit
{
    public SerializationV2SpecsDisabled(XunitTestOutputHelper output)
        : base(nameof(SerializationV2SpecsDisabled), output, logLevel: LogLevel.Information)
    {
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        // HOCON added earlier says on; the extension wins over it
        builder.AddHocon("akka.actor.serialization-v2 = on", HoconAddMode.Prepend);
        builder.WithSerializationV2(false);
    }

    [Fact(DisplayName = "Should_set_the_serialization_v2_HOCON_key_to_false_When_WithSerializationV2_is_called_with_false")]
    public async Task Should_set_the_serialization_v2_HOCON_key_to_false_When_WithSerializationV2_is_called_with_false()
    {
        await Task.Yield();
        Sys.Settings.Config.GetBoolean("akka.actor.serialization-v2", true).Should().BeFalse();
    }
}

public sealed class SerializationV2SpecsLastCallWins : TestKit
{
    public SerializationV2SpecsLastCallWins(XunitTestOutputHelper output)
        : base(nameof(SerializationV2SpecsLastCallWins), output, logLevel: LogLevel.Information)
    {
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.WithSerializationV2(false);
        builder.WithSerializationV2();
    }

    [Fact(DisplayName = "Should_use_the_last_value_When_WithSerializationV2_is_called_twice")]
    public async Task Should_use_the_last_value_When_WithSerializationV2_is_called_twice()
    {
        await Task.Yield();
        Sys.Settings.Config.GetBoolean("akka.actor.serialization-v2", false).Should().BeTrue();
    }
}
