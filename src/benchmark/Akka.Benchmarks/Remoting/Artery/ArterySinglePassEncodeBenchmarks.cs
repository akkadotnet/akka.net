//-----------------------------------------------------------------------
// <copyright file="ArterySinglePassEncodeBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;
using Codec = Akka.Remote.Artery.ArteryEnvelopeCodec;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Measures the production single-pass encoder, including serializer lookup and transport context.
    /// The reused byte-array payload isolates envelope allocations from payload construction.
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class ArterySinglePassEncodeBenchmarks
    {
        private const string SenderPath = "akka://Sys@127.0.0.1:9001/user/sender";
        private const string RecipientPath = "akka://Sys@127.0.0.1:9002/user/recipient";
        private const string Manifest = "real-benchmark-v1";

        private ActorSystem _system = null!;
        private byte[] _payload = null!;
        private byte[] _frame = null!;
        private LiteralPayload _asciiMetadata = null!;
        private LiteralPayload _unicodeMetadata = null!;

        [Params(32, 256, 4096)]
        public int PayloadSize { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("single-pass-encode");
            _payload = new byte[PayloadSize];
            _frame = new byte[Codec.MaxEncodedSize(SenderPath, RecipientPath, Manifest, PayloadSize)];
            _asciiMetadata = new LiteralPayload(_payload, "message-v1");
            _unicodeMetadata = new LiteralPayload(_payload, "型-Ω-😀");
            var serializer = new LiteralPayloadSerializer((ExtendedActorSystem)_system);
            var serialization = ((ExtendedActorSystem)_system).Serialization;
            serialization.AddSerializer("literal-metadata", serializer);
            serialization.AddSerializationMap(typeof(LiteralPayload), serializer);
        }

        [Benchmark]
        public int Encode()
        {
            using var writer = Codec.Encode(((ExtendedActorSystem)_system).Serialization, 42L,
                SenderPath, RecipientPath, _payload);
            return writer.WrittenCount;
        }

        /// <summary>Isolates all three metadata literals, including a nonempty manifest, in a reused frame buffer.</summary>
        [Benchmark]
        public int EncodeLiteralMetadata() => Codec.Encode(_frame, 42L, 4, SenderPath, RecipientPath, Manifest, _payload);

        // Exercises the production V2 overload with all three nonempty metadata strings.
        // Payloads/strings are reused, so this includes no per-operation data construction.
        [Benchmark]
        public int EncodeV2AsciiMetadata()
        {
            using var writer = Codec.Encode(((ExtendedActorSystem)_system).Serialization, 42L,
                SenderPath, RecipientPath, _asciiMetadata);
            return writer.WrittenCount;
        }

        [Benchmark]
        public int EncodeV2UnicodeMetadata()
        {
            using var writer = Codec.Encode(((ExtendedActorSystem)_system).Serialization, 42L,
                "akka://Sys@host:1/user/送信-Ω", "akka://Sys@host:2/user/受信-😀", _unicodeMetadata);
            return writer.WrittenCount;
        }

        private sealed class LiteralPayload(byte[] bytes, string manifest)
        {
            public byte[] Bytes { get; } = bytes;
            public string Manifest { get; } = manifest;
        }

        private sealed class LiteralPayloadSerializer(ExtendedActorSystem system) : SerializerV2(system)
        {
            public override int Identifier => 987656;
            public override string Manifest(object obj) => ((LiteralPayload)obj).Manifest;
            public override int SizeHint(object obj) => ((LiteralPayload)obj).Bytes.Length;
            public override int Serialize(object obj, IBufferWriter<byte> writer)
            {
                var bytes = ((LiteralPayload)obj).Bytes;
                bytes.AsSpan().CopyTo(writer.GetSpan(bytes.Length));
                writer.Advance(bytes.Length);
                return bytes.Length;
            }
            public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) =>
                new LiteralPayload(bytes.ToArray(), manifest);
        }

        [GlobalCleanup]
        public Task Cleanup() => _system.Terminate();
    }
}
