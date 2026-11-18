// -----------------------------------------------------------------------
// <copyright file="TlsSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Akka.Event;

namespace Akka.IO
{
    /// <summary>
    /// Validates a peer certificate during TLS authentication.
    /// </summary>
    /// <remarks>
    /// Callbacks run synchronously and may run concurrently or reentrantly across connections. Any state captured
    /// by a callback remains caller-owned and must support that use.
    /// </remarks>
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
    /// Immutable authentication settings for an outgoing TLS connection.
    /// </summary>
    public sealed class TlsClientSettings
    {
        // Asked for the certificate each time a connection is built, in place of a fixed one. Null for a fixed certificate.
        private readonly TlsCertificateProvider? _certificateProvider;

        private TlsClientSettings(X509Certificate2? certificate, TlsPeerPolicy serverValidation,
            TimeSpan handshakeTimeout, SslProtocols protocols, string? targetHost,
            TlsCertificateProvider? certificateProvider = null)
        {
            if (certificate is not null)
                TlsSettingsValidation.ValidateCertificate(certificate, nameof(certificate));
            Certificate = certificate;
            _certificateProvider = certificateProvider;
            ServerValidation = serverValidation ?? throw new ArgumentNullException(nameof(serverValidation));
            TlsSettingsValidation.ValidateTimeout(handshakeTimeout);
            HandshakeTimeout = handshakeTimeout;
            Protocols = protocols;
            TargetHost = ValidateTargetHost(targetHost);
        }

        /// <summary>Gets the caller-owned certificate presented to the server for mutual TLS.</summary>
        public X509Certificate2? Certificate { get; }

        /// <summary>Gets the policy used to validate the remote server.</summary>
        public TlsPeerPolicy ServerValidation { get; }

        /// <summary>Gets the maximum time allowed for the TLS handshake.</summary>
        public TimeSpan HandshakeTimeout { get; }

        /// <summary>Gets the TLS protocols available to the connection.</summary>
        public SslProtocols Protocols { get; }

        /// <summary>Gets the optional target host used for SNI and certificate name validation.</summary>
        public string? TargetHost { get; }

        /// <summary>Creates outgoing settings without a client certificate.</summary>
        public static TlsClientSettings ServerOnly(TlsPeerPolicy serverValidation) =>
            new(null, serverValidation, TimeSpan.FromSeconds(10), SslProtocols.None, null);

        /// <summary>Creates outgoing settings with a client certificate for mutual TLS.</summary>
        public static TlsClientSettings Mutual(X509Certificate2 clientCertificate, TlsPeerPolicy serverValidation)
        {
            ArgumentNullException.ThrowIfNull(clientCertificate);
            return new(clientCertificate, serverValidation, TimeSpan.FromSeconds(10), SslProtocols.None, null);
        }

        /// <summary>
        /// Internal: outgoing settings for mutual TLS whose client certificate comes from
        /// <paramref name="certificateProvider"/>, which is asked each time a connection is built.
        /// <see cref="Certificate"/> is null for these settings.
        /// </summary>
        internal static TlsClientSettings Mutual(TlsCertificateProvider certificateProvider,
            TlsPeerPolicy serverValidation)
        {
            ArgumentNullException.ThrowIfNull(certificateProvider);
            return new(null, serverValidation, TimeSpan.FromSeconds(10), SslProtocols.None, null, certificateProvider);
        }

        /// <summary>Returns a copy with a different handshake timeout.</summary>
        public TlsClientSettings WithHandshakeTimeout(TimeSpan value) =>
            new(Certificate, ServerValidation, value, Protocols, TargetHost, _certificateProvider);

        /// <summary>Returns a copy with different TLS protocols.</summary>
        public TlsClientSettings WithProtocols(SslProtocols value) =>
            new(Certificate, ServerValidation, HandshakeTimeout, value, TargetHost, _certificateProvider);

        /// <summary>Returns a copy with a different SNI and certificate-validation target.</summary>
        public TlsClientSettings WithTargetHost(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return new(Certificate, ServerValidation, HandshakeTimeout, Protocols, value, _certificateProvider);
        }

        /// <summary>Internal: returns a copy whose certificate comes from <paramref name="certificateProvider"/>.</summary>
        internal TlsClientSettings WithCertificateProvider(TlsCertificateProvider certificateProvider)
        {
            ArgumentNullException.ThrowIfNull(certificateProvider);
            return new(Certificate, ServerValidation, HandshakeTimeout, Protocols, TargetHost, certificateProvider);
        }

        internal SslClientAuthenticationOptions CreateAuthenticationOptions(string targetHost, string remotePeer,
            ILoggingAdapter log)
        {
            ArgumentNullException.ThrowIfNull(targetHost);
            ArgumentNullException.ThrowIfNull(remotePeer);
            ArgumentNullException.ThrowIfNull(log);
            TlsSettingsValidation.ValidateTimeout(HandshakeTimeout);
            var presented = Certificate;
            if (_certificateProvider is not null)
                presented = _certificateProvider(TlsConnectionDirection.Outbound, remotePeer, log); // once per connection
            else if (presented is not null)
                TlsSettingsValidation.ValidateCertificate(presented, nameof(Certificate));

            return new SslClientAuthenticationOptions
            {
                TargetHost = TargetHost ?? targetHost,
                EnabledSslProtocols = Protocols,
                ClientCertificates = presented is null ? null : new X509CertificateCollection { presented },
                LocalCertificateSelectionCallback = presented is null ? null : (_, _, _, _, _) => presented,
                RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
                    TlsSettingsValidation.ValidatePeer(ServerValidation, certificate, chain, remotePeer, errors, log)
            };
        }

        private static string? ValidateTargetHost(string? value)
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("The TLS target host cannot be empty or whitespace.", nameof(value));
            return value;
        }
    }

    /// <summary>
    /// Immutable authentication settings for an incoming TLS connection.
    /// </summary>
    public sealed class TlsServerSettings
    {
        // A fixed certificate, or null when the provider is asked for one each time a connection is built.
        private readonly X509Certificate2? _certificate;
        private readonly TlsCertificateProvider? _certificateProvider;

        private TlsServerSettings(X509Certificate2? certificate, TlsPeerPolicy? clientValidation,
            TimeSpan handshakeTimeout, SslProtocols protocols, TlsCertificateProvider? certificateProvider = null)
        {
            if (certificateProvider is null)
            {
                ArgumentNullException.ThrowIfNull(certificate);
                TlsSettingsValidation.ValidateCertificate(certificate, nameof(certificate));
            }

            _certificate = certificate;
            _certificateProvider = certificateProvider;
            ClientValidation = clientValidation;
            TlsSettingsValidation.ValidateTimeout(handshakeTimeout);
            HandshakeTimeout = handshakeTimeout;
            Protocols = protocols;
        }

        /// <summary>Gets the caller-owned certificate presented to clients.</summary>
        public X509Certificate2 Certificate => _certificate ?? throw new InvalidOperationException(
            "These TLS settings ask for a certificate each time a connection is built, so they hold no fixed certificate.");

        /// <summary>Gets the policy used to validate client certificates, or <c>null</c> for anonymous clients.</summary>
        public TlsPeerPolicy? ClientValidation { get; }

        /// <summary>Gets the maximum time allowed for the TLS handshake.</summary>
        public TimeSpan HandshakeTimeout { get; }

        /// <summary>Gets the TLS protocols available to the connection.</summary>
        public SslProtocols Protocols { get; }

        /// <summary>Creates incoming settings that permit clients without certificates.</summary>
        public static TlsServerSettings ServerOnly(X509Certificate2 serverCertificate) =>
            new(serverCertificate, null, TimeSpan.FromSeconds(10), SslProtocols.None);

        /// <summary>Creates incoming settings that require and validate client certificates.</summary>
        public static TlsServerSettings Mutual(X509Certificate2 serverCertificate, TlsPeerPolicy clientValidation) =>
            new(serverCertificate, clientValidation ?? throw new ArgumentNullException(nameof(clientValidation)),
                TimeSpan.FromSeconds(10), SslProtocols.None);

        /// <summary>
        /// Internal: incoming settings whose server certificate comes from <paramref name="certificateProvider"/>,
        /// which is asked each time a connection is built. A null <paramref name="clientValidation"/> permits clients
        /// without certificates. <see cref="Certificate"/> throws for these settings.
        /// </summary>
        internal static TlsServerSettings FromProvider(TlsCertificateProvider certificateProvider,
            TlsPeerPolicy? clientValidation)
        {
            ArgumentNullException.ThrowIfNull(certificateProvider);
            return new(null, clientValidation, TimeSpan.FromSeconds(10), SslProtocols.None, certificateProvider);
        }

        // True when the certificate comes from a provider, which is asked per connection, not from a fixed certificate.
        internal bool HasCertificateProvider => _certificateProvider is not null;

        /// <summary>Returns a copy with a different handshake timeout.</summary>
        public TlsServerSettings WithHandshakeTimeout(TimeSpan value) =>
            new(_certificate, ClientValidation, value, Protocols, _certificateProvider);

        /// <summary>Returns a copy with different TLS protocols.</summary>
        public TlsServerSettings WithProtocols(SslProtocols value) =>
            new(_certificate, ClientValidation, HandshakeTimeout, value, _certificateProvider);

        /// <summary>Internal: returns a copy whose certificate comes from <paramref name="certificateProvider"/>.</summary>
        internal TlsServerSettings WithCertificateProvider(TlsCertificateProvider certificateProvider)
        {
            ArgumentNullException.ThrowIfNull(certificateProvider);
            return new(null, ClientValidation, HandshakeTimeout, Protocols, certificateProvider);
        }

        internal SslServerAuthenticationOptions CreateAuthenticationOptions(string remotePeer, ILoggingAdapter log)
        {
            ArgumentNullException.ThrowIfNull(remotePeer);
            ArgumentNullException.ThrowIfNull(log);
            TlsSettingsValidation.ValidateTimeout(HandshakeTimeout);
            X509Certificate2 presented;
            if (_certificateProvider is not null)
            {
                presented = _certificateProvider(TlsConnectionDirection.Inbound, remotePeer, log); // once per connection
            }
            else
            {
                presented = _certificate!;
                TlsSettingsValidation.ValidateCertificate(presented, nameof(Certificate));
            }

            return new SslServerAuthenticationOptions
            {
                ServerCertificate = presented,
                ClientCertificateRequired = ClientValidation is not null,
                EnabledSslProtocols = Protocols,
                RemoteCertificateValidationCallback = ClientValidation is null ? null :
                    (_, certificate, chain, errors) =>
                        TlsSettingsValidation.ValidatePeer(ClientValidation, certificate, chain, remotePeer, errors, log)
            };
        }
    }

    /// <summary>Internal: which side of a TLS connection this node is on.</summary>
    internal enum TlsConnectionDirection
    {
        /// <summary>This node accepted the connection.</summary>
        Inbound,

        /// <summary>This node opened the connection.</summary>
        Outbound
    }

    /// <summary>
    /// Internal: returns the certificate this node presents on one new TLS connection. Called once per connection,
    /// when the connection's authentication options are built. Throws when there is no certificate to present, which
    /// fails that connection only.
    /// </summary>
    /// <param name="direction">Whether this node accepted or opened the connection.</param>
    /// <param name="remotePeer">The remote peer, for log lines.</param>
    /// <param name="log">The logger of the connection.</param>
    internal delegate X509Certificate2 TlsCertificateProvider(TlsConnectionDirection direction, string remotePeer,
        ILoggingAdapter log);

    /// <summary>
    /// Internal: what a certificate provider throws when it has no certificate to present and has already logged why.
    /// It wraps the provider's own failure, with the same message, so Akka.IO can log the failed connection at Debug
    /// instead of at Warning for every connection. The connection still fails, and the log line's cause chain still
    /// holds the original exception.
    /// </summary>
    internal sealed class TlsCertificateUnavailableException : Exception
    {
        /// <summary>Wraps <paramref name="reported"/>, which the provider already logged.</summary>
        internal TlsCertificateUnavailableException(Exception reported) : base(reported.Message, reported)
        {
        }

        /// <summary>True when <paramref name="exception"/>, or an exception it wraps, is one of these.</summary>
        internal static bool IsIn(Exception? exception)
        {
            for (var depth = 0; exception is not null && depth < 8; depth++)
            {
                if (exception is TlsCertificateUnavailableException)
                    return true;
                exception = exception.InnerException;
            }

            return false;
        }
    }

    internal static class TlsSettingsValidation
    {
        internal static bool ValidatePeer(TlsPeerPolicy policy, X509Certificate? certificate,
            X509Chain? chain, string remotePeer, SslPolicyErrors errors, ILoggingAdapter log)
        {
            if (certificate is null)
                return false;

            var suppliedCertificate = certificate as X509Certificate2;
            using var transientCertificate = suppliedCertificate is null
                ? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData())
                : null;
            return policy.ValidatePeer(suppliedCertificate ?? transientCertificate, chain, remotePeer, errors, log);
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
