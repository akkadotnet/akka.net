//-----------------------------------------------------------------------
// <copyright file="SerializerV2EnvelopeBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization
{
    /// <summary>
    /// Transport-envelope benchmarks for the V2 serialization API.
    ///
    /// <para>
    /// Simulates what <c>EndpointWriter</c> will do once Spec 3 wires the Streams TCP transport
    /// to call <see cref="SerializerV2.Serialize(IBufferWriter{byte}, object)"/> directly: writes
    /// a Remote-shaped envelope to an <see cref="IBufferWriter{T}"/>, wraps the result as a
    /// <see cref="ReadOnlySequence{T}"/>, and reads it back via
    /// <see cref="SerializerV2.Deserialize(ReadOnlySequence{byte}, string)"/>. Compares the
    /// V2-direct path against the V1-bridge path (<see cref="SerializerV2.ToBinary(object)"/> +
    /// <see cref="SerializerV2.FromBinary(byte[], string)"/>) on the same serializers and the
    /// same payload shapes.
    /// </para>
    ///
    /// <para>
    /// The envelope is <c>[serializerId: int32 LE][manifestLen: int32 LE][manifest: utf8 bytes][payload: bytes-to-end]</c>.
    /// Payload length is implicit (the outer frame boundary tells the reader where it ends),
    /// matching how the real Streams TCP transport will frame messages in Spec 3.
    /// </para>
    ///
    /// <para>
    /// No Akka.Remote / DotNetty / socket dependencies — pure shape benchmark to validate that
    /// the V2 buffer API earns its keep on allocations and throughput before downstream specs
    /// build on it.
    /// </para>
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class SerializerV2EnvelopeBenchmarks
    {
        public enum PayloadKind
        {
            StringShort,    // ~5 chars
            StringMedium,   // ~256 chars
            StringLong,     // ~4 KB
            Int32,
            Int64,
            BytesSmall,     // 16 B
            BytesMedium,    // 1 KB
            BytesLarge      // 16 KB
        }

        [Params(
            PayloadKind.StringShort, PayloadKind.StringMedium, PayloadKind.StringLong,
            PayloadKind.Int32, PayloadKind.Int64,
            PayloadKind.BytesSmall, PayloadKind.BytesMedium, PayloadKind.BytesLarge)]
        public PayloadKind Payload { get; set; }

        private ActorSystem _system;
        private SerializerV2 _serializer;
        private object _value;
        private string _manifest;

        // Reused across invocations on the V2-direct path so the benchmark measures the
        // marginal cost of one envelope round trip, not buffer churn.
        private ArrayBufferWriter<byte> _writer;

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create(
                "v2-envelope-bench",
                ConfigurationFactory.ParseString("akka.log-dead-letters = off"));

            _value = BuildPayload(Payload);
            _serializer = _system.Serialization.FindSerializerFor(_value);
            _manifest = _serializer.Manifest(_value);
            _writer = new ArrayBufferWriter<byte>(EstimatedEnvelopeSize());
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _system?.Terminate().Wait();
        }

        // ─── V2-direct path: Serialize(IBufferWriter) → wrap as ROS → Deserialize ─

        [Benchmark(Description = "V2-direct: Serialize → ReadOnlySequence → Deserialize")]
        public object V2_Direct_RoundTrip()
        {
            _writer.ResetWrittenCount();

            // Header: serializer id + manifest length + manifest UTF-8 bytes.
            WriteInt32(_writer, _serializer.Identifier);
            var manifestBytes = Encoding.UTF8.GetByteCount(_manifest);
            WriteInt32(_writer, manifestBytes);
            if (manifestBytes > 0)
            {
                var span = _writer.GetSpan(manifestBytes);
                Encoding.UTF8.GetBytes(_manifest.AsSpan(), span);
                _writer.Advance(manifestBytes);
            }

            // Payload: write directly into the writer's span — no intermediate byte[].
            _serializer.Serialize(_writer, _value);

            // Read back via SequenceReader for the header, then hand the remaining sequence
            // (the payload slice) to Deserialize. This is what EndpointWriter will do.
            var seq = new ReadOnlySequence<byte>(_writer.WrittenMemory);
            var reader = new SequenceReader<byte>(seq);
            reader.TryReadLittleEndian(out int serializerId);
            reader.TryReadLittleEndian(out int manifestLen);

            string manifest;
            if (manifestLen > 0)
            {
                var manifestSlice = reader.UnreadSequence.Slice(0, manifestLen);
                manifest = manifestSlice.IsSingleSegment
                    ? Encoding.UTF8.GetString(manifestSlice.First.Span)
                    : Encoding.UTF8.GetString(manifestSlice);
                reader.Advance(manifestLen);
            }
            else
            {
                manifest = string.Empty;
            }

            var payload = reader.UnreadSequence;
            var serializer = _system.Serialization.GetSerializerById(serializerId);
            return serializer.Deserialize(payload, manifest);
        }

        // ─── V1-bridge baseline: ToBinary → byte[] envelope → FromBinary ──────────

        [Benchmark(Baseline = true, Description = "V1-bridge: ToBinary → byte[] → FromBinary")]
        public object V1_Bridge_RoundTrip()
        {
            // Build the envelope via the byte[] bridge — what the current EndpointWriter does.
            var payloadBytes = _serializer.ToBinary(_value);
            var manifestBytes = Encoding.UTF8.GetBytes(_manifest ?? string.Empty);

            var envelope = new byte[sizeof(int) + sizeof(int) + manifestBytes.Length + payloadBytes.Length];
            var write = envelope.AsSpan();
            BinaryPrimitives.WriteInt32LittleEndian(write, _serializer.Identifier);
            write = write[sizeof(int)..];
            BinaryPrimitives.WriteInt32LittleEndian(write, manifestBytes.Length);
            write = write[sizeof(int)..];
            manifestBytes.CopyTo(write);
            write = write[manifestBytes.Length..];
            payloadBytes.CopyTo(write);

            // Read back.
            var read = (ReadOnlySpan<byte>)envelope;
            var serializerId = BinaryPrimitives.ReadInt32LittleEndian(read);
            read = read[sizeof(int)..];
            var manifestLen = BinaryPrimitives.ReadInt32LittleEndian(read);
            read = read[sizeof(int)..];
            var manifest = manifestLen > 0 ? Encoding.UTF8.GetString(read[..manifestLen]) : string.Empty;
            read = read[manifestLen..];
            var payload = read.ToArray();

            var serializer = _system.Serialization.GetSerializerById(serializerId);
            return serializer.FromBinary(payload, manifest);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static void WriteInt32(IBufferWriter<byte> buffer, int value)
        {
            var span = buffer.GetSpan(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(span, value);
            buffer.Advance(sizeof(int));
        }

        private static object BuildPayload(PayloadKind kind) => kind switch
        {
            PayloadKind.StringShort => "hello",
            PayloadKind.StringMedium => new string('m', 256),
            PayloadKind.StringLong => new string('l', 4 * 1024),
            PayloadKind.Int32 => 1234567,
            PayloadKind.Int64 => 1234567890123L,
            PayloadKind.BytesSmall => Enumerable.Range(0, 16).Select(i => (byte)i).ToArray(),
            PayloadKind.BytesMedium => Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray(),
            PayloadKind.BytesLarge => Enumerable.Range(0, 16 * 1024).Select(i => (byte)i).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        private int EstimatedEnvelopeSize() => Payload switch
        {
            PayloadKind.StringShort => 64,
            PayloadKind.StringMedium => 384,
            PayloadKind.StringLong => 4 * 1024 + 64,
            PayloadKind.Int32 => 32,
            PayloadKind.Int64 => 32,
            PayloadKind.BytesSmall => 64,
            PayloadKind.BytesMedium => 1024 + 64,
            PayloadKind.BytesLarge => 16 * 1024 + 64,
            _ => 256
        };
    }
}
