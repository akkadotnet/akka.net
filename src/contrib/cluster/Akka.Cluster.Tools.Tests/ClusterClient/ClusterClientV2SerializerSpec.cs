//-----------------------------------------------------------------------
// <copyright file="ClusterClientV2SerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.Client.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using ClientProtocol = Akka.Cluster.Tools.Client.ClusterClient;

namespace Akka.Cluster.Tools.Tests.ClusterClient;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ClusterClientV2SerializerSpec : V2PortSpec
{
    public ClusterClientV2SerializerSpec(ITestOutputHelper output) : base(output, ClientConfig)
    {
    }

    // Legacy fixtures were captured before the V2 implementation from unchanged upstream dev
    // 2d6a06797, serializer git blob 67d19516c81785ed36bcefbe67389bd6d120b141.
    internal static IReadOnlyList<V2PortCase> Corpus { get; } = new[]
    {
        new V2PortCase(new ClusterReceptionist.Contacts(ImmutableList.Create("akka.tcp://client-proof@127.0.0.1:2552/system/receptionist")), "A", "contacts"),
        new V2PortCase(new ClusterReceptionist.Contacts(ImmutableList<string>.Empty), "A", "contacts-empty"),
        new V2PortCase(ClusterReceptionist.GetContacts.Instance, "B", "get-contacts"),
        new V2PortCase(ClusterReceptionist.Heartbeat.Instance, "C", "heartbeat"),
        new V2PortCase(ClusterReceptionist.HeartbeatRsp.Instance, "D", "heartbeat-response"),
        new V2PortCase(ClusterReceptionist.ReceptionistShutdown.Instance, "E", "receptionist-shutdown"),
        new V2PortCase(new ClientProtocol.Send("/user/worker", "payload", true), "AA", "send-local-affinity"),
        new V2PortCase(new ClientProtocol.Send("/user/worker", "payload", false), "AA", "send"),
        new V2PortCase(new ClientProtocol.SendToAll("/user/worker", "payload"), "AB", "send-to-all"),
        new V2PortCase(new ClientProtocol.Publish("topic", "payload"), "AC", "publish"),
        new V2PortCase(ClientProtocol.RefreshContactsTick.Instance, "AD", "refresh-contacts"),
        new V2PortCase(ClientProtocol.HeartbeatTick.Instance, "AE", "heartbeat-tick"),
        new V2PortCase(ClientProtocol.ReconnectTimeout.Instance, "AF", "reconnect-timeout"),
        new V2PortCase(new ClientProtocol.Send("/user/worker", null!, true), "AA", "send-null"),
        new V2PortCase(new ClientProtocol.SendToAll("/user/worker", null!), "AB", "send-to-all-null"),
        new V2PortCase(new ClientProtocol.Publish("topic", null!), "AC", "publish-null")
    };

    private static Config ClientConfig => ConfigurationFactory.ParseString("akka.cluster.client.use-legacy-serialization = off")
        .WithFallback(ClusterClientReceptionist.DefaultConfig());

    protected override Serializer CreateLegacy(ExtendedActorSystem system) => new ClusterClientMessageSerializer(system);

    protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new ClusterClientMessagePackSerializer(system);

    protected override IReadOnlyList<V2PortCase> Cases => Corpus;

    [Fact(DisplayName = "Should_ReadBothDirections_When_TheGlobalSwitchDiffersBetweenNodes")]
    public async Task Should_ReadBothDirections_When_TheGlobalSwitchDiffersBetweenNodes()
    {
        await using var pair = MixedBindingPair.Create("client-v2-mixed",
            ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(ClientConfig), ClientConfig);
        ClusterClientSettings.Create(pair.V2Node);
        ClusterClientSettings.Create(pair.LegacyNode);
        pair.AssertRoundTripsBothWays(Cases, 55, 15);
    }

    [Fact(DisplayName = "Should_PreserveBothLegacyPins_When_TheGlobalSwitchIsEnabled")]
    public async Task Should_PreserveBothLegacyPins_When_TheGlobalSwitchIsEnabled()
    {
        var enabled = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(ClientConfig);
        await using var pair = MixedBindingPair.Create("client-v2-pinned", enabled,
            MixedBindingPair.BindTo("akka-cluster-client", typeof(IClusterClientMessage), typeof(IClusterClientProtocolMessage))
                .WithFallback(enabled));
        ClusterClientSettings.Create(pair.V2Node);
        ClusterClientSettings.Create(pair.LegacyNode);
        pair.AssertRoundTripsBothWays(Cases, 55, 15);
    }

    [Theory(DisplayName = "Should_PreserveJsonProtocolBinding_When_LegacyClientSettingsAreUsed")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_PreserveJsonProtocolBinding_When_LegacyClientSettingsAreUsed(bool v2)
    {
        var system = ActorSystem.Create($"client-json-{v2}", ConfigurationFactory.ParseString(
            $"akka.actor.serialization-v2 = {(v2 ? "on" : "off")}").WithFallback(ClusterClientReceptionist.DefaultConfig()));
        InitializeLogger(system);
        try
        {
            // The shipped default removes only the public client protocol binding. Do this before
            // resolving message types, as a real client's settings construction does.
            ClusterClientSettings.Create(system);
            foreach (var sample in Cases)
            {
                var writer = system.Serialization.FindSerializerFor(sample.Message);
                if (sample.Message is IClusterClientProtocolMessage)
                    writer.Should().BeOfType<NewtonSoftJsonSerializer>();
                else
                    writer.Identifier.Should().Be(v2 ? 55 : 15);
            }

            // Removing a write binding must not remove either id's reader.
            foreach (var sample in Cases)
            {
                var legacy = new ClusterClientMessageSerializer((ExtendedActorSystem)system);
                var generated = new ClusterClientMessagePackSerializer((ExtendedActorSystem)system);
                system.Serialization.Deserialize(legacy.ToBinary(sample.Message), 15, sample.Manifest).Should().Be(sample.Message);
                system.Serialization.Deserialize(generated.ToBinary(sample.Message), 55, sample.Manifest).Should().Be(sample.Message);
            }
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_ReturnOriginalInstances_When_V2ReadsEmptyMessages")]
    public void Should_ReturnOriginalInstances_When_V2ReadsEmptyMessages()
    {
        var writer = CreateV2((ExtendedActorSystem)Sys);
        foreach (var sample in Cases.Where(c => c.Message is not ClusterReceptionist.Contacts &&
                                               c.Message is not ClientProtocol.Send &&
                                               c.Message is not ClientProtocol.SendToAll &&
                                               c.Message is not ClientProtocol.Publish))
            writer.Deserialize(new ReadOnlySequence<byte>(writer.ToBinary(sample.Message)), sample.Manifest).Should().BeSameAs(sample.Message);
    }

    [Fact(DisplayName = "Should_UseTheRegisteredPayloadSerializer_When_ClientEnvelopesContainUserMessages")]
    public async Task Should_UseTheRegisteredPayloadSerializer_When_ClientEnvelopesContainUserMessages()
    {
        var setup = ActorSystemSetup.Create(
            BootstrapSetup.Create().WithConfig(ClientConfig.WithFallback(AkkaSpecConfig)),
            SerializationSetup.Create(system => ImmutableHashSet.Create(SerializerDetails.Create(
                "client-user-payload", new ClientPayloadSerializer(system), ImmutableHashSet.Create(typeof(ClientPayload))))));
        var system = ActorSystem.Create("client-payload-boundary", setup);
        InitializeLogger(system);
        try
        {
            var payload = new ClientPayload("user-codec");
            var writer = new ClusterClientMessagePackSerializer((ExtendedActorSystem)system);
            object[] envelopes = [new ClientProtocol.Send("/user/worker", payload, true),
                new ClientProtocol.SendToAll("/user/worker", payload), new ClientProtocol.Publish("topic", payload)];
            foreach (var envelope in envelopes)
                writer.FromBinary(writer.ToBinary(envelope), writer.Manifest(envelope)).Should().Be(envelope);
            var payloadSerializer = (ClientPayloadSerializer)system.Serialization.FindSerializerFor(payload);
            payloadSerializer.WriteCount.Should().Be(3);
            payloadSerializer.ReadCount.Should().Be(3);
            system.Serialization.FindSerializerFor(payload).Identifier.Should().Be(55555);
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact(DisplayName = "Should_RejectUnknownInputs_When_TheClientProtocolIsNotMatched")]
    public void Should_RejectUnknownInputs_When_TheClientProtocolIsNotMatched()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        Action manifest = () => serializer.Manifest(new object());
        Action write = () => serializer.ToBinary(new object());
        Action read = () => serializer.Deserialize(new ReadOnlySequence<byte>(new byte[] { 0x80 }), "unknown");
        manifest.Should().Throw<ArgumentException>();
        write.Should().Throw<ArgumentException>();
        read.Should().Throw<SerializationException>();
    }

    private sealed record ClientPayload(string Value);

    private sealed class ClientPayloadSerializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public int WriteCount { get; private set; }
        public int ReadCount { get; private set; }
        public override int Identifier => 55555;
        public override string Manifest(object obj) => "p";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = Encoding.UTF8.GetBytes(((ClientPayload)obj).Value);
            writer.Write(bytes);
            WriteCount++;
            return bytes.Length;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            if (manifest != "p")
                throw new SerializationException($"Unexpected client test payload manifest [{manifest}]");
            ReadCount++;
            return new ClientPayload(Encoding.UTF8.GetString(bytes.ToArray()));
        }
    }
}
