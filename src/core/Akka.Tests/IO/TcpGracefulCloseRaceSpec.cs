//-----------------------------------------------------------------------
// <copyright file="TcpGracefulCloseRaceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.IO
{
    /// <summary>
    /// Regression coverage for the read-side close race in <see cref="TcpTransportConnection"/>.
    /// <para>
    /// The inbound side is consumer-driven (no read pump): the owning <see cref="TcpConnection"/>
    /// actor keeps a <see cref="System.IO.Pipelines.PipeReader.ReadAsync"/> in flight on the
    /// transport's <c>StreamPipeReader</c>. On a graceful <see cref="TcpTransportConnection.CloseAsync"/>
    /// the transport used to call <c>Complete()</c> on that reader immediately after cancelling its
    /// CTS — while the consumer's read could still be parked inside the inner stream read. A
    /// <c>StreamPipeReader</c> is NOT safe to <c>Complete()</c> with a read in flight:
    /// <c>Complete()</c> resets the <c>BufferSegment</c> the resuming read commits bytes into, so
    /// the read does <c>segment.End += bytesRead</c> against a now-zero-length segment and throws
    /// <see cref="ArgumentOutOfRangeException"/> out of <c>BufferSegment.set_End</c>. That surfaces
    /// to the actor as an I/O error and to the peer as a spurious <c>ErrorClosed</c> on an otherwise
    /// clean close.
    /// </para>
    /// <para>
    /// The single-close coverage in the other specs never lands inside that window, so it misses
    /// the bug. The two tests below pin it:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Deterministic guard</b> — drives the exact interleaving (read parked in the inner
    /// stream, then <see cref="TcpTransportConnection.CloseAsync"/>, then the read resumes with
    /// bytes) with a gated fake stream, and asserts the in-flight read never throws
    /// <see cref="ArgumentOutOfRangeException"/> and the close completes cleanly.</item>
    /// <item><b>End-to-end loop</b> — many real connect / exchange / <see cref="Tcp.Close"/> cycles
    /// through the full actor + transport stack, asserting every close is clean (never
    /// <see cref="Tcp.ErrorClosed"/>).</item>
    /// </list>
    /// </summary>
    public class TcpGracefulCloseRaceSpec : AkkaSpec
    {
        public TcpGracefulCloseRaceSpec(ITestOutputHelper output)
            : base("akka.loglevel = INFO\nakka.log-dead-letters = off", output: output)
        {
        }

        /// <summary>
        /// Deterministic guard: a consumer read parked inside the inner stream read, then a graceful
        /// <see cref="TcpTransportConnection.CloseAsync"/>, then the read resumes with bytes. On the
        /// buggy code the concurrent <c>Complete()</c> resets the segment the read commits into and
        /// the resuming read throws <see cref="ArgumentOutOfRangeException"/> from
        /// <c>BufferSegment.set_End</c>; with the fix the close quiesces the in-flight read before
        /// completing the reader, so the read settles cleanly (cancelled) and never throws.
        /// </summary>
        [Fact]
        public async Task CloseAsync_should_not_corrupt_an_in_flight_read_on_the_StreamPipeReader()
        {
            // Repeat: even though the gated stream makes the ordering deterministic per attempt, we
            // loop so any residual scheduling variance is covered. The buggy path throws on the very
            // first attempt; the fix upholds the invariant on every one.
            const int attempts = 50;

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                using var socketPair = await ConnectedSocketPair.CreateAsync();
                var stream = new GatedReadStream();
                var transport = new TcpTransportConnection(socketPair.Server, stream);

                // The consumer (acting as the actor) issues a ReadAsync. It allocates a read-tail
                // segment and parks inside GatedReadStream.ReadAsync — exactly the window the close
                // path must serialize against.
                ArgumentOutOfRangeException? readFault = null;
                Exception? cleanSettle = null;
                var readTask = Task.Run(async () =>
                {
                    try
                    {
                        var result = await transport.Input.ReadAsync(CancellationToken.None);
                        transport.Input.AdvanceTo(result.Buffer.End);
                    }
                    catch (ArgumentOutOfRangeException ex)
                    {
                        // The exact corruption: segment.End set against a reset (zero-length)
                        // segment. This is what the bug produces and what the fix must prevent.
                        readFault = ex;
                    }
                    catch (Exception settle)
                    {
                        // Any other outcome (cancelled / completed / disposed) is a clean settle;
                        // capture it so a failure surfaces what actually happened — only the
                        // ArgumentOutOfRangeException above is the corruption this test guards against.
                        cleanSettle = settle;
                    }
                });

                // Wait until the read is actually parked inside the inner stream read (tail segment
                // allocated, sitting in ReadAsync) — that is the precondition for the race.
                (await Task.WhenAny(stream.ReadStarted, Task.Delay(TimeSpan.FromSeconds(3))))
                    .Should().BeSameAs(stream.ReadStarted, $"the consumer read should park in the stream (attempt {attempt})");

                // Close the peer so the transport's bounded inbound drain sees EOF immediately
                // (otherwise it polls the idle peer for the whole drain budget).
                socketPair.Client.Close();

                // Graceful close. On the buggy path this completes the StreamPipeReader while the
                // read above is still parked — corrupting the segment it is about to commit into.
                var closeTask = transport.CloseAsync();

                // Release the parked read so it resumes WITH bytes. On the buggy path it now does
                // segment.End += bytes against the reset segment and throws ArgumentOutOfRangeException.
                stream.ReleaseRead(bytes: 64);

                await readTask.WaitAsync(TimeSpan.FromSeconds(5));
                await closeTask.WaitAsync(TimeSpan.FromSeconds(5));

                readFault.Should().BeNull(
                    $"completing the StreamPipeReader must never race an in-flight read (attempt {attempt}): "
                    + $"{readFault?.Message} (read settled with {cleanSettle?.GetType().Name ?? "no exception"})");

                await transport.DisposeAsync();
            }
        }

        /// <summary>
        /// End-to-end guard: many real graceful-close cycles through the full actor + transport
        /// stack. Each cycle exchanges a little data both ways (so both sides have a live inbound
        /// read), then the client issues <see cref="Tcp.Close"/>. Every close must be clean —
        /// <see cref="Tcp.Closed"/> on the initiator and <see cref="Tcp.PeerClosed"/> on the peer —
        /// and never <see cref="Tcp.ErrorClosed"/> (which is how the corruption surfaces).
        /// </summary>
        [Fact]
        public async Task Repeated_graceful_close_should_never_ErrorClose_the_peer()
        {
            var bindHandler = CreateTestProbe("bind-handler");
            var bindCommander = CreateTestProbe("bind-commander");
            bindCommander.Send(Sys.Tcp(),
                new Tcp.Bind(bindHandler.Ref, new IPEndPoint(IPAddress.Loopback, 0)));
            IPEndPoint endpoint = null!;
            await bindCommander.ExpectMsgAsync<Tcp.Bound>(b => endpoint = (IPEndPoint)b.LocalAddress);

            const int cycles = 100;
            var payload = new byte[] { 1, 2, 3, 4 };

            for (var cycle = 0; cycle < cycles; cycle++)
                await RunOneCycleAsync(endpoint, bindHandler, payload, cycle);
        }

        private async Task RunOneCycleAsync(
            IPEndPoint endpoint, TestProbe bindHandler, byte[] payload, int cycle)
        {
            var connectCommander = CreateTestProbe($"connect-{cycle}");
            connectCommander.Send(Sys.Tcp(), new Tcp.Connect(endpoint));
            await connectCommander.ExpectMsgAsync<Tcp.Connected>();
            var clientConnection = connectCommander.Sender;

            var clientHandler = CreateTestProbe($"client-handler-{cycle}");
            clientConnection.Tell(new Tcp.Register(clientHandler.Ref));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            var serverConnection = bindHandler.Sender;
            var serverHandler = CreateTestProbe($"server-handler-{cycle}");
            serverConnection.Tell(new Tcp.Register(serverHandler.Ref));

            // Exchange a little data each way so both sides have a live inbound read going.
            clientHandler.Send(clientConnection, Tcp.Write.Create(payload.AsMemory()));
            await serverHandler.ExpectMsgAsync<Tcp.Received>(
                r => r.Data.Length == payload.Length, hint: $"server recv at cycle {cycle}");

            serverHandler.Send(serverConnection, Tcp.Write.Create(payload.AsMemory()));
            await clientHandler.ExpectMsgAsync<Tcp.Received>(
                r => r.Data.Length == payload.Length, hint: $"client recv at cycle {cycle}");

            // Graceful close from the client.
            clientHandler.Send(clientConnection, Tcp.Close.Instance);

            var clientClose = await FishForCloseAsync(clientHandler, cycle, "client");
            clientClose.Should().BeOfType<Tcp.Closed>(
                $"client close at cycle {cycle} must be a clean Closed, was: {Describe(clientClose)}");

            var serverClose = await FishForCloseAsync(serverHandler, cycle, "server");
            serverClose.Should().NotBeOfType<Tcp.ErrorClosed>(
                $"server close at cycle {cycle} must be clean, was: {Describe(serverClose)}");
        }

        private static async Task<Tcp.ConnectionClosed> FishForCloseAsync(
            TestProbe handler, int cycle, string side)
        {
            var msg = await handler.FishForMessageAsync(
                m => m is Tcp.ConnectionClosed,
                TimeSpan.FromSeconds(10),
                hint: $"{side} close result at cycle {cycle}");
            return (Tcp.ConnectionClosed)msg;
        }

        private static string Describe(Tcp.ConnectionClosed closed)
            => closed is Tcp.ErrorClosed err
                ? $"ErrorClosed(\"{err.Cause}\")"
                : closed.GetType().Name;

        /// <summary>
        /// A <see cref="Stream"/> whose <see cref="ReadAsync(Memory{byte},CancellationToken)"/>
        /// parks on a gate (signalling <see cref="ReadStarted"/> once the read is parked) and, on
        /// <see cref="ReleaseRead"/>, resumes returning a positive byte count. Parking the read lets
        /// the test put the transport's <c>StreamPipeReader</c> into the exact state the close path
        /// must serialize against; releasing it with bytes triggers the
        /// <c>segment.End += bytes</c> commit that throws on the buggy path. Writes are accepted
        /// (the close path flushes nothing meaningful here).
        /// </summary>
        private sealed class GatedReadStream : Stream
        {
            private readonly TaskCompletionSource<bool> _readStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<int> _releaseRead =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int _readCount;

            public Task ReadStarted => _readStarted.Task;

            /// <summary>Releases the parked read so it resumes returning <paramref name="bytes"/>.</summary>
            public void ReleaseRead(int bytes) => _releaseRead.TrySetResult(bytes);

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                // Only the FIRST read parks on the gate; later reads (e.g. the close path's drain)
                // just block on cancellation so they don't interfere with the staged race.
                if (Interlocked.Increment(ref _readCount) == 1)
                {
                    _readStarted.TrySetResult(true);
                    var bytes = await _releaseRead.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return Math.Min(bytes, buffer.Length);
                }

                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return 0;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
                => ValueTask.CompletedTask;

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public override void Write(byte[] buffer, int offset, int count) { }

            public override void Flush() { }
            public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        }

        private sealed class ConnectedSocketPair : IDisposable
        {
            private ConnectedSocketPair(Socket client, Socket server)
            {
                Client = client;
                Server = server;
            }

            public Socket Client { get; }
            public Socket Server { get; }

            public static async Task<ConnectedSocketPair> CreateAsync()
            {
                using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                listener.Listen(1);

                var endpoint = (IPEndPoint)listener.LocalEndPoint!;
                var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    var connectTask = client.ConnectAsync(endpoint);
                    var server = await listener.AcceptAsync();
                    await connectTask;
                    return new ConnectedSocketPair(client, server);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                Client.Dispose();
                Server.Dispose();
            }
        }
    }
}
