//-----------------------------------------------------------------------
// <copyright file="DotNettyExpectedHostnameSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Runs <c>CertificateValidation.ValidateHostname(expectedHostname)</c> through real TLS handshakes,
    /// on the accepting side and on the dialing side.
    /// </summary>
    public class DotNettyExpectedHostnameSpec : AkkaSpec
    {
        // subject CN=Akka.NET team, no subject alternative names
        private const string TeamCertPath = "Resources/akka-validcert.pfx";

        // subject CN=AkkaTestClient, no subject alternative names
        private const string NamedCertPath = "Resources/akka-client-cert.pfx";
        private const string NameInNamedCert = "AkkaTestClient";

        private const string Password = "password";

        private static readonly Config RemoteConfig = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                remote.dot-netty.tcp {
                    port = 0
                    hostname = ""127.0.0.1""
                    enable-ssl = on
                    log-transport = off
                }
            }");

        private static readonly CertificateValidationCallback AcceptAnything = (_, _, _, _, _) => true;

        public DotNettyExpectedHostnameSpec(ITestOutputHelper output) : base(ConfigurationFactory.Empty, output)
        {
        }

        private ActorSystem StartSystem(string name, string certificatePath, CertificateValidationCallback validator)
        {
            var certificate = CertificateHelper.LoadPkcs12(certificatePath, Password);
            var setup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create().WithConfig(RemoteConfig))
                .And(new DotNettySslSetup(certificate, suppressValidation: false, requireMutualAuthentication: true,
                    customValidator: validator));

            var system = ActorSystem.Create(name, setup);
            InitializeLogger(system, $"[{name}] ");
            return system;
        }

        /// <summary>
        /// Wraps a validator so a spec can see what it decided.
        /// </summary>
        private static CertificateValidationCallback Recording(CertificateValidationCallback validator,
            ConcurrentQueue<bool> verdicts)
        {
            return (certificate, chain, peer, errors, log) =>
            {
                var verdict = validator(certificate, chain, peer, errors, log);
                verdicts.Enqueue(verdict);
                return verdict;
            };
        }

        private static ActorPath StartEcho(ActorSystem system)
        {
            system.ActorOf(Props.Create(() => new EchoActor()), "echo");
            return new RootActorPath(RARP.For(system).Provider.DefaultAddress) / "user" / "echo";
        }

        [Fact(DisplayName = "Should_AcceptInboundConnection_When_ClientCertificateMatchesExpectedHostname")]
        public async Task Should_AcceptInboundConnection_When_ClientCertificateMatchesExpectedHostname()
        {
            ActorSystem? server = null;
            ActorSystem? client = null;
            var verdicts = new ConcurrentQueue<bool>();

            try
            {
                server = StartSystem("Server", TeamCertPath,
                    Recording(CertificateValidation.ValidateHostname(NameInNamedCert), verdicts));
                client = StartSystem("Client", NamedCertPath, AcceptAnything);
                var echoPath = StartEcho(server);

                var response = await client.ActorSelection(echoPath).Ask<string>("hello", TimeSpan.FromSeconds(10));

                Assert.Equal("hello", response);
                Assert.Contains(true, verdicts);
                Assert.DoesNotContain(false, verdicts);
            }
            finally
            {
                if (client != null)
                    await ShutdownAsync(client, TimeSpan.FromSeconds(10));
                if (server != null)
                    await ShutdownAsync(server, TimeSpan.FromSeconds(10));
            }
        }

        [Fact(DisplayName = "Should_RejectInboundConnection_When_ClientCertificateDoesNotMatchExpectedHostname")]
        public async Task Should_RejectInboundConnection_When_ClientCertificateDoesNotMatchExpectedHostname()
        {
            // The security hole this spec guards against: SslStream never reports a name mismatch for a
            // client certificate, so up to v1.5.74 the server let this client in.
            ActorSystem? server = null;
            ActorSystem? client = null;
            var verdicts = new ConcurrentQueue<bool>();

            try
            {
                server = StartSystem("Server", TeamCertPath,
                    Recording(CertificateValidation.ValidateHostname("some-other-node.internal"), verdicts));
                client = StartSystem("Client", NamedCertPath, AcceptAnything);
                var echoPath = StartEcho(server);

                await Assert.ThrowsAsync<AskTimeoutException>(async () =>
                {
                    await client.ActorSelection(echoPath).Ask<string>("hello", TimeSpan.FromSeconds(3));
                });

                Assert.Contains(false, verdicts);
                Assert.DoesNotContain(true, verdicts);

                // Rejecting an inbound peer must not take the accepting node down.
                Assert.False(server.WhenTerminated.IsCompleted);
            }
            finally
            {
                if (client != null)
                    await ShutdownAsync(client, TimeSpan.FromSeconds(10));
                if (server != null)
                    await ShutdownAsync(server, TimeSpan.FromSeconds(10));
            }
        }

        [Fact(DisplayName = "Should_AcceptOutboundConnection_When_ServerCertificateMatchesExpectedHostname")]
        public async Task Should_AcceptOutboundConnection_When_ServerCertificateMatchesExpectedHostname()
        {
            // The client dials 127.0.0.1, which the server certificate does not name. The explicit expected
            // hostname decides, so the connection must go through and the client must stay up.
            ActorSystem? server = null;
            ActorSystem? client = null;
            var verdicts = new ConcurrentQueue<bool>();

            try
            {
                server = StartSystem("Server", NamedCertPath, AcceptAnything);
                client = StartSystem("Client", TeamCertPath,
                    Recording(CertificateValidation.ValidateHostname(NameInNamedCert), verdicts));
                var echoPath = StartEcho(server);

                var response = await client.ActorSelection(echoPath).Ask<string>("hello", TimeSpan.FromSeconds(10));

                Assert.Equal("hello", response);
                Assert.Contains(true, verdicts);
                Assert.DoesNotContain(false, verdicts);
                Assert.False(client.WhenTerminated.IsCompleted);
            }
            finally
            {
                if (client != null)
                    await ShutdownAsync(client, TimeSpan.FromSeconds(10));
                if (server != null)
                    await ShutdownAsync(server, TimeSpan.FromSeconds(10));
            }
        }

        [Fact(DisplayName = "Should_ShutDownDialingNode_When_ServerCertificateDoesNotMatchExpectedHostname")]
        public async Task Should_ShutDownDialingNode_When_ServerCertificateDoesNotMatchExpectedHostname()
        {
            // A failed outbound TLS handshake shuts the dialing ActorSystem down (exit code 79).
            // This is why the matcher must never reject a certificate that does match.
            ActorSystem? server = null;
            ActorSystem? client = null;
            var verdicts = new ConcurrentQueue<bool>();

            try
            {
                server = StartSystem("Server", NamedCertPath, AcceptAnything);
                client = StartSystem("Client", TeamCertPath,
                    Recording(CertificateValidation.ValidateHostname("some-other-node.internal"), verdicts));
                var echoPath = StartEcho(server);

                client.ActorSelection(echoPath).Tell("hello");

                await AwaitAssertAsync(() => Assert.True(client.WhenTerminated.IsCompleted),
                    TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200));

                Assert.Contains(false, verdicts);
                Assert.DoesNotContain(true, verdicts);
                Assert.False(server.WhenTerminated.IsCompleted);
            }
            finally
            {
                if (client != null)
                    await ShutdownAsync(client, TimeSpan.FromSeconds(10));
                if (server != null)
                    await ShutdownAsync(server, TimeSpan.FromSeconds(10));
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
