// -----------------------------------------------------------------------
// <copyright file="TlsSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Akka.Event;

namespace Akka.IO
{
    /// <summary>
    /// Validates a peer certificate during TLS authentication.
    /// </summary>
    /// <param name="certificate">The peer certificate, or <c>null</c> if no certificate was presented.</param>
    /// <param name="chain">The certificate chain built by the TLS implementation, when available.</param>
    /// <param name="remotePeer">The remote peer address or configured host name.</param>
    /// <param name="errors">The TLS policy errors reported for the peer certificate.</param>
    /// <param name="log">The logger associated with the connection.</param>
    /// <returns><c>true</c> to accept the peer certificate; otherwise, <c>false</c>.</returns>
    public delegate bool TlsCertificateValidationCallback(
        X509Certificate2? certificate,
        X509Chain? chain,
        string remotePeer,
        SslPolicyErrors errors,
        ILoggingAdapter log);

    /// <summary>
    /// Provides certificate and validation settings for an outgoing TLS connection.
    /// </summary>
    public sealed record TlsClientSettings
    {
        /// <summary>
        /// Initializes client TLS settings with an optional certificate for mutual TLS.
        /// </summary>
        /// <param name="certificate">The caller-owned client certificate.</param>
        public TlsClientSettings(X509Certificate2? certificate = null)
        {
            Certificate = certificate;
        }

        /// <summary>
        /// Gets the caller-owned certificate presented to a server when configured.
        /// </summary>
        public X509Certificate2? Certificate { get; }

        /// <summary>
        /// Gets or initializes whether chain errors are ignored. Hostname validation remains independently controlled.
        /// </summary>
        public bool SuppressValidation { get; init; }

        /// <summary>
        /// Gets or initializes whether a client certificate is required for mutual TLS. Defaults to <c>true</c>.
        /// </summary>
        public bool RequireMutualAuthentication { get; init; } = true;

        /// <summary>
        /// Gets or initializes whether the server certificate must match the configured target host.
        /// </summary>
        public bool ValidateCertificateHostname { get; init; }

        /// <summary>
        /// Gets or initializes a custom server certificate validator. When supplied, it replaces the built-in policy.
        /// </summary>
        public TlsCertificateValidationCallback? CustomValidator { get; init; }

        /// <summary>
        /// Gets or initializes the maximum time allowed for the TLS handshake. Defaults to ten seconds.
        /// </summary>
        public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or initializes the TLS protocols available to the connection. Defaults to runtime selection.
        /// </summary>
        public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;

        /// <summary>
        /// Gets or initializes the TLS target host used for server-name indication and certificate name checks.
        /// </summary>
        public string? TargetHost { get; init; }

        /// <summary>
        /// Creates fresh authentication options for an outgoing TLS connection.
        /// </summary>
        /// <param name="targetHost">The server host name used for SNI and certificate name validation.</param>
        /// <param name="remotePeer">The remote peer address used in callback context.</param>
        /// <param name="log">The logger associated with the connection.</param>
        /// <returns>A new options instance for one TLS handshake.</returns>
        public SslClientAuthenticationOptions CreateAuthenticationOptions(string targetHost, string remotePeer, ILoggingAdapter log)
        {
            ArgumentNullException.ThrowIfNull(targetHost);
            ArgumentNullException.ThrowIfNull(remotePeer);
            ArgumentNullException.ThrowIfNull(log);
            TlsSettingsValidation.ValidateTimeout(HandshakeTimeout);
            if (Certificate is not null)
                TlsSettingsValidation.ValidateCertificate(Certificate, nameof(Certificate));
            if (RequireMutualAuthentication && Certificate is null)
                throw new InvalidOperationException("A client certificate is required when mutual TLS is enabled.");

            return new SslClientAuthenticationOptions
            {
                TargetHost = TargetHost ?? targetHost,
                EnabledSslProtocols = EnabledSslProtocols,
                ClientCertificates = RequireMutualAuthentication && Certificate is not null
                    ? new X509CertificateCollection { Certificate }
                    : null,
                LocalCertificateSelectionCallback = RequireMutualAuthentication && Certificate is not null
                    ? (_, _, _, _, _) => Certificate
                    : null,
                RemoteCertificateValidationCallback = (_, certificate, chain, errors) => TlsSettingsValidation.ValidatePeer(
                    certificate, chain, remotePeer, errors, log, CustomValidator,
                    SuppressValidation, ValidateCertificateHostname, requireCertificate: true)
            };
        }
    }

    /// <summary>
    /// Provides certificate and validation settings for an incoming TLS connection.
    /// </summary>
    public sealed record TlsServerSettings
    {
        /// <summary>
        /// Initializes server TLS settings with the certificate presented to clients.
        /// </summary>
        /// <param name="certificate">The caller-owned server certificate, including its private key.</param>
        public TlsServerSettings(X509Certificate2 certificate)
        {
            Certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        }

        /// <summary>
        /// Gets the caller-owned certificate presented to clients.
        /// </summary>
        public X509Certificate2 Certificate { get; }

        /// <summary>
        /// Gets or initializes whether chain errors are ignored. Defaults to <c>false</c>.
        /// </summary>
        public bool SuppressValidation { get; init; }

        /// <summary>
        /// Gets or initializes whether clients must present a certificate. Defaults to <c>true</c>.
        /// </summary>
        public bool RequireMutualAuthentication { get; init; } = true;

        /// <summary>
        /// Gets or initializes whether a runtime-reported peer name mismatch is rejected. Incoming connections do
        /// not infer a client host name from the remote endpoint. Defaults to <c>false</c>.
        /// </summary>
        public bool ValidateCertificateHostname { get; init; }

        /// <summary>
        /// Gets or initializes a custom client certificate validator. When supplied, it replaces the built-in policy.
        /// </summary>
        public TlsCertificateValidationCallback? CustomValidator { get; init; }

        /// <summary>
        /// Gets or initializes the maximum time allowed for the TLS handshake. Defaults to ten seconds.
        /// </summary>
        public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or initializes the TLS protocols available to the connection. Defaults to runtime selection.
        /// </summary>
        public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;

        /// <summary>
        /// Creates fresh authentication options for an incoming TLS connection.
        /// </summary>
        /// <param name="remotePeer">The remote peer address used in callback context.</param>
        /// <param name="log">The logger associated with the connection.</param>
        /// <returns>A new options instance for one TLS handshake.</returns>
        public SslServerAuthenticationOptions CreateAuthenticationOptions(string remotePeer, ILoggingAdapter log)
        {
            ArgumentNullException.ThrowIfNull(remotePeer);
            ArgumentNullException.ThrowIfNull(log);
            TlsSettingsValidation.ValidateTimeout(HandshakeTimeout);
            TlsSettingsValidation.ValidateCertificate(Certificate, nameof(Certificate));

            return new SslServerAuthenticationOptions
            {
                ServerCertificate = Certificate,
                ClientCertificateRequired = RequireMutualAuthentication,
                EnabledSslProtocols = EnabledSslProtocols,
                RemoteCertificateValidationCallback = (_, certificate, chain, errors) => TlsSettingsValidation.ValidatePeer(
                    certificate, chain, remotePeer, errors, log, CustomValidator,
                    SuppressValidation, ValidateCertificateHostname, RequireMutualAuthentication)
            };
        }
    }

    internal static class TlsSettingsValidation
    {
        internal static bool ValidatePeer(
            X509Certificate? certificate,
            X509Chain? chain,
            string remotePeer,
            SslPolicyErrors errors,
            ILoggingAdapter log,
            TlsCertificateValidationCallback? customValidator,
            bool suppressValidation,
            bool validateHostname,
            bool requireCertificate)
        {
            if (certificate is null)
                return !requireCertificate;

            var suppliedCertificate = certificate as X509Certificate2;
            using var transientCertificate = suppliedCertificate is null
                ? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData())
                : null;
            var peerCertificate = suppliedCertificate ?? transientCertificate!;
            if (customValidator is not null)
                return customValidator(peerCertificate, chain, remotePeer, errors, log);

            var allowedErrors = SslPolicyErrors.None;
            if (!validateHostname)
                allowedErrors |= SslPolicyErrors.RemoteCertificateNameMismatch;
            if (suppressValidation)
                allowedErrors |= SslPolicyErrors.RemoteCertificateChainErrors;
            return (errors & ~allowedErrors) == SslPolicyErrors.None;
        }

        internal static void ValidateTimeout(TimeSpan timeout)
        {
            var maximumTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
            if (timeout <= TimeSpan.Zero || timeout > maximumTimeout)
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout,
                    $"The TLS handshake timeout must be positive and no greater than {maximumTimeout}.");
        }

        internal static void ValidateCertificate(X509Certificate2 certificate, string parameterName)
        {
            if (!certificate.HasPrivateKey)
                throw new ArgumentException("The TLS certificate must include an accessible private key.", parameterName);

            using var rsa = certificate.GetRSAPrivateKey();
            using var ecdsa = certificate.GetECDsaPrivateKey();
            if (rsa is null && ecdsa is null)
                throw new ArgumentException("The TLS certificate private key must be accessible as RSA or ECDSA.", parameterName);
        }
    }

}
