// -----------------------------------------------------------------------
// <copyright file="TcpTlsIntegrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using TcpListener = System.Net.Sockets.TcpListener;

namespace Akka.Tests.IO
{
    public class TcpTlsIntegrationSpec : AkkaSpec
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

        public TcpTlsIntegrationSpec(ITestOutputHelper output)
            : base("akka.loglevel = DEBUG\nakka.io.tcp.trace-logging = true", output: output)
        {
        }

        [Theory(DisplayName = "Should_Exchange_Bytes_And_Preserve_Confirmed_Half_Close_Over_TLS")]
        [InlineData(SslProtocols.Tls12)]
        [InlineData(SslProtocols.Tls13)]
        public async Task Should_exchange_bytes_and_preserve_confirmed_half_close_over_tls(SslProtocols protocol)
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var peerTask = RunRawTlsServerAsync(listener, serverCertificate, protocol, "from Akka", "from peer");

            var commander = CreateTestProbe();
            var connectionHandler = CreateTestProbe();
            var connect = new Tcp.Connect(endpoint)
            {
                Tls = TlsClientSettings.ServerOnly(TlsPeerPolicy.PinnedCertificates(serverCertificate.Thumbprint)).WithTargetHost("localhost").WithProtocols(protocol)
            };
            commander.Send(Sys.Tcp(), connect);

            var connected = await commander.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
            var connection = commander.LastSender;
            await WatchAsync(connection);
            ((IPEndPoint)connected.RemoteAddress).Port.Should().Be(endpoint.Port);
            connectionHandler.Send(connection, Tcp.Write.Create(Encoding.UTF8.GetBytes("from Akka")));
            connectionHandler.Send(connection, new Tcp.Register(connectionHandler.Ref));
            connectionHandler.Send(connection, Tcp.ConfirmedClose.Instance);

            await ExpectPayloadAsync(connectionHandler, "from peer");
            await connectionHandler.ExpectMsgAsync<Tcp.ConfirmedClosed>(TestTimeout);
            await ExpectTerminatedAsync(connection, TestTimeout);
            await peerTask.WaitAsync(TestTimeout);
        }

        [Fact(DisplayName = "Should_Keep_Plain_TCP_Connections_Working_Without_TLS_Settings")]
        public async Task Should_keep_plain_tcp_connections_working_without_tls_settings()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            using var cancellation = new CancellationTokenSource(TestTimeout);
            var commander = CreateTestProbe();
            commander.Send(Sys.Tcp(), new Tcp.Connect(endpoint));

            await commander.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
            var connection = commander.LastSender;
            using var peerSocket = await listener.AcceptSocketAsync(cancellation.Token);
            using var peerStream = new NetworkStream(peerSocket, ownsSocket: false);
            var handler = CreateTestProbe();
            commander.Send(connection, new Tcp.Register(handler.Ref));
            handler.Send(connection, Tcp.Write.Create(Encoding.UTF8.GetBytes("plain tcp")));

            var payload = new byte["plain tcp".Length];
            await peerStream.ReadExactlyAsync(payload, cancellation.Token);
            Encoding.UTF8.GetString(payload).Should().Be("plain tcp");
            await AbortConnectionAsync(handler, connection);
        }

        [Fact(DisplayName = "Should_Exchange_Bytes_And_Run_Mutual_TLS_Validators")]
        public async Task Should_exchange_bytes_and_run_mutual_tls_validators()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            using var clientCertificate = CreateCertificate("client", server: false);
            var clientValidatorCalls = 0;
            var serverValidatorCalls = 0;
            var listenerHandler = CreateTestProbe();
            var bind = new Tcp.Bind(listenerHandler.Ref, new IPEndPoint(IPAddress.Loopback, 0))
            {
                Tls = TlsServerSettings.Mutual(serverCertificate, TlsPeerPolicy.CustomTrust((_, _, _, _, _) =>
                    {
                        serverValidatorCalls++;
                        return true;
                    }))
            };
            listenerHandler.Send(Sys.Tcp(), bind);
            var bound = await listenerHandler.ExpectMsgAsync<Tcp.Bound>(TestTimeout);
            var endpoint = (IPEndPoint)bound.LocalAddress;
            var listenerActor = listenerHandler.LastSender;

            using var socket = new TcpClient();
            using var cancellation = new CancellationTokenSource(TestTimeout);
            await socket.ConnectAsync(endpoint.Address, endpoint.Port, cancellation.Token);
            using var ssl = new SslStream(socket.GetStream(), leaveInnerStreamOpen: true,
                (_, _, _, _) =>
                {
                    clientValidatorCalls++;
                    return true;
                });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                ClientCertificates = new X509CertificateCollection { clientCertificate },
                EnabledSslProtocols = SslProtocols.Tls12
            }, cancellation.Token);

            await listenerHandler.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
            var connection = listenerHandler.LastSender;
            serverValidatorCalls.Should().Be(1);
            clientValidatorCalls.Should().Be(1);

            var handler = CreateTestProbe();
            listenerHandler.Send(connection, new Tcp.Register(handler.Ref));
            await ssl.WriteAsync(Encoding.UTF8.GetBytes("peer hello"), cancellation.Token);
            await ExpectPayloadAsync(handler, "peer hello");

            handler.Send(connection, Tcp.Write.Create(Encoding.UTF8.GetBytes("akka hello")));
            var reply = new byte[10];
            await ssl.ReadExactlyAsync(reply, cancellation.Token);
            Encoding.UTF8.GetString(reply).Should().Be("akka hello");
            await AbortConnectionAsync(handler, connection);
            listenerHandler.Send(listenerActor, Tcp.Unbind.Instance);
            await listenerHandler.ExpectMsgAsync<Tcp.Unbound>(TestTimeout);
        }

        [Fact(DisplayName = "Should_Reject_Missing_Mutual_TLS_Certificate_Even_When_Custom_Validator_Accepts")]
        public async Task Should_reject_missing_mutual_tls_certificate_even_when_custom_validator_accepts()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            var validatorCalls = 0;
            var listenerHandler = CreateTestProbe();
            listenerHandler.Send(Sys.Tcp(), new Tcp.Bind(listenerHandler.Ref, new IPEndPoint(IPAddress.Loopback, 0))
            {
                Tls = TlsServerSettings.Mutual(serverCertificate, TlsPeerPolicy.CustomTrust((_, _, _, _, _) =>
                    {
                        validatorCalls++;
                        return true;
                    }))
            });
            var bound = await listenerHandler.ExpectMsgAsync<Tcp.Bound>(TestTimeout);
            var listenerActor = listenerHandler.LastSender;
            using var cancellation = new CancellationTokenSource(TestTimeout);
            var endpoint = (IPEndPoint)bound.LocalAddress;
            using (var socket = new TcpClient())
            {
                await socket.ConnectAsync(endpoint.Address, endpoint.Port, cancellation.Token);
                using var ssl = new SslStream(socket.GetStream(), leaveInnerStreamOpen: true, (_, _, _, _) => true);
                var authenticationFailed = false;
                try
                {
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = "localhost",
                        EnabledSslProtocols = SslProtocols.Tls12
                    }, cancellation.Token);
                }
                catch (Exception exception) when (exception is AuthenticationException or IOException)
                {
                    authenticationFailed = true;
                }

                if (!authenticationFailed)
                    await ObserveTlsPeerCloseAsync(ssl, TimeSpan.FromSeconds(3));
            }

            validatorCalls.Should().Be(0);
            await listenerHandler.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            listenerHandler.Send(listenerActor, Tcp.Unbind.Instance);
            await listenerHandler.ExpectMsgAsync<Tcp.Unbound>(TestTimeout);
        }

        [Theory(DisplayName = "Should_Apply_Explicit_Trust_And_Hostname_Policies_During_Real_Handshake")]
        [InlineData("pinned", "localhost", true)]
        [InlineData("pinned", "wrong.example", false)]
        [InlineData("pin-rotation", "localhost", true)]
        [InlineData("wrong-pin", "localhost", false)]
        [InlineData("system", "localhost", false)]
        public async Task Should_apply_explicit_trust_and_hostname_policies_during_real_handshake(
            string trustMode,
            string targetHost,
            bool shouldConnect)
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var peerTask = AcceptAndAuthenticateAsync(listener, serverCertificate, SslProtocols.Tls12);
            var validation = trustMode switch
            {
                "pinned" => TlsPeerPolicy.PinnedCertificates(serverCertificate.Thumbprint)
                    .And(TlsCertificateValidation.ValidateHostname()),
                "wrong-pin" => TlsPeerPolicy.PinnedCertificates(new string('0', 40))
                    .And(TlsCertificateValidation.ValidateHostname()),
                "pin-rotation" => TlsPeerPolicy.PinnedCertificates(new string('0', 40), serverCertificate.Thumbprint)
                    .And(TlsCertificateValidation.ValidateHostname()),
                "system" => TlsPeerPolicy.SystemTrust(),
                _ => throw new ArgumentOutOfRangeException(nameof(trustMode), trustMode, "Unknown trust mode.")
            };
            var commander = CreateTestProbe();
            var command = new Tcp.Connect(endpoint)
            {
                Tls = TlsClientSettings.ServerOnly(validation).WithTargetHost(targetHost)
            };
            commander.Send(Sys.Tcp(), command);

            if (shouldConnect)
            {
                await commander.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
                await peerTask.WaitAsync(TestTimeout);
                await AbortConnectionAsync(commander, commander.LastSender);
            }
            else
            {
                var failure = await commander.ExpectMsgAsync<Tcp.CommandFailed>(TestTimeout);
                failure.Cause.HasValue.Should().BeTrue();
                await ObservePeerAuthenticationAsync(peerTask);
            }
        }

        [Theory(DisplayName = "Should_Reject_The_Server_When_Custom_Client_Validator_Rejects_Or_Throws")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_reject_the_server_when_custom_client_validator_rejects_or_throws(bool throwFromValidator)
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var peerTask = AcceptAndAuthenticateAsync(listener, serverCertificate, SslProtocols.Tls12);
            var commander = CreateTestProbe();
            var callbackCalls = 0;
            var connect = new Tcp.Connect(endpoint)
            {
                Tls = TlsClientSettings.ServerOnly(TlsPeerPolicy.CustomTrust((_, _, _, _, _) =>
                    {
                        callbackCalls++;
                        if (throwFromValidator)
                            throw new InvalidOperationException("intentional validator failure");
                        return false;
                    })).WithTargetHost("localhost")
            };

            commander.Send(Sys.Tcp(), connect);
            var failed = await commander.ExpectMsgAsync<Tcp.CommandFailed>(TestTimeout);
            failed.Cause.HasValue.Should().BeTrue();
            callbackCalls.Should().Be(1);
            await ObservePeerAuthenticationAsync(peerTask);
        }

        [Theory(DisplayName = "Should_Reject_A_Bad_Inbound_Client_And_Accept_A_Good_One_After_Callback_Rejects_Or_Throws")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_reject_a_bad_inbound_client_and_accept_a_good_one_after_callback_rejects_or_throws(bool throwFromValidator)
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            using var rejectedClientCertificate = CreateCertificate("rejected-client", server: false);
            using var acceptedClientCertificate = CreateCertificate("accepted-client", server: false);
            var rejectedValidation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var rejectedCalls = 0;
            var acceptedCalls = 0;
            var listenerHandler = CreateTestProbe();
            listenerHandler.Send(Sys.Tcp(), new Tcp.Bind(listenerHandler.Ref, new IPEndPoint(IPAddress.Loopback, 0))
            {
                Tls = TlsServerSettings.Mutual(serverCertificate, TlsPeerPolicy.CustomTrust((certificate, chain, remotePeer, _, log) =>
                    {
                        if (certificate!.Thumbprint == rejectedClientCertificate.Thumbprint)
                        {
                            rejectedCalls++;
                            rejectedValidation.TrySetResult();
                            if (throwFromValidator)
                                throw new InvalidOperationException("intentional inbound validator failure");
                            return false;
                        }

                        acceptedCalls++;
                        return certificate.Thumbprint == acceptedClientCertificate.Thumbprint;
                    }))
            });
            var bound = await listenerHandler.ExpectMsgAsync<Tcp.Bound>(TestTimeout);
            var listenerActor = listenerHandler.LastSender;
            var endpoint = (IPEndPoint)bound.LocalAddress;

            using var rejectedClient = new TcpClient();
            using var cancellation = new CancellationTokenSource(TestTimeout);
            await rejectedClient.ConnectAsync(endpoint.Address, endpoint.Port, cancellation.Token);
            using (var rejectedTls = new SslStream(rejectedClient.GetStream(), leaveInnerStreamOpen: true, (_, _, _, _) => true))
            {
                var authenticationFailed = false;
                try
                {
                    await rejectedTls.AuthenticateAsClientAsync(CreateClientOptions(rejectedClientCertificate), cancellation.Token);
                }
                catch (Exception exception) when (exception is AuthenticationException or IOException)
                {
                    authenticationFailed = true;
                }

                if (!authenticationFailed)
                    await ObserveTlsPeerCloseAsync(rejectedTls, TimeSpan.FromSeconds(3));
            }

            await rejectedValidation.Task.WaitAsync(TestTimeout);
            rejectedCalls.Should().Be(1);
            await listenerHandler.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

            using var acceptedClient = new TcpClient();
            await acceptedClient.ConnectAsync(endpoint.Address, endpoint.Port, cancellation.Token);
            using var acceptedTls = new SslStream(acceptedClient.GetStream(), leaveInnerStreamOpen: true, (_, _, _, _) => true);
            await acceptedTls.AuthenticateAsClientAsync(CreateClientOptions(acceptedClientCertificate), cancellation.Token);
            var connected = await listenerHandler.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
            ((IPEndPoint)connected.RemoteAddress).Port.Should().Be(((IPEndPoint)acceptedClient.Client.LocalEndPoint!).Port);
            acceptedCalls.Should().Be(1);

            var handler = CreateTestProbe();
            var acceptedConnection = listenerHandler.LastSender;
            listenerHandler.Send(acceptedConnection, new Tcp.Register(handler.Ref));
            await acceptedTls.WriteAsync(Encoding.UTF8.GetBytes("accepted"), cancellation.Token);
            await ExpectPayloadAsync(handler, "accepted");
            await AbortConnectionAsync(handler, acceptedConnection);
            listenerHandler.Send(listenerActor, Tcp.Unbind.Instance);
            await listenerHandler.ExpectMsgAsync<Tcp.Unbound>(TestTimeout);
        }

        [Fact(DisplayName = "Should_Time_Out_A_Stalled_Handshake_While_A_Later_Connection_Authenticates")]
        public async Task Should_time_out_a_stalled_handshake_while_a_later_connection_authenticates()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var stalledAccepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseStalledPeer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var serverTask = AcceptStalledThenAuthenticateAsync(listener, serverCertificate, stalledAccepted, releaseStalledPeer);
            var stalledCommander = CreateTestProbe();
            var validCommander = CreateTestProbe();
            var stalledConnect = new Tcp.Connect(endpoint)
            {
                Tls = TlsClientSettings.ServerOnly(TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true)).WithHandshakeTimeout(TimeSpan.FromSeconds(2))
            };
            stalledCommander.Send(Sys.Tcp(), stalledConnect);
            await stalledAccepted.Task.WaitAsync(TimeSpan.FromSeconds(1));

            var authenticatedConnect = new Tcp.Connect(endpoint)
            {
                Tls = TlsClientSettings.ServerOnly(TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true)).WithTargetHost("localhost")
            };
            validCommander.Send(Sys.Tcp(), authenticatedConnect);
            await validCommander.ExpectMsgAsync<Tcp.Connected>(TimeSpan.FromSeconds(1));
            var connection = validCommander.LastSender;
            await AbortConnectionAsync(validCommander, connection);
            var failure = await stalledCommander.ExpectMsgAsync<Tcp.CommandFailed>(TestTimeout);
            failure.Cause.HasValue.Should().BeTrue();
            failure.Cause.Value.Should().BeOfType<TimeoutException>();
            await stalledCommander.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            releaseStalledPeer.TrySetResult();
            await serverTask.WaitAsync(TestTimeout);
        }

        [Fact(DisplayName = "Should_Close_The_Transport_When_Commander_Stops_Before_TLS_Authentication")]
        public async Task Should_close_the_transport_when_commander_stops_before_tls_authentication()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            using var cancellation = new CancellationTokenSource(TestTimeout);
            var commander = CreateTestProbe();
            commander.Send(Sys.Tcp(), new Tcp.Connect(endpoint)
            {
                Tls = TlsClientSettings.ServerOnly(TlsPeerPolicy.SystemTrust()).WithHandshakeTimeout(TestTimeout)
            });
            using var peerSocket = await listener.AcceptSocketAsync(cancellation.Token);
            using var peerStream = new NetworkStream(peerSocket, ownsSocket: false);
            using var closeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var peerClosed = DrainTcpUntilClosedAsync(peerStream, closeDeadline.Token);
            Sys.Stop(commander.Ref);
            await peerClosed.WaitAsync(TestTimeout);
        }

        [Fact(DisplayName = "Should_Keep_TLS_Listener_Available_While_Another_Incoming_Handshake_Is_Stalled")]
        public async Task Should_keep_tls_listener_available_while_another_incoming_handshake_is_stalled()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            var listenerHandler = CreateTestProbe();
            listenerHandler.Send(Sys.Tcp(), new Tcp.Bind(listenerHandler.Ref, new IPEndPoint(IPAddress.Loopback, 0))
            {
                Tls = TlsServerSettings.ServerOnly(serverCertificate).WithHandshakeTimeout(TimeSpan.FromSeconds(5))
            });
            var bound = await listenerHandler.ExpectMsgAsync<Tcp.Bound>(TestTimeout);
            var listenerActor = listenerHandler.LastSender;
            var endpoint = (IPEndPoint)bound.LocalAddress;

            using var stalledClient = new TcpClient();
            using var cancellation = new CancellationTokenSource(TestTimeout);
            await stalledClient.ConnectAsync(endpoint.Address, endpoint.Port, cancellation.Token);
            var stalledCloseTask = DrainTcpUntilClosedAsync(stalledClient.GetStream(), TimeSpan.FromSeconds(8));

            using var validClient = new TcpClient();
            using var validHandshakeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await validClient.ConnectAsync(endpoint.Address, endpoint.Port, validHandshakeDeadline.Token);
            using var validTls = new SslStream(validClient.GetStream(), leaveInnerStreamOpen: true, (_, _, _, _) => true);
            await validTls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                EnabledSslProtocols = SslProtocols.Tls12
            }, validHandshakeDeadline.Token);

            var connected = await listenerHandler.ExpectMsgAsync<Tcp.Connected>(
                TimeSpan.FromSeconds(3), cancellationToken: validHandshakeDeadline.Token);
            ((IPEndPoint)connected.RemoteAddress).Port.Should().Be(((IPEndPoint)validClient.Client.LocalEndPoint!).Port);
            var connection = listenerHandler.LastSender;
            var handler = CreateTestProbe();
            listenerHandler.Send(connection, new Tcp.Register(handler.Ref));
            await validTls.WriteAsync(Encoding.UTF8.GetBytes("valid peer"), cancellation.Token);
            await ExpectPayloadAsync(handler, "valid peer");
            handler.Send(connection, Tcp.Write.Create(Encoding.UTF8.GetBytes("listener alive")));
            var reply = new byte[14];
            await validTls.ReadExactlyAsync(reply, cancellation.Token);
            Encoding.UTF8.GetString(reply).Should().Be("listener alive");

            await AbortConnectionAsync(handler, connection);
            await stalledCloseTask.WaitAsync(TestTimeout);
            listenerHandler.Send(listenerActor, Tcp.Unbind.Instance);
            await listenerHandler.ExpectMsgAsync<Tcp.Unbound>(TestTimeout);
        }

        [Fact(DisplayName = "Should_Report_Invalid_TLS_Bind_Settings_As_Command_Failure")]
        public async Task Should_report_invalid_tls_bind_settings_as_command_failure()
        {
            var certificate = CreateCertificate("localhost", server: true);
            var tls = TlsServerSettings.ServerOnly(certificate);
            certificate.Dispose();
            var handler = CreateTestProbe();
            var bind = new Tcp.Bind(handler.Ref, new IPEndPoint(IPAddress.Loopback, 0))
            {
                Tls = tls
            };

            handler.Send(Sys.Tcp(), bind);

            var failure = await handler.ExpectMsgAsync<Tcp.CommandFailed>(TestTimeout);
            failure.Cmd.Should().BeSameAs(bind);
            failure.Cause.Value.Should().BeOfType<CryptographicException>();
        }

        [Fact(DisplayName = "Should_Present_Client_Certificate_And_Provide_Remote_Context_To_Validation")]
        public async Task Should_present_client_certificate_and_provide_remote_context_to_validation()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            using var clientCertificate = CreateCertificate("client", server: false);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var observedClientCertificate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var peerTask = AcceptMutualTlsClientAsync(listener, serverCertificate, observedClientCertificate);
            var callbackCalls = 0;
            var commander = CreateTestProbe();
            var connect = new Tcp.Connect(endpoint)
            {
                Tls = TlsClientSettings.Mutual(clientCertificate, TlsPeerPolicy.CustomTrust((certificate, chain, remotePeer, errors, log) =>
                    {
                        callbackCalls++;
                        certificate.Should().NotBeNull();
                        chain.Should().NotBeNull();
                        remotePeer.Should().Contain(endpoint.Port.ToString());
                        log.Should().NotBeNull();
                        errors.Should().Be(SslPolicyErrors.RemoteCertificateChainErrors);
                        return true;
                    })).WithTargetHost("localhost")
            };
            commander.Send(Sys.Tcp(), connect);

            await commander.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
            var connection = commander.LastSender;
            callbackCalls.Should().Be(1);
            (await observedClientCertificate.Task.WaitAsync(TestTimeout)).Should().Be(clientCertificate.Thumbprint);
            await AbortConnectionAsync(commander, connection);
            await peerTask.WaitAsync(TestTimeout);
        }

        [Fact(DisplayName = "Should_Close_Authenticated_Transport_When_Commander_Stops_Before_Register")]
        public async Task Should_close_authenticated_transport_when_commander_stops_before_register()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var peerTask = AcceptAndDrainUntilCloseAsync(listener, serverCertificate);
            var commander = CreateTestProbe();
            commander.Send(Sys.Tcp(), new Tcp.Connect(endpoint)
            {
                Tls = TlsClientSettings.ServerOnly(TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true)).WithTargetHost("localhost")
            });
            await commander.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
            var connection = commander.LastSender;
            await WatchAsync(connection);
            Sys.Stop(commander.Ref);

            await ExpectTerminatedAsync(connection, TestTimeout);
            (await peerTask.WaitAsync(TestTimeout)).Should().Be(0);
        }

        private static async Task RunRawTlsServerAsync(
            TcpListener listener,
            X509Certificate2 certificate,
            SslProtocols protocol,
            string expectedRequest,
            string response)
        {
            using var cancellation = new CancellationTokenSource(TestTimeout);
            using var socket = await listener.AcceptSocketAsync(cancellation.Token);
            using var networkStream = new NetworkStream(socket, ownsSocket: false);
            using var ssl = new SslStream(networkStream, leaveInnerStreamOpen: true);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                EnabledSslProtocols = protocol
            }, cancellation.Token);

            var request = new byte[Encoding.UTF8.GetByteCount(expectedRequest)];
            await ssl.ReadExactlyAsync(request, cancellation.Token);
            Encoding.UTF8.GetString(request).Should().Be(expectedRequest);
            (await ssl.ReadAsync(new byte[1], cancellation.Token)).Should().Be(0);

            await ssl.WriteAsync(Encoding.UTF8.GetBytes(response), cancellation.Token);
            await ssl.FlushAsync(cancellation.Token);
            await ssl.ShutdownAsync();
            socket.Shutdown(SocketShutdown.Send);
        }

        private static async Task AcceptAndAuthenticateAsync(TcpListener listener, X509Certificate2 certificate, SslProtocols protocol)
        {
            using var cancellation = new CancellationTokenSource(TestTimeout);
            using var socket = await listener.AcceptSocketAsync(cancellation.Token);
            using var networkStream = new NetworkStream(socket, ownsSocket: false);
            using var ssl = new SslStream(networkStream, leaveInnerStreamOpen: true);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                EnabledSslProtocols = protocol
            }, cancellation.Token);
        }

        private static async Task<int> AcceptAndDrainUntilCloseAsync(TcpListener listener, X509Certificate2 certificate)
        {
            using var cancellation = new CancellationTokenSource(TestTimeout);
            using var socket = await listener.AcceptSocketAsync(cancellation.Token);
            using var networkStream = new NetworkStream(socket, ownsSocket: false);
            using var ssl = new SslStream(networkStream, leaveInnerStreamOpen: true);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                EnabledSslProtocols = SslProtocols.Tls12
            }, cancellation.Token);
            var buffer = new byte[128];
            var receivedBytes = 0;
            try
            {
                int read;
                while ((read = await ssl.ReadAsync(buffer, cancellation.Token)) > 0)
                    receivedBytes += read;
            }
            catch (IOException)
            {
                // The connection actor aborts the socket when it stops before registration.
                return receivedBytes;
            }

            return receivedBytes;
        }

        private static async Task DrainTcpUntilClosedAsync(Stream stream, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            await DrainTcpUntilClosedAsync(stream, cancellation.Token);
        }

        private static async Task DrainTcpUntilClosedAsync(Stream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[256];
            try
            {
                while (await stream.ReadAsync(buffer, cancellationToken) != 0)
                {
                }
            }
            catch (IOException)
            {
                return;
            }
        }

        private static async Task ObserveTlsPeerCloseAsync(SslStream stream, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            try
            {
                (await stream.ReadAsync(new byte[1], cancellation.Token)).Should().Be(0);
            }
            catch (Exception exception) when (exception is IOException or AuthenticationException)
            {
                return;
            }
        }

        private static async Task ObservePeerAuthenticationAsync(Task peerAuthentication)
        {
            try
            {
                await peerAuthentication.WaitAsync(TestTimeout);
            }
            catch (Exception exception) when (exception is AuthenticationException or IOException)
            {
                return;
            }
        }

        private static async Task AcceptMutualTlsClientAsync(
            TcpListener listener,
            X509Certificate2 certificate,
            TaskCompletionSource<string> observedClientCertificate)
        {
            using var cancellation = new CancellationTokenSource(TestTimeout);
            using var socket = await listener.AcceptSocketAsync(cancellation.Token);
            using var networkStream = new NetworkStream(socket, ownsSocket: false);
            using var ssl = new SslStream(networkStream, leaveInnerStreamOpen: true);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls12,
                RemoteCertificateValidationCallback = (_, clientCertificate, _, errors) =>
                {
                    if (clientCertificate is null || errors != SslPolicyErrors.RemoteCertificateChainErrors)
                        return false;
                    observedClientCertificate.TrySetResult(clientCertificate.GetCertHashString());
                    return true;
                }
            }, cancellation.Token);
        }

        private static SslClientAuthenticationOptions CreateClientOptions(X509Certificate2 certificate) => new()
        {
            TargetHost = "localhost",
            ClientCertificates = new X509CertificateCollection { certificate },
            EnabledSslProtocols = SslProtocols.Tls12
        };

        private async Task AbortConnectionAsync(Akka.TestKit.TestProbe requester, IActorRef connection)
        {
            await WatchAsync(connection);
            requester.Send(connection, Tcp.Abort.Instance);
            await requester.ExpectMsgAsync<Tcp.Aborted>(TestTimeout);
            await ExpectTerminatedAsync(connection, TestTimeout);
        }

        private static async Task AcceptStalledThenAuthenticateAsync(
            TcpListener listener,
            X509Certificate2 certificate,
            TaskCompletionSource stalledAccepted,
            TaskCompletionSource releaseStalledPeer)
        {
            using var cancellation = new CancellationTokenSource(TestTimeout);
            using var stalledSocket = await listener.AcceptSocketAsync(cancellation.Token);
            using var stalledStream = new NetworkStream(stalledSocket, ownsSocket: false);
            stalledAccepted.TrySetResult();
            using var validSocket = await listener.AcceptSocketAsync(cancellation.Token);
            using var validStream = new NetworkStream(validSocket, ownsSocket: false);
            using var ssl = new SslStream(validStream, leaveInnerStreamOpen: true);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                EnabledSslProtocols = SslProtocols.Tls12
            }, cancellation.Token);
            await releaseStalledPeer.Task.WaitAsync(cancellation.Token);
        }

        private async Task ExpectPayloadAsync(Akka.TestKit.TestProbe probe, string expected)
        {
            var bytes = new byte[Encoding.UTF8.GetByteCount(expected)];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var received = await probe.ExpectMsgAsync<Tcp.Received>(TestTimeout);
                received.Data.CopyTo(bytes.AsSpan(offset));
                offset += checked((int)received.Data.Length);
            }

            Encoding.UTF8.GetString(bytes).Should().Be(expected);
        }

        private static X509Certificate2 CreateCertificate(string host, bool server)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={host}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName(host);
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                server ? X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment : X509KeyUsageFlags.DigitalSignature,
                true));
            var usages = new OidCollection { new Oid(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
            using var generatedCertificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            // Windows Schannel needs an imported key container for generated test certificates.
            var exportedCertificate = generatedCertificate.Export(X509ContentType.Pkcs12);
            try
            {
                return X509CertificateLoader.LoadPkcs12(
                    exportedCertificate,
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.DefaultKeySet);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(exportedCertificate);
            }
        }
    }
}
