//-----------------------------------------------------------------------
// <copyright file="ArterySinglePassEncodeBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Threading.Tasks;
using Akka.Remote;
using Akka.Remote.Artery;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
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
        private RefOutboundEnvelope _refEnvelope = null!;
        private ActorRefTable _refTable = null!;
        private byte[] _coldFrame = null!;
        private byte[] _warmFrame = null!;
        private IActorRef _expectedSender = null!;

        [Params(32, 256, 4096)]
        public int PayloadSize { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("single-pass-encode", Akka.Configuration.ConfigurationFactory.ParseString("""
                akka.actor.provider = remote
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = "127.0.0.1"
                akka.remote.artery.canonical.port = 0
                """));
            _payload = new byte[PayloadSize];
            _frame = new byte[Codec.MaxEncodedSize(SenderPath, RecipientPath, Manifest, PayloadSize)];
            var provider = RARP.For(_system).Provider;
            _expectedSender = _system.ActorOf(Props.Create<Discard>(), "sender");
            var recipient = (RemoteActorRef)provider.ResolveActorRef("akka://other@127.0.0.1:1234/user/target#123");
            _refEnvelope = new RefOutboundEnvelope(_payload, _expectedSender, recipient, provider.DefaultAddress);
            _refTable = new ActorRefTable(1, new[]
            {
                new CompressedActorRef(_refEnvelope.SenderPath!, _expectedSender),
                new CompressedActorRef(_refEnvelope.RecipientPath, recipient)
            });
            using var cold = Codec.EncodeRefs(((ExtendedActorSystem)_system).Serialization, 42L, _refEnvelope, null, null);
            using var warm = Codec.EncodeRefs(((ExtendedActorSystem)_system).Serialization, 42L, _refEnvelope, null, _refTable);
            _coldFrame = cold.WrittenMemory.Slice(4).ToArray();
            _warmFrame = warm.WrittenMemory.Slice(4).ToArray();
            if (DecodeActorRefsCold() != _expectedSender || DecodeActorRefsWarm() != _expectedSender)
                throw new InvalidOperationException("Actor-ref decode benchmark did not recover the sender");
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

        // Paired arms use the same serializer, payload, refs, and envelope. Table learning and
        // setup allocations are excluded: these measure literal fallback versus warm metadata.
        [Benchmark]
        public int EncodeActorRefsCold()
        {
            using var writer = Codec.EncodeRefs(((ExtendedActorSystem)_system).Serialization, 42L, _refEnvelope, null, null);
            return writer.WrittenCount;
        }

        [Benchmark]
        public int EncodeActorRefsWarm()
        {
            using var writer = Codec.EncodeRefs(((ExtendedActorSystem)_system).Serialization, 42L, _refEnvelope, null, _refTable);
            return writer.WrittenCount;
        }

        [Benchmark]
        public IActorRef DecodeActorRefsCold()
        {
            var decoded = Codec.Decode(new ReadOnlySequence<byte>(_coldFrame));
            decoded.TryGetSenderPath(out var sender);
            decoded.TryGetRecipientPath(out var recipient);
            var provider = RARP.For(_system).Provider;
            provider.ResolveActorRefWithLocalAddress(recipient!, provider.DefaultAddress);
            return provider.ResolveActorRefWithLocalAddress(sender!, provider.DefaultAddress);
        }

        [Benchmark]
        public IActorRef DecodeActorRefsWarm()
        {
            var decoded = Codec.Decode(new ReadOnlySequence<byte>(_warmFrame));
            if (!_refTable.TryGet(decoded.RecipientCompressedIndex, out _) ||
                !_refTable.TryGet(decoded.SenderCompressedIndex, out var sender))
                throw new InvalidOperationException("Invalid compressed ref");
            return sender.Resolved!;
        }

        private sealed class Discard : ReceiveActor
        {
            public Discard() => ReceiveAny(_ => { });
        }

        [GlobalCleanup]
        public Task Cleanup() => _system.Terminate();
    }
}
