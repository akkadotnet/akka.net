//-----------------------------------------------------------------------
// <copyright file="TcpConnectionOwnershipDisposalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Concurrent;
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
    /// Synthetic "poison-pool" corruption tests for the ownership-carrying <c>Tcp.Write</c>
    /// disposal matrix (modernize-akka-io-tcp design.md, Decision 8 and its 2026-07-07 mechanism
    /// refinement; tasks.md §9.3/§9.4). These construct owner-carrying writes DIRECTLY against
    /// <see cref="OwnedSequenceSegment"/> -- no Artery, no ActorSystem-to-ActorSystem transport --
    /// and drive them through a real <see cref="TcpIncomingConnection"/> over a real loopback
    /// socket pair, exactly like <see cref="TcpConnectionPreRegisterWriteRetentionSpec"/> and
    /// <see cref="TcpConnectionBatchingSpec"/>.
    ///
    /// <para>
    /// Every test uses a <see cref="PoisonArrayPool"/> that scribbles a sentinel byte pattern over
    /// any array returned to it, mirroring the intent of the Artery poison-pool harness referenced
    /// in design.md §8(b) (300 back-to-back messages, 1-4 corrupted per run under the old
    /// pull/ack-inference disposal). Two invariants are asserted for every disposal path:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>No premature dispose</b>: the bytes that actually reach the client socket (for a write
    /// that gets acked / reaches the pipe) are byte-for-byte identical to the original payload --
    /// never the poison sentinel. A premature dispose would return (and scribble) the array to the
    /// pool BEFORE the pipe copy, corrupting the write.
    /// </description></item>
    /// <item><description>
    /// <b>No leak</b>: every owner handed to a write is disposed EXACTLY once by the end of the
    /// test (<see cref="PoisonOwner.DisposeCount"/> == 1), and the underlying array is confirmed
    /// returned to the pool.
    /// </description></item>
    /// </list>
    /// </summary>
    public class TcpConnectionOwnershipDisposalSpec : AkkaSpec
    {
        private sealed class WriteAck : Tcp.Event
        {
            public WriteAck(int id) => Id = id;
            public int Id { get; }
        }

        /// <summary>
        /// An <see cref="ArrayPool{T}"/> that scribbles a sentinel byte pattern over any array
        /// handed back via <see cref="Return"/>, and records every array it has seen returned --
        /// so a test can assert both "the bytes weren't corrupted before they should have been"
        /// (by comparing against the sentinel) and "the array really was returned" (leak check).
        /// </summary>
        private sealed class PoisonArrayPool : ArrayPool<byte>
        {
            private const byte Sentinel = 0xFE;

            private readonly ArrayPool<byte> _inner = Create();
            private readonly ConcurrentDictionary<byte[], bool> _returned = new();

            public override byte[] Rent(int minimumLength) => _inner.Rent(minimumLength);

            public override void Return(byte[] array, bool clearArray = false)
            {
                Array.Fill(array, Sentinel);
                _returned[array] = true;
                _inner.Return(array, clearArray: false);
            }

            public bool WasReturned(byte[] array) => _returned.ContainsKey(array);
        }

        /// <summary>
        /// A test-only <see cref="IMemoryOwner{T}"/> that returns its array to a
        /// <see cref="PoisonArrayPool"/> on disposal. Idempotent (mirrors
        /// <c>PooledPayloadWriter.RentedMemoryOwner</c>) and counts how many times
        /// <see cref="Dispose"/> actually returned the array, so a test can assert "disposed
        /// exactly once".
        /// </summary>
        private sealed class PoisonOwner : IMemoryOwner<byte>
        {
            private readonly ArrayPool<byte> _pool;
            private byte[]? _array;
            private readonly int _length;
            private int _disposeCount;

            public PoisonOwner(ArrayPool<byte> pool, byte[] array, int length)
            {
                _pool = pool;
                _array = array;
                _length = length;
            }

            public byte[] Array => _array ?? throw new ObjectDisposedException(nameof(PoisonOwner));

            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public Memory<byte> Memory => _array is null ? Memory<byte>.Empty : new Memory<byte>(_array, 0, _length);

            public void Dispose()
            {
                var array = Interlocked.Exchange(ref _array, null);
                if (array is null)
                    return;

                Interlocked.Increment(ref _disposeCount);
                _pool.Return(array);
            }
        }

        public TcpConnectionOwnershipDisposalSpec(ITestOutputHelper output)
            : base(@"akka.loglevel = DEBUG
                     akka.io.tcp.trace-logging = true", output: output)
        {
        }

        /// <summary>
        /// Rents an array from <paramref name="pool"/>, copies <paramref name="payload"/> into it,
        /// and wraps it in a <see cref="PoisonOwner"/> plus an owner-carrying, single-segment
        /// <see cref="ReadOnlySequence{T}"/> built via <see cref="OwnedSequenceSegment.Create(IMemoryOwner{byte},int)"/>
        /// (segment-backed, per the construction gotcha -- NOT <c>new ReadOnlySequence&lt;byte&gt;(memory)</c>).
        /// The raw array is also returned so a test can assert it was returned to the pool AFTER
        /// disposal (once disposed, <see cref="PoisonOwner.Array"/> throws).
        /// </summary>
        private static (PoisonOwner Owner, ReadOnlySequence<byte> Data, byte[] Array) CreateOwned(PoisonArrayPool pool, byte[] payload)
        {
            var array = pool.Rent(payload.Length);
            payload.CopyTo(array, 0);
            var owner = new PoisonOwner(pool, array, payload.Length);
            return (owner, OwnedSequenceSegment.Create(owner, payload.Length), array);
        }

        private static byte[] RandomPayload(int seed, int length)
        {
            var buffer = new byte[length];
            new Random(seed).NextBytes(buffer);
            return buffer;
        }

        /// <summary>
        /// A borrowed (owner-less) chain link: a plain <see cref="ReadOnlySequenceSegment{T}"/> that
        /// does NOT implement <see cref="IOwnedSequenceSegment"/>, so the disposal walk skips it
        /// entirely -- it disposes no owner (there is none) and never scribbles its memory. This
        /// mirrors how a real borrowed link (e.g. a one-time preamble prepended to owned frames) is
        /// represented in production: NOT as an <see cref="OwnedSequenceSegment"/> with a null owner,
        /// but as a segment that simply doesn't opt into <see cref="IOwnedSequenceSegment"/>.
        /// </summary>
        private sealed class BorrowedTestSegment : ReadOnlySequenceSegment<byte>
        {
            public BorrowedTestSegment(ReadOnlyMemory<byte> memory) => Memory = memory;

            /// <summary>
            /// Chains a new borrowed segment after this one, mirroring <c>OwnedSequenceSegment.Append</c>.
            /// Compiles without reflection because both instances are <see cref="BorrowedTestSegment"/>,
            /// so the protected <see cref="ReadOnlySequenceSegment{T}.RunningIndex"/>/
            /// <see cref="ReadOnlySequenceSegment{T}.Next"/> setters are accessible within the same type.
            /// </summary>
            public BorrowedTestSegment Append(ReadOnlyMemory<byte> memory)
            {
                var next = new BorrowedTestSegment(memory)
                {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = next;
                return next;
            }
        }

        [Fact]
        public async Task OwnedWrite_open_path_disposes_exactly_once_after_pipe_copy_and_does_not_corrupt_bytes()
        {
            using var socketPair = await ConnectedSocketPair.CreateAsync();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);
            var pool = new PoisonArrayPool();

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings, socketPair.Server, bindHandler.Ref, Array.Empty<Inet.SocketOption>(), false)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));

            var payload = RandomPayload(1, 256);
            var (owner, data, _) = CreateOwned(pool, payload);

            handler.Send(connection, Tcp.Write.Create(data, new WriteAck(1)));
            (await handler.ExpectMsgAsync<WriteAck>()).Id.Should().Be(1);

            // No leak: disposed exactly once, array actually returned to the pool.
            owner.DisposeCount.Should().Be(1);

            // No premature dispose: the bytes that reach the client are the ORIGINAL payload, not
            // the poison sentinel that Return() scribbles.
            var received = await ReceiveExactAsync(socketPair.Client, payload.Length, TimeSpan.FromSeconds(5));
            received.Should().Equal(payload, "the owner must only be disposed AFTER the pipe copy completed");

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact]
        public async Task OwnedWrite_multi_owner_coalesced_chain_disposes_all_owners_exactly_once_and_preserves_order()
        {
            using var socketPair = await ConnectedSocketPair.CreateAsync();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);
            var pool = new PoisonArrayPool();

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings, socketPair.Server, bindHandler.Ref, Array.Empty<Inet.SocketOption>(), false)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));

            // Simulates a coalesced write: ONE Tcp.Write chaining N (3) owner-carrying segments,
            // as TcpStages.cs's write-coalescing will build in PR2 -- constructed directly here,
            // no Streams/Artery involved.
            var payload1 = RandomPayload(11, 64);
            var payload2 = RandomPayload(22, 128);
            var payload3 = RandomPayload(33, 32);

            var array1 = pool.Rent(payload1.Length);
            payload1.CopyTo(array1, 0);
            var owner1 = new PoisonOwner(pool, array1, payload1.Length);

            var array2 = pool.Rent(payload2.Length);
            payload2.CopyTo(array2, 0);
            var owner2 = new PoisonOwner(pool, array2, payload2.Length);

            var array3 = pool.Rent(payload3.Length);
            payload3.CopyTo(array3, 0);
            var owner3 = new PoisonOwner(pool, array3, payload3.Length);

            var head = new OwnedSequenceSegment(owner1.Memory, owner1);
            var mid = head.Append(owner2.Memory, owner2);
            var tail = mid.Append(owner3.Memory, owner3);
            var data = new System.Buffers.ReadOnlySequence<byte>(head, 0, tail, tail.Memory.Length);

            data.Length.Should().Be(payload1.Length + payload2.Length + payload3.Length);

            handler.Send(connection, Tcp.Write.Create(data, new WriteAck(1)));
            (await handler.ExpectMsgAsync<WriteAck>()).Id.Should().Be(1);

            owner1.DisposeCount.Should().Be(1);
            owner2.DisposeCount.Should().Be(1);
            owner3.DisposeCount.Should().Be(1);

            var expected = new byte[payload1.Length + payload2.Length + payload3.Length];
            payload1.CopyTo(expected, 0);
            payload2.CopyTo(expected, payload1.Length);
            payload3.CopyTo(expected, payload1.Length + payload2.Length);

            var received = await ReceiveExactAsync(socketPair.Client, expected.Length, TimeSpan.FromSeconds(5));
            received.Should().Equal(expected, "all N owners must only be disposed AFTER the full coalesced write reached the pipe");

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact]
        public async Task BorrowedWrite_all_borrowed_chain_disposes_nothing_and_leaves_bytes_intact()
        {
            using var socketPair = await ConnectedSocketPair.CreateAsync();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);
            var pool = new PoisonArrayPool();

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings, socketPair.Server, bindHandler.Ref, Array.Empty<Inet.SocketOption>(), false)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));

            // A multi-segment chain of BORROWED links only -- none implement IOwnedSequenceSegment,
            // so the disposal walk must dispose nothing and touch no bytes. This also exercises the
            // walk over a segment-backed sequence whose links are all non-owned, plus the data.End
            // bound across a multi-segment chain. Production represents a borrowed preamble exactly
            // this way (a plain segment that does not opt into IOwnedSequenceSegment) and never mixes
            // it into an owned frame chain -- the preamble flushes as its own Tcp.Write. None of these
            // arrays come from the poison pool, so any mutation by the walk would show up immediately
            // as a changed byte rather than the pool's sentinel pattern.
            var payload1 = RandomPayload(111, 64);
            var payload2 = RandomPayload(222, 128);
            var payload3 = RandomPayload(333, 32);
            var snapshot1 = (byte[])payload1.Clone();
            var snapshot2 = (byte[])payload2.Clone();
            var snapshot3 = (byte[])payload3.Clone();

            var head = new BorrowedTestSegment(payload1);
            var mid = head.Append(payload2);
            var tail = mid.Append(payload3);
            var data = new System.Buffers.ReadOnlySequence<byte>(head, 0, tail, tail.Memory.Length);

            data.Length.Should().Be(payload1.Length + payload2.Length + payload3.Length);

            handler.Send(connection, Tcp.Write.Create(data, new WriteAck(1)));
            (await handler.ExpectMsgAsync<WriteAck>()).Id.Should().Be(1);

            // No segment implements IOwnedSequenceSegment, so the walk disposed nothing and scribbled
            // nothing: every borrowed array is byte-for-byte what it was before the write.
            payload1.Should().Equal(snapshot1, "a borrowed segment has no owner and must be left completely untouched");
            payload2.Should().Equal(snapshot2, "a borrowed segment has no owner and must be left completely untouched");
            payload3.Should().Equal(snapshot3, "a borrowed segment has no owner and must be left completely untouched");

            var expected = new byte[payload1.Length + payload2.Length + payload3.Length];
            payload1.CopyTo(expected, 0);
            payload2.CopyTo(expected, payload1.Length);
            payload3.CopyTo(expected, payload1.Length + payload2.Length);

            var received = await ReceiveExactAsync(socketPair.Client, expected.Length, TimeSpan.FromSeconds(5));
            received.Should().Equal(expected);

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact]
        public async Task OwnedWrite_open_path_queue_full_rejection_disposes_owner_before_CommandFailed()
        {
            using var socketPair = await ConnectedSocketPair.CreateAsync();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys) with { WriteCommandsQueueMaxSize = 32 };
            var pool = new PoisonArrayPool();

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings, socketPair.Server, bindHandler.Ref, Array.Empty<Inet.SocketOption>(), false)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));

            var payload = RandomPayload(66, 64); // exceeds the 32-byte max queued size
            var (owner, data, array) = CreateOwned(pool, payload);

            handler.Send(connection, Tcp.Write.Create(data, new WriteAck(1)));
            var failed = await handler.ExpectMsgAsync<Tcp.CommandFailed>();
            failed.Cause.Value.Should().BeOfType<IOException>();

            owner.DisposeCount.Should().Be(1, "the write never reached the pipe, so the owner must be disposed before CommandFailed is signaled");
            pool.WasReturned(array).Should().BeTrue("the underlying array must actually be returned to the pool, not merely marked disposed");

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact]
        public async Task OwnedWrite_pre_registration_queue_full_rejection_disposes_owner_before_CommandFailed()
        {
            using var socketPair = await ConnectedSocketPair.CreateAsync();
            var bindHandler = CreateTestProbe();
            var ackProbe = CreateTestProbe();
            var settings = TcpSettings.Create(Sys) with { WriteCommandsQueueMaxSize = 32 };
            var pool = new PoisonArrayPool();

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings, socketPair.Server, bindHandler.Ref, Array.Empty<Inet.SocketOption>(), false)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();

            var payload = RandomPayload(77, 64); // exceeds the 32-byte max queued size
            var (owner, data, _) = CreateOwned(pool, payload);

            // Sent BEFORE Register -- BufferSingleWriteBeforeRegister's queue-full check.
            connection.Tell(Tcp.Write.Create(data, new WriteAck(1)), ackProbe.Ref);
            var failed = await ackProbe.ExpectMsgAsync<Tcp.CommandFailed>();
            failed.Cause.Value.Should().BeOfType<IOException>();

            owner.DisposeCount.Should().Be(1, "the write never entered the pending-registration queue, so the owner must be disposed before CommandFailed is signaled");

            await WatchAsync(connection);
            bindHandler.Send(connection, Tcp.Abort.Instance);
            await bindHandler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact]
        public async Task OwnedWrite_pre_registration_owner_survives_until_flush_copy_then_disposed_exactly_once()
        {
            using var socketPair = await ConnectedSocketPair.CreateAsync();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var ackProbe = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);
            var pool = new PoisonArrayPool();

            // TestActorRef + CallingThreadDispatcher: Tell() drives the actor's Receive handler to
            // completion synchronously, so by the time Tell() returns we can deterministically
            // assert the owner has (or has not) been disposed yet -- same technique as
            // TcpConnectionPreRegisterWriteRetentionSpec.
            var connection = new TestActorRef<TcpIncomingConnection>(Sys, Props.Create(() => new TcpIncomingConnection(
                settings, socketPair.Server, bindHandler.Ref, Array.Empty<Inet.SocketOption>(), false)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();

            var payload = RandomPayload(88, 256);
            var (owner, data, _) = CreateOwned(pool, payload);

            // Sent BEFORE Register -- goes into _pendingRegistrationWrites.
            connection.Tell(Tcp.Write.Create(data, new WriteAck(1)), ackProbe.Ref);

            // Design decision (see BufferSingleWriteBeforeRegister): an OWNED pre-registration
            // write is queued AS-IS under the ownership-transfer contract, not eagerly copied+
            // disposed. The owner must still be alive here.
            owner.DisposeCount.Should().Be(0, "an owned pre-registration write must not be disposed until the deferred flush copy runs");

            // Now register -- triggers FlushPendingRegistrationWrites -> EnqueueWrite, which
            // performs the real pipe copy and disposes the owner there.
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));

            await ackProbe.ExpectMsgAsync<WriteAck>();
            owner.DisposeCount.Should().Be(1);

            var received = await ReceiveExactAsync(socketPair.Client, payload.Length, TimeSpan.FromSeconds(5));
            received.Should().Equal(payload, "the owner must only be disposed AFTER the deferred flush's pipe copy completed");

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact]
        public async Task OwnedWrite_ClosingBehaviour_rejection_disposes_owner_before_CommandFailed()
        {
            using var socketPair = await ConnectedSocketPair.CreateAsync();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);
            var pool = new PoisonArrayPool();

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings, socketPair.Server, bindHandler.Ref, Array.Empty<Inet.SocketOption>(), false)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));
            await WatchAsync(connection);

            // Same sender (handler) for both messages -> FIFO guarantees Close is fully processed
            // (Become(ClosingBehaviour) applied) before the Write below is dequeued.
            handler.Send(connection, Tcp.Close.Instance);

            var payload = RandomPayload(99, 64);
            var (owner, data, _) = CreateOwned(pool, payload);
            handler.Send(connection, Tcp.Write.Create(data, new WriteAck(1)));

            var failed = await handler.ExpectMsgAsync<Tcp.CommandFailed>();
            failed.Cause.Value.Should().BeOfType<IOException>();

            owner.DisposeCount.Should().Be(1, "a write rejected while closing never reaches the pipe, so the owner must be disposed before CommandFailed is signaled");

            await ExpectTerminatedAsync(connection, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task OwnedWrite_PostStop_drain_disposes_still_queued_pre_registration_owner()
        {
            using var socketPair = await ConnectedSocketPair.CreateAsync();
            var bindHandler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);
            var pool = new PoisonArrayPool();

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings, socketPair.Server, bindHandler.Ref, Array.Empty<Inet.SocketOption>(), false)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();

            var payload = RandomPayload(101, 128);
            var (owner, data, _) = CreateOwned(pool, payload);

            // Same sender (bindHandler) for both messages -> FIFO guarantees the Write is buffered
            // into _pendingRegistrationWrites before Abort triggers PostStop.
            bindHandler.Send(connection, Tcp.Write.Create(data, new WriteAck(1)));
            owner.DisposeCount.Should().Be(0, "the write is still sitting in the pending-registration queue, unflushed");

            await WatchAsync(connection);
            bindHandler.Send(connection, Tcp.Abort.Instance);

            // PostStop drains _pendingRegistrationWrites BEFORE sending the close notification, so
            // bindHandler (sender of the still-queued write, and recipient of the close event) sees
            // the write's CommandFailed first, then Aborted.
            var failed = await bindHandler.ExpectMsgAsync<Tcp.CommandFailed>();
            failed.Cause.Value.Should().BeOfType<IOException>();
            await bindHandler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);

            owner.DisposeCount.Should().Be(1, "PostStop must drain and dispose owners still held by queued pre-registration writes -- Register never arrived");
        }

        private static async Task<byte[]> ReceiveExactAsync(Socket socket, int count, TimeSpan timeout)
        {
            using var stream = new NetworkStream(socket, ownsSocket: false);
            using var cts = new CancellationTokenSource(timeout);
            var buffer = new byte[count];
            var received = 0;
            while (received < count)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(received, count - received), cts.Token);
                if (n == 0)
                    throw new IOException($"Socket closed after receiving {received} of {count} expected bytes.");
                received += n;
            }

            return buffer;
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
