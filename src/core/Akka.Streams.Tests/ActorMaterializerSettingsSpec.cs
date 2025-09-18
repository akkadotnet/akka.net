// -----------------------------------------------------------------------
//  <copyright file="ActorMaterializerSettingsSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests;

public class ActorMaterializerSettingsSpec: Akka.TestKit.Xunit2.TestKit
{
    private readonly ActorMaterializer _materializer;
    public ActorMaterializerSettingsSpec(ITestOutputHelper output) : base(Config.Empty, nameof(ActorMaterializerSettingsSpec), output)
    {
        _materializer = ActorMaterializer.Create(Sys);
    }

    [Fact]
    public void ActorMaterializerSettings_Should_contain_default_values()
    {
        var settings = _materializer.Settings;
        Assert.Equal(4, settings.InitialInputBufferSize);
        Assert.Equal(16, settings.MaxInputBufferSize);
        Assert.Equal(string.Empty, settings.Dispatcher);
        Assert.False(settings.IsDebugLogging);
        Assert.Equal(1000, settings.OutputBurstLimit);
        Assert.True(settings.IsAutoFusing);
        Assert.Equal(1000000000, settings.MaxFixedBufferSize);
        Assert.Equal(1000, settings.SyncProcessingLimit);
        Assert.False(settings.IsFuzzingMode);
        
        var subscriptionTimeoutSettings = settings.SubscriptionTimeoutSettings;
        Assert.Equal(StreamSubscriptionTimeoutTerminationMode.CancelTermination, subscriptionTimeoutSettings.Mode);
        Assert.Equal(TimeSpan.FromSeconds(5), subscriptionTimeoutSettings.Timeout);
        
        var streamRefSettings = settings.StreamRefSettings;
        Assert.Equal(32, streamRefSettings.BufferCapacity);
        Assert.Equal(TimeSpan.FromSeconds(1), streamRefSettings.DemandRedeliveryInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), streamRefSettings.SubscriptionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), streamRefSettings.FinalTerminationSignalDeadline);
    }

    [Fact]
    public void ActorMaterializer_serialization_binding_should_be_correct()
    {
        var config = Sys.Settings.Config.GetConfig("akka.actor");
        
        // Serializer should be registered
        var serializers = config.GetConfig("serializers").AsEnumerable().ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
        Assert.Contains(serializers.Keys, s => s is "akka-stream-ref");
        Assert.Equal("\"Akka.Streams.Serialization.StreamRefSerializer, Akka.Streams\"", serializers["akka-stream-ref"]);
        
        // Serializer should have proper type binding
        var binding = config.GetConfig("serialization-bindings").AsEnumerable().ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
        Assert.Contains(binding.Keys, s => s is "Akka.Streams.Implementation.StreamRef.SinkRefImpl, Akka.Streams");
        Assert.Equal("akka-stream-ref", binding["Akka.Streams.Implementation.StreamRef.SinkRefImpl, Akka.Streams"]);
        
        Assert.Contains(binding.Keys, s => s is "Akka.Streams.Implementation.StreamRef.SourceRefImpl, Akka.Streams");
        Assert.Equal("akka-stream-ref", binding["Akka.Streams.Implementation.StreamRef.SourceRefImpl, Akka.Streams"]);
        
        Assert.Contains(binding.Keys, s => s is "Akka.Streams.Implementation.StreamRef.IStreamRefsProtocol, Akka.Streams");
        Assert.Equal("akka-stream-ref", binding["Akka.Streams.Implementation.StreamRef.IStreamRefsProtocol, Akka.Streams"]);
        
        // Serializer should have correct id
        Assert.Equal(30, config.GetInt("serialization-identifiers.\"Akka.Streams.Serialization.StreamRefSerializer, Akka.Streams\""));
    }
}