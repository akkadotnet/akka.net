//-----------------------------------------------------------------------
// <copyright file="DotNettyTlsHandshakeFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Event;
using Xunit;
using Xunit.Abstractions;

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

        private static Config CreateConfig(bool enableSsl, string certPath, string certPassword, bool suppressValidation = true, bool requireClientCert = false, bool sendClientCert = true)
        {
            var baseConfig = ConfigurationFactory.ParseString(@"akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                remote.retry-gate-closed-for = 3s
                remote.dot-netty.tcp {
                    port = 0
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
                require-client-certificate = {(requireClientCert ? "on" : "off")}
                send-client-certificate = {(sendClientCert ? "on" : "off")}
                certificate {{
                    path = ""{escapedPath}""
                    password = ""{certPassword ?? string.Empty}""
                }}
            }}";
            return baseConfig.WithFallback(ssl);
        }

        private static void CreateCertificateWithoutPrivateKey()
        {
            var fullCert = new X509Certificate2(ValidCertPath, Password, X509KeyStorageFlags.Exportable);
            var publicKeyBytes = fullCert.Export(X509ContentType.Cert);
            var dir = Path.GetDirectoryName(NoKeyCertPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(NoKeyCertPath, publicKeyBytes);
        }

        [Fact]
        public void Server_should_fail_fast_when_server_certificate_has_no_private_key()
        {
            CreateCertificateWithoutPrivateKey();

            try
            {
                var baseCfg = CreateConfig(true, NoKeyCertPath, null, suppressValidation: true);
                var failfast = ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.ssl.fail-fast-invalid-server-certificate = on");
                var serverConfig = baseCfg.WithFallback(failfast);

                Assert.ThrowsAny<Exception>(() =>
                {
                    using var _ = ActorSystem.Create("ServerSystem", serverConfig);
                });
            }
            finally
            {
                try
                {
                    if (File.Exists(NoKeyCertPath)) File.Delete(NoKeyCertPath);
                }
                catch { /* ignore */ }
            }
        }

        [Fact]
        public async Task Tls_handshake_failure_should_be_logged_and_shutdown_server()
        {
            CreateCertificateWithoutPrivateKey();

            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                // Start TLS server with a cert that has no private key
                var serverConfig = CreateConfig(true, NoKeyCertPath, null, suppressValidation: true);

                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                // Server started - add an echo actor and subscribe to errors
                server.ActorOf(Props.Create(() => new EchoActor()), "echo");

                var errorProbe = CreateTestProbe(server);
                server.EventStream.Subscribe(errorProbe.Ref, typeof(Event.Error));

                // Start client with valid TLS cert
                var clientConfig = CreateConfig(true, ValidCertPath, Password, suppressValidation: true);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                var serverAddress = RARP.For(server).Provider.DefaultAddress;
                var echoPath = new RootActorPath(serverAddress) / "user" / "echo";
                var echoSel = client.ActorSelection(echoPath);

                // Trigger association attempt
                var probe = CreateTestProbe(client);
                echoSel.Tell("ping", probe.Ref);

                // Expect server to log TLS handshake failure promptly
                var err = errorProbe.ExpectMsg<Event.Error>(TimeSpan.FromSeconds(10));
                var msg = err.ToString();
                Assert.Contains("TLS handshake failed", msg, StringComparison.OrdinalIgnoreCase);

                // Server should shutdown due to TLS failure
                await AwaitAssertAsync(async () =>
                {
                    Assert.True(server.WhenTerminated.IsCompleted);
                    await Task.CompletedTask;
                }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
            }
            finally
            {
                if (client != null) 
                    Shutdown(client, TimeSpan.FromSeconds(10));
                if (server != null) 
                    Shutdown(server, TimeSpan.FromSeconds(10));
                try
                {
                    if (File.Exists(NoKeyCertPath)) 
                        File.Delete(NoKeyCertPath);
                } catch { /* ignore */ }
            }
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Server_side_tls_handshake_failure_should_shutdown_server()
        {
            CreateCertificateWithoutPrivateKey();

            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                // Server with invalid server cert (no private key) -> server TLS handshake fails
                var serverConfig = CreateConfig(true, NoKeyCertPath, null, suppressValidation: true);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                // Client with valid cert
                var clientConfig = CreateConfig(true, ValidCertPath, Password, suppressValidation: true);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                // Echo actor on server and client
                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");
                var clientEcho = client.ActorOf(Props.Create(() => new EchoActor()), "echo");

                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var clientAddr = RARP.For(client).Provider.DefaultAddress;

                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";
                var clientEchoPath = new RootActorPath(clientAddr) / "user" / "echo";

                // Subscribe to server errors to ensure TLS handshake failure is observed
                var serverErrorProbe = CreateTestProbe(server);
                server.EventStream.Subscribe(serverErrorProbe.Ref, typeof(Event.Error));

                // Trigger inbound handshake failure on server: client tries to talk to server
                var clientProbe = CreateTestProbe(client);
                client.ActorSelection(serverEchoPath).Tell("ping", clientProbe.Ref);

                // Expect server to log TLS handshake failure promptly
                var err = await serverErrorProbe.ExpectMsgAsync<Event.Error>(TimeSpan.FromSeconds(10));
                Assert.Contains("TLS handshake failed", err.ToString(), StringComparison.OrdinalIgnoreCase);

                // Server should shutdown due to TLS failure
                await AwaitAssertAsync(async () =>
                {
                    Assert.True(server.WhenTerminated.IsCompleted);
                    await Task.CompletedTask;
                }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
            }
            finally
            {
                if (client != null)
                    Shutdown(client, TimeSpan.FromSeconds(10));
                if (server != null)
                    Shutdown(server, TimeSpan.FromSeconds(10));
                try
                {
                    if (File.Exists(NoKeyCertPath))
                        File.Delete(NoKeyCertPath);
                }
                catch { /* ignore */ }
            }
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

        [Fact]
        public async Task MutualTLS_should_succeed_when_client_certificate_is_required_and_provided()
        {
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                // Server requires client certificate but suppresses validation (self-signed ok)
                var serverConfig = CreateConfig(true, ValidCertPath, Password, suppressValidation: true, requireClientCert: true);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                // Client sends client certificate
                var clientConfig = CreateConfig(true, ValidCertPath, Password, suppressValidation: true, requireClientCert: false, sendClientCert: true);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                var echo = server.ActorOf(Props.Create(() => new EchoActor()), "echo");
                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var echoPath = new RootActorPath(serverAddr) / "user" / "echo";

                var probe = CreateTestProbe(client);
                await AwaitAssertAsync(async () =>
                {
                    client.ActorSelection(echoPath).Tell("mtls-ok", probe.Ref);
                    await probe.ExpectMsgAsync("mtls-ok", TimeSpan.FromSeconds(1));
                }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200));
            }
            finally
            {
                if (client != null) Shutdown(client, TimeSpan.FromSeconds(10));
                if (server != null) Shutdown(server, TimeSpan.FromSeconds(10));
            }
        }

        [Fact]
        public async Task MutualTLS_should_shutdown_when_client_certificate_is_required_but_not_provided()
        {
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                // Server requires client certificate, suppress validation for self-signed
                var serverConfig = CreateConfig(true, ValidCertPath, Password, suppressValidation: false, requireClientCert: true);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                // Client does NOT send client certificate
                var clientConfig = CreateConfig(true, ValidCertPath, Password, suppressValidation: true, requireClientCert: false, sendClientCert: false);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                // Create echo on server
                var echo = server.ActorOf(Props.Create(() => new EchoActor()), "echo");

                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var echoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Attempt communication; server should shutdown due to TLS failure (client cert required but not provided)
                client.ActorSelection(echoPath).Tell("should-fail");

                await AwaitAssertAsync(async () =>
                {
                    Assert.True(server.WhenTerminated.IsCompleted);
                    await Task.CompletedTask;
                }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200));
            }
            finally
            {
                if (client != null) Shutdown(client, TimeSpan.FromSeconds(10));
                if (server != null) Shutdown(server, TimeSpan.FromSeconds(10));
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
