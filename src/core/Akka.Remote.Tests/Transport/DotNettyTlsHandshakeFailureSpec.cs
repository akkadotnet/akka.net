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
        public async Task Tls_handshake_failure_should_be_logged_and_detected()
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

        private sealed class EchoActor : ReceiveActor
        {
            public EchoActor()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }
    }
}
