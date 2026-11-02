//-----------------------------------------------------------------------
// <copyright file="DotNettyTlsHandshakeFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Event;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    public class DotNettyTlsHandshakeFailureSpec : AkkaSpec
    {
        private const string ValidCertPath = "Resources/akka-validcert.pfx";
        private const string Password = "password";
        private static readonly string NoKeyCertPath = Path.Combine("Resources", "handshake-no-key.cer");

        public DotNettyTlsHandshakeFailureSpec(ITestOutputHelper output) : base(ConfigurationFactory.Empty, output)
        {
        }

        private static Config CreateConfig(bool enableSsl, string certPath, string certPassword, bool suppressValidation = true, int port = 0)
        {
            var baseConfig = ConfigurationFactory.ParseString(@"akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                remote.dot-netty.tcp {
                    port = " + port + @"
                    hostname = ""127.0.0.1""
                    enable-ssl = " + (enableSsl ? "on" : "off") + @"
                    log-transport = off
                }
            }");

            if (!enableSsl || string.IsNullOrEmpty(certPath))
                return baseConfig;

            var escapedPath = certPath.Replace("\\", "\\\\");
            var ssl = $@"akka.remote.dot-netty.tcp.ssl {{
                suppress-validation = {(suppressValidation ? "on" : "off")}
                certificate {{
                    path = ""{escapedPath}""
                    password = ""{certPassword ?? string.Empty}""
                }}
            }}";
            return baseConfig.WithFallback(ssl);
        }

        private static void CreateCertificateWithoutPrivateKey()
        {
            var fullCert = CertificateHelper.LoadPkcs12(ValidCertPath, Password, X509KeyStorageFlags.Exportable);
            var publicKeyBytes = fullCert.Export(X509ContentType.Cert);
            var dir = Path.GetDirectoryName(NoKeyCertPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(NoKeyCertPath, publicKeyBytes);
        }



        [Fact]
        public async Task Server_should_fail_at_startup_with_certificate_without_private_key()
        {
            CreateCertificateWithoutPrivateKey();

            try
            {
                // Server with cert that has no private key should FAIL TO START
                var serverConfig = CreateConfig(true, NoKeyCertPath, null, suppressValidation: true);

                // ActorSystem.Create should throw during startup due to certificate validation
                var aggregateEx = Assert.Throws<AggregateException>(() =>
                {
                    using var server = ActorSystem.Create("ServerSystem", serverConfig);
                });

                // Unwrap to find the ConfigurationException
                var innerEx = aggregateEx.InnerException ?? aggregateEx;
                while (innerEx is AggregateException agg && agg.InnerException != null)
                    innerEx = agg.InnerException;

                // Should be ConfigurationException about private key
                Assert.IsType<ConfigurationException>(innerEx);
                Assert.Contains("private key", innerEx.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try
                {
                    if (File.Exists(NoKeyCertPath))
                        File.Delete(NoKeyCertPath);
                }
                catch { /* ignore */ }
            }
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Client_side_tls_handshake_failure_should_shutdown_client()
        {
            // Server has valid cert; client enforces validation so it should reject the self-signed server cert
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                var serverConfig = CreateConfig(true, ValidCertPath, Password, suppressValidation: true);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                var clientConfig = CreateConfig(true, ValidCertPath, Password, suppressValidation: false);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");

                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Trigger TLS handshake failure during association
                // The enhanced error message will be logged, but we can't easily assert on it
                // in a multi-system test without using the TestKit's Sys
                client.ActorSelection(serverEchoPath).Tell("hello");

                // Client should shutdown due to TLS failure
                await AwaitAssertAsync(async () =>
                {
                    Assert.True(client.WhenTerminated.IsCompleted);
                    await Task.CompletedTask;
                }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200));
            }
            finally
            {
                if (client != null)
                    Shutdown(client, TimeSpan.FromSeconds(10));
                if (server != null)
                    Shutdown(server, TimeSpan.FromSeconds(10));
            }
        }

        [Fact(DisplayName = "Server should NOT shutdown when invalid traffic (like HTTP) hits TLS port")]
        public async Task Server_side_invalid_traffic_should_not_shutdown_server()
        {
            // This test addresses issue https://github.com/akkadotnet/akka.net/issues/7938
            // When invalid traffic (like HTTP requests) hits a TLS-enabled port,
            // the server should reject the connection but NOT shut down
            ActorSystem server = null;

            try
            {
                // Start server with TLS enabled on a specific port
                var port = 15557; // Use a fixed port for this test
                var serverConfig = CreateConfig(true, ValidCertPath, Password, suppressValidation: true, port: port);
                server = ActorSystem.Create("ServerSystem", serverConfig);

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");

                // Ensure the server is ready by waiting for the remote transport to be bound
                var serverAddress = RARP.For(server).Provider.DefaultAddress;
                Assert.NotNull(serverAddress);
                Assert.Equal(port, serverAddress.Port.Value);

                // Send invalid HTTP traffic to the TLS port (simulating the issue)
                try
                {
                    using var tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync("127.0.0.1", port);

                    // Send an HTTP OPTIONS request (as described in the bug report)
                    var httpRequest = Encoding.UTF8.GetBytes("OPTIONS / HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
                    await tcpClient.GetStream().WriteAsync(httpRequest, 0, httpRequest.Length);
                    await tcpClient.GetStream().FlushAsync();

                    // Connection should be closed by server after rejecting invalid TLS
                    tcpClient.Close();
                }
                catch
                {
                    // Connection might be closed by server, that's expected
                }

                // Verify the server hasn't initiated shutdown
                // If it was going to shut down due to TLS failure, it would have done so immediately
                await AwaitConditionAsync(() => !server.WhenTerminated.IsCompleted,
                    TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(100));

                // CRITICAL ASSERTION: Server should NOT have shut down
                Assert.False(server.WhenTerminated.IsCompleted,
                    "Server should NOT shut down after receiving invalid HTTP traffic on TLS port");

                // Also verify the system is still functional
                var testActor = server.ActorOf(Props.Empty, "test-actor");
                Assert.NotNull(testActor);
            }
            finally
            {
                if (server != null)
                    Shutdown(server, TimeSpan.FromSeconds(10));
            }
        }

        private sealed class EchoActor : ReceiveActor
        {
            public EchoActor()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }
    }
}
