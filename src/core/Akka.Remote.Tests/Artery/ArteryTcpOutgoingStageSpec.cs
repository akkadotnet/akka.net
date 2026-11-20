// -----------------------------------------------------------------------
// <copyright file="ArteryTcpOutgoingStageSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.Tests.IO;
using FluentAssertions;
using Xunit;
using StreamTcp = Akka.Streams.Dsl.Tcp;

namespace Akka.Remote.Tests.Artery
{
    public sealed class ArteryTcpOutgoingStageSpec : AkkaSpec
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
        private static readonly EndPoint Endpoint = new IPEndPoint(IPAddress.Loopback, 12345);
        private readonly ActorMaterializer _materializer;
        private readonly ILoggingAdapter _log;

        public ArteryTcpOutgoingStageSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = ActorMaterializer.Create(Sys, ActorMaterializerSettings.Create(Sys)
                .WithInputBuffer(1, 1).WithSyncProcessingLimit(1).WithFuzzingMode(false));
            _log = Logging.GetLogger(Sys, typeof(ArteryTcpOutgoingStageSpec));
        }

        private (TestPublisher.Probe<ReadOnlySequence<byte>> Upstream,
            TestSubscriber.Probe<ReadOnlySequence<byte>> Downstream,
            Task<StreamTcp.OutgoingConnection> Connected) Run(ArteryTcpOutgoingStage stage)
        {
            var upstream = this.CreatePublisherProbe<ReadOnlySequence<byte>>();
            var downstream = this.CreateSubscriberProbe<ReadOnlySequence<byte>>();
            var connected = Source.FromPublisher(upstream).ViaMaterialized(stage, Keep.Right)
                .To(Sink.FromSubscriber(downstream)).Run(_materializer);
            return (upstream, downstream, connected);
        }

        private ArteryTcpOutgoingStage Stage(FakeTransport transport, ArteryTcpOutgoingStage.Lifetimes lifetimes,
            int maximumWriteBytes = -1) => new(_ => Task.FromResult(
                new ArteryTcpOutgoingStage.ConnectedTransport(transport, Endpoint, Endpoint)), maximumWriteBytes, _log, lifetimes);

        private static async Task Settled(ArteryTcpOutgoingStage.Lifetimes lifetimes)
        {
            lifetimes.StopAdmission();
            using var deadline = new CancellationTokenSource(Timeout);
            await lifetimes.WaitAsync(deadline.Token);
            lifetimes.Count.Should().Be(0);
        }

        [Fact(DisplayName = "Should_RejectNativeAdmission_When_ShutdownPrecedesMaterialization")]
        public async Task Should_reject_native_admission_when_shutdown_precedes_materialization()
        {
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            lifetimes.StopAdmission();
            var attempts = 0;
            var stage = new ArteryTcpOutgoingStage(_ =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("Native admission should have been rejected.");
            }, -1, _log, lifetimes);
            var (upstream, downstream, connected) = Run(stage);
            await downstream.EnsureSubscriptionAsync();
            await downstream.ExpectErrorAsync();
            Func<Task> connection = async () => await connected;
            await connection.Should().ThrowAsync<OperationCanceledException>();
            attempts.Should().Be(0);
            await upstream.ExpectCancellationAsync();
            await Settled(lifetimes);
        }

        [Fact(DisplayName = "Should_RemoveCompletedCleanup_When_CompletionPrecedesContinuationRegistration")]
        public async Task Should_remove_completed_cleanup_when_completion_precedes_continuation_registration()
        {
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            lifetimes.TryRegister(Task.CompletedTask).Should().BeTrue();
            await AwaitAssertAsync(() => lifetimes.Count.Should().Be(0));
            await Settled(lifetimes);
            lifetimes.TryRegister(Task.CompletedTask).Should().BeFalse();
        }

        [Fact(DisplayName = "Should_TrackLateConnectionAndDisposal_When_CancellationRacesConnect")]
        public async Task Should_track_late_connection_and_disposal_when_cancellation_races_connect()
        {
            using var pool = new TrackingMemoryPool();
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var opening = new TaskCompletionSource<ArteryTcpOutgoingStage.ConnectedTransport>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new FakeTransport(pool) { HoldDisposal = true };
            await transport.InputWriter.WriteAsync(new byte[4096]);
            var stage = new ArteryTcpOutgoingStage(_ =>
            {
                entered.TrySetResult(Done.Instance);
                return opening.Task; // Deliberately completes after cancellation.
            }, -1, _log, lifetimes);
            var (upstream, downstream, connected) = Run(stage);
            await downstream.EnsureSubscriptionAsync();
            await entered.Task.WaitAsync(Timeout);
            lifetimes.Count.Should().Be(1, "registration must precede the native operation");
            lifetimes.StopAdmission();
            await upstream.SendErrorAsync(new IOException("stop connecting"));
            await downstream.ExpectErrorAsync();
            opening.SetResult(new ArteryTcpOutgoingStage.ConnectedTransport(transport, Endpoint, Endpoint));
            await transport.DisposalEntered.Task.WaitAsync(Timeout);
            var wait = lifetimes.WaitAsync(CancellationToken.None);
            wait.IsCompleted.Should().BeFalse("the admitted late owner has not finished disposal");
            transport.ReleaseDisposal.TrySetResult(Done.Instance);
            await wait.WaitAsync(Timeout);
            transport.Aborts.Should().Be(1);
            transport.Disposals.Should().Be(1);
            transport.Reader.Completions.Should().Be(1);
            pool.Outstanding.Should().Be(0);
            lifetimes.Count.Should().Be(0);
            Func<Task> connection = async () => await connected;
            await connection.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact(DisplayName = "Should_AbortInitializingTransport_When_StageStopsDuringTlsInitialization")]
        public async Task Should_abort_initializing_transport_when_stage_stops_during_tls_initialization()
        {
            var transport = new FakeTransport { HoldInitialization = true };
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            await downstream.EnsureSubscriptionAsync();
            await transport.InitializationEntered.Task.WaitAsync(Timeout);
            connected.IsCompleted.Should().BeFalse();
            await upstream.SendErrorAsync(new IOException("cancel handshake"));
            await downstream.ExpectErrorAsync();
            await Settled(lifetimes);
            transport.Aborts.Should().Be(1);
            transport.Starts.Should().Be(0);
            transport.Disposals.Should().Be(1);
            Func<Task> connection = async () => await connected;
            await connection.Should().ThrowAsync<OperationCanceledException>();
        }

        [Theory(DisplayName = "Should_DisposeOwnedBytesAfterCopy_When_FlushCompletesOrFails")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_dispose_owned_bytes_after_copy_when_flush_completes_or_fails(bool fail)
        {
            var transport = new FakeTransport { HoldFirstFlush = true };
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            await downstream.EnsureSubscriptionAsync();
            await connected.WaitAsync(Timeout);
            await upstream.ExpectRequestAsync();
            var owner = new PoisonOwner(Enumerable.Range(0, 64).Select(value => (byte)value).ToArray());
            var expected = owner.Memory.ToArray().Skip(3).Take(57).ToArray();
            await upstream.SendNextAsync(OwnedSequenceSegment.Create(owner).Slice(3, 57));
            (await transport.Writes.Reader.ReadAsync().AsTask().WaitAsync(Timeout)).Should().Equal(expected);
            await transport.FlushEntered.Task.WaitAsync(Timeout);
            await AwaitAssertAsync(() => owner.Disposals.Should().Be(1));
            if (fail)
            {
                transport.ReleaseFlush.TrySetException(new IOException("flush failure"));
                (await downstream.ExpectErrorAsync()).Message.Should().Contain("flush failure");
            }
            else
            {
                await upstream.SendCompleteAsync();
                transport.Closes.Should().Be(0, "completion must wait for the outstanding flush");
                transport.ReleaseFlush.TrySetResult(new FlushResult(false, false));
                await downstream.ExpectCompleteAsync();
            }
            await Settled(lifetimes);
            owner.Disposals.Should().Be(1);
            transport.Disposals.Should().Be(1);
            transport.Closes.Should().Be(fail ? 0 : 1);
        }

        [Fact(DisplayName = "Should_ReturnCurrentAndBufferedOwners_When_TheNativeCopyThrows")]
        public async Task Should_return_current_and_buffered_owners_when_the_native_copy_throws()
        {
            var failure = new IOException("copy failed");
            var transport = new FakeTransport { WriteFailure = failure };
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            await downstream.EnsureSubscriptionAsync();
            await connected.WaitAsync(Timeout);
            await upstream.ExpectRequestAsync();
            var owner = new PoisonOwner(new byte[] { 1, 2, 3 });
            await upstream.SendNextAsync(OwnedSequenceSegment.Create(owner));
            (await downstream.ExpectErrorAsync()).Should().BeSameAs(failure);
            await Settled(lifetimes);
            owner.Disposals.Should().Be(1);
            transport.Flushes.Should().Be(0);
        }

        [Theory(DisplayName = "Should_FailAnIdleFlow_When_EitherNativePumpFails")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_fail_an_idle_flow_when_either_native_pump_fails(bool write)
        {
            var transport = new FakeTransport();
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            await downstream.EnsureSubscriptionAsync();
            await connected.WaitAsync(Timeout);
            var failure = new IOException("idle pump failed");
            if (write)
                transport.WritePump.TrySetException(failure);
            else
            {
                // The real byte flow's read side has demand; pump failure also completes its pipe.
                await downstream.RequestAsync(1);
                await transport.EndInputAsync(failure);
            }
            (await downstream.ExpectErrorAsync()).Should().BeSameAs(failure);
            await upstream.ExpectCancellationAsync();
            await Settled(lifetimes);
        }

        [Fact(DisplayName = "Should_KeepWritingAfterPeerFin_When_TheWriteInletRemainsOpen")]
        public async Task Should_keep_writing_after_peer_fin_when_the_write_inlet_remains_open()
        {
            var transport = new FakeTransport();
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            await downstream.EnsureSubscriptionAsync();
            await downstream.RequestAsync(1);
            await connected.WaitAsync(Timeout);
            await transport.EndInputAsync();
            await downstream.ExpectCompleteAsync();
            transport.Closes.Should().Be(0);
            await upstream.ExpectRequestAsync();
            await upstream.SendNextAsync(new ReadOnlySequence<byte>(new byte[] { 5, 6, 7 }));
            (await transport.Writes.Reader.ReadAsync().AsTask().WaitAsync(Timeout)).Should().Equal(5, 6, 7);
            await upstream.SendCompleteAsync();
            await Settled(lifetimes);
            transport.Closes.Should().Be(1);
            transport.Aborts.Should().Be(0);
        }

        [Fact(DisplayName = "Should_DeliverStableFinalBytesBeforeEof_When_ThePipeCompletes")]
        public async Task Should_deliver_stable_final_bytes_before_eof_when_the_pipe_completes()
        {
            var transport = new FakeTransport();
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            await downstream.EnsureSubscriptionAsync();
            await connected.WaitAsync(Timeout);
            var expected = Enumerable.Range(0, 100).Select(value => (byte)value).ToArray();
            await transport.InputWriter.WriteAsync(expected);
            await transport.EndInputAsync();
            await downstream.RequestAsync(1);
            var received = await downstream.ExpectNextAsync();
            received.ToArray().Should().Equal(expected);
            await downstream.ExpectCompleteAsync();
            received.ToArray().Should().Equal(expected);
            await upstream.SendCompleteAsync();
            await Settled(lifetimes);
        }

        [Theory(DisplayName = "Should_ResumePrefetchAndDisposeRetainedFrames_When_CapacityReturnsOrUpstreamFails")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_resume_prefetch_and_dispose_retained_frames_when_capacity_returns_or_upstream_fails(bool fail)
        {
            var transport = new FakeTransport { HoldFirstFlush = true };
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var pulls = CreateTestProbe();
            var owners = Enumerable.Range(0, 5).Select(index => new PoisonOwner(Enumerable.Repeat((byte)index, 8192).ToArray())).ToArray();
            var frames = owners.Select(owner => OwnedSequenceSegment.Create(owner)).ToArray();
            var ((kill, connected), finished) = Source.FromGraph(new CountingSource(frames, pulls.Ref))
                .ViaMaterialized(KillSwitches.Single<ReadOnlySequence<byte>>(), Keep.Right)
                .ViaMaterialized(Stage(transport, lifetimes), Keep.Both)
                .ToMaterialized(Sink.Ignore<ReadOnlySequence<byte>>(), Keep.Both).Run(_materializer);
            try
            {
                await connected.WaitAsync(Timeout);
                await pulls.ExpectMsgAsync(1);
                await pulls.ExpectMsgAsync(2);
                await pulls.ExpectMsgAsync(3);
                await pulls.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(150));
                transport.Flushes.Should().Be(1);
                owners[0].Disposals.Should().Be(1);
                owners[1].Disposals.Should().Be(0);
                owners[2].Disposals.Should().Be(0);
                if (fail)
                {
                    var error = new IOException("upstream failed while coalescing");
                    kill.Abort(error);
                    Func<Task> completion = async () => await finished;
                    await completion.Should().ThrowAsync<IOException>();
                }
                else
                {
                    transport.ReleaseFlush.TrySetResult(new FlushResult(false, false));
                    await pulls.ExpectMsgAsync(4);
                    await pulls.ExpectMsgAsync(5);
                    await finished.WaitAsync(Timeout);
                    var copied = new List<byte>();
                    while (transport.Writes.Reader.TryRead(out var bytes))
                        copied.AddRange(bytes);
                    copied.Should().Equal(Enumerable.Range(0, 5).SelectMany(index => Enumerable.Repeat((byte)index, 8192)));
                }
                await Settled(lifetimes);
                owners.Should().OnlyContain(owner => owner.Disposals == 1);
            }
            finally
            {
                transport.ReleaseFlush.TrySetResult(new FlushResult(false, false));
                kill.Abort(new IOException("test cleanup"));
            }
        }

        [Fact(DisplayName = "Should_UseOneShutdownDeadline_When_NativeDisposalRemainsPending")]
        public async Task Should_use_one_shutdown_deadline_when_native_disposal_remains_pending()
        {
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var cleanup = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            lifetimes.TryRegister(cleanup.Task).Should().BeTrue();
            lifetimes.StopAdmission();
            using var deadline = new CancellationTokenSource();
            try
            {
                var wait = lifetimes.WaitAsync(deadline.Token);
                wait.IsCompleted.Should().BeFalse();
                deadline.Cancel();
                await wait.WaitAsync(Timeout);
                lifetimes.Count.Should().Be(1, "deadline expiry must not abandon a still-owned native resource");
                cleanup.SetResult(Done.Instance);
                await Settled(lifetimes);
            }
            finally
            {
                cleanup.TrySetResult(Done.Instance);
            }
        }

        [Theory(DisplayName = "Should_PreserveTheRequestedDnsEndpoint_For_NativeAndGenericTcpFlows")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_preserve_the_requested_dns_endpoint_for_native_and_generic_tcp_flows(bool useNativeStage)
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            var endpoint = new DnsEndPoint("localhost", ((IPEndPoint)listener.LocalEndPoint!).Port);
            using var deadline = new CancellationTokenSource(Timeout);
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var options = ArteryRemoting.BuildArterySocketOptions(new ArterySettings(ConfigurationFactory.ParseString("enabled = on")));
            var flow = useNativeStage
                ? Flow.FromGraph(new ArteryTcpOutgoingStage(endpoint, TcpSettings.Create(Sys), options, null, _log, lifetimes))
                : Sys.TcpStream().OutgoingConnection(endpoint, options: options, halfClose: false);
            var upstream = this.CreatePublisherProbe<ReadOnlySequence<byte>>();
            var downstream = this.CreateSubscriberProbe<ReadOnlySequence<byte>>();
            var (kill, connected) = Source.FromPublisher(upstream)
                .ViaMaterialized(KillSwitches.Single<ReadOnlySequence<byte>>(), Keep.Right)
                .ViaMaterialized(flow, Keep.Both).To(Sink.FromSubscriber(downstream)).Run(_materializer);
            try
            {
                await downstream.EnsureSubscriptionAsync();
                await downstream.RequestAsync(1);
                using var peer = await listener.AcceptAsync(deadline.Token);
                var addresses = await connected.WaitAsync(deadline.Token);
                addresses.RemoteAddress.Should().BeOfType<DnsEndPoint>().Which.Should().Be(endpoint);
                ((IPEndPoint)addresses.LocalAddress).Port.Should().BeGreaterThan(0);
                lifetimes.Count.Should().Be(useNativeStage ? 1 : 0);

                using var stream = new NetworkStream(peer, ownsSocket: false);
                var payload = new byte[] { 3, 1, 4, 1, 5 };
                await upstream.ExpectRequestAsync();
                await upstream.SendNextAsync(new ReadOnlySequence<byte>(payload));
                var received = new byte[payload.Length];
                await stream.ReadExactlyAsync(received, deadline.Token);
                received.Should().Equal(payload);
                await stream.WriteAsync(payload, deadline.Token);
                (await downstream.ExpectNextAsync(Timeout)).ToArray().Should().Equal(payload);
                await upstream.SendCompleteAsync();
                await downstream.ExpectCompleteAsync(deadline.Token);
            }
            finally
            {
                kill.Abort(new IOException("The DNS endpoint test has finished."));
                await Settled(lifetimes);
            }
        }

        [Fact(DisplayName = "Should_SendAfterAsymmetricFinAndCloseCleanly_When_UsingTheNativeTcpAdapter")]
        public async Task Should_send_after_asymmetric_fin_and_close_cleanly_when_using_the_native_tcp_adapter()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            using var deadline = new CancellationTokenSource(Timeout);
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var options = ArteryRemoting.BuildArterySocketOptions(new ArterySettings(ConfigurationFactory.ParseString("enabled = on")));
            var stage = new ArteryTcpOutgoingStage(listener.LocalEndPoint!, TcpSettings.Create(Sys), options, null, _log, lifetimes);
            var accepting = listener.AcceptAsync(deadline.Token);
            var (upstream, downstream, connected) = Run(stage);
            await downstream.EnsureSubscriptionAsync();
            await downstream.RequestAsync(1);
            using var peer = await accepting;
            using var peerStream = new NetworkStream(peer, ownsSocket: false);
            var addresses = await connected.WaitAsync(deadline.Token);
            static EndPoint Normalize(EndPoint endpoint) => endpoint is IPEndPoint ip && ip.Address.IsIPv4MappedToIPv6
                ? new IPEndPoint(ip.Address.MapToIPv4(), ip.Port) : endpoint;
            Normalize(addresses.RemoteAddress).Should().Be(Normalize(listener.LocalEndPoint!));
            Normalize(addresses.LocalAddress).Should().Be(Normalize(peer.RemoteEndPoint!));
            peer.Shutdown(SocketShutdown.Send);
            await downstream.ExpectCompleteAsync();
            await upstream.ExpectRequestAsync();
            var payload = Enumerable.Range(0, 1024).Select(value => (byte)value).ToArray();
            var owner = new PoisonOwner(payload.ToArray());
            await upstream.SendNextAsync(OwnedSequenceSegment.Create(owner));
            var received = new byte[payload.Length];
            await peerStream.ReadExactlyAsync(received, deadline.Token);
            received.Should().Equal(payload);
            await upstream.SendCompleteAsync();
            (await peerStream.ReadAsync(new byte[1], deadline.Token)).Should().Be(0);
            await Settled(lifetimes);
            owner.Disposals.Should().Be(1);
        }

        [Theory(DisplayName = "Should_RefreshClientCredentials_When_NativeOrGenericTlsFlowReconnectsToTheSameEndpoint")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_refresh_client_credentials_when_native_or_generic_tls_flow_reconnects_to_the_same_endpoint(bool useNativeStage)
        {
            using var serverCertificate = TlsTestCertificates.Create("reconnecting-server", -5, 60, false, TlsTestCertificates.ServerAuth);
            using var oldClient = TlsTestCertificates.Create("client-before-renewal", -5, 60, false, TlsTestCertificates.ClientAuth);
            using var newClient = TlsTestCertificates.Create("client-after-renewal", -5, 120, false, TlsTestCertificates.ClientAuth);
            X509Certificate2 current = oldClient;
            var tls = TlsClientSettings.Mutual(oldClient,
                    TlsPeerPolicy.CustomTrust((certificate, _, _, _, _) => certificate is not null && certificate.Thumbprint == serverCertificate.Thumbprint))
                .WithCertificateProvider((_, _, _) => current);
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(2);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var options = ArteryRemoting.BuildArterySocketOptions(new ArterySettings(ConfigurationFactory.ParseString("enabled = on")));
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var attempts = CreateTestProbe();
            var restart = RestartFlow.OnFailuresWithBackoff(() =>
            {
                var flow = useNativeStage
                    ? Flow.FromGraph(new ArteryTcpOutgoingStage(listener.LocalEndPoint!, TcpSettings.Create(Sys), options, tls, _log, lifetimes))
                    : Sys.TcpStream().OutgoingConnectionTls(listener.LocalEndPoint!, tls, options: options, halfClose: false);
                return flow.MapMaterializedValue(connected =>
                {
                    attempts.Ref.Tell(connected);
                    return connected;
                });
            }, RestartSettings.Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0)
                .WithMaxRestarts(2, TimeSpan.FromSeconds(10)));
            var upstream = this.CreatePublisherProbe<ReadOnlySequence<byte>>();
            var downstream = this.CreateSubscriberProbe<ReadOnlySequence<byte>>();
            var kill = Source.FromPublisher(upstream)
                .ViaMaterialized(KillSwitches.Single<ReadOnlySequence<byte>>(), Keep.Right)
                .Via(restart).To(Sink.FromSubscriber(downstream)).Run(_materializer);
            await downstream.EnsureSubscriptionAsync();
            await downstream.RequestAsync(2);

            async Task AuthenticateAsync(SslStream stream, X509Certificate2 expected)
            {
                string? presented = null;
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = serverCertificate,
                    ClientCertificateRequired = true,
                    RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    {
                        presented = certificate?.GetCertHashString();
                        return presented == expected.Thumbprint;
                    }
                }, deadline.Token);
                presented.Should().Be(expected.Thumbprint);
            }

            try
            {
                using var firstPeer = await listener.AcceptAsync(deadline.Token);
                using var firstStream = new SslStream(new NetworkStream(firstPeer, ownsSocket: false));
                await AuthenticateAsync(firstStream, oldClient);
                var firstConnection = await attempts.ExpectMsgAsync<Task<StreamTcp.OutgoingConnection>>(Timeout);
                await firstConnection.WaitAsync(deadline.Token);
                lifetimes.Count.Should().Be(useNativeStage ? 1 : 0);
                await firstStream.WriteAsync(new byte[] { 1 }, deadline.Token);
                (await downstream.ExpectNextAsync(Timeout)).ToArray().Should().Equal(1);

                current = newClient;
                firstPeer.LingerState = new LingerOption(true, 0);
                firstPeer.Dispose(); // An abrupt peer failure, not a clean TLS FIN/half-close.

                using var secondPeer = await listener.AcceptAsync(deadline.Token);
                using var secondStream = new SslStream(new NetworkStream(secondPeer, ownsSocket: false));
                await AuthenticateAsync(secondStream, newClient);
                var secondConnection = await attempts.ExpectMsgAsync<Task<StreamTcp.OutgoingConnection>>(Timeout);
                await secondConnection.WaitAsync(deadline.Token);
                await secondStream.WriteAsync(new byte[] { 2 }, deadline.Token);
                (await downstream.ExpectNextAsync(Timeout)).ToArray().Should().Equal(2);
                await AwaitAssertAsync(() => lifetimes.Count.Should().Be(useNativeStage ? 1 : 0), Timeout);
            }
            finally
            {
                // Canceling the read outlet alone deliberately leaves the independent write side open.
                kill.Abort(new IOException("The reconnect test has finished both connection attempts."));
                await upstream.ExpectCancellationAsync();
                await Settled(lifetimes);
            }
        }

        [Theory(DisplayName = "Should_UseCurrentClientCredentialsAndPreserveTlsHalfClose_When_OpeningNativeConnections")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_use_current_client_credentials_and_preserve_tls_half_close_when_opening_native_connections(bool rejectServer)
        {
            using var serverCertificate = TlsTestCertificates.Create("direct-tcp-server", -5, 60, false, TlsTestCertificates.ServerAuth);
            using var oldClient = TlsTestCertificates.Create("direct-tcp-client-old", -5, 60, false, TlsTestCertificates.ClientAuth);
            using var newClient = TlsTestCertificates.Create("direct-tcp-client-new", -5, 120, false, TlsTestCertificates.ClientAuth);
            X509Certificate2 current = oldClient;
            var providerCalls = 0;
            var tls = TlsClientSettings.Mutual(oldClient, TlsPeerPolicy.CustomTrust((_, _, _, _, _) => !rejectServer))
                .WithCertificateProvider((_, _, _) =>
                {
                    Interlocked.Increment(ref providerCalls);
                    return current;
                });
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(2);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var options = ArteryRemoting.BuildArterySocketOptions(new ArterySettings(ConfigurationFactory.ParseString("enabled = on")));
            foreach (var certificate in new[] { oldClient, newClient })
            {
                current = certificate;
                var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
                var accepted = listener.AcceptAsync(deadline.Token);
                var stage = new ArteryTcpOutgoingStage(listener.LocalEndPoint!, TcpSettings.Create(Sys), options, tls, _log, lifetimes);
                var (upstream, downstream, connected) = Run(stage);
                await downstream.EnsureSubscriptionAsync();
                await downstream.RequestAsync(1);
                using var peer = await accepted;
                await using var stream = new SslStream(new NetworkStream(peer, ownsSocket: false), leaveInnerStreamOpen: false);
                string? presented = null;
                var authentication = stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = serverCertificate,
                    ClientCertificateRequired = true,
                    RemoteCertificateValidationCallback = (_, peerCertificate, _, _) =>
                    {
                        presented = peerCertificate?.GetCertHashString();
                        return peerCertificate is not null;
                    }
                }, deadline.Token);
                if (rejectServer)
                {
                    Func<Task> connection = async () => await connected.WaitAsync(deadline.Token);
                    await connection.Should().ThrowAsync<System.Security.Authentication.AuthenticationException>();
                    await downstream.ExpectErrorAsync();
                    try
                    {
                        await authentication;
                    }
                    catch (Exception error) when (error is System.Security.Authentication.AuthenticationException or IOException)
                    {
                        // The peer's handshake may fail before or after the client's rejection.
                        _log.Debug(error, "The TLS peer observed the rejected connection.");
                    }
                    await Settled(lifetimes);
                    break;
                }
                await authentication;
                await connected.WaitAsync(deadline.Token);
                presented.Should().Be(certificate.Thumbprint);
                await stream.ShutdownAsync();
                await downstream.ExpectCompleteAsync();
                await upstream.ExpectRequestAsync();
                await upstream.SendNextAsync(new ReadOnlySequence<byte>(new byte[] { 8, 9, 10 }));
                var bytes = new byte[3];
                await stream.ReadExactlyAsync(bytes, deadline.Token);
                bytes.Should().Equal(8, 9, 10);
                await upstream.SendCompleteAsync();
                (await stream.ReadAsync(new byte[1], deadline.Token)).Should().Be(0);
                await Settled(lifetimes);
            }
            providerCalls.Should().Be(rejectServer ? 1 : 2);
        }

        [Fact(DisplayName = "Should_FailTheMaterializedConnection_When_StartingPumpsFails")]
        public async Task Should_fail_the_materialized_connection_when_starting_pumps_fails()
        {
            var failure = new IOException("pump start failed");
            var transport = new FakeTransport { StartFailure = failure };
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            await downstream.EnsureSubscriptionAsync();
            Func<Task> connection = async () => await connected;
            (await connection.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(failure);
            (await downstream.ExpectErrorAsync()).Should().BeSameAs(failure);
            await upstream.ExpectCancellationAsync();
            await Settled(lifetimes);
            transport.Aborts.Should().Be(1);
            transport.Disposals.Should().Be(1);
        }

        [Fact(DisplayName = "Should_UseNativeOutgoingForEveryStream_When_UsingDefaultArterySettings")]
        public async Task Should_use_native_outgoing_for_every_stream_when_using_default_artery_settings()
        {
            var config = ConfigurationFactory.ParseString("""
                akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = "127.0.0.1"
                akka.remote.artery.canonical.port = 0
                akka.remote.artery.advanced.outbound-lanes = 2
                akka.remote.artery.advanced.inbound-lanes = 2
                akka.remote.artery.large-message-destinations = ["/user/large"]
                """);
            var systemA = ActorSystem.Create("directOutgoingA", config);
            var systemB = ActorSystem.Create("directOutgoingB", config);
            var transportA = (ArteryRemoting)RARP.For(systemA).Provider.Transport;
            var transportB = (ArteryRemoting)RARP.For(systemB).Provider.Transport;
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "ordinary");
                systemB.ActorOf(Props.Create(() => new Echo()), "large");
                var address = RARP.For(systemB).Provider.DefaultAddress;
                foreach (var name in new[] { "ordinary", "large" })
                {
                    var echo = await systemA.ActorSelection($"{address}/user/{name}").ResolveOne(Timeout);
                    var probe = CreateTestProbe(systemA);
                    foreach (var value in Enumerable.Range(0, 20))
                        echo.Tell($"{name}-{value}", probe.Ref);
                    foreach (var value in Enumerable.Range(0, 20))
                        await probe.ExpectMsgAsync($"{name}-{value}", Timeout);
                }
                await AwaitAssertAsync(() => transportA.DirectTcpConnectionCount.Should().Be(3), Timeout);
                await AwaitAssertAsync(() => transportB.DirectTcpConnectionCount.Should().Be(2), Timeout);
            }
            finally
            {
                await Task.WhenAll(systemA.Terminate(), systemB.Terminate()).WaitAsync(TimeSpan.FromSeconds(25));
            }
            await AwaitAssertAsync(() => transportA.DirectTcpConnectionCount.Should().Be(0), Timeout);
            await AwaitAssertAsync(() => transportB.DirectTcpConnectionCount.Should().Be(0), Timeout);
        }

        private sealed class Echo : ReceiveActor
        {
            public Echo() => ReceiveAny(message => Sender.Tell(message));
        }

        [Fact(DisplayName = "Should_RestartOnlyTheByteConnection_When_AnOutgoingAttemptFails")]
        public async Task Should_restart_only_the_byte_connection_when_an_outgoing_attempt_fails()
        {
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var first = new FakeTransport { WriteFailure = new IOException("first connection failed") };
            var second = new FakeTransport();
            var attempts = CreateTestProbe();
            var count = 0;
            var restart = RestartFlow.OnFailuresWithBackoff(() =>
            {
                var attempt = Interlocked.Increment(ref count);
                attempts.Ref.Tell(attempt);
                return Flow.FromGraph(Stage(attempt == 1 ? first : second, lifetimes));
            }, RestartSettings.Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0)
                .WithMaxRestarts(3, TimeSpan.FromSeconds(1)));
            var (queue, done) = Source.Queue<ReadOnlySequence<byte>>(1, OverflowStrategy.Backpressure)
                .Via(restart).ToMaterialized(Sink.Ignore<ReadOnlySequence<byte>>(), Keep.Both).Run(_materializer);
            var failedOwner = new PoisonOwner(new byte[] { 1, 2 });
            var deliveredOwner = new PoisonOwner(new byte[] { 3, 4 });
            try
            {
                await attempts.ExpectMsgAsync(1);
                (await queue.OfferAsync(OwnedSequenceSegment.Create(failedOwner))).Should().BeOfType<QueueOfferResult.Enqueued>();
                await first.WriteEntered.Task.WaitAsync(Timeout);
                await attempts.ExpectMsgAsync(2, Timeout);
                await second.InitializationEntered.Task.WaitAsync(Timeout);
                (await queue.OfferAsync(OwnedSequenceSegment.Create(deliveredOwner))).Should().BeOfType<QueueOfferResult.Enqueued>();
                (await second.Writes.Reader.ReadAsync().AsTask().WaitAsync(Timeout)).Should().Equal(3, 4);
                queue.Complete();
                await done.WaitAsync(Timeout);
                await Settled(lifetimes);
                count.Should().Be(2, "a successful graceful completion must not reconnect");
                failedOwner.Disposals.Should().Be(1);
                deliveredOwner.Disposals.Should().Be(1);
                first.Aborts.Should().Be(1);
                second.Closes.Should().Be(1);
            }
            finally
            {
                queue.Fail(new IOException("test cleanup"));
            }
        }

        [Fact(DisplayName = "Should_SettleWithoutNativeAdmission_When_PostStopRunsBeforePreStart")]
        public async Task Should_settle_without_native_admission_when_post_stop_runs_before_pre_start()
        {
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var attempts = 0;
            var stage = new ArteryTcpOutgoingStage(_ =>
            {
                Interlocked.Increment(ref attempts);
                throw new IOException("connect must not run");
            }, -1, _log, lifetimes);
            var assembly = new GraphAssembly(
                new IGraphStageWithMaterializedValue<Shape, object>[] { stage }, new[] { Attributes.None },
                new Inlet[] { stage.Shape.Inlet, null! }, new[] { 0, GraphInterpreter.Boundary },
                new Outlet[] { null!, stage.Shape.Outlet }, new[] { GraphInterpreter.Boundary, 0 });
            var values = new Dictionary<IModule, object>();
            var (_, logics) = assembly.Materialize(Attributes.None, new[] { stage.Module }, values, _ => { });
            lifetimes.Count.Should().Be(0);
            logics[0].PostStop();
            Func<Task> connection = async () => await (Task<StreamTcp.OutgoingConnection>)values[stage.Module];
            await connection.Should().ThrowAsync<OperationCanceledException>();
            attempts.Should().Be(0);
            await Settled(lifetimes);
        }

        [Fact(DisplayName = "Should_RetireRegistration_When_TheConnectionFactoryThrowsSynchronously")]
        public async Task Should_retire_registration_when_the_connection_factory_throws_synchronously()
        {
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var failure = new IOException("connect factory failed");
            var stage = new ArteryTcpOutgoingStage(_ =>
            {
                lifetimes.Count.Should().Be(1);
                throw failure;
            }, -1, _log, lifetimes);
            var (upstream, downstream, connected) = Run(stage);
            await downstream.EnsureSubscriptionAsync();
            Func<Task> connection = async () => await connected;
            (await connection.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(failure);
            (await downstream.ExpectErrorAsync()).Should().BeSameAs(failure);
            await upstream.ExpectCancellationAsync();
            await Settled(lifetimes);
        }

        [Theory(DisplayName = "Should_AttemptDisposalAndSettleCleanup_When_AbortAndDisposalReportFailures")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_attempt_disposal_and_settle_cleanup_when_abort_and_disposal_report_failures(bool initializing)
        {
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var transport = new FakeTransport
            {
                HoldInitialization = initializing,
                AbortFailure = new IOException("abort failed"),
                DisposalFailure = new IOException("dispose failed")
            };
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            await downstream.EnsureSubscriptionAsync();
            await transport.InitializationEntered.Task.WaitAsync(Timeout);
            if (!initializing)
                await connected.WaitAsync(Timeout);
            var errors = CreateTestProbe();
            Sys.EventStream.Subscribe(errors.Ref, typeof(Error));
            var stop = new IOException("stop flow");
            await upstream.SendErrorAsync(stop);
            (await downstream.ExpectErrorAsync()).Should().BeSameAs(stop);
            await Settled(lifetimes);
            transport.Aborts.Should().Be(1);
            transport.Disposals.Should().Be(1);
            transport.Reader.Completions.Should().Be(1, "input retirement must still run when native cleanup reports errors");
            await errors.FishForMessageAsync<Error>(message => message.Message.ToString()!.Contains("cleanup"), Timeout);
            if (initializing)
            {
                Func<Task> connection = async () => await connected;
                await connection.Should().ThrowAsync<OperationCanceledException>();
            }
        }

        [Fact(DisplayName = "Should_AvoidNativeAdmission_When_AFollowingStageRefusesMaterialization")]
        public void Should_avoid_native_admission_when_a_following_stage_refuses_materialization()
        {
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var attempts = 0;
            var stage = new ArteryTcpOutgoingStage(_ =>
            {
                Interlocked.Increment(ref attempts);
                throw new IOException("connect must not run");
            }, -1, _log, lifetimes);
            Action materialize = () => Source.Maybe<ReadOnlySequence<byte>>().Via(stage)
                .Via(new RefusingStage(new IOException("refused materialization")))
                .To(Sink.Ignore<ReadOnlySequence<byte>>()).Run(_materializer);
            materialize.Should().Throw<Exception>().Which.ToString().Should().Contain("refused materialization");
            attempts.Should().Be(0);
            lifetimes.Count.Should().Be(0);
            lifetimes.StopAdmission();
        }

        [Fact(DisplayName = "Should_DisposeBeforeAwaitingPumpNotifications_When_ClosureNotificationsAreDelayed")]
        public async Task Should_dispose_before_awaiting_pump_notifications_when_closure_notifications_are_delayed()
        {
            var transport = new FakeTransport { DelayPumpNotificationsUntilDisposal = true };
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            await downstream.EnsureSubscriptionAsync();
            await connected.WaitAsync(Timeout);
            await upstream.SendCompleteAsync();
            await downstream.ExpectCompleteAsync();
            await transport.DisposalEntered.Task.WaitAsync(Timeout);
            await Settled(lifetimes);
            transport.Closes.Should().Be(1);
            transport.Disposals.Should().Be(1);
        }

        [Fact(DisplayName = "Should_DisposeBeforeAwaitingClose_When_MaterializerStopsDuringClose")]
        public async Task Should_dispose_before_awaiting_close_when_materializer_stops_during_close()
        {
            var transport = new FakeTransport { HoldCloseUntilDisposal = true };
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            try
            {
                await downstream.EnsureSubscriptionAsync();
                await connected.WaitAsync(Timeout);
                await upstream.SendCompleteAsync();
                await transport.CloseEntered.Task.WaitAsync(Timeout);
                _materializer.Shutdown();
                await transport.DisposalEntered.Task.WaitAsync(Timeout);
                await Settled(lifetimes);
                transport.Closes.Should().Be(1);
                transport.Disposals.Should().Be(1);
            }
            finally
            {
                transport.ReleaseClose.TrySetResult(Done.Instance);
            }
        }

        [Theory(DisplayName = "Should_ReturnUnreadInputLeases_When_AnOwnerIsCancelledBeforeOrAfterTransfer")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_return_unread_input_leases_when_an_owner_is_cancelled_before_or_after_transfer(bool initializing)
        {
            using var pool = new TrackingMemoryPool();
            var transport = new FakeTransport(pool) { HoldInitialization = initializing };
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            await downstream.EnsureSubscriptionAsync();
            await transport.InitializationEntered.Task.WaitAsync(Timeout);
            if (!initializing)
                await connected.WaitAsync(Timeout);
            await transport.InputWriter.WriteAsync(new byte[4096]);
            pool.Outstanding.Should().BeGreaterThan(0);
            await upstream.SendErrorAsync(new IOException("cancel unread input"));
            await downstream.ExpectErrorAsync();
            await Settled(lifetimes);
            pool.Outstanding.Should().Be(0, "cleanup must release input segments that never had downstream demand");
            transport.Reader.Completions.Should().Be(1);
            if (initializing)
            {
                Func<Task> connection = async () => await connected;
                await connection.Should().ThrowAsync<OperationCanceledException>();
            }
        }

        [Fact(DisplayName = "Should_RetireTheActualReadBeforeCompletingInput_When_CancellationFollowsOwnerTransfer")]
        public async Task Should_retire_the_actual_read_before_completing_input_when_cancellation_follows_owner_transfer()
        {
            using var pool = new TrackingMemoryPool();
            var transport = new FakeTransport(pool);
            transport.Reader.HoldFirstReadResult = true;
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            try
            {
                await downstream.EnsureSubscriptionAsync();
                await connected.WaitAsync(Timeout);
                await transport.InputWriter.WriteAsync(new byte[4096]);
                await downstream.RequestAsync(1);
                await transport.Reader.ReadResultEntered.Task.WaitAsync(Timeout);
                await upstream.SendErrorAsync(new IOException("cancel active read"));
                await downstream.ExpectErrorAsync();
                await transport.DisposalEntered.Task.WaitAsync(Timeout);
                lifetimes.StopAdmission();
                var cleanup = lifetimes.WaitAsync(CancellationToken.None);
                cleanup.IsCompleted.Should().BeFalse("the established owner still has an unadvanced read");
                transport.Reader.Completions.Should().Be(0);
                pool.Outstanding.Should().BeGreaterThan(0);
                transport.Reader.ReleaseReadResult.TrySetResult(Done.Instance);
                await cleanup.WaitAsync(Timeout);
                transport.Reader.Completions.Should().Be(1);
                transport.Reader.CompletionOvertookRead.Should().BeFalse();
                pool.Outstanding.Should().Be(0);
                lifetimes.Count.Should().Be(0);
            }
            finally
            {
                transport.Reader.ReleaseReadResult.TrySetResult(Done.Instance);
            }
        }

        [Fact(DisplayName = "Should_ReportConnectionRefusalAndReleaseAdmission_When_TheNativePeerIsNotListening")]
        public async Task Should_report_connection_refusal_and_release_admission_when_the_native_peer_is_not_listening()
        {
            using var unavailable = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            unavailable.Bind(new IPEndPoint(IPAddress.Loopback, 0)); // Keep the port reserved without listening.
            var settings = TcpSettings.Create(ConfigurationFactory.ParseString("finish-connect-retries = 0")
                .WithFallback(Sys.Settings.Config.GetConfig("akka.io.tcp")));
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var options = ArteryRemoting.BuildArterySocketOptions(new ArterySettings(ConfigurationFactory.ParseString("enabled = on")));
            var stage = new ArteryTcpOutgoingStage(unavailable.LocalEndPoint!, settings, options, null, _log, lifetimes);
            var (upstream, downstream, connected) = Run(stage);
            await downstream.EnsureSubscriptionAsync();
            Func<Task> connection = async () => await connected.WaitAsync(Timeout);
            await connection.Should().ThrowAsync<SocketException>();
            (await downstream.ExpectErrorAsync()).Should().BeOfType<SocketException>();
            await upstream.ExpectCancellationAsync();
            await Settled(lifetimes);
        }

        private sealed class RefusingStage(Exception error) : GraphStage<FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>>
        {
            public override FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>> Shape { get; } =
                new(new Inlet<ReadOnlySequence<byte>>("refusing.in"), new Outlet<ReadOnlySequence<byte>>("refusing.out"));
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => throw error;
        }

        [Fact(DisplayName = "Should_AllowAnExistingByteFlowToRetry_When_ShutdownIsDrainingItsBacklog")]
        public async Task Should_allow_an_existing_byte_flow_to_retry_when_shutdown_is_draining_its_backlog()
        {
            var config = ConfigurationFactory.ParseString("""
                akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = "127.0.0.1"
                akka.remote.artery.canonical.port = 0
                akka.remote.artery.advanced.flush-wait-on-shutdown = 5s
                """);
            var system = ActorSystem.Create("directTcpDrain", config);
            var transport = (ArteryRemoting)RARP.For(system).Provider.Transport;
            static T Field<T>(ArteryRemoting owner, string name) =>
                (T)(typeof(ArteryRemoting).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner)
                    ?? throw new InvalidOperationException($"Missing transport field {name}."));
            var materializer = Field<ActorMaterializer>(transport, "_materializer");
            var lifetimes = Field<ArteryTcpOutgoingStage.Lifetimes>(transport, "_directTcpLifetimes");
            var registry = Field<AssociationRegistry>(transport, "_registry");
            var first = new FakeTransport { HoldInitialization = true };
            var second = new FakeTransport();
            var attempts = CreateTestProbe();
            var attemptCount = 0;
            var backlog = Channel.CreateBounded<ReadOnlySequence<byte>>(2);
            backlog.Writer.TryWrite(new ReadOnlySequence<byte>(new byte[] { 1, 2 })).Should().BeTrue();
            backlog.Writer.TryWrite(new ReadOnlySequence<byte>(new byte[] { 3, 4 })).Should().BeTrue();
            backlog.Writer.TryComplete().Should().BeTrue();
            var restart = RestartFlow.OnFailuresWithBackoff(() =>
            {
                var attempt = Interlocked.Increment(ref attemptCount);
                attempts.Ref.Tell(attempt);
                return Flow.FromGraph(Stage(attempt == 1 ? first : second, lifetimes));
            }, RestartSettings.Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), 0)
                .WithMaxRestarts(3, TimeSpan.FromSeconds(1)));
            var done = ChannelSource.FromReader(backlog.Reader).Via(restart)
                .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), materializer);
            // Publish the same completion boundary Shutdown uses for an existing association.
            registry.AssociationFor(new Address("akka", "draining-peer", "127.0.0.1", 1))
                .SetOutboundStreamCompletion(ArteryStreamId.Ordinary, done);
            try
            {
                await attempts.ExpectMsgAsync(1);
                await first.InitializationEntered.Task.WaitAsync(Timeout);
                var shutdown = transport.Shutdown();
                shutdown.IsCompleted.Should().BeFalse("the existing accepted byte flow has not drained");
                first.ReleaseInitialization.TrySetException(new IOException("first connection failed during drain"));
                await attempts.ExpectMsgAsync(2, Timeout);
                await done.WaitAsync(Timeout);
                var delivered = new List<byte>();
                while (second.Writes.Reader.TryRead(out var bytes))
                    delivered.AddRange(bytes);
                delivered.Should().Equal(1, 2, 3, 4);
                await shutdown.WaitAsync(TimeSpan.FromSeconds(15));
                lifetimes.Count.Should().Be(0);
                lifetimes.TryRegister(Task.CompletedTask).Should().BeFalse("native admission closes before final cleanup snapshots");
            }
            finally
            {
                first.ReleaseInitialization.TrySetResult(Done.Instance);
                await system.Terminate().WaitAsync(TimeSpan.FromSeconds(20));
            }
        }

        [Fact(DisplayName = "Should_DrainBufferedBytesBeforeReadFailure_When_ThePumpFaultsBeforeDemand")]
        public async Task Should_drain_buffered_bytes_before_read_failure_when_the_pump_faults_before_demand()
        {
            var transport = new FakeTransport();
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            await downstream.EnsureSubscriptionAsync();
            await connected.WaitAsync(Timeout);
            var payload = new byte[] { 11, 12, 13, 14 };
            await transport.InputWriter.WriteAsync(payload);
            var failure = new IOException("read failed after final bytes");
            await transport.EndInputAsync(failure);
            await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(150));
            await downstream.RequestAsync(1);
            (await downstream.ExpectNextAsync()).ToArray().Should().Equal(payload);
            (await downstream.ExpectErrorAsync()).Should().BeSameAs(failure);
            await upstream.ExpectCancellationAsync();
            await Settled(lifetimes);
        }

        [Fact(DisplayName = "Should_NotOvertakeTheFinalDrain_When_ReadFaultNotificationFollowsNonemptyRead")]
        public async Task Should_not_overtake_the_final_drain_when_read_fault_notification_follows_nonempty_read()
        {
            var transport = new FakeTransport { DeferReadErrorNotification = true };
            transport.Reader.HoldSecondRead = true;
            var lifetimes = new ArteryTcpOutgoingStage.Lifetimes(_log);
            var (upstream, downstream, connected) = Run(Stage(transport, lifetimes));
            try
            {
                await downstream.EnsureSubscriptionAsync();
                await connected.WaitAsync(Timeout);
                var payload = new byte[] { 21, 22, 23, 24 };
                await transport.InputWriter.WriteAsync(payload);
                var failure = new IOException("read pump fault before empty drain");
                await transport.EndInputAsync(failure);
                // A completed writer makes the first read a nonempty final read deterministically.
                await downstream.RequestAsync(1);
                (await downstream.ExpectNextAsync()).ToArray().Should().Equal(payload);
                await transport.Reader.DrainEntered.Task.WaitAsync(Timeout);
                transport.ReadPump.TrySetException(failure).Should().BeTrue();
                await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(150));
                transport.Reader.ReleaseDrain.TrySetResult(Done.Instance);
                (await downstream.ExpectErrorAsync()).Should().BeSameAs(failure);
                await upstream.ExpectCancellationAsync();
                await Settled(lifetimes);
            }
            finally
            {
                transport.Reader.ReleaseDrain.TrySetResult(Done.Instance);
            }
        }

        private sealed class GatedReader(PipeReader reader) : PipeReader
        {
            private int _reads;
            private int _completions;
            private bool _heldResult;
            internal int Completions => Volatile.Read(ref _completions);
            internal bool CompletionOvertookRead { get; private set; }
            internal bool HoldFirstReadResult { get; set; }
            internal bool HoldSecondRead { get; set; }
            internal TaskCompletionSource<Done> ReadResultEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<Done> ReleaseReadResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<Done> DrainEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<Done> ReleaseDrain { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public override void AdvanceTo(SequencePosition consumed)
            {
                reader.AdvanceTo(consumed);
                _heldResult = false;
            }
            public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => reader.AdvanceTo(consumed, examined);
            public override void CancelPendingRead() => reader.CancelPendingRead();
            public override void Complete(Exception? exception = null) => reader.Complete(exception);
            public override ValueTask CompleteAsync(Exception? exception = null)
            {
                Interlocked.Increment(ref _completions);
                CompletionOvertookRead |= _heldResult;
                return reader.CompleteAsync(exception);
            }
            public override bool TryRead(out ReadResult result) => reader.TryRead(out result);
            public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
            {
                var read = Interlocked.Increment(ref _reads);
                if (read == 1 && HoldFirstReadResult)
                    return new ValueTask<ReadResult>(HoldResultAsync(cancellationToken));
                if (read == 2 && HoldSecondRead)
                {
                    DrainEntered.TrySetResult(Done.Instance);
                    return new ValueTask<ReadResult>(WaitForDrainAsync(cancellationToken));
                }
                return reader.ReadAsync(cancellationToken);
            }
            private async Task<ReadResult> HoldResultAsync(CancellationToken token)
            {
                var result = await reader.ReadAsync(token);
                _heldResult = true;
                ReadResultEntered.TrySetResult(Done.Instance);
                // A read may already have completed when cancellation arrives. Hold its result
                // independently of cancellation to prove that retirement waits for AdvanceTo.
                await ReleaseReadResult.Task;
                return result;
            }
            private async Task<ReadResult> WaitForDrainAsync(CancellationToken token)
            {
                await ReleaseDrain.Task.WaitAsync(token);
                return await reader.ReadAsync(token);
            }
        }

        private sealed class CountingSource(ReadOnlySequence<byte>[] frames, IActorRef observer)
            : GraphStage<SourceShape<ReadOnlySequence<byte>>>
        {
            private readonly Outlet<ReadOnlySequence<byte>> _out = new("test.owned-frames");
            private readonly ReadOnlySequence<byte>[] _frames = frames;
            private readonly IActorRef _observer = observer;
            public override SourceShape<ReadOnlySequence<byte>> Shape => new(_out);
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

            private sealed class Logic : OutGraphStageLogic
            {
                private readonly CountingSource _stage;
                private int _next;
                internal Logic(CountingSource stage) : base(stage.Shape)
                {
                    _stage = stage;
                    SetHandler(stage._out, this);
                }
                public override void OnPull()
                {
                    var frame = _stage._frames[_next++];
                    _stage._observer.Tell(_next);
                    Push(_stage._out, frame);
                    if (_next == _stage._frames.Length)
                        CompleteStage();
                }
                public override void PostStop()
                {
                    while (_next < _stage._frames.Length)
                        _stage._frames[_next++].DisposeOwnedSegments();
                }
            }
        }

        private sealed class PoisonOwner(byte[] bytes) : IMemoryOwner<byte>
        {
            internal int Disposals { get; private set; }
            public Memory<byte> Memory => bytes;
            public void Dispose()
            {
                Disposals++;
                Array.Fill(bytes, (byte)0xCC);
            }
        }

        private sealed class TrackingMemoryPool : MemoryPool<byte>
        {
            private int _outstanding;
            internal int Outstanding => Volatile.Read(ref _outstanding);
            public override int MaxBufferSize => int.MaxValue;
            public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
            {
                Interlocked.Increment(ref _outstanding);
                return new Lease(this, new byte[Math.Max(minBufferSize, 4096)]);
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    Outstanding.Should().Be(0, "all pipe leases must be returned before the test pool is disposed");
            }

            private sealed class Lease(TrackingMemoryPool pool, byte[] bytes) : IMemoryOwner<byte>
            {
                private int _disposed;
                public Memory<byte> Memory => bytes;
                public void Dispose()
                {
                    if (Interlocked.Exchange(ref _disposed, 1) == 0)
                        Interlocked.Decrement(ref pool._outstanding);
                }
            }
        }

        private sealed class FakeTransport : ITransportConnection
        {
            private readonly Pipe _input;
            private readonly GatedReader _reader;
            private Exception? _readError;
            internal FakeTransport(MemoryPool<byte>? pool = null)
            {
                _input = new Pipe(new PipeOptions(pool: pool, useSynchronizationContext: false));
                _reader = new GatedReader(_input.Reader);
            }
            private int _aborts;
            private int _disposals;
            private int _flushes;
            private int _closes;
            private int _starts;
            internal bool HoldInitialization { get; init; }
            internal bool HoldFirstFlush { get; init; }
            internal bool HoldDisposal { get; init; }
            internal bool DelayPumpNotificationsUntilDisposal { get; init; }
            internal bool HoldCloseUntilDisposal { get; init; }
            internal bool DeferReadErrorNotification { get; init; }
            internal GatedReader Reader => _reader;
            internal Exception? WriteFailure { get; init; }
            internal Exception? StartFailure { get; init; }
            internal Exception? AbortFailure { get; init; }
            internal Exception? DisposalFailure { get; init; }
            internal int Aborts => Volatile.Read(ref _aborts);
            internal int Disposals => Volatile.Read(ref _disposals);
            internal int Flushes => Volatile.Read(ref _flushes);
            internal int Closes => Volatile.Read(ref _closes);
            internal int Starts => Volatile.Read(ref _starts);
            internal Channel<byte[]> Writes { get; } = Channel.CreateUnbounded<byte[]>();
            internal TaskCompletionSource<Done> InitializationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<Done> ReleaseInitialization { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<Done> WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<Done> FlushEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<FlushResult> ReleaseFlush { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<Done> DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<Done> ReleaseDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<Done> CloseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<Done> ReleaseClose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<Done> ReadPump { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<Done> WritePump { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public PipeReader Input => _reader;
            internal PipeWriter InputWriter => _input.Writer;
            public Task ReadCompleted => ReadPump.Task;
            public Task WriteCompleted => WritePump.Task;
            public bool HasReadError => Volatile.Read(ref _readError) is not null;
            public Exception? ReadError => Volatile.Read(ref _readError);

            public async Task InitializeAsync(CancellationToken cancellationToken = default)
            {
                InitializationEntered.TrySetResult(Done.Instance);
                if (HoldInitialization)
                    await ReleaseInitialization.Task.WaitAsync(cancellationToken);
            }

            public void Start()
            {
                Interlocked.Increment(ref _starts);
                if (StartFailure is { } error)
                    throw error;
            }

            public void Write(ReadOnlySequence<byte> data)
            {
                WriteEntered.TrySetResult(Done.Instance);
                if (WriteFailure is { } failure)
                    throw failure;
                Writes.Writer.TryWrite(data.ToArray()).Should().BeTrue();
            }

            public ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
            {
                var count = Interlocked.Increment(ref _flushes);
                FlushEntered.TrySetResult(Done.Instance);
                return HoldFirstFlush && count == 1
                    ? new ValueTask<FlushResult>(ReleaseFlush.Task.WaitAsync(cancellationToken))
                    : new ValueTask<FlushResult>(new FlushResult(false, false));
            }

            internal async Task EndInputAsync(Exception? error = null)
            {
                Volatile.Write(ref _readError, error);
                await InputWriter.CompleteAsync();
                if (error is null)
                    ReadPump.TrySetResult(Done.Instance);
                else if (!DeferReadErrorNotification)
                    ReadPump.TrySetException(error);
            }

            public Task ShutdownAsync() => throw new InvalidOperationException("Outgoing TCP must request a full close.");

            public async Task CloseAsync()
            {
                Interlocked.Increment(ref _closes);
                CloseEntered.TrySetResult(Done.Instance);
                await InputWriter.CompleteAsync();
                if (HoldCloseUntilDisposal)
                    await ReleaseClose.Task;
                if (!DelayPumpNotificationsUntilDisposal)
                {
                    WritePump.TrySetResult(Done.Instance);
                    ReadPump.TrySetResult(Done.Instance);
                }
            }

            public void Abort()
            {
                Interlocked.Increment(ref _aborts);
                if (AbortFailure is { } error)
                    throw error;
                ReadPump.TrySetCanceled();
                WritePump.TrySetCanceled();
                _input.Reader.CancelPendingRead();
            }

            public async ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref _disposals);
                DisposalEntered.TrySetResult(Done.Instance);
                ReleaseClose.TrySetResult(Done.Instance);
                if (HoldDisposal)
                    await ReleaseDisposal.Task;
                ReadPump.TrySetCanceled();
                WritePump.TrySetCanceled();
                await InputWriter.CompleteAsync();
                if (DisposalFailure is { } error)
                    throw error;
            }
        }
    }
}
