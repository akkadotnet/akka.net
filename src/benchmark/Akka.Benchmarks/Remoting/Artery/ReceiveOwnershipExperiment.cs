//-----------------------------------------------------------------------
// <copyright file="ReceiveOwnershipExperiment.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using Codec = Akka.Remote.Artery.ArteryEnvelopeCodec;

namespace Akka.Benchmarks.Experimental
{
    // Experimental only: this is NOT an implementation of the public TCP receive contract.
    // Linked into Remote.Tests so lifecycle tests exercise the same code measured by BDN.
    internal static class ReceiveOwnershipExperiment
    {
        internal const int BenchmarkFrames = 4096;
        private const int PayloadLength = 128;
        private const string SenderPath = "akka://receive/user/sender";
        private const string RecipientPath = "akka://receive/user/recipient";
        private const string Manifest = "receive-experiment";

        internal sealed class Corpus
        {
            internal Corpus(int frames, bool fragmented)
            {
                Frames = frames;
                var frameLength = Codec.MaxEncodedSize(SenderPath, RecipientPath, Manifest, PayloadLength);
                var bytes = new byte[checked(frames * frameLength)];
                var payload = new byte[PayloadLength];
                for (var i = 4; i < payload.Length; i++)
                    payload[i] = (byte)i;
                for (var i = 0; i < frames; i++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(payload, i);
                    if (Codec.Encode(bytes.AsSpan(i * frameLength, frameLength), 42, 17,
                            SenderPath, RecipientPath, Manifest, payload) != frameLength)
                        throw new InvalidOperationException("Unexpected encoded frame length.");
                }

                var chunks = new List<ReadOnlyMemory<byte>>();
                for (var offset = 0; offset < bytes.Length;)
                {
                    // The fragmented case first splits a length field, then repeatedly splits
                    // bodies. Complete reads carry32frames to model batching rather than one
                    // artificial lease allocation per application message.
                    var requested = fragmented
                        ? offset == 0 ? 2 : frameLength * 16 + 3
                        : frameLength * 32;
                    var length = Math.Min(requested, bytes.Length - offset);
                    chunks.Add(bytes.AsMemory(offset, length));
                    offset += length;
                }
                Chunks = chunks.ToArray();
            }

            internal int Frames { get; }
            internal ReadOnlyMemory<byte>[] Chunks { get; }
        }

        internal sealed class Observation
        {
            private int _acknowledged;
            private int _finished;
            internal int Acknowledged => Volatile.Read(ref _acknowledged);
            internal int Finished => Volatile.Read(ref _finished);
            internal void Acknowledge() => Interlocked.Increment(ref _acknowledged);
            internal void Finish()
            {
                if (Interlocked.Increment(ref _finished) != 1)
                    throw new InvalidOperationException("Experiment finished more than once.");
            }
        }

        internal sealed class Control
        {
            internal MemoryPool<byte> Pool { get; init; } = MemoryPool<byte>.Shared;
            internal Task? ConsumerGate { get; init; }
            internal TaskCompletionSource<bool>? ConsumerEntered { get; init; }
            internal Exception? WriterFailure { get; init; }
            internal Observation Observation { get; } = new();
        }

        internal readonly record struct Totals(int Frames, int Reads, long CopiedBytes, int Acknowledgements);

        internal static async Task<Totals> RunAsync(ActorSystem system, Corpus corpus, bool borrow,
            bool queuedConsumer, Control? control = null, CancellationToken cancellationToken = default)
        {
            control ??= new Control();
            var observedReads = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
            var pipe = new Pipe(new PipeOptions(pool: control.Pool, pauseWriterThreshold: 1024 * 1024,
                resumeWriterThreshold: 512 * 1024, useSynchronizationContext: false));
            var consumedFrames = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var consumer = system.ActorOf(Props.Create(() => new PayloadConsumer(
                queuedConsumer, control, consumedFrames, corpus.Frames)));
            var parser = system.ActorOf(Props.Create(() => new ParserConsumer(consumer, borrow)));
            var acknowledgements = new List<Task>();
            var reads = 0;
            long copiedBytes = 0;
            var writer = WriteChunksAsync();
            try
            {
                try
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var result = await pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                        var buffer = result.Buffer;
                        if (buffer.IsEmpty)
                        {
                            pipe.Reader.AdvanceTo(buffer.End);
                            if (result.IsCompleted)
                                break;
                            continue;
                        }

                        reads++;
                        var data = buffer;
                        if (!borrow)
                        {
                            var array = buffer.ToArray();
                            copiedBytes += array.Length;
                            data = new ReadOnlySequence<byte>(array);
                            pipe.Reader.AdvanceTo(buffer.End);
                        }
                        var chunk = new ReadChunk(data, control.Observation);
                        acknowledgements.Add(chunk.Completion.Task);
                        parser.Tell(chunk);
                        observedReads.Writer.TryWrite(true);
                        if (borrow)
                        {
                            // Cancellation/abort cannot revoke memory still held by the actor.
                            // Wait for its acknowledgement without the cancellation token, then
                            // AdvanceTo in finally even if consumption reported a failure.
                            try
                            {
                                copiedBytes += await chunk.Completion.Task.ConfigureAwait(false);
                            }
                            finally
                            {
                                pipe.Reader.AdvanceTo(buffer.End);
                            }
                        }
                    }
                    parser.Tell(Finish.Instance);
                }
                finally
                {
                    await pipe.Reader.CompleteAsync().ConfigureAwait(false);
                }
                await writer.ConfigureAwait(false);
                await Task.WhenAll(acknowledgements).ConfigureAwait(false);
                var frames = await consumedFrames.Task.ConfigureAwait(false);
                if (frames != corpus.Frames || control.Observation.Acknowledged != reads)
                    throw new InvalidOperationException("Incomplete receive experiment delivery or acknowledgements.");
                return new Totals(frames, reads, copiedBytes, control.Observation.Acknowledged);
            }
            finally
            {
                observedReads.Writer.TryComplete();
                // The writer may be parked waiting for a read when cancellation or a parser
                // failure ends the reader. Complete the channel to release that wait.
                try
                {
                    await Task.WhenAll(writer, Task.WhenAll(acknowledgements)).ConfigureAwait(false);
                }
                finally
                {
                    await Task.WhenAll(parser.GracefulStop(TimeSpan.FromSeconds(5)),
                        consumer.GracefulStop(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                    control.Observation.Finish();
                }
            }

            async Task WriteChunksAsync()
            {
                Exception? failure = null;
                try
                {
                    foreach (var chunk in corpus.Chunks)
                    {
                        pipe.Writer.Write(chunk.Span);
                        var flush = await pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                        if (flush.IsCompleted || flush.IsCanceled)
                            return;
                        if (!await observedReads.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                            return;
                        if (!observedReads.Reader.TryRead(out _))
                            throw new InvalidOperationException("Missing read observation.");
                    }
                    failure = control.WriterFailure;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Surface cancellation from the reader; the writer still completes its pipe.
                    failure = null;
                }
                finally
                {
                    await pipe.Writer.CompleteAsync(failure).ConfigureAwait(false);
                }
            }
        }

        private sealed class ReadChunk
        {
            private readonly Observation _observation;
            private int _completed;
            internal ReadChunk(ReadOnlySequence<byte> data, Observation observation)
            {
                Data = data;
                _observation = observation;
            }
            internal ReadOnlySequence<byte> Data { get; }
            internal TaskCompletionSource<long> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal void Complete(long copiedTailBytes, Exception? failure = null)
            {
                if (Interlocked.Exchange(ref _completed, 1) != 0)
                    throw new InvalidOperationException("Read acknowledged more than once.");
                _observation.Acknowledge();
                if (failure is null)
                    Completion.SetResult(copiedTailBytes);
                else
                    Completion.SetException(failure);
            }
        }

        private sealed record FrameBatch(ReadOnlySequence<byte>[] Frames, ReadChunk Chunk, long CopiedTailBytes);
        private sealed record DeferredBatch(FrameBatch Batch);
        private sealed class Finish
        {
            internal static readonly Finish Instance = new();
            private Finish() { }
        }

        private sealed class ParserConsumer : ReceiveActor
        {
            private ReadOnlySequence<byte> _tail;
            public ParserConsumer(IActorRef consumer, bool borrow)
            {
                Receive<ReadChunk>(chunk =>
                {
                    try
                    {
                        var combined = Concat(_tail, chunk.Data);
                        var parser = new ArteryFrameParser(4096);
                        foreach (var memory in combined)
                            parser.Append(memory);
                        var frames = new List<ReadOnlySequence<byte>>();
                        long consumed = 0;
                        while (parser.TryReadFrame(out var frame))
                        {
                            frames.Add(frame);
                            consumed += ArteryFrameParser.LengthFieldSize + frame.Length;
                        }
                        _tail = combined.Slice(consumed);
                        long copiedTailBytes = 0;
                        if (borrow && !_tail.IsEmpty)
                        {
                            var array = _tail.ToArray();
                            copiedTailBytes = array.Length;
                            _tail = new ReadOnlySequence<byte>(array);
                        }
                        consumer.Tell(new FrameBatch(frames.ToArray(), chunk, copiedTailBytes));
                    }
                    catch (Exception failure)
                    {
                        chunk.Complete(0, failure);
                    }
                });
                Receive<Finish>(_ => consumer.Tell(_tail.IsEmpty
                    ? Finish.Instance
                    : new Status.Failure(new InvalidDataException("EOF left an incomplete Artery frame."))));
            }
        }

        private sealed class PayloadConsumer : ReceiveActor
        {
            private int _frames;
            public PayloadConsumer(bool queued, Control control, TaskCompletionSource<int> finished, int expectedFrames)
            {
                Receive<FrameBatch>(batch =>
                {
                    if (queued || control.ConsumerGate is not null)
                        Self.Tell(new DeferredBatch(batch));
                    else
                        Consume(batch);
                });
                ReceiveAsync<DeferredBatch>(async deferred =>
                {
                    control.ConsumerEntered?.TrySetResult(true);
                    if (control.ConsumerGate is not null)
                        await control.ConsumerGate;
                    Consume(deferred.Batch);
                });
                Receive<Finish>(_ =>
                {
                    // Deferred self-messages can follow Finish. Keep the final count promise
                    // pending until they drain rather than treating input EOF as consumption.
                    if (_frames == expectedFrames)
                        finished.TrySetResult(_frames);
                    else
                        _finishRequested = true;
                });
                Receive<Status.Failure>(failure => finished.TrySetException(failure.Cause));

                void Consume(FrameBatch batch)
                {
                    try
                    {
                        Span<byte> bytes = stackalloc byte[PayloadLength];
                        foreach (var frame in batch.Frames)
                        {
                            var decoded = Codec.Decode(frame);
                            var payload = decoded.Payload;
                            if (payload.Length != PayloadLength)
                                throw new InvalidDataException("Changed payload length.");
                            payload.CopyTo(bytes);
                            if (BinaryPrimitives.ReadInt32LittleEndian(bytes) != _frames)
                                throw new InvalidDataException("Lost, duplicated, or reordered receive frame.");
                            for (var i = 4; i < bytes.Length; i++)
                            {
                                if (bytes[i] != (byte)i)
                                    throw new InvalidDataException("Borrowed frame was overwritten before consumption.");
                            }
                            _frames++;
                        }
                        batch.Chunk.Complete(batch.CopiedTailBytes);
                        if (_finishRequested && _frames == expectedFrames)
                            finished.TrySetResult(_frames);
                    }
                    catch (Exception failure)
                    {
                        batch.Chunk.Complete(batch.CopiedTailBytes, failure);
                        finished.TrySetException(failure);
                    }
                }
            }
            private bool _finishRequested;
        }

        private static ReadOnlySequence<byte> Concat(ReadOnlySequence<byte> left, ReadOnlySequence<byte> right)
        {
            if (left.IsEmpty)
                return right;
            Segment? head = null;
            Segment? tail = null;
            foreach (var memory in left)
                Append(memory);
            foreach (var memory in right)
                Append(memory);
            return new ReadOnlySequence<byte>(head!, 0, tail!, tail!.Memory.Length);

            void Append(ReadOnlyMemory<byte> memory)
            {
                if (head is null)
                    head = tail = new Segment(memory);
                else
                    tail = tail!.Append(memory);
            }
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            internal Segment(ReadOnlyMemory<byte> memory) => Memory = memory;
            internal Segment Append(ReadOnlyMemory<byte> memory)
            {
                var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
                Next = next;
                return next;
            }
        }
    }
}
