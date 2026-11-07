// -----------------------------------------------------------------------
// <copyright file="ArteryTlsSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor.Setup;
using Akka.IO;

namespace Akka.Remote.Artery
{
    /// <summary>Immutable node-level TLS settings shared by every Artery TCP channel.</summary>
    public sealed class ArteryTlsSettings
    {
        private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
        private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
        private const string AnyExtendedKeyUsageOid = "2.5.29.37.0";

        private readonly TlsClientSettings _clientSettings;
        private readonly TlsServerSettings _serverSettings;

        private ArteryTlsSettings(TlsClientSettings clientSettings, TlsServerSettings serverSettings)
        {
            _clientSettings = clientSettings ?? throw new ArgumentNullException(nameof(clientSettings));
            _serverSettings = serverSettings ?? throw new ArgumentNullException(nameof(serverSettings));
        }

        /// <summary>Gets the maximum time allowed for each channel's TLS handshake.</summary>
        public TimeSpan HandshakeTimeout => _clientSettings.HandshakeTimeout;

        /// <summary>Gets the TLS protocols available to each channel.</summary>
        public SslProtocols Protocols => _clientSettings.Protocols;

        /// <summary>Creates a mutual TLS profile using one peer policy in both directions.</summary>
        /// <remarks>The certificate must be suitable for both client and server authentication.</remarks>
        public static ArteryTlsSettings Mutual(X509Certificate2 certificate, TlsPeerPolicy peerPolicy)
        {
            ArgumentNullException.ThrowIfNull(peerPolicy);
            return Mutual(certificate, peerPolicy, peerPolicy);
        }

        /// <summary>Creates a mutual TLS profile with independent inbound and outbound peer policies.</summary>
        /// <param name="certificate">The caller-owned certificate presented in both directions.</param>
        /// <param name="inboundClientPolicy">The policy used to validate connecting Artery nodes.</param>
        /// <param name="outboundServerPolicy">The policy used to validate remote Artery nodes.</param>
        public static ArteryTlsSettings Mutual(X509Certificate2 certificate,
            TlsPeerPolicy inboundClientPolicy, TlsPeerPolicy outboundServerPolicy)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            ArgumentNullException.ThrowIfNull(inboundClientPolicy);
            ArgumentNullException.ThrowIfNull(outboundServerPolicy);
            ValidateCertificateRole(certificate, ClientAuthenticationOid, nameof(certificate));
            ValidateCertificateRole(certificate, ServerAuthenticationOid, nameof(certificate));

            return new ArteryTlsSettings(
                TlsClientSettings.Mutual(certificate, outboundServerPolicy),
                TlsServerSettings.Mutual(certificate, inboundClientPolicy));
        }

        /// <summary>Creates a TLS profile that accepts anonymous inbound clients and validates remote servers.</summary>
        public static ArteryTlsSettings ServerOnly(X509Certificate2 serverCertificate,
            TlsPeerPolicy outboundServerPolicy)
        {
            ArgumentNullException.ThrowIfNull(serverCertificate);
            ArgumentNullException.ThrowIfNull(outboundServerPolicy);
            ValidateCertificateRole(serverCertificate, ServerAuthenticationOid, nameof(serverCertificate));

            return new ArteryTlsSettings(
                TlsClientSettings.ServerOnly(outboundServerPolicy),
                TlsServerSettings.ServerOnly(serverCertificate));
        }

        /// <summary>Returns a copy with a different handshake timeout.</summary>
        public ArteryTlsSettings WithHandshakeTimeout(TimeSpan value) =>
            new(_clientSettings.WithHandshakeTimeout(value), _serverSettings.WithHandshakeTimeout(value));

        /// <summary>Returns a copy with different TLS protocols.</summary>
        public ArteryTlsSettings WithProtocols(SslProtocols value) =>
            new(_clientSettings.WithProtocols(value), _serverSettings.WithProtocols(value));

        internal TlsClientSettings ClientSettings => _clientSettings;

        internal TlsServerSettings ServerSettings => _serverSettings;

        private static void ValidateCertificateRole(X509Certificate2 certificate, string requiredEku,
            string parameterName)
        {
            var eku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
            if (eku is null)
                return;

            if (!eku.EnhancedKeyUsages.Cast<Oid>().Any(oid =>
                    string.Equals(oid.Value, requiredEku, StringComparison.Ordinal) ||
                    string.Equals(oid.Value, AnyExtendedKeyUsageOid, StringComparison.Ordinal)))
                throw new ArgumentException("The TLS certificate does not permit the required authentication role.",
                    parameterName);
        }
    }

    /// <summary>Actor-system setup that enables TLS for all Artery TCP channels.</summary>
    public sealed class ArteryTlsSetup : Setup
    {
        /// <summary>Creates a setup using one immutable Artery TLS profile.</summary>
        public ArteryTlsSetup(ArteryTlsSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>Gets the TLS profile used by all Artery TCP channels.</summary>
        public ArteryTlsSettings Settings { get; }
    }
}
