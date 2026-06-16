//-----------------------------------------------------------------------
// <copyright file="TcpConcurrentWriteSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
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
    /// Liveness/correctness coverage for the write-side double-buffer in
    /// <see cref="TcpTransportConnection"/>.
    /// <para>
    /// The single-connection batching gate (<c>TcpConnectionBatchingSpec</c>) drives only ONE
    /// connection and never overlaps a graceful close with an in-flight write, so it cannot catch
    /// the bug that the <c>TcpOperationsBenchmarks</c> macro-benchmark hangs on at
    /// <c>ClientsCount &gt;= 10</c>: the double-buffer's backpressure flush completion runs OFF the
    /// owning actor's thread and mutates the shared bookkeeping (<c>_inflightWrite</c>,
    /// <c>_activeIdx</c>, the buffer slots) with no synchronization against either the actor's own
    /// flushes or the close-time drain. Under concurrency that lets two
    /// <see cref="Stream.WriteAsync(ReadOnlyMemory{byte},CancellationToken)"/> calls run on the
    /// SAME stream at once — illegal on <see cref="NetworkStream"/> — which corrupts the shared
    /// socket and surfaces as faulted writes, faulted reads, and ultimately a stalled connection.
    /// </para>
    /// </summary>
    public class TcpConcurrentWriteSpec : AkkaSpec
    {
        public TcpConcurrentWriteSpec(ITestOutputHelper output)
            : base("akka.loglevel = INFO\nakka.log-dead-letters = off", output: output)
        {
        }

        /// <summary>
        /// Deterministic regression guard for the core invariant: the double-buffer must never
        /// have two stream writes outstanding at the same time, even when a graceful close races a
        /// flush that arrived while a previous write was in flight.
        /// <para>
        /// Reproduction (single producer thread, exactly as the <c>TcpConnection</c> actor drives
        /// it — fire-and-forget, discarding the flush result):
        /// </para>
        /// <list type="number">
        /// <item>buffer batch A and flush → write #1 starts and is gated open (stays in flight);</item>
        /// <item>buffer batch B and flush → a write is in flight, so B waits in the standby slot;</item>
        /// <item>call <see cref="TcpTransportConnection.CloseAsync"/> (drain) while write #1 is still gated;</item>
        /// <item>release write #1.</item>
        /// </list>
        /// On the buggy double-buffer the detached backpressure-flush continuation and the drain
        /// both try to write batch B, so two writes overlap on the stream — the
        /// <see cref="ConcurrencyDetectingStream"/> records the overlap and the test fails. With
        /// the fix every write transition is serialized, so writes never overlap and all bytes
        /// arrive in order.
        /// </para>
        /// </summary>
        [Fact]
        public async Task DoubleBuffer_should_never_overlap_stream_writes_when_close_races_an_inflight_write()
        {
            // The race is probabilistic per attempt (it needs the drain continuation and the
            // backpressure-flush continuation to both observe the standby batch before either
            // swaps the active slot), so we repeat the scenario many times. The buggy
            // double-buffer overlaps/duplicates the standby write within a handful of attempts;
            // the fix upholds the invariant on every one.
            const int attempts = 120;
            var a = MakeBytes('A', 8);
            var b = MakeBytes('B', 16);
            var expected = Concat(a, b);

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                using var socketPair = await ConnectedSocketPair.CreateAsync();
                var stream = new ConcurrencyDetectingStream();
                var transport = new TcpTransportConnection(socketPair.Server, stream);

                // Step 1: batch A and flush. Write #1 starts and parks on the first gate (in flight).
                transport.WriteUnflushed(a);
                _ = transport.FlushAsync(); // fire-and-forget, exactly like the actor
                (await Task.WhenAny(stream.FirstWriteStarted, Task.Delay(TimeSpan.FromSeconds(3))))
                    .Should().BeSameAs(stream.FirstWriteStarted, "the first batch should start writing on the fast path");

                // Step 2: batch B and flush while write #1 is in flight. B waits in the standby
                // slot; on the buggy path this flush also spawns a detached backpressure
                // continuation that will try to write B once write #1 finishes.
                transport.WriteUnflushed(b);
                _ = transport.FlushAsync(); // fire-and-forget

                // Close the peer socket so the transport's inbound drain-on-close sees EOF and
                // returns immediately (otherwise it polls the idle peer for the full drain budget,
                // adding ~250ms per attempt).
                socketPair.Client.Close();

                // Step 3: graceful close. The drain also waits for write #1, then intends to write B.
                var closeTask = transport.CloseAsync();

                // Step 4: release write #1. Both the drain and (on the buggy path) the detached
                // continuation wake up and race to write B, parking on the later gate.
                stream.ReleaseFirstWrite();

                // Let any racing second writer reach WriteAsync(B), then snapshot peak concurrency.
                await AwaitConditionAsync(() => stream.LaterWritersParked >= 1, TimeSpan.FromSeconds(3));
                var peakConcurrency = stream.MaxConcurrentWrites;

                stream.ReleaseLaterWrites();
                await closeTask.WaitAsync(TimeSpan.FromSeconds(5));
                await transport.WriteCompleted.WaitAsync(TimeSpan.FromSeconds(5));

                peakConcurrency.Should().Be(1,
                    $"the double-buffer must never have two stream writes outstanding at once (attempt {attempt})");
                stream.WrittenBytes.Should().Equal(expected,
                    $"every buffered byte should reach the stream exactly once, in order (attempt {attempt})");

                await transport.DisposeAsync();
            }
        }

        /// <summary>
        /// Concurrent-connections smoke test mirroring the benchmark workload: an echo server and N
        /// concurrent clients each running an opening burst plus a sustained bidirectional echo
        /// loop over a large number of round-trips. Complements the deterministic guard above by
        /// exercising the real actor + transport stack end-to-end under concurrency; every
        /// connection must complete its round-trips well inside the timeout.
        /// </summary>
        [Fact]
        public async Task Many_concurrent_connections_with_sustained_writes_should_complete()
        {
            const int messageLength = 100;
            const int clientsCount = 16;
            const int messagesPerClient = 50_000; // round-trips driven per connection

            var message = new byte[messageLength];

            var bindProbe = CreateTestProbe("bind-probe");
            var server = Sys.ActorOf(Props.Create(() => new EchoServer(messageLength)), "echo-server");
            server.Tell(new SubscribeBound(bindProbe.Ref));
            var bound = await bindProbe.ExpectMsgAsync<Tcp.Bound>(TimeSpan.FromSeconds(10));
            var endpoint = (IPEndPoint)bound.LocalAddress;

            var completions = new List<Task<bool>>(clientsCount);
            for (var i = 0; i < clientsCount; i++)
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                completions.Add(tcs.Task);
                Sys.ActorOf(Props.Create(() => new BurstEchoClient(endpoint, messagesPerClient, message, tcs)),
                    $"burst-client-{i}");
            }

            var all = Task.WhenAll(completions);
            var winner = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(30)));

            winner.Should().BeSameAs(all,
                "all concurrent connections should complete their sustained writes without hanging");
            (await all).Should().OnlyContain(ok => ok,
                "no connection should observe a premature/error close before reaching its round-trip target");
        }

        private static async Task AwaitConditionAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    return;
                await Task.Delay(10);
            }
        }

        private static byte[] MakeBytes(char fill, int count)
        {
            var b = new byte[count];
            for (var i = 0; i < count; i++)
                b[i] = (byte)fill;
            return b;
        }

        private static byte[] Concat(byte[] x, byte[] y)
        {
            var r = new byte[x.Length + y.Length];
            Buffer.BlockCopy(x, 0, r, 0, x.Length);
            Buffer.BlockCopy(y, 0, r, x.Length, y.Length);
            return r;
        }

        /// <summary>
        /// A <see cref="Stream"/> that (a) records the total bytes written in order, (b) tracks the
        /// peak number of <see cref="WriteAsync(ReadOnlyMemory{byte},CancellationToken)"/> calls
        /// outstanding at once (any value &gt; 1 means the double-buffer overlapped writes), and
        /// (c) gates the FIRST write open until <see cref="ReleaseGate"/> so the test can hold a
        /// write in flight on demand. Reads block forever (the spec drives only the write side).
        /// </summary>
        private sealed class ConcurrencyDetectingStream : Stream
        {
            private readonly object _sync = new();
            private readonly List<byte> _written = new();

            // Two gates. The FIRST write (batch A) parks on _firstGate; every LATER write parks on
            // _laterGate. The test releases _firstGate to let write #1 finish, which wakes BOTH the
            // drain and (on the buggy path) the detached backpressure continuation; they then race
            // to write batch B and both park on _laterGate at once — so _maxConcurrent climbs above
            // 1 and the bug is caught deterministically. The test then releases _laterGate.
            private readonly TaskCompletionSource<bool> _firstGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _laterGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _firstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int _concurrent;
            private int _maxConcurrent;
            private int _writeCount;
            private int _laterWritersParked;

            public int MaxConcurrentWrites
            {
                get { lock (_sync) { return _maxConcurrent; } }
            }

            public byte[] WrittenBytes
            {
                get { lock (_sync) { return _written.ToArray(); } }
            }

            public void ReleaseFirstWrite() => _firstGate.TrySetResult(true);
            public void ReleaseLaterWrites() => _laterGate.TrySetResult(true);

            public Task FirstWriteStarted => _firstWriteStarted.Task;

            public int LaterWritersParked
            {
                get { lock (_sync) { return _laterWritersParked; } }
            }

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                int index;
                lock (_sync)
                {
                    index = ++_writeCount;
                    _concurrent++;
                    if (_concurrent > _maxConcurrent)
                        _maxConcurrent = _concurrent;
                }

                try
                {
                    if (index == 1)
                    {
                        _firstWriteStarted.TrySetResult(true);
                        await _firstGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        lock (_sync) { _laterWritersParked++; }
                        await _laterGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }

                    lock (_sync)
                    {
                        for (var i = 0; i < buffer.Length; i++)
                            _written.Add(buffer.Span[i]);
                    }
                }
                finally
                {
                    lock (_sync)
                    {
                        _concurrent--;
                    }
                }
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override void Write(byte[] buffer, int offset, int count)
                => throw new NotSupportedException();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return 0;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

        private sealed class SubscribeBound
        {
            public SubscribeBound(IActorRef subscriber) => Subscriber = subscriber;
            public IActorRef Subscriber { get; }
        }

        private sealed class EchoServer : ReceiveActor
        {
            private IActorRef _subscriber = ActorRefs.Nobody;
            private Tcp.Bound _bound;

            public EchoServer(int messageSize)
            {
                Context.System.Tcp().Tell(new Tcp.Bind(Self, new IPEndPoint(IPAddress.Loopback, 0)));

                Receive<SubscribeBound>(sub =>
                {
                    _subscriber = sub.Subscriber;
                    if (_bound != null)
                        _subscriber.Tell(_bound);
                });
                Receive<Tcp.Bound>(bound =>
                {
                    _bound = bound;
                    if (!_subscriber.IsNobody())
                        _subscriber.Tell(bound);
                });
                Receive<Tcp.Connected>(_ =>
                {
                    var connection = Context.ActorOf(Props.Create(() => new EchoConnection(Sender, messageSize)));
                    Sender.Tell(new Tcp.Register(connection));
                });
            }
        }

        private sealed class EchoConnection : ReceiveActor
        {
            public EchoConnection(IActorRef connection, int messageSize)
            {
                var framer = new Framer(messageSize);
                Receive<Tcp.Received>(received =>
                {
                    foreach (var m in framer.Deframe(received.Data))
                        connection.Tell(Tcp.Write.Create(m));
                });
                Receive<Tcp.ConnectionClosed>(_ => Context.Stop(Self));
            }
        }

        /// <summary>
        /// Connects, registers, fires an opening burst, then echoes every framed message it
        /// receives back to the server until it has driven <c>messagesToSend</c> round-trips, at
        /// which point it reports completion and stops echoing. It deliberately does NOT issue a
        /// <see cref="Tcp.Close"/>: this spec is scoped to write-side <em>liveness</em> under
        /// concurrency (the connections are torn down with the actor system), keeping it
        /// independent of the graceful-close path. Mirrors the benchmark's burst-of-20 + echo loop.
        /// </summary>
        private sealed class BurstEchoClient : ReceiveActor
        {
            private const int OpeningBurst = 20;

            private readonly Framer _framer;
            private readonly Tcp.WriteCommand _write;
            private readonly TaskCompletionSource<bool> _done;
            private readonly int _messagesToSend;
            private IActorRef _connection = ActorRefs.Nobody;
            private int _receivedCount;
            private bool _finished;

            public BurstEchoClient(IPEndPoint endpoint, int messagesToSend, byte[] message, TaskCompletionSource<bool> done)
            {
                _framer = new Framer(message.Length);
                _messagesToSend = messagesToSend;
                _done = done;
                _write = Tcp.Write.Create(message.AsMemory());

                Context.System.Tcp().Tell(new Tcp.Connect(endpoint, timeout: TimeSpan.FromSeconds(5)));

                Receive<Tcp.Connected>(_ =>
                {
                    _connection = Sender;
                    Sender.Tell(new Tcp.Register(Self));

                    for (var i = 0; i < OpeningBurst; i++)
                        _connection.Tell(_write);
                });

                Receive<Tcp.Received>(received =>
                {
                    if (_finished)
                        return;

                    foreach (var _ in _framer.Deframe(received.Data))
                    {
                        _receivedCount++;
                        if (_receivedCount >= _messagesToSend)
                        {
                            _finished = true;
                            _done.TrySetResult(true);
                            return;
                        }

                        _connection.Tell(_write);
                    }
                });

                // Any close or write failure before the target means the workload didn't complete.
                Receive<Tcp.ConnectionClosed>(_ => _done.TrySetResult(_finished));
                Receive<Tcp.CommandFailed>(_ => _done.TrySetResult(_finished));
            }
        }

        private sealed class Framer
        {
            private readonly int _messageSize;
            private ReadOnlySequence<byte> _partialRead = ReadOnlySequence<byte>.Empty;

            public Framer(int messageSize) => _messageSize = messageSize;

            public IEnumerable<ReadOnlySequence<byte>> Deframe(ReadOnlySequence<byte> data)
            {
                if (_partialRead.Length > 0)
                {
                    var partialLen = (int)_partialRead.Length;
                    var combined = new byte[partialLen + (int)data.Length];
                    _partialRead.CopyTo(combined.AsSpan(0, partialLen));
                    data.CopyTo(combined.AsSpan(partialLen));
                    data = new ReadOnlySequence<byte>(combined);
                    _partialRead = ReadOnlySequence<byte>.Empty;
                }

                var msgs = new List<ReadOnlySequence<byte>>();
                var offset = 0;
                while (offset + _messageSize <= data.Length)
                {
                    msgs.Add(data.Slice(offset, _messageSize));
                    offset += _messageSize;
                }

                if (offset < data.Length)
                    _partialRead = data.Slice(offset, (int)data.Length - offset);

                return msgs;
            }
        }
    }
}
