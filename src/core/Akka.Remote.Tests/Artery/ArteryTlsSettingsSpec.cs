// -----------------------------------------------------------------------
// <copyright file="ArteryTlsSettingsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Akka.IO;
using Akka.Remote.Artery;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public class ArteryTlsSettingsSpec
    {
        private static TlsPeerPolicy AcceptAllPolicy() =>
            TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true);

        [Fact(DisplayName = "Artery TLS defaults to a ten second timeout and runtime protocol selection")]
        public void Should_UseDefaultTimeoutAndProtocols()
        {
            using var certificate = CreateCertificate(
                "1.3.6.1.5.5.7.3.1", "1.3.6.1.5.5.7.3.2");

            var settings = ArteryTlsSettings.Mutual(certificate, AcceptAllPolicy());

            settings.HandshakeTimeout.Should().Be(TimeSpan.FromSeconds(10));
            settings.Protocols.Should().Be(SslProtocols.None);
            settings.WithHandshakeTimeout(TimeSpan.FromSeconds(5)).HandshakeTimeout
                .Should().Be(TimeSpan.FromSeconds(5));
            settings.WithProtocols(SslProtocols.Tls12).Protocols.Should().Be(SslProtocols.Tls12);
            settings.HandshakeTimeout.Should().Be(TimeSpan.FromSeconds(10), "copy methods preserve the original value");
        }

        [Fact(DisplayName = "Artery mutual TLS rejects a certificate restricted to only one authentication role")]
        public void Should_RejectCertificateWithoutBothAuthenticationRoles()
        {
            using var serverOnlyCertificate = CreateCertificate("1.3.6.1.5.5.7.3.1");
            using var clientOnlyCertificate = CreateCertificate("1.3.6.1.5.5.7.3.2");

            var action = () => ArteryTlsSettings.Mutual(serverOnlyCertificate, AcceptAllPolicy());
            var serverOnlyAction = () => ArteryTlsSettings.ServerOnly(clientOnlyCertificate, AcceptAllPolicy());

            action.Should().Throw<ArgumentException>();
            serverOnlyAction.Should().Throw<ArgumentException>();
        }

        [Fact(DisplayName = "Artery accepts a certificate with anyExtendedKeyUsage for both roles")]
        public void Should_AcceptCertificateWithAnyExtendedKeyUsage()
        {
            using var certificate = CreateCertificate("2.5.29.37.0");

            var settings = ArteryTlsSettings.Mutual(certificate, AcceptAllPolicy());

            settings.Should().NotBeNull();
        }

        [Fact(DisplayName = "Artery mutual TLS keeps independent inbound and outbound peer policies in their matching directions")]
        public void Should_KeepDirectionalPeerPolicies()
        {
            using var certificate = CreateCertificate(
                "1.3.6.1.5.5.7.3.1", "1.3.6.1.5.5.7.3.2");
            var inboundPolicy = AcceptAllPolicy();
            var outboundPolicy = AcceptAllPolicy();

            var settings = ArteryTlsSettings.Mutual(certificate, inboundPolicy, outboundPolicy);

            settings.ServerSettings.Certificate.Should().BeSameAs(certificate);
            settings.ServerSettings.ClientValidation.Should().BeSameAs(inboundPolicy);
            settings.ClientSettings.Certificate.Should().BeSameAs(certificate);
            settings.ClientSettings.ServerValidation.Should().BeSameAs(outboundPolicy);
        }

        [Fact(DisplayName = "Artery ServerOnly omits client certificates and accepts anonymous inbound clients")]
        public void Should_KeepServerOnlyRolesDirectional()
        {
            using var certificate = CreateCertificate("1.3.6.1.5.5.7.3.1");
            var outboundPolicy = AcceptAllPolicy();

            var settings = ArteryTlsSettings.ServerOnly(certificate, outboundPolicy);

            settings.ServerSettings.Certificate.Should().BeSameAs(certificate);
            settings.ServerSettings.ClientValidation.Should().BeNull();
            settings.ClientSettings.Certificate.Should().BeNull();
            settings.ClientSettings.ServerValidation.Should().BeSameAs(outboundPolicy);
        }

        [Fact(DisplayName = "Artery TLS rejects non-positive handshake timeouts")]
        public void Should_RejectInvalidHandshakeTimeout()
        {
            using var certificate = CreateCertificate("1.3.6.1.5.5.7.3.1", "1.3.6.1.5.5.7.3.2");
            var settings = ArteryTlsSettings.Mutual(certificate, AcceptAllPolicy());

            var action = () => settings.WithHandshakeTimeout(TimeSpan.Zero);

            action.Should().Throw<ArgumentOutOfRangeException>();
        }

        private static X509Certificate2 CreateCertificate(params string[] ekuOids)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=artery-test", key, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var usages = new OidCollection();
            foreach (var oid in ekuOids)
                usages.Add(new Oid(oid));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, critical: true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        }
    }
}
