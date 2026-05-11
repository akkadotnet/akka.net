//-----------------------------------------------------------------------
// <copyright file="V2ProtoSpike.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

// V2 wrap-pipeline spike.
//
// Reuses the existing Akka.Remote Protobuf message types unchanged:
//   AckAndEnvelopeContainer → RemoteEnvelope → Payload → user bytes
//
// What's different from V1 (today's MessageSerializer + AkkaPduCodec):
//   1. We hand-write the Protobuf wire format directly into an IBufferWriter<byte>
//      instead of constructing the proto objects and calling .ToByteString().
//   2. Length prefixes for nested length-delimited fields use FIXED-WIDTH 5-byte
//      varints (over-long but spec-compliant — Google.Protobuf's CodedInputStream
//      accepts them). This lets us write a placeholder, then run the inner serializer,
//      then PATCH the length prefix once we know the byte count — all in one pass,
//      no scratch buffers, no intermediate byte[] allocations.
//   3. Inner-payload dispatch is via SerializerRegistry (Type→V2 lookup). No
//      Type.GetType(manifest) reflection on the receive side — we look up by
//      serializer ID, which is the integer baked into the wire format.
//
// The V2 inner serializer writes its bytes directly into the same IBufferWriter,
// honoring SerializerV2.Serialize(IBufferWriter<byte>, object) → int.
//
// The wire output is byte-for-byte parseable by the existing
// AckAndEnvelopeContainer.Parser.ParseFrom — the spike includes a round-trip test
// to confirm wire compatibility.

#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using Akka.Actor;
using Akka.Serialization;
using Google.Protobuf;

namespace Akka.Benchmarks.Serialization
{
    // ─── PatchingBufferWriter — IBufferWriter<byte> with patch capability ─────

    /// <summary>
    /// Minimal <see cref="IBufferWriter{T}"/> implementation that also exposes a mutable
    /// span over already-written bytes for length-prefix patching. Reuses a growable byte
    /// array across <see cref="Reset"/> calls.
    /// </summary>
    public sealed class PatchingBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer;
        private int _written;

        public PatchingBufferWriter(int initialCapacity = 256)
        {
            _buffer = new byte[Math.Max(initialCapacity, 16)];
        }

        public int WrittenCount => _written;
        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);
        public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

        public void Reset() => _written = 0;

        public void Advance(int count) => _written += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_written);
        }

        /// <summary>Returns a mutable span over already-written bytes for length-prefix patching.</summary>
        public Span<byte> PatchSpan(int offset, int length) => _buffer.AsSpan(offset, length);

        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint <= 0) sizeHint = 1;
            var needed = _written + sizeHint;
            if (needed <= _buffer.Length) return;

            var newCapacity = _buffer.Length;
            while (newCapacity < needed) newCapacity *= 2;

            var newBuffer = new byte[newCapacity];
            Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _written);
            _buffer = newBuffer;
        }
    }

    // ─── ProtoWire — Protobuf wire-format primitives ──────────────────────────

    /// <summary>
    /// Hand-rolled Protobuf wire-format helpers. Mirror what
    /// <see cref="Google.Protobuf.CodedOutputStream"/> does internally, but operate against
    /// an <see cref="IBufferWriter{T}"/> directly (no Stream-wrapper indirection, no
    /// CodedOutputStream buffering).
    ///
    /// Wire-format reference: https://protobuf.dev/programming-guides/encoding/
    /// </summary>
    internal static class ProtoWire
    {
        public const int FixedWidthVarintBytes = 5;

        // Wire types from the Protobuf spec.
        public const byte WireTypeVarint = 0;
        public const byte WireTypeFixed64 = 1;
        public const byte WireTypeLengthDelimited = 2;
        public const byte WireTypeFixed32 = 5;

        /// <summary>
        /// Writes a single-byte field tag (field number ≤ 15 fits in 1 byte).
        /// Tag = (field_number << 3) | wire_type, varint-encoded. For field numbers 1–15
        /// the tag fits in one byte.
        /// </summary>
        public static int WriteTag(IBufferWriter<byte> buffer, int fieldNumber, byte wireType)
        {
            var tag = (uint)((fieldNumber << 3) | wireType);
            return WriteVarint32(buffer, tag);
        }

        /// <summary>Writes a varint using the minimum number of bytes (1–5 for uint32).</summary>
        public static int WriteVarint32(IBufferWriter<byte> buffer, uint value)
        {
            var span = buffer.GetSpan(5);
            var written = 0;
            while (value >= 0x80)
            {
                span[written++] = (byte)(value | 0x80);
                value >>= 7;
            }
            span[written++] = (byte)value;
            buffer.Advance(written);
            return written;
        }

        /// <summary>
        /// Reserves a fixed 5-byte varint placeholder in the buffer. The placeholder is a
        /// valid (over-long but spec-compliant) varint encoding of 0; it will be patched
        /// with the actual length once the nested write completes.
        /// </summary>
        public static int ReserveFixedWidthVarint(IBufferWriter<byte> buffer)
        {
            var span = buffer.GetSpan(FixedWidthVarintBytes);
            span[0] = 0x80;
            span[1] = 0x80;
            span[2] = 0x80;
            span[3] = 0x80;
            span[4] = 0x00;
            buffer.Advance(FixedWidthVarintBytes);
            return FixedWidthVarintBytes;
        }

        /// <summary>
        /// Patches a previously-reserved 5-byte varint placeholder with the given value.
        /// Always writes 5 bytes (over-long encoding for small values), which any conformant
        /// Protobuf parser accepts.
        /// </summary>
        public static void PatchFixedWidthVarint(Span<byte> placeholder, uint value)
        {
            // 5 bytes × 7 data bits = 35 bits — plenty for a uint32.
            placeholder[0] = (byte)((value & 0x7F) | 0x80);
            placeholder[1] = (byte)(((value >> 7) & 0x7F) | 0x80);
            placeholder[2] = (byte)(((value >> 14) & 0x7F) | 0x80);
            placeholder[3] = (byte)(((value >> 21) & 0x7F) | 0x80);
            placeholder[4] = (byte)((value >> 28) & 0x7F);
        }

        public static int WriteFixed64(IBufferWriter<byte> buffer, ulong value)
        {
            var span = buffer.GetSpan(sizeof(ulong));
            BinaryPrimitives.WriteUInt64LittleEndian(span, value);
            buffer.Advance(sizeof(ulong));
            return sizeof(ulong);
        }

        public static int WriteString(IBufferWriter<byte> buffer, string value)
        {
            // Length-delimited string: varint(byte-length) + utf8 bytes.
            var byteCount = Encoding.UTF8.GetByteCount(value);
            var lengthBytes = WriteVarint32(buffer, (uint)byteCount);
            var span = buffer.GetSpan(byteCount);
            var written = Encoding.UTF8.GetBytes(value.AsSpan(), span);
            buffer.Advance(written);
            return lengthBytes + written;
        }
    }

    // ─── V2 serializer registry (Type → SerializerV2, ID → SerializerV2) ──────

    /// <summary>
    /// Static-dispatch lookup for V2 inner serializers. No reflection at dispatch time —
    /// Type-keyed for write, integer-ID-keyed for read.
    /// </summary>
    public sealed class V2SerializerRegistry
    {
        private readonly ConcurrentDictionary<Type, SerializerV2> _byType = new();
        private readonly ConcurrentDictionary<int, SerializerV2> _byId = new();

        public void Register(SerializerV2 serializer, params Type[] types)
        {
            _byId[serializer.Identifier] = serializer;
            foreach (var t in types)
                _byType[t] = serializer;
        }

        public SerializerV2 FindFor(object obj) => _byType[obj.GetType()];
        public SerializerV2 GetById(int id) => _byId[id];
    }

    // ─── V2 envelope serializer — writes the full AckAndEnvelopeContainer ─────

    /// <summary>
    /// V2 wrap-pipeline entry point. Hand-writes the Protobuf wire format for
    /// AckAndEnvelopeContainer → RemoteEnvelope → Payload in a single pass over the
    /// IBufferWriter, with no intermediate byte[] allocations. The inner V2 serializer
    /// is invoked exactly once to write directly into the same buffer.
    /// </summary>
    public sealed class V2RemoteEnvelopeWriter
    {
        private readonly V2SerializerRegistry _registry;

        public V2RemoteEnvelopeWriter(V2SerializerRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>
        /// Writes the full AckAndEnvelopeContainer wire bytes for a single Send.
        /// Recipient and sender are passed as pre-serialized ActorRefData proto bytes —
        /// caller is expected to cache these across calls (V1 also constructs them per
        /// call, so the benchmark is on equal footing).
        /// </summary>
        /// <returns>Total bytes written.</returns>
        public int Serialize(
            PatchingBufferWriter buffer,
            ReadOnlySpan<byte> recipientProtoBytes,
            ReadOnlySpan<byte> senderProtoBytes,
            ulong seq,
            object payload)
        {
            var start = buffer.WrittenCount;
            var inner = _registry.FindFor(payload);

            // ─── AckAndEnvelopeContainer ──────────────────────────────────────
            //   field 2: envelope (RemoteEnvelope) — length-delimited
            //   (field 1 ack omitted — V1's ConstructMessage skips it for SendNoAck;
            //    benchmark V1 path uses ackOption=null so this matches.)
            ProtoWire.WriteTag(buffer, fieldNumber: 2, ProtoWire.WireTypeLengthDelimited);
            var envelopeLenOffset = buffer.WrittenCount;
            ProtoWire.ReserveFixedWidthVarint(buffer);
            var envelopeStart = buffer.WrittenCount;

            // ─── RemoteEnvelope ───────────────────────────────────────────────
            //   field 1: recipient (ActorRefData) — length-delimited
            //   field 2: message   (Payload)      — length-delimited
            //   field 4: sender    (ActorRefData) — length-delimited
            //   field 5: seq       (fixed64)
            ProtoWire.WriteTag(buffer, fieldNumber: 1, ProtoWire.WireTypeLengthDelimited);
            ProtoWire.WriteVarint32(buffer, (uint)recipientProtoBytes.Length);
            buffer.GetSpan(recipientProtoBytes.Length).Slice(0, recipientProtoBytes.Length);
            WriteRaw(buffer, recipientProtoBytes);

            ProtoWire.WriteTag(buffer, fieldNumber: 2, ProtoWire.WireTypeLengthDelimited);
            var payloadLenOffset = buffer.WrittenCount;
            ProtoWire.ReserveFixedWidthVarint(buffer);
            var payloadStart = buffer.WrittenCount;

            // ─── Payload ──────────────────────────────────────────────────────
            //   field 1: message       (bytes) — length-delimited
            //   field 2: serializerId  (int32)
            //   field 3: messageManifest (bytes) — length-delimited UTF-8
            ProtoWire.WriteTag(buffer, fieldNumber: 1, ProtoWire.WireTypeLengthDelimited);
            var messageLenOffset = buffer.WrittenCount;
            ProtoWire.ReserveFixedWidthVarint(buffer);

            // ─── INNER PAYLOAD — direct inline write, no intermediate byte[] ──
            var innerBytesWritten = inner.Serialize(buffer, payload);

            // Patch the message-length placeholder.
            ProtoWire.PatchFixedWidthVarint(
                buffer.PatchSpan(messageLenOffset, ProtoWire.FixedWidthVarintBytes),
                (uint)innerBytesWritten);

            // Payload field 2: serializerId (int32 varint)
            ProtoWire.WriteTag(buffer, fieldNumber: 2, ProtoWire.WireTypeVarint);
            ProtoWire.WriteVarint32(buffer, (uint)inner.Identifier);

            // Payload field 3: messageManifest (length-delimited UTF-8)
            var manifest = inner.Manifest(payload);
            if (!string.IsNullOrEmpty(manifest))
            {
                ProtoWire.WriteTag(buffer, fieldNumber: 3, ProtoWire.WireTypeLengthDelimited);
                ProtoWire.WriteString(buffer, manifest);
            }

            var payloadEnd = buffer.WrittenCount;
            // Patch the payload-length placeholder (Payload bytes inside RemoteEnvelope.message).
            ProtoWire.PatchFixedWidthVarint(
                buffer.PatchSpan(payloadLenOffset, ProtoWire.FixedWidthVarintBytes),
                (uint)(payloadEnd - payloadStart));

            // RemoteEnvelope field 4: sender
            if (!senderProtoBytes.IsEmpty)
            {
                ProtoWire.WriteTag(buffer, fieldNumber: 4, ProtoWire.WireTypeLengthDelimited);
                ProtoWire.WriteVarint32(buffer, (uint)senderProtoBytes.Length);
                WriteRaw(buffer, senderProtoBytes);
            }

            // RemoteEnvelope field 5: seq (fixed64)
            ProtoWire.WriteTag(buffer, fieldNumber: 5, ProtoWire.WireTypeFixed64);
            ProtoWire.WriteFixed64(buffer, seq);

            var envelopeEnd = buffer.WrittenCount;
            // Patch the envelope-length placeholder (RemoteEnvelope bytes inside
            // AckAndEnvelopeContainer.envelope).
            ProtoWire.PatchFixedWidthVarint(
                buffer.PatchSpan(envelopeLenOffset, ProtoWire.FixedWidthVarintBytes),
                (uint)(envelopeEnd - envelopeStart));

            return buffer.WrittenCount - start;
        }

        private static void WriteRaw(IBufferWriter<byte> buffer, ReadOnlySpan<byte> bytes)
        {
            var span = buffer.GetSpan(bytes.Length);
            bytes.CopyTo(span);
            buffer.Advance(bytes.Length);
        }
    }
}
