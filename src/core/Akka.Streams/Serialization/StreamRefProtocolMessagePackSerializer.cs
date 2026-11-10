// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Buffers;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Serialization;
using Akka.Serialization.V2;
using Akka.Streams.Implementation.StreamRef;
using Akka.Util;

namespace Akka.Streams.Serialization;

/// <summary>Generated stream-ref messages. Bare generic reconstruction requires dynamic type loading.</summary>
internal sealed class StreamRefProtocolMessagePackSerializer : SerializerV2
{
    private readonly StreamRefProtocolMessagePackCodec _codec;

    public StreamRefProtocolMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new StreamRefProtocolMessagePackCodec(system);
    }

    public override int Identifier => 70;

    public override string Manifest(object obj) => obj switch
    {
        SequencedOnNext => "A",
        CumulativeDemand => "B",
        RemoteStreamFailure => "C",
        RemoteStreamCompleted => "D",
        SourceRefImpl => BareRefManifest("E"),
        SinkRefImpl => BareRefManifest("F"),
        OnSubscribeHandshake => "G",
        _ => throw Unsupported(obj)
    };

    public override int SizeHint(object obj)
    {
        _ = Manifest(obj);
        return UnknownSize;
    }

    public override byte[] ToBinary(object obj)
    {
        var writer = new ArrayBufferWriter<byte>();
        Serialize(obj, writer);
        return writer.WrittenSpan.ToArray();
    }

    // Like the legacy protocol, use the caller's transport context without installing one for
    // direct byte-array calls. Remoting supplies its address; local fixture paths remain stable.
    public override int Serialize(object obj, IBufferWriter<byte> writer) => _codec.Serialize(ToWire(obj), writer);

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
    {
        if (manifest is "E" or "F")
            RequireDynamicTypeLoading();

        return _codec.Deserialize(bytes, manifest) switch
        {
            StreamNextWire message => new SequencedOnNext(message.Sequence, message.Payload),
            StreamDemandWire message => new CumulativeDemand(message.Sequence),
            StreamFailureWire message => new RemoteStreamFailure(message.Message),
            StreamCompletedWire message => new RemoteStreamCompleted(message.Sequence),
            StreamSourceRefWire message => SerializationTools.ToSourceRefImpl(system, message.ElementType, message.PartnerPath),
            StreamSinkRefWire message => SerializationTools.ToSinkRefImpl(system, message.ElementType, message.PartnerPath),
            StreamHandshakeWire message => new OnSubscribeHandshake(message.Target ?? system.Provider.ResolveActorRef(string.Empty)),
            var wire => throw new SerializationException($"Unknown stream-ref wire message [{wire.GetType()}].")
        };
    }

    private static IStreamProtocolWire ToWire(object obj) => obj switch
    {
        SequencedOnNext message => new StreamNextWire(message.SeqNr, message.Payload),
        CumulativeDemand message => new StreamDemandWire(message.SeqNr),
        RemoteStreamFailure message => new StreamFailureWire(message.Message),
        RemoteStreamCompleted message => new StreamCompletedWire(message.SeqNr),
        SourceRefImpl reference => new StreamSourceRefWire(BareRefElementType(reference.EventType),
            Akka.Serialization.Serialization.SerializedActorPath(reference.InitialPartnerRef)),
        SinkRefImpl reference => new StreamSinkRefWire(BareRefElementType(reference.EventType),
            Akka.Serialization.Serialization.SerializedActorPath(reference.InitialPartnerRef)),
        OnSubscribeHandshake message => new StreamHandshakeWire(message.TargetRef),
        _ => throw Unsupported(obj)
    };

    private static string BareRefManifest(string manifest)
    {
        RequireDynamicTypeLoading();
        return manifest;
    }

    private static string BareRefElementType(Type elementType)
    {
        RequireDynamicTypeLoading();
        return elementType.TypeQualifiedName();
    }

    private static void RequireDynamicTypeLoading()
    {
        if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
            throw new SerializationException(SerializationTools.StreamRefTypeNotSupported("runtime element type"));
    }

    private static ArgumentException Unsupported(object obj) => new(
        $"Unsupported stream-ref message [{obj.GetType()}].", nameof(obj));
}

internal interface IStreamProtocolWire;

[AkkaSerializer<IStreamProtocolWire>("akka-stream-ref-v2-codec", 70)]
internal sealed partial class StreamRefProtocolMessagePackCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

[AkkaSerializable(Manifest = "A")]
internal sealed record StreamNextWire([property: AkkaField(1)] long Sequence,
    [property: AkkaField(2)] object Payload) : IStreamProtocolWire;

[AkkaSerializable(Manifest = "B")]
internal sealed record StreamDemandWire([property: AkkaField(1)] long Sequence) : IStreamProtocolWire;

[AkkaSerializable(Manifest = "C")]
internal sealed record StreamFailureWire([property: AkkaField(1)] string Message) : IStreamProtocolWire;

[AkkaSerializable(Manifest = "D")]
internal sealed record StreamCompletedWire([property: AkkaField(1)] long Sequence) : IStreamProtocolWire;

[AkkaSerializable(Manifest = "E")]
internal sealed record StreamSourceRefWire([property: AkkaField(1)] string ElementType,
    [property: AkkaField(2)] string PartnerPath) : IStreamProtocolWire;

[AkkaSerializable(Manifest = "F")]
internal sealed record StreamSinkRefWire([property: AkkaField(1)] string ElementType,
    [property: AkkaField(2)] string PartnerPath) : IStreamProtocolWire;

[AkkaSerializable(Manifest = "G")]
internal sealed record StreamHandshakeWire([property: AkkaField(1)] IActorRef? Target) : IStreamProtocolWire;
