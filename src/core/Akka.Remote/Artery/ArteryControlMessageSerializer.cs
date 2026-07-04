//-----------------------------------------------------------------------
// <copyright file="ArteryControlMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Runtime.Serialization;
using Akka.Actor;
using MessagePack;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Hand-rolled V2 MessagePack serializer for the Artery control/handshake messages
    /// (<see cref="HandshakeReq"/> / <see cref="HandshakeRsp"/>).
    ///
    /// <para>
    /// Design.md ("Handshake + association/UID (gate G2)") pins handshake message encoding to
    /// V2 source-generated MessagePack, with an explicit fallback: "a hand-written
    /// <c>MessagePackSerializer&lt;T&gt;</c> subclass (still V2, still msgpack) with a tracked
    /// follow-up to move onto the generator." That fallback is what this class is — see the
    /// task report for why: the <c>Akka.Serialization.V2</c> source generator requires every
    /// nested field type to carry <c>[AkkaSerializable]</c> (enforced by
    /// <c>AkkaSerializerGenerator.MissingNestedSerializableDefinition</c>), and
    /// <see cref="Address"/> is a core <c>Akka.Actor</c> type this change may not annotate.
    /// </para>
    /// <para>
    /// This class still writes/reads plain MessagePack (map-with-field-ids, forward-compatible
    /// via unknown-field skipping) and still derives from the V2
    /// <see cref="Akka.Serialization.V2.MessagePackSerializer{TProtocol}"/> base (itself a
    /// <see cref="Akka.Serialization.SerializerV2"/>) purely to reuse its buffer-size and
    /// bytes-written helpers — no source-generated code is involved.
    /// </para>
    /// <para>
    /// Follow-up (tracked): once <see cref="Address"/> can be expressed as a generator-visible
    /// nested type (or the generator grows an escape hatch for foreign types), replace this class
    /// with a generated one and delete it.
    /// </para>
    /// </summary>
    internal sealed class ArteryControlMessageSerializer : Akka.Serialization.V2.MessagePackSerializer<IArteryControlMessage>
    {
        /// <summary>
        /// The manifest for <see cref="HandshakeReq"/>.
        /// </summary>
        public const string HandshakeReqManifest = "HSReq";

        /// <summary>
        /// The manifest for <see cref="HandshakeRsp"/>.
        /// </summary>
        public const string HandshakeRspManifest = "HSRsp";

        private const int FromFieldId = 1;
        private const int ToFieldId = 2;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryControlMessageSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system that owns this serializer.</param>
        public ArteryControlMessageSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        /// <summary>
        /// See HOCON registration fragment in the task report — this identifier (23) is unused
        /// in the reserved 0-40 Akka-internal range as of this change (verified against
        /// <c>Remote.conf</c>, <c>Cluster.conf</c>, <c>persistence.conf</c>, and the contrib
        /// cluster-tools / distributed-data / streams reference.conf files; 17, which Pekko uses
        /// for its <c>ArteryMessageSerializer</c>, is already taken in Akka.NET by
        /// <c>Akka.Remote.Serialization.PrimitiveSerializers</c>).
        /// </summary>
        public override int Identifier => 23;

        /// <inheritdoc/>
        public override string Manifest(object obj) => obj switch
        {
            HandshakeReq => HandshakeReqManifest,
            HandshakeRsp => HandshakeRspManifest,
            _ => throw new ArgumentException($"Unsupported Artery control message type: {obj.GetType()}", nameof(obj))
        };

        /// <inheritdoc/>
        public override int SizeHint(object obj) => obj switch
        {
            HandshakeReq req => SizeOfReq(req),
            HandshakeRsp rsp => SizeOfRsp(rsp),
            _ => UnknownSize
        };

        /// <inheritdoc/>
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var counting = new CountingBufferWriter(writer);
            var messagePackWriter = new MessagePackWriter(counting);

            switch (obj)
            {
                case HandshakeReq req:
                    WriteReq(ref messagePackWriter, req);
                    break;
                case HandshakeRsp rsp:
                    WriteRsp(ref messagePackWriter, rsp);
                    break;
                default:
                    throw new ArgumentException($"Unsupported Artery control message type: {obj.GetType()}", nameof(obj));
            }

            messagePackWriter.Flush();
            return checked((int)counting.BytesWritten);
        }

        /// <inheritdoc/>
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            var reader = new MessagePackReader(bytes);
            return manifest switch
            {
                HandshakeReqManifest => ReadReq(ref reader),
                HandshakeRspManifest => ReadRsp(ref reader),
                _ => throw new SerializationException($"Unknown Artery control message manifest [{manifest}].")
            };
        }

        private static void WriteReq(ref MessagePackWriter writer, HandshakeReq req)
        {
            writer.WriteMapHeader(2);
            writer.Write(FromFieldId);
            WriteUniqueAddress(ref writer, req.From);
            writer.Write(ToFieldId);
            WriteAddress(ref writer, req.To);
        }

        private static HandshakeReq ReadReq(ref MessagePackReader reader)
        {
            var fieldCount = reader.ReadMapHeader();
            UniqueAddress? from = null;
            Address? to = null;

            for (var i = 0; i < fieldCount; i++)
            {
                var fieldId = reader.ReadInt32();
                switch (fieldId)
                {
                    case FromFieldId:
                        from = ReadUniqueAddress(ref reader);
                        break;
                    case ToFieldId:
                        to = ReadAddress(ref reader);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (from is null)
                throw new SerializationException($"Missing required field [From] with index [{FromFieldId}] for [{HandshakeReqManifest}].");
            if (to is null)
                throw new SerializationException($"Missing required field [To] with index [{ToFieldId}] for [{HandshakeReqManifest}].");

            return new HandshakeReq(from.Value, to);
        }

        private static void WriteRsp(ref MessagePackWriter writer, HandshakeRsp rsp)
        {
            writer.WriteMapHeader(1);
            writer.Write(FromFieldId);
            WriteUniqueAddress(ref writer, rsp.From);
        }

        private static HandshakeRsp ReadRsp(ref MessagePackReader reader)
        {
            var fieldCount = reader.ReadMapHeader();
            UniqueAddress? from = null;

            for (var i = 0; i < fieldCount; i++)
            {
                var fieldId = reader.ReadInt32();
                switch (fieldId)
                {
                    case FromFieldId:
                        from = ReadUniqueAddress(ref reader);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (from is null)
                throw new SerializationException($"Missing required field [From] with index [{FromFieldId}] for [{HandshakeRspManifest}].");

            return new HandshakeRsp(from.Value);
        }

        private static void WriteAddress(ref MessagePackWriter writer, Address address)
        {
            writer.WriteArrayHeader(4);
            writer.Write(address.Protocol);
            writer.Write(address.System);

            if (address.Host is { } host)
                writer.Write(host);
            else
                writer.WriteNil();

            if (address.Port is { } port)
                writer.Write(port);
            else
                writer.WriteNil();
        }

        private static Address ReadAddress(ref MessagePackReader reader)
        {
            var length = reader.ReadArrayHeader();
            if (length != 4)
                throw new SerializationException($"Expected a 4-element address array, got {length}.");

            var protocol = reader.ReadString() ?? throw new SerializationException("Missing address protocol.");
            var system = reader.ReadString() ?? throw new SerializationException("Missing address system name.");
            var host = reader.TryReadNil() ? null : reader.ReadString();
            var port = reader.TryReadNil() ? (int?)null : reader.ReadInt32();

            return new Address(protocol, system, host, port);
        }

        private static void WriteUniqueAddress(ref MessagePackWriter writer, UniqueAddress uniqueAddress)
        {
            writer.WriteArrayHeader(2);
            WriteAddress(ref writer, uniqueAddress.Address);
            writer.Write(uniqueAddress.Uid);
        }

        private static UniqueAddress ReadUniqueAddress(ref MessagePackReader reader)
        {
            var length = reader.ReadArrayHeader();
            if (length != 2)
                throw new SerializationException($"Expected a 2-element unique-address array, got {length}.");

            var address = ReadAddress(ref reader);
            var uid = reader.ReadInt64();
            return new UniqueAddress(address, uid);
        }

        private static int SizeOfAddress(Address address) =>
            SizeOfArrayHeader(4) +
            SizeOfString(address.Protocol) +
            SizeOfString(address.System) +
            (address.Host is { } host ? SizeOfString(host) : SizeOfNil()) +
            (address.Port is { } port ? SizeOfInt32(port) : SizeOfNil());

        private static int SizeOfUniqueAddress(UniqueAddress uniqueAddress) =>
            SizeOfArrayHeader(2) + SizeOfAddress(uniqueAddress.Address) + SizeOfInt64(uniqueAddress.Uid);

        private static int SizeOfReq(HandshakeReq req) =>
            SizeOfMapHeader(2) +
            SizeOfInt32(FromFieldId) + SizeOfUniqueAddress(req.From) +
            SizeOfInt32(ToFieldId) + SizeOfAddress(req.To);

        private static int SizeOfRsp(HandshakeRsp rsp) =>
            SizeOfMapHeader(1) +
            SizeOfInt32(FromFieldId) + SizeOfUniqueAddress(rsp.From);

        /// <summary>
        /// Counts bytes advanced through an inner <see cref="IBufferWriter{T}"/> so
        /// <see cref="Serialize"/> can report the bytes-written contract required by
        /// <see cref="Akka.Serialization.SerializerV2"/>, mirroring the counting wrapper the V2
        /// MessagePack source generator emits for the same purpose
        /// (<c>AkkaGeneratedCountingBufferWriter</c> in
        /// <c>Akka.Serialization.V2.Generators.AkkaSerializerGenerator</c>).
        /// </summary>
        private sealed class CountingBufferWriter : IBufferWriter<byte>
        {
            private readonly IBufferWriter<byte> _inner;

            public CountingBufferWriter(IBufferWriter<byte> inner)
            {
                _inner = inner;
            }

            public long BytesWritten { get; private set; }

            public void Advance(int count)
            {
                _inner.Advance(count);
                BytesWritten += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0) => _inner.GetMemory(sizeHint);

            public Span<byte> GetSpan(int sizeHint = 0) => _inner.GetSpan(sizeHint);
        }
    }
}
