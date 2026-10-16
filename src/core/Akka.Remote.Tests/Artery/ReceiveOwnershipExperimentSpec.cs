//-----------------------------------------------------------------------
// <copyright file="ReceiveOwnershipExperimentSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Experimental;
using Akka.IO;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public class ReceiveOwnershipExperimentSpec : AkkaSpec
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        public ReceiveOwnershipExperimentSpec(ITestOutputHelper output)
            : base("akka.log-dead-letters = off", output: output) { }

        [Theory(DisplayName = "Should_preserve_all_frames_and_acknowledge_once_when_actor_consumers_read_pipe_chunks")]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(true, true, true)]
        public async Task Should_preserve_frames(bool borrow, bool fragmented, bool queued)
        {
            using var pool = new PoisonPool();
            var control = new ReceiveOwnershipExperiment.Control { Pool = pool };
            var result = await ReceiveOwnershipExperiment.RunAsync(Sys,
                new ReceiveOwnershipExperiment.Corpus(129, fragmented), borrow, queued, control,
                TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);

            Assert.Equal(129, result.Frames);
            Assert.Equal(result.Reads, result.Acknowledgements);
            Assert.Equal(1, control.Observation.Finished);
            Assert.Equal(pool.Rented, pool.Returned);
            Assert.All(pool.Owners, owner => Assert.Equal(1, owner.DisposeCount));
            if (borrow && !fragmented)
                Assert.Equal(0, result.CopiedBytes);
            else
                Assert.True(result.CopiedBytes > 0);
        }

        [Fact(DisplayName = "Should_keep_copy_stable_when_pipe_pool_is_poisoned_before_delayed_actor_consumption")]
        public async Task Should_keep_copied_bytes_stable()
        {
            using var pool = new PoisonPool();
            var gate = NewSignal();
            var entered = NewSignal();
            var control = new ReceiveOwnershipExperiment.Control
            {
                Pool = pool, ConsumerGate = gate.Task, ConsumerEntered = entered
            };
            var run = ReceiveOwnershipExperiment.RunAsync(Sys,
                new ReceiveOwnershipExperiment.Corpus(1, false), false, true, control,
                TestContext.Current.CancellationToken);
            try
            {
                await entered.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
                await pool.FirstReturn.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
                Assert.False(run.IsCompleted);
                Assert.Equal(0, control.Observation.Acknowledged);
            }
            finally
            {
                gate.TrySetResult(true);
            }
            var result = await run.WaitAsync(Timeout, TestContext.Current.CancellationToken);
            Assert.Equal(1, result.Frames);
            Assert.Equal(1, result.Acknowledgements);
            Assert.Equal(1, control.Observation.Finished);
        }

        [Theory(DisplayName = "Should_hold_borrowed_memory_until_actor_acknowledges_when_receive_is_canceled_or_aborted")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_hold_borrowed_memory_during_termination(bool abort)
        {
            using var pool = new PoisonPool();
            using var canceled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var gate = NewSignal();
            var entered = NewSignal();
            var control = new ReceiveOwnershipExperiment.Control
            {
                Pool = pool, ConsumerGate = gate.Task, ConsumerEntered = entered,
                WriterFailure = abort ? new IOException("Injected receive abort") : null
            };
            var run = ReceiveOwnershipExperiment.RunAsync(Sys,
                new ReceiveOwnershipExperiment.Corpus(1, false), true, true, control, canceled.Token);
            try
            {
                await entered.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
                if (!abort)
                    canceled.Cancel();
                Assert.False(run.IsCompleted);
                Assert.Equal(0, pool.Returned);
                Assert.Equal(0, control.Observation.Acknowledged);
            }
            finally
            {
                gate.TrySetResult(true);
            }
            if (abort)
                await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(Timeout, TestContext.Current.CancellationToken));
            else
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Timeout, TestContext.Current.CancellationToken));
            Assert.Equal(1, control.Observation.Acknowledged);
            Assert.Equal(1, control.Observation.Finished);
            Assert.Equal(pool.Rented, pool.Returned);
            Assert.All(pool.Owners, owner => Assert.Equal(1, owner.DisposeCount));
        }

        [Theory(DisplayName = "Should_finish_once_without_reads_when_pipe_reaches_empty_EOF")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_finish_on_empty_eof(bool borrow)
        {
            var control = new ReceiveOwnershipExperiment.Control();
            var result = await ReceiveOwnershipExperiment.RunAsync(Sys,
                new ReceiveOwnershipExperiment.Corpus(0, false), borrow, true, control,
                TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);
            Assert.Equal(0, result.Frames);
            Assert.Equal(0, result.Reads);
            Assert.Equal(0, result.Acknowledgements);
            Assert.Equal(1, control.Observation.Finished);
        }

        [Theory(DisplayName = "Should_reject_retained_partial_frame_at_EOF_and_release_owned_buffers_once")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_reject_partial_frame_at_eof(bool borrow)
        {
            using var pool = new PoisonPool();
            var control = new ReceiveOwnershipExperiment.Control { Pool = pool };
            var corpus = new ReceiveOwnershipExperiment.Corpus(1, true);
            corpus.Chunks[^1] = corpus.Chunks[^1][..^1];
            await Assert.ThrowsAsync<InvalidDataException>(() => ReceiveOwnershipExperiment.RunAsync(
                Sys, corpus, borrow, true, control, TestContext.Current.CancellationToken)
                .WaitAsync(Timeout, TestContext.Current.CancellationToken));
            Assert.Equal(corpus.Chunks.Length, control.Observation.Acknowledged);
            Assert.Equal(1, control.Observation.Finished);
            Assert.Equal(pool.Rented, pool.Returned);
            Assert.All(pool.Owners, owner => Assert.Equal(1, owner.DisposeCount));
        }

        [Fact(DisplayName = "Should_preserve_public_Tcp_Received_bytes_retained_after_subsequent_socket_reads")]
        public async Task Should_preserve_public_tcp_received_contract()
        {
            var bindCommander = CreateTestProbe();
            var serverHandler = CreateTestProbe();
            var clientHandler = CreateTestProbe();
            bindCommander.Send(Sys.Tcp(), new Tcp.Bind(serverHandler.Ref, new IPEndPoint(IPAddress.Loopback, 0)));
            var bound = await bindCommander.ExpectMsgAsync<Tcp.Bound>(Timeout);
            var listener = bindCommander.Sender;
            clientHandler.Send(Sys.Tcp(), new Tcp.Connect(bound.LocalAddress));
            await clientHandler.ExpectMsgAsync<Tcp.Connected>(Timeout);
            var client = clientHandler.Sender;
            clientHandler.Send(client, new Tcp.Register(clientHandler.Ref));
            await serverHandler.ExpectMsgAsync<Tcp.Connected>(Timeout);
            var server = serverHandler.Sender;
            serverHandler.Send(server, new Tcp.Register(serverHandler.Ref));
            try
            {
                var firstBytes = new byte[512];
                Array.Fill(firstBytes, (byte)0x2A);
                clientHandler.Send(client, Tcp.Write.Create(firstBytes, WriteAck.Instance));
                await clientHandler.ExpectMsgAsync<WriteAck>(Timeout);
                var retained = new List<ReadOnlySequence<byte>>();
                long firstLength = 0;
                while (firstLength < firstBytes.Length)
                {
                    var received = await serverHandler.ExpectMsgAsync<Tcp.Received>(Timeout);
                    retained.Add(received.Data);
                    firstLength += received.Data.Length;
                }
                Assert.Equal(firstBytes.Length, firstLength);

                // Force many later pipe reads while an arbitrary public TCP consumer retains
                // the original message. No owner/ack opt-in is required by this contract.
                var subsequent = new byte[128 * 1024];
                Array.Fill(subsequent, (byte)0xCC);
                clientHandler.Send(client, Tcp.Write.Create(subsequent, WriteAck.Instance));
                await clientHandler.ExpectMsgAsync<WriteAck>(Timeout);
                long subsequentLength = 0;
                while (subsequentLength < subsequent.Length)
                    subsequentLength += (await serverHandler.ExpectMsgAsync<Tcp.Received>(Timeout)).Data.Length;
                Assert.Equal(subsequent.Length, subsequentLength);
                foreach (var data in retained)
                    Assert.All(data.ToArray(), value => Assert.Equal((byte)0x2A, value));
            }
            finally
            {
                clientHandler.Send(client, Tcp.Abort.Instance);
                serverHandler.Send(server, Tcp.Abort.Instance);
                bindCommander.Send(listener, Tcp.Unbind.Instance);
                await bindCommander.ExpectMsgAsync<Tcp.Unbound>(Timeout);
            }
        }

        private sealed class WriteAck : Tcp.Event
        {
            internal static readonly WriteAck Instance = new();
            private WriteAck() { }
        }

        private static TaskCompletionSource<bool> NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class PoisonPool : MemoryPool<byte>
        {
            private int _rented;
            private int _returned;
            internal int Rented => Volatile.Read(ref _rented);
            internal int Returned => Volatile.Read(ref _returned);
            internal ConcurrentBag<Owner> Owners { get; } = new();
            internal TaskCompletionSource<bool> FirstReturn { get; } = NewSignal();
            public override int MaxBufferSize => int.MaxValue;
            public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
            {
                var owner = new Owner(this, Math.Max(4096, minBufferSize));
                Owners.Add(owner);
                Interlocked.Increment(ref _rented);
                return owner;
            }
            protected override void Dispose(bool disposing)
            {
                // Pipe, not the pool, owns outstanding rentals. Tests assert every rental was
                // returned once before leaving the scope; disposing the pool must not mask leaks.
            }
            internal sealed class Owner : IMemoryOwner<byte>
            {
                private readonly PoisonPool _pool;
                private readonly byte[] _bytes;
                private int _disposed;
                internal Owner(PoisonPool pool, int length)
                {
                    _pool = pool;
                    _bytes = new byte[length];
                }
                public Memory<byte> Memory => _bytes;
                internal int DisposeCount => Volatile.Read(ref _disposed);
                public void Dispose()
                {
                    if (Interlocked.Increment(ref _disposed) != 1)
                        throw new InvalidOperationException("Pool owner returned more than once.");
                    Array.Fill(_bytes, (byte)0xCC);
                    Interlocked.Increment(ref _pool._returned);
                    _pool.FirstReturn.TrySetResult(true);
                }
            }
        }
    }
}
