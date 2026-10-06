// -----------------------------------------------------------------------
// <copyright file="ArteryTlsTransportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
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

        private static Config ArteryConfig(int port = 0) => ConfigurationFactory.ParseString($$"""
            akka.loglevel = DEBUG
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = {{port}}
            akka.remote.artery.large-message-destinations = ["/user/large"]
            akka.remote.artery.advanced.outbound-lanes = 2
            akka.remote.artery.advanced.maximum-frame-size = 64k
            akka.remote.artery.advanced.maximum-large-frame-size = 256k
            """);

        private static TlsPeerPolicy TrustCertificate(X509Certificate2 certificate, Action? onValidation = null) =>
            TlsPeerPolicy.CustomTrust((peerCertificate, _, _, _, _) =>
            {
                onValidation?.Invoke();
                return string.Equals(peerCertificate?.Thumbprint, certificate.Thumbprint,
                    StringComparison.OrdinalIgnoreCase);
            }).And(TlsCertificateValidation.ValidateHostname());

        private static ActorSystemSetup MutualTlsSetup(X509Certificate2 certificate,
            TlsPeerPolicy inboundClientPolicy, TlsPeerPolicy outboundServerPolicy, int port = 0) =>
            ActorSystemSetup.Create(
                BootstrapSetup.Create().WithConfig(ArteryConfig(port)),
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

        [Fact(DisplayName = "Mutual TLS protects Artery ordinary, large, and reliable control traffic with multiple lanes")]
        public async Task Should_UseTlsForEveryArteryChannel()
        {
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
                systemA = ActorSystem.Create("ArteryTlsA", MutualTlsSetup(certificate, inboundPolicyA, outboundPolicyA));
                InitializeLogger(systemA, "[A] ");
                systemB = ActorSystem.Create("ArteryTlsB", MutualTlsSetup(certificate, inboundPolicyB, outboundPolicyB));
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
                outboundValidationsA.Should().BeGreaterThan(2,
                    "the shared ordinary socket, control channel, and large channel authenticate the remote server");
                inboundValidationsB.Should().BeGreaterThan(2,
                    "the shared ordinary socket, control channel, and large channel authenticate the connecting node");
                inboundValidationsA.Should().BeGreaterThan(1,
                    "the reverse ordinary and control channels authenticate inbound peers too");
                outboundValidationsB.Should().BeGreaterThan(1,
                    "the reverse ordinary and control channels authenticate outbound peers too");
            }
            finally
            {
                if (systemA is not null)
                    await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Artery ServerOnly TLS completes a real actor round-trip without a client certificate")]
        public async Task Should_UseServerOnlyTls_ForAnonymousInboundClient()
        {
            using var certificate = CreateCertificate("1.3.6.1.5.5.7.3.1");
            var trust = TrustCertificate(certificate);
            ActorSystem? systemA = null;
            ActorSystem? systemB = null;
            try
            {
                ActorSystemSetup ServerOnlySetup() => ActorSystemSetup.Create(
                    BootstrapSetup.Create().WithConfig(ArteryConfig()),
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

        [Fact(DisplayName = "Mutual TLS does not create an Artery association when the outbound server certificate is untrusted")]
        public async Task Should_RejectUntrustedPeerWithoutPlaintextFallback()
        {
            using var certificateA = CreateCertificate();
            using var certificateB = CreateCertificate();
            using var unrelatedCertificate = CreateCertificate();
            var trustA = TrustCertificate(certificateA);
            var trustB = TrustCertificate(certificateB);
            ActorSystem? systemA = null;
            ActorSystem? systemB = null;
            try
            {
                var systemASetup = MutualTlsSetup(certificateA, trustB, TrustCertificate(unrelatedCertificate));
                var systemBSetup = MutualTlsSetup(certificateB, trustA, trustA);
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
            }
            finally
            {
                if (systemA is not null)
                    await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Artery mutual TLS rejects a client without a certificate and does not fall back to plaintext")]
        public async Task Should_RejectMissingClientCertificate()
        {
            using var certificateA = CreateCertificate();
            using var certificateB = CreateCertificate();
            var trustB = TrustCertificate(certificateB);
            var trustA = TrustCertificate(certificateA);
            ActorSystem? systemA = null;
            ActorSystem? systemB = null;
            try
            {
                var systemASetup = ActorSystemSetup.Create(
                    BootstrapSetup.Create().WithConfig(ArteryConfig()),
                    new ArteryTlsSetup(ArteryTlsSettings.ServerOnly(certificateA, trustB)));
                var systemBSetup = MutualTlsSetup(certificateB, trustA, trustA);
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
            }
            finally
            {
                if (systemA is not null)
                    await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Artery reconnects over TLS after a peer restarts and changes incarnation")]
        public async Task Should_ReconnectWithTls_AfterPeerRestart()
        {
            const string peerName = "ArteryTlsRestartB";
            using var certificate = CreateCertificate();
            var trust = TrustCertificate(certificate);
            ActorSystem? systemA = null;
            ActorSystem? systemB = null;
            ActorSystem? restartedSystemB = null;
            try
            {
                systemA = ActorSystem.Create("ArteryTlsRestartA", MutualTlsSetup(certificate, trust, trust));
                InitializeLogger(systemA, "[A] ");
                systemB = ActorSystem.Create(peerName, MutualTlsSetup(certificate, trust, trust));
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
                    MutualTlsSetup(certificate, trust, trust, port));
                InitializeLogger(restartedSystemB, "[B restarted] ");
                restartedSystemB.ActorOf(Props.Create(() => new Echo()), "echo");
                var newUid = AddressUidExtension.Uid(restartedSystemB);
                newUid.Should().NotBe(oldUid);

                var transport = (ArteryRemoting)RARP.For(systemA).Provider.Transport;
                var association = transport.Registry.AssociationFor(address);
                await AwaitConditionAsync(
                    () => Task.FromResult(association.CurrentState.UniqueRemoteAddress?.Uid == newUid),
                    TimeSpan.FromSeconds(30));
                association.IsQuarantined(oldUid).Should().BeFalse();
                transport.Quarantine(address, oldUid);
                association.IsQuarantined(oldUid).Should().BeFalse("a stale UID cannot quarantine a new peer incarnation");
                association.IsQuarantined(newUid).Should().BeFalse("a stale UID quarantine leaves the current incarnation usable");

                var echoAfterRestart = await systemA.ActorSelection(
                        SelectionPath(restartedSystemB, "echo"))
                    .ResolveOne(TimeSpan.FromSeconds(10));
                echoAfterRestart.Tell("after-restart", probe.Ref);
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

        private static X509Certificate2 CreateCertificate(params string[] ekuOids)
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
