// -----------------------------------------------------------------------
//  <copyright file="ActorMaterializerSettingsSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Xunit;

namespace Akka.Streams.Tests;

public sealed class ActorMaterializerSettingsSpec : Akka.TestKit.Xunit.TestKit
{
    public ActorMaterializerSettingsSpec(ITestOutputHelper output)
        : base(Config.Empty, nameof(ActorMaterializerSettingsSpec), output)
    {
    }

    [Fact(DisplayName = "Should_read_default_StreamRefSettings_from_reference_conf_When_materializer_is_created")]
    public void Should_read_default_StreamRefSettings_from_reference_conf_When_materializer_is_created()
    {
        var streamRefSettings = ActorMaterializer.Create(Sys).Settings.StreamRefSettings;
        Assert.Equal(32, streamRefSettings.BufferCapacity);
        Assert.Equal(TimeSpan.FromSeconds(1), streamRefSettings.DemandRedeliveryInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), streamRefSettings.SubscriptionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), streamRefSettings.FinalTerminationSignalDeadline);
    }

    [Fact(DisplayName = "Should_read_FinalTerminationSignalDeadline_from_config_and_preserve_it_When_settings_are_copied")]
    public void Should_read_FinalTerminationSignalDeadline_from_config_and_preserve_it_When_settings_are_copied()
    {
        var settings = StreamRefSettings.Create(ConfigurationFactory.ParseString("final-termination-signal-deadline = 7s"));
        Assert.Equal(TimeSpan.FromSeconds(7), settings.FinalTerminationSignalDeadline);

        // Copy/With* must carry the value over
        Assert.Equal(TimeSpan.FromSeconds(7), settings.WithBufferCapacity(8).FinalTerminationSignalDeadline);
        Assert.Equal(TimeSpan.FromSeconds(3), settings.WithFinalTerminationSignalDeadline(TimeSpan.FromSeconds(3)).FinalTerminationSignalDeadline);

        // and so must ActorMaterializerSettings
        var materializerSettings = ActorMaterializerSettings.Create(Sys).WithStreamRefSettings(settings);
        Assert.Equal(TimeSpan.FromSeconds(7), materializerSettings.StreamRefSettings.FinalTerminationSignalDeadline);
    }
}
