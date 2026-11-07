//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonV2SerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Cluster.Tools.Singleton.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton;

/// <summary>Port contracts for all four singleton hand-over messages, including original singleton identity.</summary>
[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ClusterSingletonV2SerializerSpec : V2PortSpec
{
    private static readonly IReadOnlyList<V2PortCase> Corpus = new[]
    {
        new V2PortCase(HandOverToMe.Instance, "A", "hand-over-to-me"),
        new V2PortCase(HandOverInProgress.Instance, "B", "hand-over-in-progress"),
        new V2PortCase(HandOverDone.Instance, "C", "hand-over-done"),
        new V2PortCase(TakeOverFromMe.Instance, "D", "take-over-from-me")
    };

    public ClusterSingletonV2SerializerSpec(ITestOutputHelper output)
        : base(output, ClusterSingleton.DefaultConfig())
    {
    }

    protected override Serializer CreateLegacy(ExtendedActorSystem system) => new ClusterSingletonMessageSerializer(system);

    protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new ClusterSingletonMessagePackSerializer(system);

    protected override IReadOnlyList<V2PortCase> Cases => Corpus;

    [Fact(DisplayName = "Should_ReturnOriginalSingletonInstances_When_V2DeserializesEveryManifest")]
    public void Should_ReturnOriginalSingletonInstances_When_V2DeserializesEveryManifest()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        foreach (var sample in Cases)
        {
            var bytes = serializer.ToBinary(sample.Message);
            serializer.FromBinary(bytes, sample.Manifest).Should().BeSameAs(sample.Message);
            serializer.Deserialize(new ReadOnlySequence<byte>(bytes), sample.Manifest).Should().BeSameAs(sample.Message);
        }
    }

    [Fact(DisplayName = "Should_ReadBothDirections_When_TheGlobalSwitchIsEnabledOnOneNode")]
    public async Task Should_ReadBothDirections_When_TheGlobalSwitchIsEnabledOnOneNode()
    {
        await using var pair = MixedBindingPair.Create("singleton-v2-mixed",
            ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(ClusterSingleton.DefaultConfig()),
            ClusterSingleton.DefaultConfig());
        pair.AssertRoundTripsBothWays(Cases, 54, 14);
    }

    [Fact(DisplayName = "Should_PreserveTheLegacyPin_When_BothNodesEnableTheGlobalSwitch")]
    public async Task Should_PreserveTheLegacyPin_When_BothNodesEnableTheGlobalSwitch()
    {
        var enabled = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(ClusterSingleton.DefaultConfig());
        await using var pair = MixedBindingPair.Create("singleton-v2-pinned", enabled,
            MixedBindingPair.BindTo("akka-singleton", typeof(IClusterSingletonMessage)).WithFallback(enabled));
        pair.AssertRoundTripsBothWays(Cases, 54, 14);
    }

    [Fact(DisplayName = "Should_RejectUnknownInputs_When_TheSingletonProtocolIsNotMatched")]
    public void Should_RejectUnknownInputs_When_TheSingletonProtocolIsNotMatched()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        Action manifest = () => serializer.Manifest(new object());
        Action write = () => serializer.ToBinary(new object());
        Action read = () => serializer.Deserialize(new ReadOnlySequence<byte>(new byte[] { 0x80 }), "unknown");
        manifest.Should().Throw<ArgumentException>();
        write.Should().Throw<ArgumentException>();
        read.Should().Throw<SerializationException>();
    }
}
