// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Akka.Actor;
using Akka.Serialization;
using Akka.Serialization.V2;
using MessagePack;

namespace Akka.Remote.Serialization;

/// <summary>Generated actor-selection wrapper. Legacy id 6 retains its empty manifest and bytes.</summary>
internal sealed class MessageContainerMessagePackSerializer : SerializerV2
{
    internal const int LegacyIdentifier = 6;
    internal const int SerializerIdentifier = 46;
    internal const string SelectionManifest = "a";
    private readonly MessageContainerMessagePackCodec _codec;

    public MessageContainerMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new MessageContainerMessagePackCodec(system);
    }

    public override int Identifier => SerializerIdentifier;

    public override string Manifest(object obj) => obj is ActorSelectionMessage
        ? SelectionManifest
        : throw new ArgumentException($"Unsupported actor-selection wrapper [{obj.GetType()}].", nameof(obj));

    public override byte[] ToBinary(object obj)
    {
        var writer = new ArrayBufferWriter<byte>();
        Serialize(obj, writer);
        return writer.WrittenSpan.ToArray();
    }

    public override int Serialize(object obj, IBufferWriter<byte> writer)
    {
        if (obj is not ActorSelectionMessage selection)
            throw new ArgumentException($"Unsupported actor-selection wrapper [{obj.GetType()}].", nameof(obj));
        // Validate every path element before writing anything, including the nested payload.
        var elements = selection.Elements.Select(ToWire).ToArray();
        var wire = new ActorSelectionWire(selection.Message, elements, selection.WildCardFanOut);
        var previous = Akka.Serialization.Serialization.CurrentTransportInformation;
        try
        {
            if (previous is null)
                Akka.Serialization.Serialization.CurrentTransportInformation = system.Provider.SerializationInformation;
            return _codec.Serialize(wire, writer);
        }
        finally
        {
            Akka.Serialization.Serialization.CurrentTransportInformation = previous;
        }
    }

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
    {
        if (manifest != SelectionManifest)
            throw new SerializationException($"Unsupported actor-selection manifest [{manifest}] for serializer [{Identifier}].");
        try
        {
            var wire = (ActorSelectionWire)_codec.Deserialize(bytes, manifest);
            return new ActorSelectionMessage(wire.Payload, wire.Elements.Select(FromWire).ToArray(), wire.WildCardFanOut);
        }
        catch (Exception error)
        {
            throw new SerializationException(
                $"Failed to deserialize {nameof(ActorSelectionMessage)} with serializer [{Identifier}], " +
                $"manifest [{manifest}], addressed to [{TryReadSelectionLaneKey(bytes) ?? "unknown"}].", error);
        }
    }

    private static SelectionElementWire ToWire(SelectionPathElement element) => element switch
    {
        SelectChildName name => new SelectionElementWire(0, name.Name),
        SelectChildPattern pattern => new SelectionElementWire(1, pattern.PatternStr),
        SelectParent => new SelectionElementWire(2, null),
        _ => throw new ArgumentException($"Unsupported selection path element [{element?.GetType()}].", nameof(element))
    };

    private static SelectionPathElement FromWire(SelectionElementWire element) => element.Kind switch
    {
        0 when element.Matcher is not null => new SelectChildName(element.Matcher),
        1 when element.Matcher is not null => new SelectChildPattern(element.Matcher),
        2 => new SelectParent(),
        _ => throw new SerializationException($"Unsupported selection path discriminator [{element.Kind}].")
    };

    /// <summary>
    /// Reads only the outer path metadata for Artery lane routing. The envelope payload is skipped
    /// as MessagePack data; its serializer is never resolved or invoked here.
    /// </summary>
    internal static string? TryReadSelectionLaneKey(in ReadOnlySequence<byte> bytes)
    {
        try
        {
            var reader = new MessagePackReader(bytes);
            var fields = reader.ReadMapHeader();
            string? key = null;
            for (var field = 0; field < fields; field++)
            {
                if (reader.ReadInt32() != 2)
                {
                    reader.Skip();
                    continue;
                }

                var count = reader.ReadArrayHeader();
                if (count == 0)
                    return null;
                var builder = new StringBuilder();
                for (var index = 0; index < count; index++)
                {
                    var elementFields = reader.ReadMapHeader();
                    var kind = -1;
                    string? matcher = null;
                    for (var elementField = 0; elementField < elementFields; elementField++)
                    {
                        switch (reader.ReadInt32())
                        {
                            case 1:
                                kind = reader.ReadInt32();
                                break;
                            case 2:
                                matcher = reader.ReadString();
                                break;
                            default:
                                reader.Skip();
                                break;
                        }
                    }

                    if (kind is < 0 or > 2 || (kind != 2 && matcher is null))
                        return null;
                    // Match legacy SelectionEnvelope's matcher-based key, including an empty
                    // matcher for SelectParent, so both wire formats choose the same lane.
                    builder.Append('/').Append(kind == 2 ? string.Empty : matcher);
                }
                key = builder.ToString();
            }
            return reader.End ? key : null;
        }
        catch (Exception)
        {
            // Invalid outer metadata takes the existing safe recipient-anchor fallback. No
            // application payload deserialization occurs on this routing path.
            return null;
        }
    }
}

internal interface IActorSelectionWire
{
}

[AkkaSerializer<IActorSelectionWire>("akka-containers-codec", MessageContainerMessagePackSerializer.SerializerIdentifier)]
internal sealed partial class MessageContainerMessagePackCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

// The new id has a nonempty native manifest. No token or bytes change under legacy id 6.
[AkkaSerializable(Manifest = MessageContainerMessagePackSerializer.SelectionManifest)]
internal sealed record ActorSelectionWire([property: AkkaField(1)] object Payload,
    [property: AkkaField(2)] SelectionElementWire[] Elements,
    [property: AkkaField(3)] bool WildCardFanOut) : IActorSelectionWire;

[AkkaSerializable]
internal sealed record SelectionElementWire([property: AkkaField(1)] int Kind,
    [property: AkkaField(2)] string? Matcher);
