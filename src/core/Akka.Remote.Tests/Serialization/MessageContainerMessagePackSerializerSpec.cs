// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Serialization;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class MessageContainerMessagePackSerializerSpec : V2PortSpec
{
    internal static readonly Config PortConfig = ConfigurationFactory.ParseString("""
        akka.actor.provider = remote
        akka.remote.dot-netty.tcp.port = 0
        """);

    public MessageContainerMessagePackSerializerSpec(ITestOutputHelper output) : base(output, PortConfig)
    {
    }

    protected override Serializer CreateLegacy(ExtendedActorSystem system) => new MessageContainerSerializer(system);
    protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new MessageContainerMessagePackSerializer(system);
    protected override IReadOnlyList<V2PortCase> Cases => Corpus;

    internal static readonly V2PortCase[] Corpus =
    [
        Sample("hello", [], "empty-path"),
        Sample(null!, [], "null-payload"),
        Sample("", [new SelectChildName("user")], "empty-payload"),
        Sample("hello", [new SelectChildName("user"), new SelectChildName("target")], "child-names"),
        Sample("hello", [new SelectChildPattern("worker-*")], "child-pattern"),
        Sample("hello", [new SelectParent()], "parent"),
        Sample("hello", [new SelectChildName("user"), new SelectChildName("target"), new SelectParent(),
            new SelectChildPattern("worker-*"), new SelectChildName("leaf")], "mixed-path"),
        Sample(new byte[] { 0, 1, 127, 255 }, [new SelectChildName("bytes")], "binary-payload"),
        Sample(123456, [new SelectChildName("integer")], "integer-payload"),
        Sample(new ActorSelectionMessage("inner", [new SelectChildName("nested")]),
            [new SelectChildName("outer")], "nested-selection")
    ];

    private static V2PortCase Sample(object payload, SelectionPathElement[] elements, string name)
        => new(new ActorSelectionMessage(payload, elements), "", name)
        {
            V2Manifest = MessageContainerMessagePackSerializer.SelectionManifest
        };

    [Fact(DisplayName = "Should_UseANativeManifestWithoutChangingLegacy_When_TheNewIdIsSelected")]
    public void Should_UseANativeManifestWithoutChangingLegacy_When_TheNewIdIsSelected()
    {
        var message = Corpus[0].Message;
        Assert.Equal("", Akka.Serialization.Serialization.ManifestFor(CreateLegacy((ExtendedActorSystem)Sys), message));
        Assert.Equal("a", Akka.Serialization.Serialization.ManifestFor(CreateV2((ExtendedActorSystem)Sys), message));
    }

    [Fact(DisplayName = "Should_ExchangeBothFormats_When_OneNodePinsLegacyWithTheGlobalSwitchOn")]
    public async Task Should_ExchangeBothFormats_When_OneNodePinsLegacyWithTheGlobalSwitchOn()
    {
        var enabled = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(PortConfig);
        var pinned = MixedBindingPair.BindTo("akka-containers", typeof(ActorSelectionMessage)).WithFallback(enabled);
        await using var pair = MixedBindingPair.Create("selection-mixed", enabled, pinned);
        pair.AssertRoundTripsBothWays(Corpus, 46, 6);
    }

    [Fact(DisplayName = "Should_PreserveWildcardFanOut_When_V2RoundTripsTheNewField")]
    public void Should_PreserveWildcardFanOut_When_V2RoundTripsTheNewField()
    {
        // Legacy id 6 omits this flag; true is a V2-only behavioral check, outside parity corpus.
        var message = new ActorSelectionMessage("fan-out", [new SelectChildPattern("*")], wildCardFanOut: true);
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var result = Assert.IsType<ActorSelectionMessage>(serializer.FromBinary(serializer.ToBinary(message), "a"));
        Assert.True(result.WildCardFanOut);
        Assert.Equal(message.Message, result.Message);
        Assert.Equal(message.Elements, result.Elements);
    }

    [Theory(DisplayName = "Should_RejectUnknownManifests_When_ReadingTheNativeFormat")]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("unknown")]
    public void Should_RejectUnknownManifests_When_ReadingTheNativeFormat(string manifest)
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var bytes = serializer.ToBinary(Corpus[0].Message);
        Assert.Throws<System.Runtime.Serialization.SerializationException>(() => serializer.FromBinary(bytes, manifest));
    }

    [Fact(DisplayName = "Should_RejectRecursiveSelectorsBeforeWriting_When_TheyAreUnsupported")]
    public void Should_RejectRecursiveSelectorsBeforeWriting_When_TheyAreUnsupported()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var message = new ActorSelectionMessage("recursive", [new SelectChildName("user"), SelectChildRecursive.Instance]);
        var writer = new ArrayBufferWriter<byte>();
        Assert.Throws<ArgumentException>(() => serializer.Serialize(message, writer));
        Assert.Equal(0, writer.WrittenCount);
        Assert.Throws<ArgumentException>(() => serializer.ToBinary(message));
    }
}
