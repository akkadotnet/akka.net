// -----------------------------------------------------------------------
// <copyright file="ArteryTlsTransportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using System.Net.Quic;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public class ArteryTlsTransportSpec : AkkaSpec
    {
        public ArteryTlsTransportSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig(int port = 0, string transport = "tcp") => ConfigurationFactory.ParseString($$"""
            akka.loglevel = DEBUG
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.transport = {{transport}}
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = {{port}}
            akka.remote.artery.large-message-destinations = ["/user/large"]
            akka.remote.artery.advanced.outbound-lanes = 2
            akka.remote.artery.advanced.maximum-frame-size = 64k
            akka.remote.artery.advanced.maximum-large-frame-size = 256k
            """);

        internal static TlsPeerPolicy TrustCertificate(X509Certificate2 certificate, Action? onValidation = null) =>
            TlsPeerPolicy.CustomTrust((peerCertificate, _, _, _, _) =>
            {
                onValidation?.Invoke();
                return string.Equals(peerCertificate?.Thumbprint, certificate.Thumbprint,
                    StringComparison.OrdinalIgnoreCase);
            }).And(TlsCertificateValidation.ValidateHostname());

        private static ActorSystemSetup MutualTlsSetup(X509Certificate2 certificate,
            TlsPeerPolicy inboundClientPolicy, TlsPeerPolicy outboundServerPolicy, int port = 0, string transport = "tcp") =>
            ActorSystemSetup.Create(
                BootstrapSetup.Create().WithConfig(ArteryConfig(port, transport)),
                new ArteryTlsSetup(ArteryTlsSettings.Mutual(certificate, inboundClientPolicy, outboundServerPolicy)));

        private static string SelectionPath(ActorSystem system, string actorName) =>
            $"akka://{system.Name}@127.0.0.1:{RARP.For(system).Provider.DefaultAddress.Port!.Value}/user/{actorName}";

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(message => Sender.Tell(message));
            }
        }

        private sealed class LargeReceiver : ReceiveActor
        {
            public LargeReceiver()
            {
                Receive<string>(message => Sender.Tell(message.Length));
            }
        }

        [Theory(DisplayName = "Mutual TLS protects Artery ordinary, large, and reliable control traffic with multiple lanes")]
        [InlineData("tcp")]
        [InlineData("quic")]
        public async Task Should_UseTlsForEveryArteryChannel(string byteTransport)
        {
            RequireTransportSupport(byteTransport);
            using var certificate = CreateCertificate();
            var inboundValidationsA = 0;
            var outboundValidationsA = 0;
            var inboundValidationsB = 0;
            var outboundValidationsB = 0;
            var inboundPolicyA = TrustCertificate(certificate, () => Interlocked.Increment(ref inboundValidationsA));
            var outboundPolicyA = TrustCertificate(certificate, () => Interlocked.Increment(ref outboundValidationsA));
            var inboundPolicyB = TrustCertificate(certificate, () => Interlocked.Increment(ref inboundValidationsB));
            var outboundPolicyB = TrustCertificate(certificate, () => Interlocked.Increment(ref outboundValidationsB));
            ActorSystem? systemA = null;
            ActorSystem? systemB = null;
            try
            {
                systemA = ActorSystem.Create("ArteryTlsA", MutualTlsSetup(certificate, inboundPolicyA, outboundPolicyA, transport: byteTransport));
                InitializeLogger(systemA, "[A] ");
                systemB = ActorSystem.Create("ArteryTlsB", MutualTlsSetup(certificate, inboundPolicyB, outboundPolicyB, transport: byteTransport));
                InitializeLogger(systemB, "[B] ");
                var ordinaryOnB = systemB.ActorOf(Props.Create(() => new Echo()), "ordinary");
                var largeOnB = systemB.ActorOf(Props.Create(() => new LargeReceiver()), "large");

                var ordinaryRefs = new System.Collections.Generic.List<IActorRef>();
                ordinaryRefs.Add(await systemA.ActorSelection(SelectionPath(systemB, "ordinary"))
                    .ResolveOne(TimeSpan.FromSeconds(10)));
                var coveredLanes = new System.Collections.Generic.HashSet<int>
                {
                    Association.SelectLane(ordinaryRefs[0].Path.Uid, lanes: 2)
                };
                for (var i = 1; i <= 64 && coveredLanes.Count < 2; i++)
                {
                    var actorName = $"ordinary-{i}";
                    systemB.ActorOf(Props.Create(() => new Echo()), actorName);
                    var actorRef = await systemA.ActorSelection(SelectionPath(systemB, actorName))
                        .ResolveOne(TimeSpan.FromSeconds(10));
                    ordinaryRefs.Add(actorRef);
                    coveredLanes.Add(Association.SelectLane(actorRef.Path.Uid, lanes: 2));
                }

                coveredLanes.Should().HaveCount(2, "ordinary actors must resolve to both configured outbound lanes");

                var ordinaryProbes = ordinaryRefs.Select(_ => CreateTestProbe(systemA)).ToArray();
                for (var i = 0; i < ordinaryRefs.Count; i++)
                    ordinaryRefs[i].Tell($"ordinary-payload-{i}", ordinaryProbes[i].Ref);
                await Task.WhenAll(ordinaryProbes.Select((probe, i) =>
                    probe.ExpectMsgAsync($"ordinary-payload-{i}", TimeSpan.FromSeconds(10)).AsTask()));

                var largeFromA = await systemA.ActorSelection(SelectionPath(systemB, "large"))
                    .ResolveOne(TimeSpan.FromSeconds(10));
                var largeProbe = CreateTestProbe(systemA);
                var largePayload = new string('x', 128 * 1024);
                largeFromA.Tell(largePayload, largeProbe.Ref);
                await largeProbe.ExpectMsgAsync(largePayload.Length, TimeSpan.FromSeconds(10));

                var terminatedProbe = CreateTestProbe(systemA);
                await terminatedProbe.WatchAsync(ordinaryRefs[0]);
                ordinaryOnB.Tell(PoisonPill.Instance);
                await terminatedProbe.ExpectMsgAsync<Terminated>(TimeSpan.FromSeconds(10));

                largeOnB.Tell(PoisonPill.Instance);
                if (byteTransport == "tcp")
                {
                    outboundValidationsA.Should().BeGreaterThan(2,
                        "the shared ordinary socket, control channel, and large channel authenticate the remote server");
                    inboundValidationsB.Should().BeGreaterThan(2,
                        "the shared ordinary socket, control channel, and large channel authenticate the connecting node");
                    inboundValidationsA.Should().BeGreaterThan(1,
                        "the reverse ordinary and control channels authenticate inbound peers too");
                    outboundValidationsB.Should().BeGreaterThan(1,
                        "the reverse ordinary and control channels authenticate outbound peers too");
                }
                else
                {
                    outboundValidationsA.Should().Be(1, "all three QUIC channels share one authenticated connection");
                    inboundValidationsB.Should().Be(1);
                    inboundValidationsA.Should().Be(1, "the peer independently authenticates its reverse connection");
                    outboundValidationsB.Should().Be(1);
                }
            }
            finally
            {
                if (systemA is not null)
                    await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Theory(DisplayName = "Artery ServerOnly TLS completes a real actor round-trip without a client certificate")]
        [InlineData("tcp")]
        [InlineData("quic")]
        public async Task Should_UseServerOnlyTls_ForAnonymousInboundClient(string byteTransport)
        {
            RequireTransportSupport(byteTransport);
            using var certificate = CreateCertificate("1.3.6.1.5.5.7.3.1");
            var trust = TrustCertificate(certificate);
            ActorSystem? systemA = null;
            ActorSystem? systemB = null;
            try
            {
                ActorSystemSetup ServerOnlySetup() => ActorSystemSetup.Create(
                    BootstrapSetup.Create().WithConfig(ArteryConfig(transport: byteTransport)),
                    new ArteryTlsSetup(ArteryTlsSettings.ServerOnly(certificate, trust)));

                systemA = ActorSystem.Create("ArteryTlsServerOnlyA", ServerOnlySetup());
                InitializeLogger(systemA, "[A] ");
                systemB = ActorSystem.Create("ArteryTlsServerOnlyB", ServerOnlySetup());
                InitializeLogger(systemB, "[B] ");
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");

                var echo = await systemA.ActorSelection(SelectionPath(systemB, "echo"))
                    .ResolveOne(TimeSpan.FromSeconds(10));
                var probe = CreateTestProbe(systemA);
                echo.Tell("server-only-payload", probe.Ref);
                await probe.ExpectMsgAsync("server-only-payload", TimeSpan.FromSeconds(10));
            }
            finally
            {
                if (systemA is not null)
                    await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Theory(DisplayName = "Mutual TLS does not create an Artery association when the outbound server certificate is untrusted")]
        [InlineData("tcp")]
        [InlineData("quic")]
        public async Task Should_RejectUntrustedPeerWithoutPlaintextFallback(string byteTransport)
        {
            RequireTransportSupport(byteTransport);
            using var certificateA = CreateCertificate();
            using var certificateB = CreateCertificate();
            using var unrelatedCertificate = CreateCertificate();
            var trustA = TrustCertificate(certificateA);
            var trustB = TrustCertificate(certificateB);
            ActorSystem? systemA = null;
            ActorSystem? systemB = null;
            try
            {
                var systemASetup = MutualTlsSetup(certificateA, trustB, TrustCertificate(unrelatedCertificate), transport: byteTransport);
                var systemBSetup = MutualTlsSetup(certificateB, trustA, trustA, transport: byteTransport);
                systemA = ActorSystem.Create("ArteryTlsRejectA", systemASetup);
                InitializeLogger(systemA, "[A] ");
                systemB = ActorSystem.Create("ArteryTlsRejectB", systemBSetup);
                InitializeLogger(systemB, "[B] ");
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");

                var resolve = () => systemA.ActorSelection(SelectionPath(systemB, "echo"))
                    .ResolveOne(TimeSpan.FromSeconds(3));

                await resolve.Should().ThrowAsync<ActorNotFoundException>();
                var transport = (ArteryRemoting)RARP.For(systemA).Provider.Transport;
                transport.Registry.AssociationFor(RARP.For(systemB).Provider.DefaultAddress)
                    .CurrentState.UniqueRemoteAddress.Should().BeNull("TLS authentication must complete before Artery associates the peer");
                await VerifyAcceptedPeerAsync(systemB, certificateA, trustB, byteTransport);
            }
            finally
            {
                if (systemA is not null)
                    await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Theory(DisplayName = "Artery mutual TLS rejects a client without a certificate and does not fall back to plaintext")]
        [InlineData("tcp")]
        [InlineData("quic")]
        public async Task Should_RejectMissingClientCertificate(string byteTransport)
        {
            RequireTransportSupport(byteTransport);
            using var certificateA = CreateCertificate();
            using var certificateB = CreateCertificate();
            var trustB = TrustCertificate(certificateB);
            var trustA = TrustCertificate(certificateA);
            ActorSystem? systemA = null;
            ActorSystem? systemB = null;
            try
            {
                var systemASetup = ActorSystemSetup.Create(
                    BootstrapSetup.Create().WithConfig(ArteryConfig(transport: byteTransport)),
                    new ArteryTlsSetup(ArteryTlsSettings.ServerOnly(certificateA, trustB)));
                var systemBSetup = MutualTlsSetup(certificateB, trustA, trustA, transport: byteTransport);
                systemA = ActorSystem.Create("ArteryTlsMissingClientA", systemASetup);
                InitializeLogger(systemA, "[A] ");
                systemB = ActorSystem.Create("ArteryTlsMissingClientB", systemBSetup);
                InitializeLogger(systemB, "[B] ");
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");

                var resolve = () => systemA.ActorSelection(SelectionPath(systemB, "echo"))
                    .ResolveOne(TimeSpan.FromSeconds(3));

                await resolve.Should().ThrowAsync<ActorNotFoundException>();
                var transport = (ArteryRemoting)RARP.For(systemA).Provider.Transport;
                transport.Registry.AssociationFor(RARP.For(systemB).Provider.DefaultAddress)
                    .CurrentState.UniqueRemoteAddress.Should().BeNull("a client without a certificate must not associate");
                await VerifyAcceptedPeerAsync(systemB, certificateA, trustB, byteTransport);
            }
            finally
            {
                if (systemA is not null)
                    await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Theory(DisplayName = "Artery reconnects over TLS after a peer restarts and changes incarnation")]
        [InlineData("tcp")]
        [InlineData("quic")]
        public async Task Should_ReconnectWithTls_AfterPeerRestart(string byteTransport)
        {
            RequireTransportSupport(byteTransport);
            const string peerName = "ArteryTlsRestartB";
            using var certificate = CreateCertificate();
            var trust = TrustCertificate(certificate);
            ActorSystem? systemA = null;
            ActorSystem? systemB = null;
            ActorSystem? restartedSystemB = null;
            try
            {
                systemA = ActorSystem.Create("ArteryTlsRestartA", MutualTlsSetup(certificate, trust, trust, transport: byteTransport));
                InitializeLogger(systemA, "[A] ");
                systemB = ActorSystem.Create(peerName, MutualTlsSetup(certificate, trust, trust, transport: byteTransport));
                InitializeLogger(systemB, "[B] ");
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");
                var port = RARP.For(systemB).Provider.DefaultAddress.Port!.Value;
                var address = RARP.For(systemB).Provider.DefaultAddress;

                var echo = await systemA.ActorSelection(SelectionPath(systemB, "echo"))
                    .ResolveOne(TimeSpan.FromSeconds(10));
                var probe = CreateTestProbe(systemA);
                echo.Tell("before-restart", probe.Ref);
                await probe.ExpectMsgAsync("before-restart", TimeSpan.FromSeconds(10));
                var oldUid = AddressUidExtension.Uid(systemB);

                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
                systemB = null;

                restartedSystemB = ActorSystem.Create(peerName,
                    MutualTlsSetup(certificate, trust, trust, port, transport: byteTransport));
                InitializeLogger(restartedSystemB, "[B restarted] ");
                restartedSystemB.ActorOf(Props.Create(() => new Echo()), "echo");
                var newUid = AddressUidExtension.Uid(restartedSystemB);
                newUid.Should().NotBe(oldUid);

                // Idle associations reconnect on demand; resolution drives the new TLS handshake.
                var selectionAfterRestart = systemA.ActorSelection(SelectionPath(restartedSystemB, "echo"));
                IActorRef? echoAfterRestart = null;
                await AwaitAssertAsync(async () =>
                {
                    echoAfterRestart = await selectionAfterRestart.ResolveOne(
                        TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
                }, TimeSpan.FromSeconds(30), cancellationToken: TestContext.Current.CancellationToken);

                var transport = (ArteryRemoting)RARP.For(systemA).Provider.Transport;
                var association = transport.Registry.AssociationFor(address);
                await AwaitConditionAsync(
                    () => Task.FromResult(association.CurrentState.UniqueRemoteAddress?.Uid == newUid),
                    TimeSpan.FromSeconds(30));
                association.IsQuarantined(oldUid).Should().BeFalse();
                transport.Quarantine(address, oldUid);
                association.IsQuarantined(oldUid).Should().BeFalse("a stale UID cannot quarantine a new peer incarnation");
                association.IsQuarantined(newUid).Should().BeFalse("a stale UID quarantine leaves the current incarnation usable");

                echoAfterRestart!.Tell("after-restart", probe.Ref);
                await probe.ExpectMsgAsync("after-restart", TimeSpan.FromSeconds(10));
            }
            finally
            {
                if (systemA is not null)
                    await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(10.Seconds());
                if (restartedSystemB is not null)
                    await restartedSystemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        private async Task VerifyAcceptedPeerAsync(ActorSystem server, X509Certificate2 certificate,
            TlsPeerPolicy serverPolicy, string byteTransport)
        {
            var valid = ActorSystem.Create("ArteryTlsValidPeer", MutualTlsSetup(certificate,
                serverPolicy, serverPolicy, transport: byteTransport));
            InitializeLogger(valid, "[valid] ");
            try
            {
                var actor = await valid.ActorSelection(SelectionPath(server, "echo"))
                    .ResolveOne(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                var probe = CreateTestProbe(valid);
                actor.Tell("accepted-after-rejection", probe.Ref);
                await probe.ExpectMsgAsync("accepted-after-rejection", TimeSpan.FromSeconds(5));
            }
            finally
            {
                await valid.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Theory(DisplayName = "Artery preserves explicit certificate hostname checks for both TLS roles")]
        [InlineData("tcp", true, "localhost", true)]
        [InlineData("tcp", true, "wrong.example", false)]
        [InlineData("tcp", false, "localhost", true)]
        [InlineData("tcp", false, "wrong.example", false)]
        [InlineData("quic", true, "localhost", true)]
        [InlineData("quic", true, "wrong.example", false)]
        [InlineData("quic", false, "localhost", true)]
        [InlineData("quic", false, "wrong.example", false)]
        public async Task Should_ValidateExplicitHostname_When_PolicySpecifiesIt(
            string byteTransport, bool inbound, string expectedName, bool accepted)
        {
            RequireTransportSupport(byteTransport);
            using var certificate = CreateCertificate();
            var trusted = TrustCertificate(certificate);
            var named = trusted.And(TlsCertificateValidation.ValidateHostname(expectedName));
            var client = ActorSystem.Create("ArteryTlsNamedClient",
                MutualTlsSetup(certificate, trusted, inbound ? trusted : named, transport: byteTransport));
            ActorSystem? server = null;
            InitializeLogger(client, "[client] ");
            try
            {
                server = ActorSystem.Create("ArteryTlsNamedServer",
                    MutualTlsSetup(certificate, inbound ? named : trusted, trusted, transport: byteTransport));
                InitializeLogger(server, "[server] ");
                server.ActorOf(Props.Create(() => new Echo()), "echo");
                if (accepted)
                {
                    var echo = await client.ActorSelection(SelectionPath(server, "echo"))
                        .ResolveOne(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                    var probe = CreateTestProbe(client);
                    echo.Tell("explicit-name-accepted", probe.Ref);
                    await probe.ExpectMsgAsync("explicit-name-accepted", TimeSpan.FromSeconds(5));
                }
                else
                {
                    var resolve = () => client.ActorSelection(SelectionPath(server, "echo"))
                        .ResolveOne(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
                    await resolve.Should().ThrowAsync<ActorNotFoundException>();
                }
            }
            finally
            {
                await client.Terminate().AwaitWithTimeout(10.Seconds());
                if (server is not null)
                    await server.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        internal static void RequireTransportSupport(string byteTransport)
        {
            if (byteTransport == "quic")
                Assert.SkipUnless(QuicConnection.IsSupported && QuicListener.IsSupported,
                    "Native QUIC tests require supported MsQuic and TLS 1.3. A skip does not establish QUIC correctness.");
        }

        internal static X509Certificate2 CreateCertificate(params string[] ekuOids)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            subjectAlternativeNames.AddDnsName("localhost");
            subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(subjectAlternativeNames.Build());
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
            var usages = new OidCollection();
            if (ekuOids.Length == 0)
            {
                usages.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                usages.Add(new Oid("1.3.6.1.5.5.7.3.2"));
            }
            else
            {
                foreach (var oid in ekuOids)
                    usages.Add(new Oid(oid));
            }
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, critical: true));
            using var generatedCertificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var pkcs12 = generatedCertificate.Export(X509ContentType.Pkcs12);
            try
            {
                return X509CertificateLoader.LoadPkcs12(pkcs12, password: null, X509KeyStorageFlags.DefaultKeySet);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkcs12);
            }
        }
    }
}
