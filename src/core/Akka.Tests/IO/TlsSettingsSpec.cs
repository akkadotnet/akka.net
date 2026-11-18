// -----------------------------------------------------------------------
// <copyright file="TlsSettingsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Akka.Event;
using Akka.IO;
using Xunit;

namespace Akka.Tests.IO
{
    public sealed class TlsSettingsSpec
    {
        [Fact(DisplayName = "Should_Create_Role_Specific_Options_And_Leave_Original_Settings_Unchanged")]
        public void Should_create_role_specific_options_and_keep_copy_settings_immutable()
        {
            using var clientCertificate = CreateCertificate("client", server: false);
            using var serverCertificate = CreateCertificate("localhost", server: true);
            var peerPolicy = TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true);
            var originalClient = TlsClientSettings.Mutual(clientCertificate, peerPolicy);
            var client = originalClient.WithTargetHost("server.example").WithProtocols(SslProtocols.Tls12);
            Assert.Null(originalClient.TargetHost);
            Assert.Equal(SslProtocols.None, originalClient.Protocols);
            Assert.Equal(TimeSpan.FromSeconds(10), originalClient.HandshakeTimeout);
            var clientOptions = client.CreateAuthenticationOptions("fallback", "peer", NoLogger.Instance);
            Assert.Equal("server.example", clientOptions.TargetHost);
            Assert.Equal(SslProtocols.Tls12, clientOptions.EnabledSslProtocols);
            Assert.NotNull(clientOptions.ClientCertificates);
            Assert.NotNull(clientOptions.LocalCertificateSelectionCallback);

            var clientOnly = TlsClientSettings.ServerOnly(peerPolicy);
            var clientOnlyOptions = clientOnly.CreateAuthenticationOptions("server.example", "peer", NoLogger.Instance);
            Assert.Null(clientOnlyOptions.ClientCertificates);
            Assert.Null(clientOnlyOptions.LocalCertificateSelectionCallback);
            Assert.Equal(TimeSpan.FromSeconds(10), clientOnly.HandshakeTimeout);
            Assert.Equal(SslProtocols.None, clientOnly.Protocols);

            var mutualServer = TlsServerSettings.Mutual(serverCertificate, peerPolicy);
            var serverOptions = mutualServer.CreateAuthenticationOptions("peer", NoLogger.Instance);
            Assert.True(serverOptions.ClientCertificateRequired);
            Assert.NotNull(serverOptions.RemoteCertificateValidationCallback);
            Assert.False(TlsServerSettings.ServerOnly(serverCertificate)
                .CreateAuthenticationOptions("peer", NoLogger.Instance).ClientCertificateRequired);
        }

        [Fact(DisplayName = "Should_Reject_Missing_Peer_Certificate_Before_Invoking_Trust_Callback")]
        public void Should_reject_missing_peer_certificate_before_invoking_trust_callback()
        {
            var callbackCalls = 0;
            var policy = TlsPeerPolicy.CustomTrust((_, _, _, _, _) =>
            {
                callbackCalls++;
                return true;
            });

            Assert.False(policy.ValidatePeer(null, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.Equal(0, callbackCalls);
        }

        [Fact(DisplayName = "Should_Require_Chain_Trust_While_Allowing_Separate_Hostname_Validation")]
        public void Should_require_chain_trust_while_allowing_separate_hostname_validation()
        {
            using var certificate = CreateCertificate("worker-one", server: true);
            var validateChain = TlsCertificateValidation.ValidateChain();

            Assert.True(validateChain(certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.True(validateChain(certificate, null, "peer", SslPolicyErrors.RemoteCertificateNameMismatch,
                NoLogger.Instance));
            Assert.False(validateChain(certificate, null, "peer", SslPolicyErrors.RemoteCertificateChainErrors,
                NoLogger.Instance));
            Assert.False(validateChain(certificate, null, "peer",
                SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch,
                NoLogger.Instance));
            Assert.False(validateChain(certificate, null, "peer", SslPolicyErrors.RemoteCertificateNotAvailable,
                NoLogger.Instance));
            Assert.False(validateChain(null, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
        }

        [Fact(DisplayName = "Should_Preserve_Original_Errors_And_Only_Narrow_Base_Trust")]
        public void Should_preserve_original_errors_and_only_narrow_base_trust()
        {
            using var leaf = CreateCertificate("worker-one", server: true);
            var errors = SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch;
            var observedErrors = SslPolicyErrors.None;
            var validators = new TlsCertificateValidationCallback[]
            {
                (_, _, _, suppliedErrors, _) => { observedErrors = suppliedErrors; return true; }
            };
            var policy = TlsPeerPolicy.CustomTrust((_, _, _, suppliedErrors, _) =>
                    suppliedErrors == errors)
                .And(validators);
            validators[0] = (_, _, _, _, _) => false;

            Assert.True(policy.ValidatePeer(leaf, null, "peer", errors, NoLogger.Instance));
            Assert.Equal(errors, observedErrors);

            var systemTrust = TlsPeerPolicy.SystemTrust()
                .And((_, _, _, _, _) => true);
            Assert.False(systemTrust.ValidatePeer(leaf, null, "peer",
                SslPolicyErrors.RemoteCertificateChainErrors, NoLogger.Instance));
        }

        [Fact(DisplayName = "Should_Validate_Settings_Factories_And_Copy_Arguments")]
        public void Should_validate_settings_factories_and_copy_arguments()
        {
            var policy = TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true);
            Assert.Throws<ArgumentNullException>(() => TlsClientSettings.ServerOnly(null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => TlsClientSettings.ServerOnly(policy).WithHandshakeTimeout(TimeSpan.Zero));
            Assert.Throws<ArgumentNullException>(() => TlsClientSettings.ServerOnly(policy).WithTargetHost(null!));
            Assert.Throws<ArgumentException>(() => TlsClientSettings.ServerOnly(policy).WithTargetHost(" "));
            Assert.Throws<ArgumentException>(() => TlsPeerPolicy.PinnedCertificates());
            Assert.Throws<ArgumentException>(() => TlsCertificateValidation.ValidateHostname("*.example.net"));

            using var certificate = CreateCertificate("localhost", server: true);
            using var publicCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);
            Assert.Throws<ArgumentException>(() => TlsServerSettings.ServerOnly(publicCertificate));
            Assert.Throws<ArgumentException>(() => TlsClientSettings.Mutual(publicCertificate, policy));
        }

        [Theory(DisplayName = "Should_Reject_Handshake_Timeouts_Outside_The_Transport_Timer_Range")]
        [InlineData(0L)]
        [InlineData(-1L)]
        [InlineData(4294967295L)]
        public void Should_reject_handshake_timeouts_outside_the_transport_timer_range(long milliseconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TlsClientSettings.ServerOnly(TlsPeerPolicy.SystemTrust())
                    .WithHandshakeTimeout(TimeSpan.FromMilliseconds(milliseconds)));
        }

        [Fact(DisplayName = "Should_Accept_Only_Configured_Certificate_Pins")]
        public void Should_accept_only_configured_certificate_pins()
        {
            using var certificate = CreateCertificate("worker-one", server: true);
            var spacedPin = string.Join(" ", certificate.Thumbprint.ToLowerInvariant().ToCharArray());
            var allowedPins = new[] { spacedPin };
            var pinned = TlsCertificateValidation.PinnedCertificate(allowedPins);
            allowedPins[0] = new string('0', 40);

            Assert.True(pinned(certificate, null, "peer", SslPolicyErrors.RemoteCertificateChainErrors, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.PinnedCertificate(new string('0', 40))(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.Throws<ArgumentNullException>(() => TlsCertificateValidation.PinnedCertificate(null!));
            Assert.Throws<ArgumentException>(() => TlsCertificateValidation.PinnedCertificate(" "));
            Assert.Throws<ArgumentNullException>(() => TlsCertificateValidation.ValidateSubject(null!));
            Assert.Throws<ArgumentException>(() => TlsCertificateValidation.ValidateIssuer(" "));
        }

        [Fact(DisplayName = "Should_Reject_Subject_Issuer_And_Hostname_Mismatches")]
        public void Should_reject_subject_issuer_and_hostname_mismatches()
        {
            using var certificate = CreateCertificate("worker-one", server: true);

            Assert.True(TlsCertificateValidation.ValidateSubject("CN=worker-*")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.ValidateSubject("CN=other-*")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.ValidateSubject("cn=worker-one")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.ValidateSubject("CN=worker-one-extra")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.True(TlsCertificateValidation.ValidateIssuer("CN=worker-one")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.ValidateIssuer("CN=other-*")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.ValidateIssuer("cn=worker-one")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.ValidateIssuer("CN=worker-one-extra")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));

            using var distinguishedNameCertificate = CreateCertificate("worker.1", server: true);
            Assert.True(TlsCertificateValidation.ValidateSubject("CN=worker.1")(
                distinguishedNameCertificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.ValidateSubject("CN=workerX1")(
                distinguishedNameCertificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.True(TlsCertificateValidation.ValidateIssuer("CN=worker.1")(
                distinguishedNameCertificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.ValidateIssuer("CN=workerX1")(
                distinguishedNameCertificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.True(TlsCertificateValidation.ValidateHostname("localhost")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.ValidateHostname("wrong.example")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.ValidateHostname()(
                certificate, null, "peer", SslPolicyErrors.RemoteCertificateNameMismatch, NoLogger.Instance));
        }

        [Fact(DisplayName = "Should_Match_DNS_Wildcard_And_IP_Subject_Alternative_Names")]
        public void Should_match_dns_wildcard_and_ip_subject_alternative_names()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=unused.example.net", rsa, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("*.example.net");
            san.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(san.Build());
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddHours(1));

            Assert.True(TlsCertificateValidation.ValidateHostname("worker.example.net")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.ValidateHostname("deep.worker.example.net")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.True(TlsCertificateValidation.ValidateHostname("127.0.0.1")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.False(TlsCertificateValidation.ValidateHostname("127.0.0.2")(
                certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
        }

        [Fact(DisplayName = "Should_Validate_ECDSA_Private_Key_Access_And_Reject_A_Public_Only_Certificate")]
        public void Should_validate_ecdsa_private_key_access_and_reject_public_only_certificate()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=ecdsa.example.net", ecdsa, HashAlgorithmName.SHA256);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddHours(1));
            using var publicCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);

            Assert.Same(certificate, TlsServerSettings.ServerOnly(certificate).Certificate);
            Assert.Throws<ArgumentException>(() => TlsServerSettings.ServerOnly(publicCertificate));
        }

        [Fact(DisplayName = "Should_Snapshot_And_Short_Circuit_Composed_Validation_Callbacks")]
        public void Should_snapshot_and_short_circuit_composed_validation_callbacks()
        {
            using var certificate = CreateCertificate("worker-one", server: true);
            var laterCalls = 0;
            TlsCertificateValidationCallback later = (_, _, _, _, _) =>
            {
                laterCalls++;
                return true;
            };
            var validators = new TlsCertificateValidationCallback[] { (_, _, _, _, _) => true, later };
            var combined = TlsCertificateValidation.Combine(validators);
            validators[0] = (_, _, _, _, _) => false;
            validators[1] = (_, _, _, _, _) => false;

            Assert.True(combined(certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.Equal(1, laterCalls);

            laterCalls = 0;
            var shortCircuit = TlsCertificateValidation.Combine((_, _, _, _, _) => false, later);
            Assert.False(shortCircuit(certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.Equal(0, laterCalls);
            Assert.Throws<ArgumentNullException>(() => TlsCertificateValidation.Combine(null!));
            Assert.Throws<ArgumentException>(() => TlsCertificateValidation.Combine());
            Assert.Throws<ArgumentException>(() => TlsCertificateValidation.Combine((TlsCertificateValidationCallback)null!));

            laterCalls = 0;
            var chainThen = TlsCertificateValidation.ChainPlusThen((_, _, _) =>
            {
                laterCalls++;
                return true;
            });
            Assert.False(chainThen(certificate, null, "peer", SslPolicyErrors.RemoteCertificateChainErrors, NoLogger.Instance));
            Assert.Equal(0, laterCalls);
            Assert.True(chainThen(certificate, null, "peer", SslPolicyErrors.None, NoLogger.Instance));
            Assert.Equal(1, laterCalls);

            X509Certificate2? observedCertificate = null;
            X509Chain? observedChain = null;
            string? observedPeer = null;
            var successfulChainThen = TlsCertificateValidation.ChainPlusThen((peerCertificate, chain, remotePeer) =>
            {
                observedCertificate = peerCertificate;
                observedChain = chain;
                observedPeer = remotePeer;
                return true;
            });
            Assert.True(successfulChainThen(certificate, null, "authenticated-peer", SslPolicyErrors.None,
                NoLogger.Instance));
            Assert.Same(certificate, observedCertificate);
            Assert.Null(observedChain);
            Assert.Equal("authenticated-peer", observedPeer);
            Assert.Throws<ArgumentNullException>(() => TlsCertificateValidation.ChainPlusThen(null!));
        }

        private static X509Certificate2 CreateCertificate(string host, bool server)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={host}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            var usages = server ? X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment : X509KeyUsageFlags.DigitalSignature;
            request.CertificateExtensions.Add(new X509KeyUsageExtension(usages, true));
            var eku = new OidCollection
            {
                new Oid(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2")
            };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, true));
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        }
    }
}
