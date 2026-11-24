//-----------------------------------------------------------------------
// <copyright file="PipeReadToActorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

#nullable enable

namespace Akka.Tests.IO
{
    /// <summary>
    /// Covers <see cref="TcpConnection.PipeReadToActor"/>, the per-connection reader that reuses one cached
    /// continuation for every read. The pipes use <see cref="PipeOptions.Default"/>, so continuations run on the
    /// thread pool as they do on a real connection.
    /// </summary>
    public class PipeReadToActorSpec : AkkaSpec
    {
        public PipeReadToActorSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static async Task WriteAsync(PipeWriter writer, string text)
        {
            await writer.WriteAsync(Encoding.ASCII.GetBytes(text));
        }

        private static string Text(ReadOnlySequence<byte> data) => Encoding.ASCII.GetString(data.ToArray());

        [Fact(DisplayName = "PipeReadToActor should deliver data that arrives while a read is waiting, reusing the instance across reads")]
        public async Task Should_deliver_data_for_waiting_reads_across_reuse()
        {
            var pipe = new Pipe(PipeOptions.Default);
            var probe = CreateTestProbe();
            var reader = new TcpConnection.PipeReadToActor(pipe.Reader, CancellationToken.None, probe.Ref);

            for (var i = 0; i < 3; i++)
            {
                // The pipe is empty, so the read has to wait and goes through the cached continuation.
                reader.Start();
                await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                await WriteAsync(pipe.Writer, $"read-{i}");

                var msg = await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>();
                Text(msg.Data).Should().Be($"read-{i}");
                msg.IsCompleted.Should().BeFalse();
                msg.IsCanceled.Should().BeFalse();
            }
        }

        [Fact(DisplayName = "PipeReadToActor should deliver data already in the pipe without waiting")]
        public async Task Should_deliver_data_already_in_the_pipe()
        {
            var pipe = new Pipe(PipeOptions.Default);
            var probe = CreateTestProbe();
            var reader = new TcpConnection.PipeReadToActor(pipe.Reader, CancellationToken.None, probe.Ref);

            await WriteAsync(pipe.Writer, "ready");
            reader.Start();

            var msg = await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>();
            Text(msg.Data).Should().Be("ready");
            msg.IsCompleted.Should().BeFalse();
        }

        [Theory(DisplayName = "PipeReadToActor should deliver one complete read in fresh independent arrays below the LOH threshold")]
        [InlineData(64 * 1024)]
        [InlineData(64 * 1024 + 1)]
        [InlineData(2 * 64 * 1024 + 29)]
        public async Task Should_deliver_owned_chunks_and_retain_them_across_reads(int length)
        {
            var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
            var probe = CreateTestProbe();
            var reader = new TcpConnection.PipeReadToActor(pipe.Reader, CancellationToken.None, probe.Ref);
            var expected = new byte[length];
            new Random(42).NextBytes(expected);
            var transport = pipe.Writer.GetMemory(length);
            expected.CopyTo(transport);
            MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)transport, out var transportArray).Should().BeTrue();
            pipe.Writer.Advance(length);
            await pipe.Writer.FlushAsync();

            reader.Start();
            var first = await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>();
            first.Data.Length.Should().Be(length);
            first.Data.ToArray().Should().Equal(expected);
            first.Data.IsSingleSegment.Should().Be(length <= TcpConnection.PipeReadToActor.ReadCopyChunkSize);
            first.IsCompleted.Should().BeFalse();
            first.IsCanceled.Should().BeFalse();
            var arrays = new HashSet<byte[]>();
            var segmentCount = 0;
            foreach (var memory in first.Data)
            {
                memory.Length.Should().BeInRange(1, TcpConnection.PipeReadToActor.ReadCopyChunkSize);
                MemoryMarshal.TryGetArray(memory, out var owned).Should().BeTrue();
                owned.Array!.Length.Should().BeLessThan(85000);
                owned.Array.Should().NotBeSameAs(transportArray.Array);
                arrays.Add(owned.Array).Should().BeTrue("each segment must own a distinct array");
                // Mutating receiver-owned bytes must not affect any later read or another segment.
                owned.Array[owned.Offset] ^= 0xff;
                expected[segmentCount * TcpConnection.PipeReadToActor.ReadCopyChunkSize] ^= 0xff;
                segmentCount++;
            }
            segmentCount.Should().Be((length + TcpConnection.PipeReadToActor.ReadCopyChunkSize - 1)
                / TcpConnection.PipeReadToActor.ReadCopyChunkSize);

            var next = new byte[length];
            new Random(99).NextBytes(next);
            await pipe.Writer.WriteAsync(next);
            reader.Start();
            var second = await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>();
            second.Data.ToArray().Should().Equal(next);
            foreach (var memory in second.Data)
            {
                MemoryMarshal.TryGetArray(memory, out var owned).Should().BeTrue();
                arrays.Contains(owned.Array!).Should().BeFalse("receiver arrays must never be reused");
            }
            first.Data.ToArray().Should().Equal(expected, "retained bytes must survive the next pipe read");
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }

        [Fact(DisplayName = "PipeReadToActor should send PipeReadCanceled when the token is cancelled while a read is waiting")]
        public async Task Should_send_PipeReadCanceled_when_cancelled_while_waiting()
        {
            var pipe = new Pipe(PipeOptions.Default);
            var probe = CreateTestProbe();
            using var cts = new CancellationTokenSource();
            var reader = new TcpConnection.PipeReadToActor(pipe.Reader, cts.Token, probe.Ref);

            reader.Start();
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

            cts.Cancel();

            await probe.ExpectMsgAsync<TcpConnection.PipeReadCanceled>();
        }

        [Fact(DisplayName = "PipeReadToActor should send IoTaskFailed when the writer fails while a read is waiting")]
        public async Task Should_send_IoTaskFailed_when_writer_fails_while_waiting()
        {
            var pipe = new Pipe(PipeOptions.Default);
            var probe = CreateTestProbe();
            var reader = new TcpConnection.PipeReadToActor(pipe.Reader, CancellationToken.None, probe.Ref);

            reader.Start();
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

            var error = new IOException("connection reset");
            await pipe.Writer.CompleteAsync(error);

            var msg = await probe.ExpectMsgAsync<TcpConnection.IoTaskFailed>();
            msg.Cause.Should().BeOfType<IOException>().Which.Message.Should().Be("connection reset");
        }

        [Fact(DisplayName = "PipeReadToActor should return an empty completed result on the drain read after a completed read that carried data")]
        public async Task Should_return_empty_completed_result_on_drain_read()
        {
            var pipe = new Pipe(PipeOptions.Default);
            var probe = CreateTestProbe();
            var reader = new TcpConnection.PipeReadToActor(pipe.Reader, CancellationToken.None, probe.Ref);

            await WriteAsync(pipe.Writer, "last");
            await pipe.Writer.CompleteAsync();

            reader.Start();
            var first = await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>();
            Text(first.Data).Should().Be("last");
            first.IsCompleted.Should().BeTrue();

            // The drain read the actor issues after a completed read that still carried data.
            reader.Start();
            var drain = await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>();
            drain.Data.Length.Should().Be(0);
            drain.IsCompleted.Should().BeTrue();
        }
    }
}
