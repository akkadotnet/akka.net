//-----------------------------------------------------------------------
// <copyright file="ArteryActorBaselineBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Akka.Actor;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// <b>Task 0.1 — Config 1: actor-only baseline.</b> Hand-written producer → decode actor →
    /// N lane actors → recipient Tell chain. Same framing, header decode, deserialize knob, and
    /// per-recipient dispatch as the stream configs, but plumbed entirely with mailbox hops —
    /// no stream interpreter anywhere. This is the floor that mirrors DotNetty's structure;
    /// config 2/3 numbers are read against it to isolate the interpreter tax.
    /// </summary>
    [Config(typeof(ArterySubstrateConfig))]
    public class ArteryActorBaselineBenchmarks
    {
        [Params(1, 2, 4, 8, 16)]
        public int Lanes { get; set; }

        [Params(32, 128, 1024)]
        public int HashBytes { get; set; }

        private ActorSystem _system = null!;
        private ArteryFrameCorpus _corpus = null!;

        private IActorRef[]? _recipients;
        private IActorRef[] _lanes = null!;
        private IActorRef _decoder = null!;
        private Task _allDone = null!;
        private int _iteration;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _system = ActorSystem.Create("artery-actor-baseline", ArterySubstrateFixture.SystemConfig);
            _corpus = ArterySubstrateFixture.CreateCorpus();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _system.Dispose();
        }

        [IterationSetup]
        public void IterationSetup()
        {
            var iteration = ++_iteration;
            _recipients = ArterySubstrateFixture.SpawnRecipients(_system, out _allDone);

            _lanes = new IActorRef[Lanes];
            for (var i = 0; i < Lanes; i++)
            {
                _lanes[i] = _system.ActorOf(
                    Props.Create(() => new LaneActor(_recipients, HashBytes)),
                    $"baseline-lane-{iteration}-{i}");
            }

            _decoder = _system.ActorOf(
                Props.Create(() => new FrameDecodeActor(_lanes)),
                $"baseline-decoder-{iteration}");
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            _system.Stop(_decoder);
            foreach (var lane in _lanes)
                _system.Stop(lane);
            ArterySubstrateFixture.StopRecipients(_system, _recipients);
        }

        [Benchmark(OperationsPerInvoke = ArterySubstrateFixture.TotalMessages)]
        public Task ActorPipeline()
        {
            var chunks = _corpus.Chunks;
            for (var r = 0; r < ArterySubstrateFixture.Repeat; r++)
            {
                for (var c = 0; c < chunks.Length; c++)
                    _decoder.Tell(chunks[c]);
            }

            return _allDone;
        }

        /// <summary>
        /// The hand-written analogue of the stream decode island: reassembles frames from
        /// chunk boundaries (zero-copy, chained segments — the same shape
        /// <c>Framing.LengthField</c> uses), decodes the fixed header, and routes each
        /// envelope to its lane by recipient hash. One mailbox hop per chunk in, one
        /// mailbox hop per message out.
        /// </summary>
        private sealed class FrameDecodeActor : UntypedActor
        {
            private readonly IActorRef[] _lanes;
            private ReadOnlySequence<byte> _buffer = ReadOnlySequence<byte>.Empty;

            public FrameDecodeActor(IActorRef[] lanes)
            {
                _lanes = lanes;
            }

            protected override void OnReceive(object message)
            {
                if (message is not ReadOnlySequence<byte> chunk)
                    return;

                _buffer = ChainedSegment.Concat(_buffer, chunk);

                while (true)
                {
                    if (_buffer.Length < ArteryEnvelopeCodec.FrameLengthFieldLength)
                        break;

                    var frameLength = ReadFrameLength(_buffer);
                    var total = ArteryEnvelopeCodec.FrameLengthFieldLength + frameLength;
                    if (_buffer.Length < total)
                        break;

                    var envelope = ArteryEnvelopeCodec.Decode(_buffer.Slice(0, total));
                    _lanes[envelope.RecipientId % _lanes.Length].Tell(envelope);
                    _buffer = _buffer.Slice(total);
                }
            }

            private static int ReadFrameLength(in ReadOnlySequence<byte> buffer)
            {
                var firstSpan = buffer.First.Span;
                if (firstSpan.Length >= ArteryEnvelopeCodec.FrameLengthFieldLength)
                    return BinaryPrimitives.ReadInt32LittleEndian(firstSpan);

                Span<byte> tmp = stackalloc byte[ArteryEnvelopeCodec.FrameLengthFieldLength];
                buffer.Slice(0, ArteryEnvelopeCodec.FrameLengthFieldLength).CopyTo(tmp);
                return BinaryPrimitives.ReadInt32LittleEndian(tmp);
            }
        }

        /// <summary>
        /// The hand-written analogue of a deserialize lane: runs the deserialize knob and
        /// dispatches to the recipient. One mailbox hop per message in and out.
        /// </summary>
        private sealed class LaneActor : UntypedActor
        {
            private readonly IActorRef[] _recipients;
            private readonly int _hashBytes;

            public LaneActor(IActorRef[] recipients, int hashBytes)
            {
                _recipients = recipients;
                _hashBytes = hashBytes;
            }

            protected override void OnReceive(object message)
            {
                if (message is not InboundFrame envelope)
                    return;

                envelope.Checksum = DeserializeKnob.Run(envelope.Payload, _hashBytes);
                _recipients[envelope.RecipientId].Tell(envelope);
            }
        }
    }
}
