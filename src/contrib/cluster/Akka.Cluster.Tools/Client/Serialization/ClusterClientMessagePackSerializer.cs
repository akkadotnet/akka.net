//-----------------------------------------------------------------------
// <copyright file="ClusterClientMessagePackSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Serialization;
using Akka.Serialization.V2;

namespace Akka.Cluster.Tools.Client.Serialization;

/// <summary>
/// Generated Cluster Client codec. Both id 15 and id 55 readers stay registered. The global
/// V2 switch redirects the module's default bindings; ClusterClientSettings still honors
/// use-legacy-serialization by removing the public client-protocol binding when requested.
/// User payloads keep their independently registered serializers through object envelopes.
/// </summary>
internal sealed class ClusterClientMessagePackSerializer : SerializerV2
{
    private readonly ClusterClientMessagePackCodec _codec;

    public ClusterClientMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new ClusterClientMessagePackCodec(system);
    }

    public override int Identifier => 55;

    public override string Manifest(object obj) => obj switch
    {
        ClusterReceptionist.Contacts => "A",
        ClusterReceptionist.GetContacts => "B",
        ClusterReceptionist.Heartbeat => "C",
        ClusterReceptionist.HeartbeatRsp => "D",
        ClusterReceptionist.ReceptionistShutdown => "E",
        ClusterClient.Send => "AA",
        ClusterClient.SendToAll => "AB",
        ClusterClient.Publish => "AC",
        ClusterClient.RefreshContactsTick => "AD",
        ClusterClient.HeartbeatTick => "AE",
        ClusterClient.ReconnectTimeout => "AF",
        _ => throw UnknownMessage(obj)
    };

    public override int Serialize(object obj, IBufferWriter<byte> writer) => _codec.Serialize(ToWire(obj), writer);

    public override int SizeHint(object obj) => _codec.SizeHint(ToWire(obj));

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => _codec.Deserialize(bytes, manifest) switch
    {
        ClientContactsWire message => new ClusterReceptionist.Contacts(message.ContactPoints),
        ClientGetContactsWire => ClusterReceptionist.GetContacts.Instance,
        ClientHeartbeatWire => ClusterReceptionist.Heartbeat.Instance,
        ClientHeartbeatResponseWire => ClusterReceptionist.HeartbeatRsp.Instance,
        ClientReceptionistShutdownWire => ClusterReceptionist.ReceptionistShutdown.Instance,
        ClientSendWire message => new ClusterClient.Send(message.Path, message.Payload, message.LocalAffinity),
        ClientSendToAllWire message => new ClusterClient.SendToAll(message.Path, message.Payload),
        ClientPublishWire message => new ClusterClient.Publish(message.Topic, message.Payload),
        ClientRefreshContactsWire => ClusterClient.RefreshContactsTick.Instance,
        ClientHeartbeatTickWire => ClusterClient.HeartbeatTick.Instance,
        ClientReconnectTimeoutWire => ClusterClient.ReconnectTimeout.Instance,
        var wire => throw new SerializationException($"Unknown Cluster Client wire message [{wire.GetType()}]")
    };

    private static IClusterClientWireMessage ToWire(object obj) => obj switch
    {
        ClusterReceptionist.Contacts message => new ClientContactsWire(message.ContactPoints),
        ClusterReceptionist.GetContacts => ClientGetContactsWire.Instance,
        ClusterReceptionist.Heartbeat => ClientHeartbeatWire.Instance,
        ClusterReceptionist.HeartbeatRsp => ClientHeartbeatResponseWire.Instance,
        ClusterReceptionist.ReceptionistShutdown => ClientReceptionistShutdownWire.Instance,
        ClusterClient.Send message => new ClientSendWire(message.Path, message.Message, message.LocalAffinity),
        ClusterClient.SendToAll message => new ClientSendToAllWire(message.Path, message.Message),
        ClusterClient.Publish message => new ClientPublishWire(message.Topic, message.Message),
        ClusterClient.RefreshContactsTick => ClientRefreshContactsWire.Instance,
        ClusterClient.HeartbeatTick => ClientHeartbeatTickWire.Instance,
        ClusterClient.ReconnectTimeout => ClientReconnectTimeoutWire.Instance,
        _ => throw UnknownMessage(obj)
    };

    private static ArgumentException UnknownMessage(object message) => new(
        $"Cannot serialize object of type [{message.GetType()}] in [{typeof(ClusterClientMessagePackSerializer)}]", nameof(message));
}

internal interface IClusterClientWireMessage
{
}

[AkkaSerializer<IClusterClientWireMessage>("akka-cluster-client-v2-codec", 55)]
internal sealed partial class ClusterClientMessagePackCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

// Field ids and manifest tokens are permanent once shipped. Contacts retain their list order;
// the three payload fields use the existing (serializer id, manifest, bytes) envelope support.
[AkkaSerializable(Manifest = "A")]
internal sealed record ClientContactsWire(
    [property: AkkaField(1)] ImmutableList<string> ContactPoints) : IClusterClientWireMessage;

[AkkaSerializable(Manifest = "B", AllowEmpty = true)]
internal sealed record ClientGetContactsWire : IClusterClientWireMessage
{
    internal static readonly ClientGetContactsWire Instance = new();
}

[AkkaSerializable(Manifest = "C", AllowEmpty = true)]
internal sealed record ClientHeartbeatWire : IClusterClientWireMessage
{
    internal static readonly ClientHeartbeatWire Instance = new();
}

[AkkaSerializable(Manifest = "D", AllowEmpty = true)]
internal sealed record ClientHeartbeatResponseWire : IClusterClientWireMessage
{
    internal static readonly ClientHeartbeatResponseWire Instance = new();
}

[AkkaSerializable(Manifest = "E", AllowEmpty = true)]
internal sealed record ClientReceptionistShutdownWire : IClusterClientWireMessage
{
    internal static readonly ClientReceptionistShutdownWire Instance = new();
}

[AkkaSerializable(Manifest = "AA")]
internal sealed record ClientSendWire(
    [property: AkkaField(1)] string Path,
    [property: AkkaField(2)] object? Payload,
    [property: AkkaField(3)] bool LocalAffinity) : IClusterClientWireMessage;

[AkkaSerializable(Manifest = "AB")]
internal sealed record ClientSendToAllWire(
    [property: AkkaField(1)] string Path,
    [property: AkkaField(2)] object? Payload) : IClusterClientWireMessage;

[AkkaSerializable(Manifest = "AC")]
internal sealed record ClientPublishWire(
    [property: AkkaField(1)] string Topic,
    [property: AkkaField(2)] object? Payload) : IClusterClientWireMessage;

[AkkaSerializable(Manifest = "AD", AllowEmpty = true)]
internal sealed record ClientRefreshContactsWire : IClusterClientWireMessage
{
    internal static readonly ClientRefreshContactsWire Instance = new();
}

[AkkaSerializable(Manifest = "AE", AllowEmpty = true)]
internal sealed record ClientHeartbeatTickWire : IClusterClientWireMessage
{
    internal static readonly ClientHeartbeatTickWire Instance = new();
}

[AkkaSerializable(Manifest = "AF", AllowEmpty = true)]
internal sealed record ClientReconnectTimeoutWire : IClusterClientWireMessage
{
    internal static readonly ClientReconnectTimeoutWire Instance = new();
}
