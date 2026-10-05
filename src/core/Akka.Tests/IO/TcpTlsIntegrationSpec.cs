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
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using TcpListener = System.Net.Sockets.TcpListener;

namespace Akka.Tests.IO
{
    public class TcpTlsIntegrationSpec : AkkaSpec
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

        private sealed class CountingMemoryOwner : IMemoryOwner<byte>
        {
            private byte[]? _buffer;
            private int _disposeCount;

            public CountingMemoryOwner(byte[] buffer) => _buffer = buffer;

            public Memory<byte> Memory => _buffer is null ? Memory<byte>.Empty : new Memory<byte>(_buffer);
            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _buffer, null) is not null)
                    Interlocked.Increment(ref _disposeCount);
            }
        }

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
                Tls = new TlsClientSettings
                {
                    RequireMutualAuthentication = false,
                    TargetHost = "localhost",
                    EnabledSslProtocols = protocol,
                    CustomValidator = (_, _, _, _, _) => true
                }
            };
            commander.Send(Sys.Tcp(), connect);

            var connected = await commander.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
            var connection = commander.LastSender;
            ((IPEndPoint)connected.RemoteAddress).Port.Should().Be(endpoint.Port);
            connectionHandler.Send(connection, Tcp.Write.Create(Encoding.UTF8.GetBytes("from Akka")));
            connectionHandler.Send(connection, new Tcp.Register(connectionHandler.Ref));
            connectionHandler.Send(connection, Tcp.ConfirmedClose.Instance);

            await ExpectPayloadAsync(connectionHandler, "from peer");
            await connectionHandler.ExpectMsgAsync<Tcp.ConfirmedClosed>(TestTimeout);
            await peerTask.WaitAsync(TestTimeout);
        }

        [Fact(DisplayName = "Should_Authenticate_Incoming_TLS_Before_Reporting_Connected_And_Run_Mutual_Validators")]
        public async Task Should_authenticate_incoming_tls_before_reporting_connected_and_run_mutual_validators()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            using var clientCertificate = CreateCertificate("client", server: false);
            var clientValidatorCalls = 0;
            var serverValidatorCalls = 0;
            var listenerHandler = CreateTestProbe();
            var bind = new Tcp.Bind(listenerHandler.Ref, new IPEndPoint(IPAddress.Loopback, 0))
            {
                Tls = new TlsServerSettings(serverCertificate)
                {
                    RequireMutualAuthentication = true,
                    CustomValidator = (_, _, _, _, _) =>
                    {
                        serverValidatorCalls++;
                        return true;
                    }
                }
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
            await ReadExactlyAsync(ssl, reply, cancellation.Token);
            Encoding.UTF8.GetString(reply).Should().Be("akka hello");
            handler.Send(connection, Tcp.Abort.Instance);
            listenerHandler.Send(listenerActor, Tcp.Unbind.Instance);
            await listenerHandler.ExpectMsgAsync<Tcp.Unbound>(TestTimeout);
        }

        [Fact(DisplayName = "Should_Reject_Missing_Mutual_TLS_Certificate_Even_When_Custom_Validator_Accepts")]
        public async Task Should_reject_missing_mutual_tls_certificate_even_when_custom_validator_accepts()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            using var clientCertificate = CreateCertificate("client", server: false);
            var validatorCalls = 0;
            var listenerHandler = CreateTestProbe();
            listenerHandler.Send(Sys.Tcp(), new Tcp.Bind(listenerHandler.Ref, new IPEndPoint(IPAddress.Loopback, 0))
            {
                Tls = new TlsServerSettings(serverCertificate)
                {
                    RequireMutualAuthentication = true,
                    CustomValidator = (_, _, _, _, _) =>
                    {
                        validatorCalls++;
                        return true;
                    }
                }
            });
            var bound = await listenerHandler.ExpectMsgAsync<Tcp.Bound>(TestTimeout);
            var listenerActor = listenerHandler.LastSender;
            using var cancellation = new CancellationTokenSource(TestTimeout);
            var endpoint = (IPEndPoint)bound.LocalAddress;
            using (var socket = new TcpClient())
            {
                await socket.ConnectAsync(endpoint.Address, endpoint.Port, cancellation.Token);
                using var ssl = new SslStream(socket.GetStream(), leaveInnerStreamOpen: true, (_, _, _, _) => true);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = "localhost",
                    EnabledSslProtocols = SslProtocols.Tls12
                }, cancellation.Token);
                (await ObserveTlsPeerCloseAsync(ssl, TimeSpan.FromSeconds(3))).Should().BeTrue();
            }

            validatorCalls.Should().Be(0);
            await listenerHandler.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

            using var validSocket = new TcpClient();
            await validSocket.ConnectAsync(endpoint.Address, endpoint.Port, cancellation.Token);
            using var validSsl = new SslStream(validSocket.GetStream(), leaveInnerStreamOpen: true, (_, _, _, _) => true);
            await validSsl.AuthenticateAsClientAsync(CreateClientOptions(clientCertificate), cancellation.Token);
            await listenerHandler.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
            var connection = listenerHandler.LastSender;
            validatorCalls.Should().Be(1);
            var handler = CreateTestProbe();
            listenerHandler.Send(connection, new Tcp.Register(handler.Ref));
            await validSsl.WriteAsync(Encoding.UTF8.GetBytes("valid client"), cancellation.Token);
            await ExpectPayloadAsync(handler, "valid client");
            handler.Send(connection, Tcp.Abort.Instance);
            listenerHandler.Send(listenerActor, Tcp.Unbind.Instance);
            await listenerHandler.ExpectMsgAsync<Tcp.Unbound>(TestTimeout);
        }

        [Fact(DisplayName = "Should_Apply_Chain_Suppression_And_Hostname_Validation_Independently_During_Real_Handshake")]
        public async Task Should_apply_chain_suppression_and_hostname_validation_independently_during_real_handshake()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            await VerifyServerPolicyAsync(serverCertificate, "localhost", suppressValidation: true, shouldConnect: true);
            await VerifyServerPolicyAsync(serverCertificate, "wrong.example", suppressValidation: true, shouldConnect: false);
            await VerifyServerPolicyAsync(serverCertificate, string.Empty, suppressValidation: true, shouldConnect: false);
            await VerifyServerPolicyAsync(serverCertificate, "localhost", suppressValidation: false, shouldConnect: false);
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
                Tls = new TlsClientSettings
                {
                    RequireMutualAuthentication = false,
                    TargetHost = "localhost",
                    CustomValidator = (_, _, _, _, _) =>
                    {
                        callbackCalls++;
                        if (throwFromValidator)
                            throw new InvalidOperationException("intentional validator failure");
                        return false;
                    }
                }
            };

            commander.Send(Sys.Tcp(), connect);
            var failed = await commander.ExpectMsgAsync<Tcp.CommandFailed>(TestTimeout);
            failed.Cause.HasValue.Should().BeTrue();
            callbackCalls.Should().Be(1);
            await peerTask;
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
                Tls = new TlsServerSettings(serverCertificate)
                {
                    RequireMutualAuthentication = true,
                    CustomValidator = (certificate, chain, remotePeer, _, log) =>
                    {
                        certificate.Should().NotBeNull();
                        chain.Should().NotBeNull();
                        remotePeer.Should().Contain(":");
                        log.Should().NotBeNull();
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
                    }
                }
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
                catch (AuthenticationException)
                {
                    authenticationFailed = true;
                }

                var peerClosed = authenticationFailed || await ObserveTlsPeerCloseAsync(rejectedTls, TimeSpan.FromSeconds(3));
                peerClosed.Should().BeTrue();
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
            listenerHandler.Send(listenerHandler.LastSender, new Tcp.Register(handler.Ref));
            await acceptedTls.WriteAsync(Encoding.UTF8.GetBytes("accepted"), cancellation.Token);
            await ExpectPayloadAsync(handler, "accepted");
            handler.Send(listenerHandler.LastSender, Tcp.Abort.Instance);
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
                Tls = new TlsClientSettings
                {
                    RequireMutualAuthentication = false,
                    HandshakeTimeout = TimeSpan.FromSeconds(2),
                    CustomValidator = (_, _, _, _, _) => true
                }
            };
            stalledCommander.Send(Sys.Tcp(), stalledConnect);
            await stalledAccepted.Task.WaitAsync(TimeSpan.FromSeconds(1));

            var authenticatedConnect = new Tcp.Connect(endpoint)
            {
                Tls = new TlsClientSettings
                {
                    RequireMutualAuthentication = false,
                    TargetHost = "localhost",
                    CustomValidator = (_, _, _, _, _) => true
                }
            };
            validCommander.Send(Sys.Tcp(), authenticatedConnect);
            await validCommander.ExpectMsgAsync<Tcp.Connected>(TimeSpan.FromSeconds(1));
            var connection = validCommander.LastSender;
            validCommander.Send(connection, Tcp.Abort.Instance);
            var failure = await stalledCommander.ExpectMsgAsync<Tcp.CommandFailed>(TestTimeout);
            failure.Cause.HasValue.Should().BeTrue();
            failure.Cause.Value.Should().BeOfType<TimeoutException>();
            await stalledCommander.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            releaseStalledPeer.TrySetResult();
            await serverTask.WaitAsync(TestTimeout);
        }

        [Fact(DisplayName = "Should_Close_The_Socket_When_The_Handshake_Commander_Stops")]
        public async Task Should_close_the_socket_when_the_handshake_commander_stops()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            using var cancellation = new CancellationTokenSource(TestTimeout);
            var accepted = new TaskCompletionSource<Socket>(TaskCreationOptions.RunContinuationsAsynchronously);
            var peerTask = Task.Run(async () =>
            {
                var socket = await listener.AcceptSocketAsync(cancellation.Token);
                accepted.TrySetResult(socket);
                using (socket)
                using (var stream = new NetworkStream(socket, ownsSocket: false))
                {
                    var buffer = new byte[1];
                    using var closeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        while (await stream.ReadAsync(buffer, closeDeadline.Token) != 0)
                        {
                        }

                        return true;
                    }
                    catch (IOException)
                    {
                        return true;
                    }
                }
            }, cancellation.Token);

            var commander = CreateTestProbe();
            commander.Send(Sys.Tcp(), new Tcp.Connect(endpoint)
            {
                Tls = new TlsClientSettings
                {
                    RequireMutualAuthentication = false,
                    HandshakeTimeout = TestTimeout
                }
            });
            await accepted.Task.WaitAsync(TestTimeout);
            Sys.Stop(commander.Ref);

            (await peerTask.WaitAsync(TestTimeout)).Should().BeTrue();
        }

        [Fact(DisplayName = "Should_Keep_TLS_Listener_Available_While_Another_Incoming_Handshake_Is_Stalled")]
        public async Task Should_keep_tls_listener_available_while_another_incoming_handshake_is_stalled()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            var listenerHandler = CreateTestProbe();
            listenerHandler.Send(Sys.Tcp(), new Tcp.Bind(listenerHandler.Ref, new IPEndPoint(IPAddress.Loopback, 0))
            {
                Tls = new TlsServerSettings(serverCertificate)
                {
                    RequireMutualAuthentication = false,
                    HandshakeTimeout = TimeSpan.FromSeconds(5),
                    CustomValidator = (_, _, _, _, _) => true
                }
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
            await ReadExactlyAsync(validTls, reply, cancellation.Token);
            Encoding.UTF8.GetString(reply).Should().Be("listener alive");

            handler.Send(connection, Tcp.Abort.Instance);
            (await stalledCloseTask.WaitAsync(TestTimeout)).Should().BeTrue();
            listenerHandler.Send(listenerActor, Tcp.Unbind.Instance);
            await listenerHandler.ExpectMsgAsync<Tcp.Unbound>(TestTimeout);
        }

        [Fact(DisplayName = "Should_Report_Invalid_TLS_Connect_Settings_As_Command_Failure")]
        public async Task Should_report_invalid_tls_connect_settings_as_command_failure()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            using var cancellation = new CancellationTokenSource(TestTimeout);
            var peerTask = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptSocketAsync(cancellation.Token);
                await accepted.DisconnectAsync(reuseSocket: false);
            }, cancellation.Token);
            var commander = CreateTestProbe();
            var connect = new Tcp.Connect(endpoint)
            {
                Tls = new TlsClientSettings
                {
                    RequireMutualAuthentication = false,
                    HandshakeTimeout = TimeSpan.Zero
                }
            };

            commander.Send(Sys.Tcp(), connect);

            var failure = await commander.ExpectMsgAsync<Tcp.CommandFailed>(TestTimeout);
            failure.Cmd.Should().BeSameAs(connect);
            failure.Cause.HasValue.Should().BeTrue();
            await peerTask.WaitAsync(TestTimeout);
        }

        [Fact(DisplayName = "Should_Report_Invalid_TLS_Bind_Settings_As_Command_Failure")]
        public async Task Should_report_invalid_tls_bind_settings_as_command_failure()
        {
            using var certificateWithKey = CreateCertificate("localhost", server: true);
            using var publicCertificate = X509CertificateLoader.LoadCertificate(certificateWithKey.RawData);
            var handler = CreateTestProbe();
            var bind = new Tcp.Bind(handler.Ref, new IPEndPoint(IPAddress.Loopback, 0))
            {
                Tls = new TlsServerSettings(publicCertificate)
            };

            handler.Send(Sys.Tcp(), bind);

            var failure = await handler.ExpectMsgAsync<Tcp.CommandFailed>(TestTimeout);
            failure.Cmd.Should().BeSameAs(bind);
            failure.Cause.HasValue.Should().BeTrue();
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
                Tls = new TlsClientSettings(clientCertificate)
                {
                    TargetHost = "localhost",
                    CustomValidator = (certificate, chain, remotePeer, errors, log) =>
                    {
                        callbackCalls++;
                        certificate.Should().NotBeNull();
                        chain.Should().NotBeNull();
                        remotePeer.Should().Contain(endpoint.Port.ToString());
                        log.Should().NotBeNull();
                        errors.Should().Be(SslPolicyErrors.RemoteCertificateChainErrors);
                        return true;
                    }
                }
            };
            commander.Send(Sys.Tcp(), connect);

            await commander.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
            var connection = commander.LastSender;
            callbackCalls.Should().Be(1);
            (await observedClientCertificate.Task.WaitAsync(TestTimeout)).Should().Be(clientCertificate.Thumbprint);
            commander.Send(connection, Tcp.Abort.Instance);
            await peerTask.WaitAsync(TestTimeout);
        }

        [Fact(DisplayName = "Should_Dispose_Authenticated_Stream_And_Queued_Write_When_Owner_Stops_Before_Register")]
        public async Task Should_dispose_authenticated_stream_and_queued_write_when_owner_stops_before_register()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var peerTask = AcceptAndDrainUntilCloseAsync(listener, serverCertificate);
            var commander = CreateTestProbe();
            var handler = CreateTestProbe();
            commander.Send(Sys.Tcp(), new Tcp.Connect(endpoint)
            {
                Tls = new TlsClientSettings
                {
                    RequireMutualAuthentication = false,
                    TargetHost = "localhost",
                    CustomValidator = (_, _, _, _, _) => true
                }
            });
            await commander.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
            var connection = commander.LastSender;
            var owner = new CountingMemoryOwner(Encoding.UTF8.GetBytes("must not be written"));
            var queuedWrite = Tcp.Write.Create(OwnedSequenceSegment.Create(owner, owner.Memory.Length));
            handler.Send(connection, queuedWrite);
            await WatchAsync(connection);
            handler.Send(connection, PoisonPill.Instance);

            await ExpectTerminatedAsync(connection, TestTimeout);
            var failedWrite = await handler.ExpectMsgAsync<Tcp.CommandFailed>(TestTimeout);
            failedWrite.Cmd.Should().BeSameAs(queuedWrite);
            owner.DisposeCount.Should().Be(1);
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
            await ReadExactlyAsync(ssl, request, cancellation.Token);
            Encoding.UTF8.GetString(request).Should().Be(expectedRequest);
            (await ssl.ReadAsync(new byte[1], cancellation.Token)).Should().Be(0);

            await ssl.WriteAsync(Encoding.UTF8.GetBytes(response), cancellation.Token);
            await ssl.FlushAsync(cancellation.Token);
            await ssl.ShutdownAsync();
            socket.Shutdown(SocketShutdown.Send);
        }

        private async Task VerifyServerPolicyAsync(
            X509Certificate2 certificate,
            string targetHost,
            bool suppressValidation,
            bool shouldConnect)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var peerTask = AcceptAndAuthenticateAsync(listener, certificate, SslProtocols.Tls12);
            var commander = CreateTestProbe();
            var command = new Tcp.Connect(endpoint)
            {
                Tls = new TlsClientSettings
                {
                    RequireMutualAuthentication = false,
                    SuppressValidation = suppressValidation,
                    ValidateCertificateHostname = true,
                    TargetHost = targetHost
                }
            };
            commander.Send(Sys.Tcp(), command);

            if (shouldConnect)
            {
                await commander.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
                commander.Send(commander.LastSender, Tcp.Abort.Instance);
            }
            else
            {
                var failure = await commander.ExpectMsgAsync<Tcp.CommandFailed>(TestTimeout);
                failure.Cause.HasValue.Should().BeTrue();
            }

            await peerTask.WaitAsync(TestTimeout);
        }

        private static async Task<bool> AcceptAndAuthenticateAsync(TcpListener listener, X509Certificate2 certificate, SslProtocols protocol)
        {
            using var cancellation = new CancellationTokenSource(TestTimeout);
            using var socket = await listener.AcceptSocketAsync(cancellation.Token);
            using var networkStream = new NetworkStream(socket, ownsSocket: false);
            using var ssl = new SslStream(networkStream, leaveInnerStreamOpen: true);
            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    EnabledSslProtocols = protocol
                }, cancellation.Token);
                return true;
            }
            catch (AuthenticationException)
            {
                // The outgoing Akka connection can reject the server during its certificate callback.
                return false;
            }
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

        private static async Task<bool> DrainTcpUntilClosedAsync(Stream stream, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            var buffer = new byte[256];
            try
            {
                while (await stream.ReadAsync(buffer, cancellation.Token) != 0)
                {
                }

                return true;
            }
            catch (IOException)
            {
                return true;
            }
        }

        private static async Task<bool> ObserveTlsPeerCloseAsync(SslStream stream, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            try
            {
                return await stream.ReadAsync(new byte[1], cancellation.Token) == 0;
            }
            catch (Exception exception) when (exception is IOException or AuthenticationException)
            {
                return true;
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

        private static async Task ReadExactlyAsync(Stream stream, Memory<byte> destination, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < destination.Length)
            {
                var count = await stream.ReadAsync(destination[offset..], cancellationToken);
                if (count == 0)
                    throw new EndOfStreamException("The TLS peer closed before the expected payload was read.");
                offset += count;
            }
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
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        }
    }
}
