// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Cluster.Tools.PublishSubscribe.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Status = Akka.Cluster.Tools.PublishSubscribe.Internal.Status;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class DistributedPubSubMessagePackSerializerSpec : V2PortSpec
{
    private readonly ITestOutputHelper _output;

    public DistributedPubSubMessagePackSerializerSpec(ITestOutputHelper output) : base(output)
    {
        _output = output;
    }

    protected override Serializer CreateLegacy(ExtendedActorSystem system) => new DistributedPubSubMessageSerializer(system);
    protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new DistributedPubSubMessagePackSerializer(system);
    protected override IReadOnlyList<V2PortCase> Cases { get; } = PubSubWireCases.Create()
        .Select(item => new V2PortCase(item.Message, item.Manifest, item.Name)).ToArray();

    [Theory(DisplayName = "Should_SelectPubSubWriter_When_GlobalToggleChanges")]
    [InlineData(false, 9)]
    [InlineData(true, 49)]
    public async Task Should_SelectPubSubWriter_When_GlobalToggleChanges(bool enabled, int id)
    {
        using var system = ActorSystem.Create("pubsub-toggle", Toggle(enabled));
        InitializeLogger(system);
        try
        {
            foreach (var item in Cases)
                system.Serialization.FindSerializerFor(item.Message).Identifier.Should().Be(id, item.Name);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact(DisplayName = "Should_PreserveExplicitLegacyBindings_When_GlobalToggleIsEnabled")]
    public async Task Should_PreserveExplicitLegacyBindings_When_GlobalToggleIsEnabled()
    {
        var pins = MixedBindingPair.BindTo("akka-pubsub", typeof(IDistributedPubSubMessage), typeof(SendToOneSubscriber));
        using var system = ActorSystem.Create("pubsub-pinned", pins.WithFallback(Toggle(true)));
        InitializeLogger(system);
        try
        {
            foreach (var item in Cases)
                system.Serialization.FindSerializerFor(item.Message).Identifier.Should().Be(9, item.Name);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact(DisplayName = "Should_ReadBothWireFormats_When_PeersUseDifferentToggleValues")]
    public async Task Should_ReadBothWireFormats_When_PeersUseDifferentToggleValues()
    {
        await using var pair = MixedBindingPair.Create("pubsub-mixed", Toggle(true), Toggle(false));
        InitializeLogger(pair.V2Node);
        InitializeLogger(pair.LegacyNode);
        pair.AssertRoundTripsBothWays(Cases, 49, 9);
    }

    [Fact(DisplayName = "Should_RestoreRegistryState_When_DeltaContainsActorReferencesAndTombstones")]
    public async Task Should_RestoreRegistryState_When_DeltaContainsActorReferencesAndTombstones()
    {
        var actor = Sys.ActorOf(Props.Empty, "registered");
        try
        {
            var secondOwner = new Address("akka.tcp", "pubsub", "127.0.0.2", 2553);
            var delta = new Delta(ImmutableList.Create(
                new Bucket(PubSubWireCases.Owner, 19, ImmutableDictionary<string, ValueHolder>.Empty
                    .Add("/user/registered", new ValueHolder(17, actor))
                    .Add("/user/gone", new ValueHolder(18, null!))),
                new Bucket(secondOwner, 23, ImmutableDictionary<string, ValueHolder>.Empty)));
            var serializer = CreateV2((ExtendedActorSystem)Sys);
            var restored = (Delta)Sys.Serialization.Deserialize(serializer.ToBinary(delta), 49, "B");
            restored.Buckets.Count.Should().Be(2);
            restored.Buckets[0].Owner.Should().Be(PubSubWireCases.Owner);
            restored.Buckets[0].Version.Should().Be(19);
            restored.Buckets[0].Content["/user/registered"].Ref.Should().BeSameAs(actor);
            restored.Buckets[0].Content["/user/registered"].Version.Should().Be(17);
            restored.Buckets[0].Content["/user/gone"].Ref.Should().BeNull();
            restored.Buckets[0].Content["/user/gone"].Version.Should().Be(18);
            restored.Buckets[1].Owner.Should().Be(secondOwner);
            restored.Buckets[1].Version.Should().Be(23);
            restored.Buckets[1].Content.Should().BeEmpty();
        }
        finally
        {
            await WatchAsync(actor);
            Sys.Stop(actor);
            await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(5));
        }
    }

    [Fact(DisplayName = "Should_RestoreAllVersions_When_StatusContainsMultipleOwners")]
    public void Should_RestoreAllVersions_When_StatusContainsMultipleOwners()
    {
        var versions = ImmutableDictionary<Address, long>.Empty
            .Add(PubSubWireCases.Owner, long.MaxValue)
            .Add(new Address("akka.tcp", "pubsub", "127.0.0.2", 2553), 0);
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var restored = (Status)serializer.FromBinary(serializer.ToBinary(new Status(versions, false)), "A");
        restored.Versions.Should().BeEquivalentTo(versions);
        restored.IsReplyToStatus.Should().BeFalse();
    }

    [Fact(DisplayName = "Should_PreserveBinaryPayload_When_UserSerializerOwnsThePayload")]
    public void Should_PreserveBinaryPayload_When_UserSerializerOwnsThePayload()
    {
        var payload = new byte[] { 0, 1, 127, 255 };
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var restored = (Send)serializer.FromBinary(serializer.ToBinary(new Send("/user/target", payload, true)), "C");
        restored.Message.Should().BeOfType<byte[]>().Which.Should().Equal(payload);
        restored.Path.Should().Be("/user/target");
        restored.LocalAffinity.Should().BeTrue();
    }

    [Fact(DisplayName = "Should_UseRegisteredPayloadSerializer_When_EnvelopeContainsACustomPayload")]
    public async Task Should_UseRegisteredPayloadSerializer_When_EnvelopeContainsACustomPayload()
    {
        var config = ConfigurationFactory.ParseString($$"""
            akka.actor.serialization-v2 = on
            akka.actor.serializers.pubsub-payload = "{{typeof(PayloadSerializer).AssemblyQualifiedName}}"
            akka.actor.serialization-bindings {
                "{{typeof(Payload).AssemblyQualifiedName}}" = pubsub-payload
            }
            """);
        await using var kit = new Akka.TestKit.Xunit.TestKit(config, "pubsub-custom-payload", _output);
        var payload = new Payload(17);
        var registered = (PayloadSerializer)kit.Sys.Serialization.FindSerializerFor(payload);
        foreach (var message in new object[]
                 { new Send("/user/target", payload, false), new SendToAll("/user/target", payload, true),
                     new Publish("topic", payload, true), new SendToOneSubscriber(payload) })
        {
            var serializer = kit.Sys.Serialization.FindSerializerFor(message);
            serializer.Identifier.Should().Be(49);
            var restored = serializer.FromBinary(serializer.ToBinary(message), serializer.Manifest(message));
            var restoredPayload = restored switch
            {
                Send send => send.Message,
                SendToAll all => all.Message,
                Publish publish => publish.Message,
                SendToOneSubscriber subscriber => subscriber.Message,
                _ => throw new InvalidOperationException("Unexpected restored envelope")
            };
            restoredPayload.Should().BeOfType<Payload>().Which.Value.Should().Be(17);
        }
        registered.Writes.Should().Be(4);
        registered.Reads.Should().Be(4);
    }

    [Fact(DisplayName = "Should_RejectUnsupportedMessagesAndManifests_When_CodecIsCalledDirectly")]
    public void Should_RejectUnsupportedMessagesAndManifests_When_CodecIsCalledDirectly()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        Assert.Throws<SerializationException>(() => serializer.ToBinary(new PublishWithAck("topic", "message", TimeSpan.FromSeconds(3))));
        Assert.Throws<ArgumentException>(() => serializer.Manifest(new object()));
        Assert.Throws<SerializationException>(() => serializer.FromBinary([], "unknown"));
    }

    public sealed record Payload(int Value);

    public sealed class PayloadSerializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public override int Identifier => 777;
        public override string Manifest(object obj) => "payload";
        public override byte[] ToBinary(object obj)
        {
            Writes++;
            return BitConverter.GetBytes(((Payload)obj).Value);
        }
        public override object FromBinary(byte[] bytes, string manifest)
        {
            Reads++;
            return new Payload(BitConverter.ToInt32(bytes));
        }
    }

    private static Config Toggle(bool enabled) => ConfigurationFactory.ParseString(
        $"akka.actor.serialization-v2 = {(enabled ? "on" : "off")}");
}
