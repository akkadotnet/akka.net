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
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using Akka.Serialization;
using Google.Protobuf;

namespace Akka.Remote.Serialization.V2
{
    // ─── PatchingBufferWriter — IBufferWriter<byte> with patch capability ─────

    /// <summary>
    /// Minimal <see cref="IBufferWriter{T}"/> implementation that also exposes a mutable
    /// span over already-written bytes for length-prefix patching. Reuses a growable byte
    /// array across <see cref="Reset"/> calls.
    /// </summary>
    internal sealed class PatchingBufferWriter : IBufferWriter<byte>
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
        /// Tag = (field_number &lt;&lt; 3) | wire_type, varint-encoded. For field numbers 1–15
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

        // ─── Read helpers ─────────────────────────────────────────────────
        // All read helpers take `ref ReadOnlySpan<byte>` and advance the span past
        // what they consumed. They accept both canonical and over-long varints.

        public static uint ReadVarint32(ref ReadOnlySpan<byte> bytes)
        {
            uint value = 0;
            var shift = 0;
            var consumed = 0;
            while (true)
            {
                var b = bytes[consumed++];
                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
                if (shift >= 35)
                    throw new InvalidOperationException("Varint exceeds 5 bytes");
            }
            bytes = bytes.Slice(consumed);
            return value;
        }

        public static (int fieldNumber, byte wireType) ReadTag(ref ReadOnlySpan<byte> bytes)
        {
            var tag = ReadVarint32(ref bytes);
            return ((int)(tag >> 3), (byte)(tag & 0x7));
        }

        public static ulong ReadFixed64(ref ReadOnlySpan<byte> bytes)
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            bytes = bytes.Slice(sizeof(ulong));
            return value;
        }

        public static ReadOnlySpan<byte> ReadLengthDelimited(ref ReadOnlySpan<byte> bytes)
        {
            var length = (int)ReadVarint32(ref bytes);
            var slice = bytes.Slice(0, length);
            bytes = bytes.Slice(length);
            return slice;
        }

        public static string ReadString(ref ReadOnlySpan<byte> bytes)
        {
            var slice = ReadLengthDelimited(ref bytes);
            return Encoding.UTF8.GetString(slice);
        }

        /// <summary>
        /// Skips a field whose tag has already been read. Used for forward compat when
        /// encountering unknown field numbers from newer producers.
        /// </summary>
        public static void SkipField(ref ReadOnlySpan<byte> bytes, byte wireType)
        {
            switch (wireType)
            {
                case WireTypeVarint:
                    ReadVarint32(ref bytes);
                    break;
                case WireTypeFixed64:
                    bytes = bytes.Slice(sizeof(ulong));
                    break;
                case WireTypeLengthDelimited:
                    ReadLengthDelimited(ref bytes);
                    break;
                case WireTypeFixed32:
                    bytes = bytes.Slice(sizeof(uint));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown wire type [{wireType}]");
            }
        }
    }

    // ─── V2 serializer registry (Type → SerializerV2, ID → SerializerV2) ──────

    /// <summary>
    /// Static-dispatch lookup for V2 inner serializers. No reflection at dispatch time —
    /// Type-keyed for write, integer-ID-keyed for read.
    ///
    /// Two construction modes:
    /// <list type="bullet">
    ///   <item>Standalone (no-arg ctor) — caller explicitly registers serializers via
    ///   <see cref="Register"/>. Used by benchmarks where we want full control over the
    ///   registered set.</item>
    ///   <item>Serialization-backed (ctor takes an <see cref="Akka.Serialization.Serialization"/>) —
    ///   the registry caches per-Type / per-ID lookups but falls through to the running
    ///   <see cref="Akka.Serialization.Serialization"/> on first miss. Used by the Akka.Remote
    ///   integration so HOCON-configured V1 serializers (auto-wrapped) and V2-native serializers
    ///   are both reachable without explicit registration.</item>
    /// </list>
    /// </summary>
    internal sealed class V2SerializerRegistry
    {
        private readonly ConcurrentDictionary<Type, SerializerV2> _byType = new();
        private readonly ConcurrentDictionary<int, SerializerV2> _byId = new();
        private readonly Akka.Serialization.Serialization? _fallback;

        public V2SerializerRegistry() { }

        public V2SerializerRegistry(Akka.Serialization.Serialization serialization)
        {
            _fallback = serialization ?? throw new ArgumentNullException(nameof(serialization));
        }

        public void Register(SerializerV2 serializer, params Type[] types)
        {
            _byId[serializer.Identifier] = serializer;
            foreach (var t in types)
                _byType[t] = serializer;
        }

        public SerializerV2 FindFor(object obj)
        {
            var type = obj.GetType();
            if (_byType.TryGetValue(type, out var cached))
                return cached;
            if (_fallback is null)
                throw new InvalidOperationException($"No V2 serializer registered for type [{type}]");
            var serializer = _fallback.FindSerializerForType(type);
            _byType[type] = serializer;
            // Also cache by ID so receive-side lookups are fast.
            _byId[serializer.Identifier] = serializer;
            return serializer;
        }

        public SerializerV2 GetById(int id)
        {
            if (_byId.TryGetValue(id, out var cached))
                return cached;
            if (_fallback is null)
                throw new InvalidOperationException($"No V2 serializer registered for id [{id}]");
            var serializer = _fallback.GetSerializerById(id);
            _byId[id] = serializer;
            return serializer;
        }
    }

    // ─── V2 envelope serializer — writes the full AckAndEnvelopeContainer ─────

    /// <summary>
    /// V2 wrap-pipeline entry point. Hand-writes the Protobuf wire format for
    /// AckAndEnvelopeContainer → RemoteEnvelope → Payload in a single pass over the
    /// IBufferWriter, with no intermediate byte[] allocations. The inner V2 serializer
    /// is invoked exactly once to write directly into the same buffer.
    /// </summary>
    internal sealed class V2RemoteEnvelopeWriter
    {
        private readonly V2SerializerRegistry _registry;

        public V2RemoteEnvelopeWriter(V2SerializerRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>
        /// Writes the full AckAndEnvelopeContainer wire bytes for a single Send.
        /// Recipient and sender are passed as pre-serialized ActorRefData proto bytes.
        /// Optional Ack is encoded as AcknowledgementInfo at field 1.
        /// </summary>
        /// <returns>Total bytes written.</returns>
        public int Serialize(
            PatchingBufferWriter buffer,
            ReadOnlySpan<byte> recipientProtoBytes,
            ReadOnlySpan<byte> senderProtoBytes,
            ulong seq,
            object payload,
            ulong? ackCumulative = null,
            IReadOnlyList<ulong>? ackNacks = null)
        {
            var start = buffer.WrittenCount;
            var inner = _registry.FindFor(payload);

            // ─── AckAndEnvelopeContainer ──────────────────────────────────────
            //   field 1: ack (AcknowledgementInfo, length-delimited) — optional
            //   field 2: envelope (RemoteEnvelope, length-delimited)

            if (ackCumulative.HasValue)
            {
                ProtoWire.WriteTag(buffer, fieldNumber: 1, ProtoWire.WireTypeLengthDelimited);
                var ackLenOffset = buffer.WrittenCount;
                ProtoWire.ReserveFixedWidthVarint(buffer);
                var ackStart = buffer.WrittenCount;

                // AcknowledgementInfo field 1: cumulativeAck (fixed64)
                ProtoWire.WriteTag(buffer, fieldNumber: 1, ProtoWire.WireTypeFixed64);
                ProtoWire.WriteFixed64(buffer, ackCumulative.Value);

                // AcknowledgementInfo field 2: nacks (repeated fixed64)
                if (ackNacks is not null)
                {
                    for (var i = 0; i < ackNacks.Count; i++)
                    {
                        ProtoWire.WriteTag(buffer, fieldNumber: 2, ProtoWire.WireTypeFixed64);
                        ProtoWire.WriteFixed64(buffer, ackNacks[i]);
                    }
                }

                var ackEnd = buffer.WrittenCount;
                ProtoWire.PatchFixedWidthVarint(
                    buffer.PatchSpan(ackLenOffset, ProtoWire.FixedWidthVarintBytes),
                    (uint)(ackEnd - ackStart));
            }

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

    // ─── V2 envelope reader — parses AckAndEnvelopeContainer wire bytes ───────

    /// <summary>
    /// Result of a V2 envelope read. Mirrors the fields that the V1 receive path
    /// extracts from <c>AckAndEnvelopeContainer</c> / <c>RemoteEnvelope</c> /
    /// <c>Payload</c>, ready to be handed to a dispatcher.
    /// </summary>
    internal readonly struct V2DeserializedEnvelope
    {
        public V2DeserializedEnvelope(string recipientPath, string senderPath, ulong seq, object payload)
        {
            RecipientPath = recipientPath;
            SenderPath = senderPath;
            Seq = seq;
            Payload = payload;
        }

        public string RecipientPath { get; }
        public string SenderPath { get; }
        public ulong Seq { get; }
        public object Payload { get; }
    }

    /// <summary>
    /// V2 receive-side reader. Parses the AckAndEnvelopeContainer wire format directly
    /// from a byte span, without materializing the intermediate proto objects. The inner
    /// payload bytes are sliced (no copy) and dispatched to the registered V2 serializer
    /// by integer ID — no <c>Type.GetType(manifest)</c>, no reflection.
    /// </summary>
    internal sealed class V2RemoteEnvelopeReader
    {
        private readonly V2SerializerRegistry _registry;

        public V2RemoteEnvelopeReader(V2SerializerRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>
        /// Span-based read for use cases where the wire bytes don't have a Memory backing
        /// (e.g. stack-allocated). The inner-payload slice is materialized via .ToArray()
        /// since we can't construct a ReadOnlySequence&lt;byte&gt; from a Span. Prefer
        /// <see cref="Read(ReadOnlyMemory{byte})"/> when possible for zero-copy slicing.
        /// </summary>
        public V2DeserializedEnvelope Read(ReadOnlySpan<byte> wireBytes)
            => ReadCore(wireBytes, useMemorySlicing: false, root: default);

        /// <summary>
        /// Memory-based read. The inner-payload slice is a zero-copy slice of
        /// <paramref name="wireBytes"/> — no <c>.ToArray()</c> allocation.
        /// </summary>
        public V2DeserializedEnvelope Read(ReadOnlyMemory<byte> wireBytes)
            => ReadCore(wireBytes.Span, useMemorySlicing: true, root: wireBytes);

        private V2DeserializedEnvelope ReadCore(
            ReadOnlySpan<byte> wireBytes,
            bool useMemorySlicing,
            ReadOnlyMemory<byte> root)
        {
            var span = wireBytes;

            string recipientPath = string.Empty;
            string senderPath = string.Empty;
            ulong seq = 0;
            object? payload = null;

            while (!span.IsEmpty)
            {
                var (fieldNumber, wireType) = ProtoWire.ReadTag(ref span);
                switch (fieldNumber)
                {
                    case 2: // envelope (RemoteEnvelope, length-delimited)
                    {
                        var envelopeLen = (int)ProtoWire.ReadVarint32(ref span);
                        // Offset of the envelope's first byte within wireBytes.
                        var envelopeOffset = wireBytes.Length - span.Length;
                        var envelopeSpan = span.Slice(0, envelopeLen);
                        ParseRemoteEnvelope(
                            envelopeSpan,
                            useMemorySlicing ? root.Slice(envelopeOffset, envelopeLen) : default,
                            useMemorySlicing,
                            out recipientPath, out senderPath, out seq, out payload);
                        span = span.Slice(envelopeLen);
                        break;
                    }
                    default:
                        ProtoWire.SkipField(ref span, wireType);
                        break;
                }
            }

            return new V2DeserializedEnvelope(recipientPath, senderPath, seq, payload!);
        }

        private void ParseRemoteEnvelope(
            ReadOnlySpan<byte> envelopeBytes,
            ReadOnlyMemory<byte> envelopeMemory,
            bool useMemorySlicing,
            out string recipientPath,
            out string senderPath,
            out ulong seq,
            out object? payload)
        {
            recipientPath = string.Empty;
            senderPath = string.Empty;
            seq = 0;
            payload = null;

            var bytes = envelopeBytes;
            while (!bytes.IsEmpty)
            {
                var (fieldNumber, wireType) = ProtoWire.ReadTag(ref bytes);
                switch (fieldNumber)
                {
                    case 1: // recipient (ActorRefData)
                    {
                        var actorRefBytes = ProtoWire.ReadLengthDelimited(ref bytes);
                        recipientPath = ExtractActorRefPath(actorRefBytes);
                        break;
                    }
                    case 2: // message (Payload)
                    {
                        var payloadLen = (int)ProtoWire.ReadVarint32(ref bytes);
                        var payloadOffset = envelopeBytes.Length - bytes.Length;
                        var payloadSpan = bytes.Slice(0, payloadLen);
                        payload = ParsePayload(
                            payloadSpan,
                            useMemorySlicing ? envelopeMemory.Slice(payloadOffset, payloadLen) : default,
                            useMemorySlicing);
                        bytes = bytes.Slice(payloadLen);
                        break;
                    }
                    case 4: // sender (ActorRefData)
                    {
                        var actorRefBytes = ProtoWire.ReadLengthDelimited(ref bytes);
                        senderPath = ExtractActorRefPath(actorRefBytes);
                        break;
                    }
                    case 5: // seq (fixed64)
                        seq = ProtoWire.ReadFixed64(ref bytes);
                        break;
                    default:
                        ProtoWire.SkipField(ref bytes, wireType);
                        break;
                }
            }
        }

        private object ParsePayload(
            ReadOnlySpan<byte> payloadBytes,
            ReadOnlyMemory<byte> payloadMemory,
            bool useMemorySlicing)
        {
            var messageOffset = -1;
            var messageLength = 0;
            var serializerId = 0;
            var manifest = string.Empty;
            var bytes = payloadBytes;

            while (!bytes.IsEmpty)
            {
                var (fieldNumber, wireType) = ProtoWire.ReadTag(ref bytes);
                switch (fieldNumber)
                {
                    case 1: // message (bytes — the inner serialized payload)
                    {
                        var len = (int)ProtoWire.ReadVarint32(ref bytes);
                        messageOffset = payloadBytes.Length - bytes.Length;
                        messageLength = len;
                        bytes = bytes.Slice(len);
                        break;
                    }
                    case 2: // serializerId (int32 / varint)
                        serializerId = (int)ProtoWire.ReadVarint32(ref bytes);
                        break;
                    case 3: // messageManifest (bytes — UTF-8)
                        manifest = ProtoWire.ReadString(ref bytes);
                        break;
                    default:
                        ProtoWire.SkipField(ref bytes, wireType);
                        break;
                }
            }

            var serializer = _registry.GetById(serializerId);

            // Memory-slicing path: zero-copy — slice the original memory at the inner offset,
            // wrap as ReadOnlySequence, hand to V2 serializer's Deserialize. Allocations on
            // this path come only from the inner deserialization itself (e.g. Encoding.UTF8.GetString).
            if (useMemorySlicing)
            {
                var innerMemory = payloadMemory.Slice(messageOffset, messageLength);
                return serializer.Deserialize(new ReadOnlySequence<byte>(innerMemory), manifest);
            }

            // Span-only path: must materialize to byte[] to construct a ReadOnlySequence.
            var messageSlice = payloadBytes.Slice(messageOffset, messageLength);
            return serializer.Deserialize(new ReadOnlySequence<byte>(messageSlice.ToArray()), manifest);
        }

        private static string ExtractActorRefPath(ReadOnlySpan<byte> actorRefDataBytes)
        {
            var bytes = actorRefDataBytes;
            while (!bytes.IsEmpty)
            {
                var (fieldNumber, wireType) = ProtoWire.ReadTag(ref bytes);
                if (fieldNumber == 1 && wireType == ProtoWire.WireTypeLengthDelimited)
                    return ProtoWire.ReadString(ref bytes);
                ProtoWire.SkipField(ref bytes, wireType);
            }
            return string.Empty;
        }
    }
}
