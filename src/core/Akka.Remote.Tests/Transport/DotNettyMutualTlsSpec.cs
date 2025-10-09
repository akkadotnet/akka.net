//-----------------------------------------------------------------------
// <copyright file="DotNettyMutualTlsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Tests mutual TLS authentication enforcement in DotNetty transport.
    /// When require-mutual-authentication is enabled, both client and server must
    /// present valid certificates with accessible private keys.
    /// </summary>
    public class DotNettyMutualTlsSpec : AkkaSpec
    {
        private const string ValidCertPath = "Resources/akka-validcert.pfx";
        private const string ClientCertPath = "Resources/akka-client-cert.pfx";
        private const string Password = "password";

        public DotNettyMutualTlsSpec(ITestOutputHelper output) : base(ConfigurationFactory.Empty, output)
        {
        }

        private static Config CreateConfig(bool enableSsl, bool requireMutualAuth, bool suppressValidation = false, string certPath = null, bool? validateCertificateHostname = null)
        {
            var config = ConfigurationFactory.ParseString($@"
                akka {{
                    loglevel = DEBUG
                    actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                    remote.dot-netty.tcp {{
                        port = 0
                        hostname = ""127.0.0.1""
                        enable-ssl = {(enableSsl ? "on" : "off")}
                        log-transport = off
                    }}
                }}
            ");

            if (!enableSsl)
                return config;

            var escapedPath = (certPath ?? ValidCertPath).Replace("\\", "\\\\");
            var hostnameValidationConfig = validateCertificateHostname.HasValue
                ? $"validate-certificate-hostname = {(validateCertificateHostname.Value ? "on" : "off")}"
                : "";

            var ssl = $@"
                akka.remote.dot-netty.tcp.ssl {{
                    suppress-validation = {(suppressValidation ? "on" : "off")}
                    require-mutual-authentication = {(requireMutualAuth ? "on" : "off")}
                    {hostnameValidationConfig}
                    certificate {{
                        path = ""{escapedPath}""
                        password = ""{Password}""
                    }}
                }}
            ";
            return ConfigurationFactory.ParseString(ssl).WithFallback(config);
        }

        [Fact]
        public async Task Mutual_TLS_should_allow_connection_when_both_nodes_have_valid_certificates()
        {
            // Both server and client have valid certs, mutual TLS enabled
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                var serverConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                var clientConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");

                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Should successfully connect and communicate
                var response = await client.ActorSelection(serverEchoPath).Ask<string>("hello", TimeSpan.FromSeconds(5));
                Assert.Equal("hello", response);
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
        public async Task Mutual_TLS_disabled_should_allow_standard_TLS_connection()
        {
            // Server has mutual TLS disabled (standard server-only TLS)
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                var serverConfig = CreateConfig(enableSsl: true, requireMutualAuth: false, suppressValidation: true);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                var clientConfig = CreateConfig(enableSsl: true, requireMutualAuth: false, suppressValidation: true);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");

                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Should successfully connect with standard TLS
                var response = await client.ActorSelection(serverEchoPath).Ask<string>("hello", TimeSpan.FromSeconds(5));
                Assert.Equal("hello", response);
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
        public void System_should_start_successfully_with_mutual_TLS_enabled()
        {
            // Verify that enabling mutual TLS doesn't break system startup
            ActorSystem sys = null;

            try
            {
                var config = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true);
                sys = ActorSystem.Create("TestSystem", config);
                InitializeLogger(sys);

                // System should be running
                Assert.False(sys.WhenTerminated.IsCompleted);

                // Remote should be initialized
                var remoteAddress = RARP.For(sys).Provider.DefaultAddress;
                Assert.NotNull(remoteAddress);
            }
            finally
            {
                if (sys != null)
                    Shutdown(sys, TimeSpan.FromSeconds(10));
            }
        }

        [Fact]
        public async Task Mutual_TLS_should_fail_when_client_has_no_certificate()
        {
            // Server requires mutual TLS, client has SSL enabled but no certificate configured
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                // Server with mutual TLS required
                var serverConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");
                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Client with SSL enabled but mutual TLS disabled (won't send client certificate)
                var clientConfig = CreateConfig(enableSsl: true, requireMutualAuth: false, suppressValidation: true);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                // Should fail to connect because server requires client certificate
                // Enhanced error message "no client certificate provided" will be logged to server logs
                await Assert.ThrowsAsync<AskTimeoutException>(async () =>
                {
                    await client.ActorSelection(serverEchoPath).Ask<string>("hello", TimeSpan.FromSeconds(3));
                });
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
        public async Task Mutual_TLS_can_be_disabled_for_backward_compatibility()
        {
            // Test that setting require-mutual-authentication = false allows old behavior
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                // Server with mutual TLS explicitly disabled
                var serverConfig = CreateConfig(enableSsl: true, requireMutualAuth: false, suppressValidation: true);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                // Client with SSL but potentially no valid client cert
                var clientConfig = CreateConfig(enableSsl: true, requireMutualAuth: false, suppressValidation: true);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");
                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Should successfully connect even with mutual TLS disabled
                var response = await client.ActorSelection(serverEchoPath).Ask<string>("hello", TimeSpan.FromSeconds(5));
                Assert.Equal("hello", response);
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
        public async Task Mutual_TLS_should_fail_when_client_has_different_valid_certificate()
        {
            // Server and client have different valid certificates - mutual TLS should fail
            // because the certificates are not trusted by each other
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                // Server with mutual TLS using the original certificate
                var serverConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: false,
                    certPath: ValidCertPath);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");
                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Client with mutual TLS using a different certificate
                var clientConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: false,
                    certPath: ClientCertPath);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                // Connection should fail due to certificate mismatch
                // Enhanced error message with certificate validation details will be logged to server logs
                await Assert.ThrowsAsync<AskTimeoutException>(async () =>
                {
                    await client.ActorSelection(serverEchoPath).Ask<string>("hello", TimeSpan.FromSeconds(3));
                });
            }
            finally
            {
                if (client != null)
                    Shutdown(client, TimeSpan.FromSeconds(10));
                if (server != null)
                    Shutdown(server, TimeSpan.FromSeconds(10));
            }
        }

        [Fact(DisplayName = "Different certificates with hostname validation disabled should connect successfully")]
        public async Task Hostname_validation_disabled_should_allow_different_certificates()
        {
            // Per-node certificates should work when hostname validation is disabled
            // Note: Using suppressValidation=true to bypass chain validation since test certs are self-signed
            // This isolates the hostname validation logic we're testing
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                // Server with one certificate, hostname validation disabled
                var serverConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true,
                    certPath: ValidCertPath, validateCertificateHostname: false);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");
                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Client with different certificate, hostname validation disabled
                var clientConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true,
                    certPath: ClientCertPath, validateCertificateHostname: false);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                // Should successfully connect because hostname validation is disabled
                var response = await client.ActorSelection(serverEchoPath).Ask<string>("hello", TimeSpan.FromSeconds(5));
                Assert.Equal("hello", response);
            }
            finally
            {
                if (client != null)
                    Shutdown(client, TimeSpan.FromSeconds(10));
                if (server != null)
                    Shutdown(server, TimeSpan.FromSeconds(10));
            }
        }

        [Fact(DisplayName = "Different certificates with hostname validation enabled should fail with name mismatch")]
        public async Task Hostname_validation_enabled_should_reject_different_certificates()
        {
            // When hostname validation is enabled, different certificates should fail with RemoteCertificateNameMismatch
            // Note: Using suppressValidation=true to bypass chain validation and test hostname validation specifically
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                // Server with one certificate, hostname validation enabled
                var serverConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true,
                    certPath: ValidCertPath, validateCertificateHostname: true);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");
                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Client with different certificate, hostname validation enabled
                var clientConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true,
                    certPath: ClientCertPath, validateCertificateHostname: true);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                // Should fail because hostname in certificate doesn't match connection target (127.0.0.1)
                await Assert.ThrowsAsync<AskTimeoutException>(async () =>
                {
                    await client.ActorSelection(serverEchoPath).Ask<string>("hello", TimeSpan.FromSeconds(3));
                });
            }
            finally
            {
                if (client != null)
                    Shutdown(client, TimeSpan.FromSeconds(10));
                if (server != null)
                    Shutdown(server, TimeSpan.FromSeconds(10));
            }
        }

        [Fact(DisplayName = "Same certificate should connect successfully (typical mutual TLS scenario)")]
        public async Task Same_certificate_should_connect_in_mutual_tls()
        {
            // Typical mutual TLS: Both nodes use the same shared certificate
            // Hostname validation disabled because we're using IPs/per-node certs
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                // Server with same certificate, hostname validation disabled (typical for mutual TLS)
                var serverConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true,
                    certPath: ValidCertPath, validateCertificateHostname: false);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");
                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Client with same certificate, hostname validation disabled
                var clientConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true,
                    certPath: ValidCertPath, validateCertificateHostname: false);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                // Should successfully connect - typical mutual TLS scenario
                var response = await client.ActorSelection(serverEchoPath).Ask<string>("hello", TimeSpan.FromSeconds(5));
                Assert.Equal("hello", response);
            }
            finally
            {
                if (client != null)
                    Shutdown(client, TimeSpan.FromSeconds(10));
                if (server != null)
                    Shutdown(server, TimeSpan.FromSeconds(10));
            }
        }

        [Fact(DisplayName = "Hostname validation unspecified should default to disabled (backward compatibility)")]
        public async Task Hostname_validation_default_should_be_disabled()
        {
            // When validate-certificate-hostname is not specified, it should default to false
            // Note: Using suppressValidation=true to bypass chain validation and test hostname default behavior
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                // Server without specifying hostname validation (should default to false)
                var serverConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true,
                    certPath: ValidCertPath, validateCertificateHostname: null);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");
                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Client with different certificate, hostname validation unspecified (should default to false)
                var clientConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true,
                    certPath: ClientCertPath, validateCertificateHostname: null);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                // Should successfully connect because hostname validation defaults to disabled
                var response = await client.ActorSelection(serverEchoPath).Ask<string>("hello", TimeSpan.FromSeconds(5));
                Assert.Equal("hello", response);
            }
            finally
            {
                if (client != null)
                    Shutdown(client, TimeSpan.FromSeconds(10));
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
