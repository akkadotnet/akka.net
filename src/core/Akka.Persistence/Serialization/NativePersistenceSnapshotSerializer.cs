//-----------------------------------------------------------------------
// <copyright file="NativePersistenceSnapshotSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Persistence.Serialization.Proto.Msg;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.Persistence.Serialization;

/// <summary>
/// Native buffer implementation of the existing snapshot envelope: same id, alias, manifest
/// and protobuf bytes. The public legacy serializer remains available with its original API.
/// Snapshot stores that recover by type binding continue to read their existing records.
/// </summary>
internal sealed class NativePersistenceSnapshotSerializer : SerializerV2
{
    private static readonly Dictionary<string, Type> ManifestTypes = TypeExtensions.ManifestTable(typeof(Snapshot));

    public NativePersistenceSnapshotSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    public override int Identifier => 8;

    public override string Manifest(object obj) => obj is Snapshot
        ? typeof(Snapshot).TypeQualifiedName()
        : throw UnexpectedMessage(obj);

    // Preserve protobuf's exact-sized byte[] path for callers such as LocalSnapshotStore.
    // Native transports instead call Serialize, avoiding the V1 adapter's outer byte[] and copy.
    public override byte[] ToBinary(object obj) => GetPersistentPayload(RequireSnapshot(obj)).ToByteArray();

    public override int Serialize(object obj, IBufferWriter<byte> writer)
    {
        var payload = GetPersistentPayload(RequireSnapshot(obj));
        var size = payload.CalculateSize();
        payload.WriteTo(writer);
        return size;
    }

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
    {
        if (!ManifestTypes.TryResolveManifestType(manifest, out _))
            throw new SerializationException($"Unknown snapshot envelope manifest [{manifest}] for serializer [{Identifier}].");

        var payload = PersistentPayload.Parser.ParseFrom(bytes);
        return new Snapshot(system.Serialization.Deserialize(new ReadOnlySequence<byte>(payload.Payload.Memory),
            payload.SerializerId, payload.PayloadManifest.ToStringUtf8()));
    }

    private PersistentPayload GetPersistentPayload(Snapshot snapshot)
    {
        var oldInfo = AkkaSerialization.CurrentTransportInformation;
        try
        {
            if (oldInfo is null)
                AkkaSerialization.CurrentTransportInformation = system.Provider.SerializationInformation;

            var serializer = system.Serialization.FindSerializerFor(snapshot.Data);
            var manifest = AkkaSerialization.ManifestFor(serializer, snapshot.Data);
            var payload = new PersistentPayload();
            if (!string.IsNullOrEmpty(manifest))
                payload.PayloadManifest = ByteString.CopyFromUtf8(manifest);
            payload.Payload = ByteString.CopyFrom(serializer.ToBinary(snapshot.Data));
            payload.SerializerId = serializer.Identifier;
            return payload;
        }
        finally
        {
            AkkaSerialization.CurrentTransportInformation = oldInfo;
        }
    }

    private static Snapshot RequireSnapshot(object obj) => obj as Snapshot ?? throw UnexpectedMessage(obj);

    private static ArgumentException UnexpectedMessage(object obj) => new(
        $"Cannot serialize object of type [{obj.GetType()}] in [{typeof(NativePersistenceSnapshotSerializer)}]", nameof(obj));
}
