//-----------------------------------------------------------------------
// <copyright file="TcpTlsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using FluentAssertions;
using Xunit;
using Tcp = Akka.Streams.Dsl.Tcp;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.IO
{
    public class TcpTlsSpec : TcpHelper
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

        public TcpTlsSpec(ITestOutputHelper output) : base("", output)
        {
        }

        [Fact(DisplayName = "Should exchange bytes over an authenticated mutual TLS Streams graph")]
        public async Task Should_exchange_bytes_over_an_authenticated_mutual_tls_streams_graph()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            using var clientCertificate = CreateCertificate("client", server: false);
            var serverValidatorCalls = 0;
            SslPolicyErrors? observedServerErrors = null;
            SslPolicyErrors? observedClientErrors = null;
            var serverTls = TlsServerSettings.Mutual(serverCertificate, TlsPeerPolicy.CustomTrust((certificate, _, _, errors, _) =>
            {
                Interlocked.Increment(ref serverValidatorCalls);
                observedServerErrors = errors;
                return certificate?.Thumbprint == clientCertificate.Thumbprint;
            }));
            var clientTls = TlsClientSettings.Mutual(clientCertificate, TlsPeerPolicy.CustomTrust((certificate, _, _, errors, _) =>
            {
                observedClientErrors = errors;
                return certificate?.Thumbprint == serverCertificate.Thumbprint;
            })).WithTargetHost("localhost");

            var binding = await Sys.TcpStream().BindAndHandleTls(Flow.Create<ReadOnlySequence<byte>>(), Materializer,
                    "127.0.0.1", 0, serverTls)
                .WaitAsync(TestTimeout);
            try
            {
                var payload = Encoding.UTF8.GetBytes("authenticated Streams echo");
                var received = await Source.Single(new ReadOnlySequence<byte>(payload))
                    .ViaMaterialized(Sys.TcpStream().OutgoingConnectionTls(binding.LocalAddress, clientTls), Keep.Right)
                    .RunWith(Sink.First<ReadOnlySequence<byte>>(), Materializer)
                    .WaitAsync(TestTimeout);

                received.ToArray().Should().Equal(payload);
                serverValidatorCalls.Should().Be(1);
                observedServerErrors.HasValue.Should().BeTrue();
                observedServerErrors.GetValueOrDefault().Should().HaveFlag(SslPolicyErrors.RemoteCertificateChainErrors);
                observedClientErrors.HasValue.Should().BeTrue();
                observedClientErrors.GetValueOrDefault().Should().HaveFlag(SslPolicyErrors.RemoteCertificateChainErrors);
            }
            finally
            {
                await binding.Unbind().WaitAsync(TestTimeout);
            }
        }

        [Fact(DisplayName = "Should fail outgoing materialization when the TLS peer certificate is rejected")]
        public async Task Should_fail_outgoing_materialization_when_the_tls_peer_certificate_is_rejected()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            var binding = await Sys.TcpStream().BindAndHandleTls(Flow.Create<ReadOnlySequence<byte>>(), Materializer,
                    "127.0.0.1", 0, TlsServerSettings.ServerOnly(serverCertificate))
                .WaitAsync(TestTimeout);
            try
            {
                var wrongPin = TlsClientSettings.ServerOnly(TlsPeerPolicy.PinnedCertificates(new string('0', 40)))
                    .WithTargetHost("localhost");
                var (inputQueue, connectTask) = Source.Queue<ReadOnlySequence<byte>>(1, OverflowStrategy.Backpressure)
                    .ViaMaterialized(Sys.TcpStream().OutgoingConnectionTls(binding.LocalAddress, wrongPin), Keep.Both)
                    .ToMaterialized(Sink.Ignore<ReadOnlySequence<byte>>(), Keep.Left)
                    .Run(Materializer);

                var connectionFailure = await CaptureConnectionFailureAsync(connectTask);
                connectionFailure.Flatten().InnerExceptions.Should().Contain(exception => exception is StreamTcpException);
                inputQueue.Complete();
            }
            finally
            {
                await binding.Unbind().WaitAsync(TestTimeout);
            }
        }

        [Fact(DisplayName = "Should reject a missing mutual TLS client certificate before publishing a connection")]
        public async Task Should_reject_a_missing_mutual_tls_client_certificate_before_publishing_a_connection()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            var validationCalls = 0;
            var serverTls = TlsServerSettings.Mutual(serverCertificate, TlsPeerPolicy.CustomTrust((_, _, _, _, _) =>
            {
                Interlocked.Increment(ref validationCalls);
                return true;
            }));
            var (bindingTask, incomingQueue) = Sys.TcpStream().BindTls("127.0.0.1", 0, serverTls)
                .ToMaterialized(Sink.Queue<Tcp.IncomingConnection>(), Keep.Both)
                .Run(Materializer);
            var binding = await bindingTask.WaitAsync(TestTimeout);
            using var client = new TcpClient();
            using var cancellation = new CancellationTokenSource(TestTimeout);
            try
            {
                await client.ConnectAsync((IPEndPoint)binding.LocalAddress, cancellation.Token);
                using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: true, (_, _, _, _) => true);
                Exception? authenticationFailure = null;
                try
                {
                    await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = "localhost",
                        EnabledSslProtocols = SslProtocols.Tls12
                    }, cancellation.Token);
                }
                catch (Exception exception) when (exception is AuthenticationException or IOException)
                {
                    // A peer that requires a client certificate can reject during authentication.
                    authenticationFailure = exception;
                }

                if (authenticationFailure is null)
                {
                    var readBuffer = new byte[1];
                    var peerRejected = false;
                    try
                    {
                        var read = await tls.ReadAsync(readBuffer, cancellation.Token);
                        peerRejected = read == 0;
                    }
                    catch (IOException)
                    {
                        peerRejected = true;
                    }

                    peerRejected.Should().BeTrue();
                }

                await Awaiting(() => incomingQueue.PullAsync().WaitAsync(TimeSpan.FromMilliseconds(300)))
                    .Should().ThrowAsync<TimeoutException>();
                validationCalls.Should().Be(0);
            }
            finally
            {
                client.Dispose();
                await binding.Unbind().WaitAsync(TestTimeout);
            }
        }

        [Fact(DisplayName = "Should keep a TLS Streams listener available while another handshake stalls")]
        public async Task Should_keep_a_tls_streams_listener_available_while_another_handshake_stalls()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            var binding = await Sys.TcpStream().BindAndHandleTls(Flow.Create<ReadOnlySequence<byte>>(), Materializer,
                    "127.0.0.1", 0, TlsServerSettings.ServerOnly(serverCertificate)
                        .WithHandshakeTimeout(TimeSpan.FromMilliseconds(500)))
                .WaitAsync(TestTimeout);
            using var stalledClient = new TcpClient();
            using var cancellation = new CancellationTokenSource(TestTimeout);
            try
            {
                await stalledClient.ConnectAsync((IPEndPoint)binding.LocalAddress, cancellation.Token);
                var payload = Encoding.UTF8.GetBytes("second authenticated connection");
                var received = await Source.Single(new ReadOnlySequence<byte>(payload))
                    .ViaMaterialized(Sys.TcpStream().OutgoingConnectionTls(binding.LocalAddress,
                        TlsClientSettings.ServerOnly(TlsPeerPolicy.PinnedCertificates(serverCertificate.Thumbprint))
                            .WithTargetHost("localhost")), Keep.Right)
                    .RunWith(Sink.First<ReadOnlySequence<byte>>(), Materializer)
                    .WaitAsync(TestTimeout);

                received.ToArray().Should().Equal(payload);
            }
            finally
            {
                stalledClient.Dispose();
                await binding.Unbind().WaitAsync(TestTimeout);
            }
        }

        [Fact(DisplayName = "Should keep an accepted TLS connection readable after the handler write side completes")]
        public async Task Should_keep_an_accepted_tls_connection_readable_after_the_handler_write_side_completes()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            var receivedPayload = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeSideCompletion = new TaskCompletionSource<Task<Akka.Done>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var binding = await Sys.TcpStream().BindTls("127.0.0.1", 0,
                    TlsServerSettings.ServerOnly(serverCertificate), halfClose: true)
                .ToMaterialized(Sink.ForEach<Tcp.IncomingConnection>(connection =>
                {
                    var handler = Flow.FromSinkAndSource(
                        Sink.ForEach<ReadOnlySequence<byte>>(bytes =>
                            receivedPayload.TrySetResult(Encoding.UTF8.GetString(bytes.ToArray()))),
                        Source.Empty<ReadOnlySequence<byte>>().WatchTermination((_, completion) => completion),
                        Keep.Right);
                    writeSideCompletion.TrySetResult(connection.HandleWith(handler, Materializer));
                }), Keep.Left)
                .Run(Materializer)
                .WaitAsync(TestTimeout);

            using var client = new TcpClient();
            using var cancellation = new CancellationTokenSource(TestTimeout);
            try
            {
                await client.ConnectAsync((IPEndPoint)binding.LocalAddress, cancellation.Token);
                using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: true,
                    (_, certificate, _, _) => certificate?.GetCertHashString() == serverCertificate.Thumbprint);
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = "localhost"
                }, cancellation.Token);

                await (await writeSideCompletion.Task.WaitAsync(TestTimeout)).WaitAsync(TestTimeout);
                var closeNotify = new byte[1];
                (await tls.ReadAsync(closeNotify, cancellation.Token)).Should().Be(0);
                await tls.WriteAsync(Encoding.UTF8.GetBytes("read after server write completion"), cancellation.Token);
                await tls.FlushAsync(cancellation.Token);
                (await receivedPayload.Task.WaitAsync(TestTimeout)).Should().Be("read after server write completion");
            }
            finally
            {
                client.Dispose();
                await binding.Unbind().WaitAsync(TestTimeout);
            }
        }

        [Fact(DisplayName = "Should time out a stalled outgoing TLS handshake")]
        public async Task Should_time_out_a_stalled_outgoing_tls_handshake()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var cancellation = new CancellationTokenSource(TestTimeout);
            var accepted = listener.AcceptTcpClientAsync(cancellation.Token);
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var tls = TlsClientSettings.ServerOnly(TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true))
                .WithTargetHost("localhost").WithHandshakeTimeout(TimeSpan.FromMilliseconds(400));
            var (inputQueue, outgoing) = Source.Queue<ReadOnlySequence<byte>>(1, OverflowStrategy.Backpressure)
                .ViaMaterialized(Sys.TcpStream().OutgoingConnectionTls(endpoint, tls), Keep.Both)
                .ToMaterialized(Sink.Ignore<ReadOnlySequence<byte>>(), Keep.Left)
                .Run(Materializer);

            using var stalledPeer = await accepted.AsTask().WaitAsync(TestTimeout);
            AggregateException? connectionFailure = null;
            try
            {
                await outgoing.WaitAsync(TestTimeout);
            }
            catch (AggregateException exception)
            {
                connectionFailure = exception;
            }

            connectionFailure.Should().NotBeNull();
            connectionFailure!.Flatten().InnerExceptions.Any(exception =>
                    exception is StreamTcpException streamException && streamException.InnerException is TimeoutException)
                .Should().BeTrue();
            inputQueue.Complete();
        }

        [Fact(DisplayName = "Should close a stalled TLS handshake when the outgoing graph is cancelled")]
        public async Task Should_close_a_stalled_tls_handshake_when_the_outgoing_graph_is_cancelled()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var cancellation = new CancellationTokenSource(TestTimeout);
            var accepted = listener.AcceptTcpClientAsync(cancellation.Token);
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var tls = TlsClientSettings.ServerOnly(TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true))
                .WithTargetHost("localhost").WithHandshakeTimeout(TestTimeout);
            var ((inputQueue, outgoing), killSwitch) = Source.Queue<ReadOnlySequence<byte>>(1, OverflowStrategy.Backpressure)
                .ViaMaterialized(Sys.TcpStream().OutgoingConnectionTls(endpoint, tls), Keep.Both)
                .ViaMaterialized(KillSwitches.Single<ReadOnlySequence<byte>>(), Keep.Both)
                .ToMaterialized(Sink.Ignore<ReadOnlySequence<byte>>(), Keep.Left)
                .Run(Materializer);

            using var stalledPeer = await accepted.AsTask().WaitAsync(TestTimeout);
            killSwitch.Shutdown();
            var connectionFailure = await CaptureConnectionFailureAsync(outgoing);
            connectionFailure.Flatten().InnerExceptions.Should().Contain(exception => exception is StreamTcpException);

            var peerClosed = false;
            var readBuffer = new byte[4096];
            while (!peerClosed)
            {
                try
                {
                    peerClosed = await stalledPeer.GetStream().ReadAsync(readBuffer, cancellation.Token) == 0;
                }
                catch (IOException)
                {
                    peerClosed = true;
                }
            }

            peerClosed.Should().BeTrue();
            inputQueue.Complete();
        }

        [Fact(DisplayName = "Should preserve plaintext outgoing connection materialization failures")]
        public async Task Should_preserve_plaintext_outgoing_connection_materialization_failures()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, GetUnusedPort());
            var outgoing = Source.Empty<ReadOnlySequence<byte>>()
                .ViaMaterialized(Sys.TcpStream().OutgoingConnection(endpoint, connectionTimeout: TimeSpan.FromSeconds(2)),
                    Keep.Right)
                .ToMaterialized(Sink.Ignore<ReadOnlySequence<byte>>(), Keep.Left)
                .Run(Materializer);

            await Awaiting(() => outgoing.WaitAsync(TestTimeout))
                .Should().ThrowAsync<Akka.Streams.StreamTcpException>()
                .WithMessage("Connection failed*");
        }

        private static async Task<AggregateException> CaptureConnectionFailureAsync(Task connectionTask)
        {
            try
            {
                await connectionTask.WaitAsync(TestTimeout);
            }
            catch (AggregateException exception)
            {
                return exception;
            }

            throw new InvalidOperationException("The connection materialization task did not fail.");
        }

        private static X509Certificate2 CreateCertificate(string subject, bool server)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={subject}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName(subject);
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                server ? X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment : X509KeyUsageFlags.DigitalSignature,
                true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var exported = generated.Export(X509ContentType.Pkcs12);
            try
            {
                return X509CertificateLoader.LoadPkcs12(exported, null, X509KeyStorageFlags.DefaultKeySet);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(exported);
            }
        }

        private static int GetUnusedPort()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
