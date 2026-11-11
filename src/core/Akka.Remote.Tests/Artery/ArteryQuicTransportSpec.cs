// -----------------------------------------------------------------------
// <copyright file="ArteryQuicTransportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using static Akka.Remote.Tests.Artery.ArteryTlsTransportSpec;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>Native byte-adapter failure tests; actor/protocol parity is exercised by ArteryTlsTransportSpec.</summary>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("osx")]
    public sealed class ArteryQuicTransportSpec : AkkaSpec
    {
        public ArteryQuicTransportSpec(ITestOutputHelper output) : base(output) { }

        [Fact(DisplayName = "QUIC sends FIN when upstream finishes before connection opening completes")]
        public async Task Should_CompleteWrites_When_UpstreamFinishesDuringConnect()
        {
            RequireTransportSupport("quic");
            using var certificate = CreateCertificate();
            var tls = ArteryTlsSettings.ServerOnly(certificate, TrustCertificate(certificate));
            var sender = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), Logging.GetLogger(Sys, GetType()));
            var materializer = ActorMaterializer.Create(Sys);
            var allowHandshake = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var listener = await QuicListener.ListenAsync(new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                ApplicationProtocols = new() { ArteryQuicTransport.ApplicationProtocol },
                ConnectionOptionsCallback = async (_, _, cancellationToken) =>
                {
                    await allowHandshake.Task.WaitAsync(cancellationToken);
                    return new QuicServerConnectionOptions
                    {
                        ServerAuthenticationOptions = new SslServerAuthenticationOptions
                        {
                            ServerCertificate = certificate,
                            EnabledSslProtocols = SslProtocols.Tls13,
                            ApplicationProtocols = new() { ArteryQuicTransport.ApplicationProtocol }
                        },
                        DefaultCloseErrorCode = 1,
                        DefaultStreamErrorCode = 1,
                        MaxInboundUnidirectionalStreams = 1
                    };
                }
            }, deadline.Token);
            try
            {
                var ((upstreamFinished, connected), writerFinished) = Source.Empty<ReadOnlySequence<byte>>()
                    .WatchTermination(Keep.Right)
                    .ViaMaterialized(sender.Outgoing(listener.LocalEndPoint, ArteryStreamId.Control), Keep.Both)
                    .ToMaterialized(Sink.Ignore<ReadOnlySequence<byte>>(), Keep.Both)
                    .Run(materializer);

                // Keep TLS opening pending until the upstream completion has reached the graph.
                await upstreamFinished.WaitAsync(deadline.Token);
                allowHandshake.SetResult(Done.Instance);
                await using var connection = await listener.AcceptConnectionAsync(deadline.Token);
                await using var stream = await connection.AcceptInboundStreamAsync(deadline.Token);
                await connected.WaitAsync(deadline.Token);
                (await stream.ReadAsync(new byte[1], deadline.Token)).Should().Be(0, "an empty upstream still sends a graceful FIN");
                await writerFinished.WaitAsync(deadline.Token);
            }
            finally
            {
                allowHandshake.TrySetCanceled();
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await sender.ShutdownAsync(shutdown.Token);
                materializer.Shutdown();
            }
        }

        [Theory(DisplayName = "QUIC writes a bounded ordinary batch with fewer calls and the same bytes")]
        [InlineData(4096, 0, 1)]
        [InlineData(8192, 8192, 1)]
        [InlineData(8192, 8193, 2)]
        public async Task Should_CoalesceEligibleOrdinaryBatch_When_WritingOwnedSegments(
            int firstSize, int secondSize, int expectedWrites)
        {
            RequireTransportSupport("quic");
            using var certificate = CreateCertificate();
            var tls = ArteryTlsSettings.Mutual(certificate, TrustCertificate(certificate));
            var log = Logging.GetLogger(Sys, GetType());
            var sender = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), log, enableIoStatistics: true);
            var receiver = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), log);
            var materializer = ActorMaterializer.Create(Sys);
            var received = new TaskCompletionSource<Task<IImmutableList<ReadOnlySequence<byte>>>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var first = new TrackingOwner(firstSize, 0x13);
            var second = secondSize == 0 ? null : new TrackingOwner(secondSize, 0xB1);
            var input = OwnedSequenceSegment.Create(first, firstSize);
            if (second is not null)
                input = ArteryRemoting.AppendFrameToBatch(input, OwnedSequenceSegment.Create(second, secondSize));
            var expected = input.ToArray();
            try
            {
                var endpoint = await receiver.BindAsync("127.0.0.1", 0, deadline.Token);
                receiver.StartAccepting((source, _, admitted) =>
                {
                    received.TrySetResult(source.RunWith(Sink.Seq<ReadOnlySequence<byte>>(), materializer));
                    admitted();
                });
                var preamble = Preamble(ArteryStreamId.Ordinary);
                await Source.Single(new ReadOnlySequence<byte>(preamble)).Concat(Source.Single(input))
                    .Via(sender.Outgoing(endpoint, ArteryStreamId.Ordinary))
                    .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), materializer).WaitAsync(deadline.Token);
                var chunks = await (await received.Task.WaitAsync(deadline.Token)).WaitAsync(deadline.Token);
                chunks.SelectMany(chunk => chunk.ToArray()).Should().Equal(preamble.Concat(expected));
                first.DisposeCount.Should().Be(1);
                if (second is not null)
                    second.DisposeCount.Should().Be(1);
                sender.IoSnapshot.WriteBytes.Should().Be(preamble.Length + expected.Length);
                sender.IoSnapshot.WriteOperations.Should().Be(1 + expectedWrites);
            }
            finally
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await sender.ShutdownAsync(shutdown.Token);
                await receiver.ShutdownAsync(shutdown.Token);
                materializer.Shutdown();
            }
        }

        private sealed class TrackingOwner(int length, byte fill) : IMemoryOwner<byte>
        {
            private readonly byte[] _bytes = Enumerable.Repeat(fill, length).ToArray();
            private int _disposeCount;

            public Memory<byte> Memory => _bytes;
            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
                Array.Fill(_bytes, (byte)0xEE);
            }
        }

        [Theory(DisplayName = "QUIC coalescing returns the rental once the write settles")]
        [InlineData("success")]
        [InlineData("cancellation")]
        [InlineData("failure")]
        public async Task Should_ReturnRentalOnlyAfterSettlement_When_CoalescedWriteIsPending(string outcome)
        {
            var bytes = new byte[] { 0x13, 0xB1, 0x2C };
            var pool = new TrackingPool();
            using var stream = new PendingWriteStream();
            using var cancellation = new CancellationTokenSource();
            var write = ArteryQuicWriteStage.WriteCoalescedAsync(
                stream, new ReadOnlySequence<byte>(bytes), pool, cancellation.Token).AsTask();

            write.IsCompleted.Should().BeFalse();
            pool.ReturnCount.Should().Be(0);
            stream.Submitted.ToArray().Should().Equal(bytes);
            stream.Submitted.Length.Should().Be(bytes.Length, "a rental can be larger than the write");

            switch (outcome)
            {
                case "success":
                    stream.Completion.SetResult();
                    await write;
                    break;
                case "cancellation":
                    cancellation.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
                    break;
                case "failure":
                    var error = new IOException("write failed");
                    stream.Completion.SetException(error);
                    (await Assert.ThrowsAsync<IOException>(() => write)).Should().BeSameAs(error);
                    break;
            }

            pool.ReturnCount.Should().Be(1);
            pool.Returned.Should().BeSameAs(pool.Buffer);
        }

        private sealed class PendingWriteStream : MemoryStream
        {
            public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public ReadOnlyMemory<byte> Submitted { get; private set; }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                Submitted = buffer;
                return new ValueTask(Completion.Task.WaitAsync(cancellationToken));
            }
        }

        private sealed class TrackingPool : ArrayPool<byte>
        {
            public byte[] Buffer { get; } = new byte[16];
            public byte[]? Returned { get; private set; }
            public int ReturnCount { get; private set; }

            public override byte[] Rent(int minimumLength) => Buffer;

            public override void Return(byte[] array, bool clearArray = false)
            {
                Returned = array;
                ReturnCount++;
            }
        }

        [Fact(DisplayName = "QUIC receive chunks retain their bytes across subsequent reads and FIN")]
        public async Task Should_RetainDistinctChunks_When_NativeReaderReusesScratchBuffer()
        {
            RequireTransportSupport("quic");
            using var certificate = CreateCertificate();
            var tls = ArteryTlsSettings.Mutual(certificate, TrustCertificate(certificate));
            var log = Logging.GetLogger(Sys, GetType());
            var sender = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), log);
            var receiver = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), log, enableIoStatistics: true);
            var materializer = ActorMaterializer.Create(Sys);
            var received = new TaskCompletionSource<Task<IImmutableList<ReadOnlySequence<byte>>>>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                var endpoint = await receiver.BindAsync("127.0.0.1", 0, deadline.Token);
                receiver.StartAccepting((source, _, admitted) =>
                {
                    // Keep every chunk alive until EOF, rather than consuming/copying each on arrival.
                    received.TrySetResult(source.RunWith(Sink.Seq<ReadOnlySequence<byte>>(), materializer));
                    admitted();
                });
                var channel = await sender.OpenAsync(endpoint, ArteryStreamId.Ordinary, deadline.Token);
                var preamble = Preamble(ArteryStreamId.Ordinary);
                var payload = Enumerable.Range(0, 3 * 16 * 1024 + 29)
                    .Select(index => (byte)((index * 31 + index / 251) % 256)).ToArray();
                await channel.Stream.WriteAsync(preamble, deadline.Token);
                await channel.Stream.WriteAsync(payload, deadline.Token);
                channel.Stream.CompleteWrites();
                await channel.Stream.WritesClosed.WaitAsync(deadline.Token);
                var chunks = await (await received.Task.WaitAsync(deadline.Token)).WaitAsync(deadline.Token);
                chunks.Count.Should().BeGreaterOrEqualTo(5, "the payload exceeds three native read buffers, plus its preamble");
                chunks.SelectMany(chunk => chunk.ToArray()).Should().Equal(preamble.Concat(payload));
                var io = receiver.IoSnapshot;
                io.ReadBytes.Should().Be(payload.Length);
                io.ReadOperations.Should().Be(chunks.Count - 1);
            }
            finally
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await sender.ShutdownAsync(shutdown.Token);
                await receiver.ShutdownAsync(shutdown.Token);
                materializer.Shutdown();
            }
        }

        [Theory(DisplayName = "QUIC retires every sibling channel and reconnects when an incoming data stream is reset")]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        public async Task Should_CloseSiblingChannels_When_DataStreamResets(int streams)
        {
            RequireTransportSupport("quic");
            using var certificate = CreateCertificate();
            var policy = TrustCertificate(certificate);
            var tls = ArteryTlsSettings.Mutual(certificate, policy);
            var log = Logging.GetLogger(Sys, GetType());
            var sender = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), log, ordinaryStreams: streams);
            var receiver = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), log);
            var materializer = ActorMaterializer.Create(Sys);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var endpoint = await receiver.BindAsync("127.0.0.1", 0, deadline.Token);
                receiver.StartAccepting((source, _, admitted) =>
                {
                    // This test isolates native channel retirement. Protocol admission itself is
                    // covered by real actors; here the consumer acknowledges admission explicitly.
                    source.RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), materializer);
                    admitted();
                });
                var control = await sender.OpenAsync(endpoint, ArteryStreamId.Control, deadline.Token);
                await control.Stream.WriteAsync(Preamble(ArteryStreamId.Control), deadline.Token);
                var ordinary = new ArteryQuicTransport.Channel[streams];
                for (var i = 0; i < streams; i++)
                {
                    ordinary[i] = await sender.OpenAsync(endpoint, ArteryStreamId.Ordinary, deadline.Token, i);
                    await ordinary[i].Stream.WriteAsync(Preamble(ArteryStreamId.Ordinary), deadline.Token);
                }
                ordinary[^1].Stream.Abort(QuicAbortDirection.Write, 42);

                foreach (var sibling in ordinary.Take(streams - 1).Prepend(control))
                {
                    var closed = async () => await sibling.Stream.WritesClosed.WaitAsync(deadline.Token);
                    await closed.Should().ThrowAsync<QuicException>();
                }
                control.Retire();
                var replacement = await sender.OpenAsync(endpoint, ArteryStreamId.Control, deadline.Token);
                await replacement.Stream.WriteAsync(Preamble(ArteryStreamId.Control), deadline.Token);
                replacement.Stream.Should().NotBeSameAs(control.Stream);
                for (var i = 0; i < streams; i++)
                {
                    var channel = await sender.OpenAsync(endpoint, ArteryStreamId.Ordinary, deadline.Token, i);
                    await channel.Stream.WriteAsync(Preamble(ArteryStreamId.Ordinary), deadline.Token);
                }
                sender.OrdinaryStreamCounts().Values.Should().ContainSingle().Which.Should().Be(streams);
            }
            finally
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await sender.ShutdownAsync(shutdown.Token);
                await receiver.ShutdownAsync(shutdown.Token);
                materializer.Shutdown();
            }
        }

        [Theory(DisplayName = "QUIC rejects duplicate control and excess ordinary channels")]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(2)]
        public async Task Should_RejectExtraRole_When_AdmissionLimitIsExceeded(int roleValue)
        {
            var role = (ArteryStreamId)roleValue;
            RequireTransportSupport("quic");
            using var certificate = CreateCertificate();
            var policy = TrustCertificate(certificate);
            var tls = ArteryTlsSettings.Mutual(certificate, policy);
            var log = Logging.GetLogger(Sys, GetType());
            var receiver = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), log, maximumInboundOrdinaryStreams: 1);
            var materializer = ActorMaterializer.Create(Sys);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var endpoint = await receiver.BindAsync("127.0.0.1", 0, deadline.Token);
                receiver.StartAccepting((source, _, admitted) =>
                {
                    source.RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), materializer);
                    admitted();
                });
                var authentication = TlsClientSettings.Mutual(certificate, policy)
                    .CreateAuthenticationOptions("127.0.0.1", "127.0.0.1", log);
                authentication.ApplicationProtocols = new() { ArteryQuicTransport.ApplicationProtocol };
                await using var connection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
                {
                    RemoteEndPoint = endpoint,
                    ClientAuthenticationOptions = authentication,
                    DefaultCloseErrorCode = 1,
                    DefaultStreamErrorCode = 1
                }, deadline.Token);
                await using var first = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
                await first.WriteAsync(Preamble(role), deadline.Token);
                await using var extra = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
                await extra.WriteAsync(Preamble(role), deadline.Token);
                var closed = async () => await first.WritesClosed.WaitAsync(deadline.Token);
                await closed.Should().ThrowAsync<QuicException>();
            }
            finally
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await receiver.ShutdownAsync(shutdown.Token);
                materializer.Shutdown();
            }
        }

        [Fact(DisplayName = "QUIC control and a healthy ordinary stream progress while another data reader is blocked")]
        public async Task Should_ProgressOtherStreams_When_OneOrdinaryReaderIsBlocked()
        {
            RequireTransportSupport("quic");
            using var certificate = CreateCertificate();
            var tls = ArteryTlsSettings.Mutual(certificate, TrustCertificate(certificate));
            var log = Logging.GetLogger(Sys, GetType());
            var sender = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), log, ordinaryStreams: 2);
            var receiver = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), log);
            var materializer = ActorMaterializer.Create(Sys);
            var blockedAccepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var healthyReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var controlReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var accepted = 0;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            Task? blockedWrite = null;
            try
            {
                var endpoint = await receiver.BindAsync("127.0.0.1", 0, deadline.Token);
                receiver.StartAccepting((source, _, admitted) =>
                {
                    var index = Interlocked.Increment(ref accepted);
                    if (index == 1)
                    {
                        // Do not materialize this reader: the native receive window must fill.
                        blockedAccepted.TrySetResult();
                    }
                    else
                    {
                        source.RunWith(Sink.ForEach<ReadOnlySequence<byte>>(bytes =>
                        {
                            var data = bytes.ToArray();
                            if (data.Contains((byte)127)) healthyReceived.TrySetResult();
                            if (data.Contains((byte)126)) controlReceived.TrySetResult();
                        }), materializer);
                    }
                    admitted();
                });
                var blocked = await sender.OpenAsync(endpoint, ArteryStreamId.Ordinary, deadline.Token, 0);
                await blocked.Stream.WriteAsync(Preamble(ArteryStreamId.Ordinary), deadline.Token);
                await blockedAccepted.Task.WaitAsync(deadline.Token);
                blockedWrite = FillBlockedStreamAsync();
                async Task FillBlockedStreamAsync()
                {
                    await blocked.Stream.WriteAsync(new byte[32 * 1024 * 1024], deadline.Token);
                    // Native sends can buffer and complete WriteAsync before delivery. FIN's
                    // completion proves that flow-controlled bytes actually reached the peer.
                    blocked.Stream.CompleteWrites();
                    await blocked.Stream.WritesClosed.WaitAsync(deadline.Token);
                }
                var healthy = await sender.OpenAsync(endpoint, ArteryStreamId.Ordinary, deadline.Token, 1);
                await healthy.Stream.WriteAsync(Preamble(ArteryStreamId.Ordinary), deadline.Token);
                await healthy.Stream.WriteAsync(new byte[] { 127 }, deadline.Token);
                var control = await sender.OpenAsync(endpoint, ArteryStreamId.Control, deadline.Token);
                await control.Stream.WriteAsync(Preamble(ArteryStreamId.Control), deadline.Token);
                await control.Stream.WriteAsync(new byte[] { 126 }, deadline.Token);
                await Task.WhenAll(healthyReceived.Task, controlReceived.Task).WaitAsync(deadline.Token);
                blockedWrite.IsCompleted.Should().BeFalse($"the stalled reader must still exert native flow control (status {blockedWrite.Status}, error {blockedWrite.Exception})");
                blocked.Retire();
                var aborted = async () => await blockedWrite.WaitAsync(deadline.Token);
                (await aborted.Should().ThrowAsync<Exception>()).Where(ex => ex is QuicException || ex is OperationCanceledException);
            }
            finally
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await sender.ShutdownAsync(shutdown.Token);
                await receiver.ShutdownAsync(shutdown.Token);
                if (blockedWrite is not null && !blockedWrite.IsCompletedSuccessfully)
                {
                    var settled = async () => await blockedWrite;
                    await settled.Should().ThrowAsync<Exception>();
                }
                materializer.Shutdown();
            }
        }

        [Fact(DisplayName = "QUIC closes a peer that sends a preamble but never completes the Artery handshake")]
        public async Task Should_EnforceAdmissionDeadline_When_PeerSendsOnlyPreamble()
        {
            RequireTransportSupport("quic");
            using var certificate = CreateCertificate();
            var tls = ArteryTlsSettings.Mutual(certificate, TrustCertificate(certificate));
            var log = Logging.GetLogger(Sys, GetType());
            var sender = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), log);
            var receiver = new ArteryQuicTransport(tls, TimeSpan.FromMilliseconds(500), log);
            var materializer = ActorMaterializer.Create(Sys);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var endpoint = await receiver.BindAsync("127.0.0.1", 0, deadline.Token);
                receiver.StartAccepting((source, _, _) =>
                    source.RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), materializer));
                var channel = await sender.OpenAsync(endpoint, ArteryStreamId.Control, deadline.Token);
                await channel.Stream.WriteAsync(Preamble(ArteryStreamId.Control), deadline.Token);

                var closed = async () => await channel.Stream.WritesClosed.WaitAsync(deadline.Token);
                await closed.Should().ThrowAsync<QuicException>();
            }
            finally
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await sender.ShutdownAsync(shutdown.Token);
                await receiver.ShutdownAsync(shutdown.Token);
                materializer.Shutdown();
            }
        }

        [Fact(DisplayName = "QUIC cancellation interrupts a dial to an unresponsive UDP endpoint")]
        public async Task Should_CancelPendingDial_When_CallerCancels()
        {
            RequireTransportSupport("quic");
            using var certificate = CreateCertificate();
            var tls = ArteryTlsSettings.Mutual(certificate, TrustCertificate(certificate));
            var sender = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), Logging.GetLogger(Sys, GetType()));
            using var blackhole = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            blackhole.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                var dial = async () => await sender.OpenAsync(blackhole.LocalEndPoint!, ArteryStreamId.Control, deadline.Token);
                await dial.Should().ThrowAsync<OperationCanceledException>();
            }
            finally
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await sender.ShutdownAsync(shutdown.Token);
            }
        }

        private static byte[] Preamble(ArteryStreamId role)
        {
            var bytes = new byte[ArteryConnectionHeader.Length];
            ArteryConnectionHeader.WriteTo(bytes, role);
            return bytes;
        }

        [Theory(DisplayName = "QUIC rejects incompatible protocol bytes and continues accepting valid peers")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_RejectIncompatibleProtocol_When_PeerUsesWrongAlpnOrPreamble(bool wrongAlpn)
        {
            RequireTransportSupport("quic");
            using var certificate = CreateCertificate();
            var policy = TrustCertificate(certificate);
            var tls = ArteryTlsSettings.Mutual(certificate, policy);
            var log = Logging.GetLogger(Sys, GetType());
            var sender = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), log);
            var receiver = new ArteryQuicTransport(tls, TimeSpan.FromSeconds(10), log);
            var materializer = ActorMaterializer.Create(Sys);
            var admitted = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var endpoint = await receiver.BindAsync("127.0.0.1", 0, deadline.Token);
                receiver.StartAccepting((source, _, accepted) =>
                {
                    source.RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), materializer);
                    accepted();
                    admitted.TrySetResult(Done.Instance);
                });
                var authentication = TlsClientSettings.Mutual(certificate, policy)
                    .CreateAuthenticationOptions("127.0.0.1", "127.0.0.1", log);
                authentication.ApplicationProtocols = new()
                {
                    wrongAlpn ? new SslApplicationProtocol("incompatible") : ArteryQuicTransport.ApplicationProtocol
                };
                var options = new QuicClientConnectionOptions
                {
                    RemoteEndPoint = endpoint,
                    ClientAuthenticationOptions = authentication,
                    DefaultCloseErrorCode = 1,
                    DefaultStreamErrorCode = 1
                };
                if (wrongAlpn)
                {
                    var connect = async () => await QuicConnection.ConnectAsync(options, deadline.Token);
                    var failure = await connect.Should().ThrowAsync<Exception>();
                    failure.Where(ex => ex is AuthenticationException || ex is QuicException);
                }
                else
                {
                    await using var connection = await QuicConnection.ConnectAsync(options, deadline.Token);
                    await using var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, deadline.Token);
                    await stream.WriteAsync(new byte[] { 0, 0, 0, 0, 1 }, deadline.Token);
                    var rejected = async () => await stream.WritesClosed.WaitAsync(deadline.Token);
                    await rejected.Should().ThrowAsync<QuicException>();
                }

                admitted.Task.IsCompleted.Should().BeFalse("incompatible protocol data must not be admitted");
                var valid = await sender.OpenAsync(endpoint, ArteryStreamId.Control, deadline.Token);
                await valid.Stream.WriteAsync(Preamble(ArteryStreamId.Control), deadline.Token);
                await admitted.Task.WaitAsync(deadline.Token);
            }
            finally
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await sender.ShutdownAsync(shutdown.Token);
                await receiver.ShutdownAsync(shutdown.Token);
                materializer.Shutdown();
            }
        }
    }
}
