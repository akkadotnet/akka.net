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

        private static Config CreateConfig(bool enableSsl, string certPath, string certPassword, bool suppressValidation = true)
        {
            var baseConfig = ConfigurationFactory.ParseString(@"akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
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



        private sealed class EchoActor : ReceiveActor
        {
            public EchoActor()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }
    }
}
