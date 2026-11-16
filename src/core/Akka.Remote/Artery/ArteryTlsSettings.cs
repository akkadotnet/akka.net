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
using System.Threading.Tasks;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
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
        private readonly ArteryCertificateSource? _certificateSource;

        private ArteryTlsSettings(TlsClientSettings clientSettings, TlsServerSettings serverSettings,
            ArteryCertificateSource? certificateSource = null)
        {
            _clientSettings = clientSettings ?? throw new ArgumentNullException(nameof(clientSettings));
            _serverSettings = serverSettings ?? throw new ArgumentNullException(nameof(serverSettings));
            _certificateSource = certificateSource;
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

        /// <summary>
        /// Creates a mutual TLS profile whose certificate comes from a source, using one peer policy in both
        /// directions. Each new connection asks the source for the current certificate, so a renewed certificate is
        /// used with no restart, while open connections keep theirs.
        /// </summary>
        /// <remarks>
        /// The node presents one certificate in both directions, so the source picks only a certificate that permits
        /// both client and server authentication. The node does not start unless the source has a usable
        /// certificate. A folder or store source is also checked once a minute.
        /// </remarks>
        /// <param name="certificateSource">The source of the certificate presented in both directions.</param>
        /// <param name="peerPolicy">The policy used to validate remote Artery nodes in both directions.</param>
        public static ArteryTlsSettings Mutual(TlsCertificateSource certificateSource, TlsPeerPolicy peerPolicy)
        {
            ArgumentNullException.ThrowIfNull(peerPolicy);
            return Mutual(certificateSource, peerPolicy, peerPolicy);
        }

        /// <summary>
        /// Creates a mutual TLS profile whose certificate comes from a source, with independent inbound and outbound
        /// peer policies. Each new connection asks the source for the current certificate, so a renewed certificate
        /// is used with no restart, while open connections keep theirs.
        /// </summary>
        /// <remarks>
        /// The node presents one certificate in both directions, so the source picks only a certificate that permits
        /// both client and server authentication. The node does not start unless the source has a usable
        /// certificate. A folder or store source is also checked once a minute.
        /// </remarks>
        /// <param name="certificateSource">The source of the certificate presented in both directions.</param>
        /// <param name="inboundClientPolicy">The policy used to validate connecting Artery nodes.</param>
        /// <param name="outboundServerPolicy">The policy used to validate remote Artery nodes.</param>
        public static ArteryTlsSettings Mutual(TlsCertificateSource certificateSource,
            TlsPeerPolicy inboundClientPolicy, TlsPeerPolicy outboundServerPolicy)
        {
            ArgumentNullException.ThrowIfNull(certificateSource);
            ArgumentNullException.ThrowIfNull(inboundClientPolicy);
            ArgumentNullException.ThrowIfNull(outboundServerPolicy);

            var source = new ArteryCertificateSource(certificateSource, TlsCertificateRole.ClientAndServer);
            return new ArteryTlsSettings(
                TlsClientSettings.Mutual(source.Present, outboundServerPolicy),
                TlsServerSettings.FromProvider(source.Present, inboundClientPolicy),
                source);
        }

        /// <summary>
        /// Creates a TLS profile whose server certificate comes from a source; it accepts anonymous inbound clients
        /// and validates remote servers. Each new inbound connection asks the source for the current certificate, so
        /// a renewed certificate is used with no restart, while open connections keep theirs.
        /// </summary>
        /// <remarks>
        /// The node presents a certificate only as a server, so the source picks a certificate that permits server
        /// authentication. The node does not start unless the source has a usable certificate. A folder or store
        /// source is also checked once a minute.
        /// </remarks>
        /// <param name="certificateSource">The source of the certificate presented to connecting nodes.</param>
        /// <param name="outboundServerPolicy">The policy used to validate remote Artery nodes.</param>
        public static ArteryTlsSettings ServerOnly(TlsCertificateSource certificateSource,
            TlsPeerPolicy outboundServerPolicy)
        {
            ArgumentNullException.ThrowIfNull(certificateSource);
            ArgumentNullException.ThrowIfNull(outboundServerPolicy);

            var source = new ArteryCertificateSource(certificateSource, TlsCertificateRole.Server);
            return new ArteryTlsSettings(
                TlsClientSettings.ServerOnly(outboundServerPolicy),
                TlsServerSettings.FromProvider(source.Present, clientValidation: null),
                source);
        }

        /// <summary>Returns a copy with a different handshake timeout.</summary>
        public ArteryTlsSettings WithHandshakeTimeout(TimeSpan value) =>
            new(_clientSettings.WithHandshakeTimeout(value), _serverSettings.WithHandshakeTimeout(value),
                _certificateSource);

        /// <summary>Returns a copy with different TLS protocols.</summary>
        public ArteryTlsSettings WithProtocols(SslProtocols value) =>
            new(_clientSettings.WithProtocols(value), _serverSettings.WithProtocols(value), _certificateSource);

        /// <summary>
        /// INTERNAL API. A copy for one Artery transport. It keeps the same <see cref="TlsCertificateSource"/>, which
        /// holds the picks and look-ups, and gets its own error limiter, so two nodes that share one settings object
        /// do not hide each other's errors. A fixed-certificate profile has no limiter and returns itself.
        /// </summary>
        internal ArteryTlsSettings ForTransport()
        {
            if (_certificateSource is null)
                return this;

            var source = new ArteryCertificateSource(_certificateSource.Source, _certificateSource.Role);
            // Only a mutual profile presents a certificate when it opens a connection.
            var client = _certificateSource.Role == TlsCertificateRole.ClientAndServer
                ? _clientSettings.WithCertificateProvider(source.Present)
                : _clientSettings;
            return new ArteryTlsSettings(client, _serverSettings.WithCertificateProvider(source.Present), source);
        }

        internal TlsClientSettings ClientSettings => _clientSettings;

        internal TlsServerSettings ServerSettings => _serverSettings;

        // The source this node asks, or null for a fixed certificate.
        internal ArteryCertificateSource? CertificateSource => _certificateSource;

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

    /// <summary>
    /// INTERNAL API. The certificate source of one Artery TLS profile, with the role the node presents its certificate
    /// in: client and server under mutual TLS, server otherwise, as the DotNetty transport does. The transport asks it
    /// each time it builds a connection, once at startup, and once a minute for a folder or a store. It logs and fails
    /// the way the DotNetty transport does, with the same text.
    /// </summary>
    internal sealed class ArteryCertificateSource
    {
        // When the source has nothing usable, every connection fails the same way. Say so at most once a minute for
        // each direction and cause, so one cause never hides another.
        private readonly TlsLogLimiter _certificateErrors = new TlsLogLimiter(TimeSpan.FromMinutes(1));

        public ArteryCertificateSource(TlsCertificateSource source, TlsCertificateRole role)
        {
            Source = source;
            Role = role;
        }

        public TlsCertificateSource Source { get; }

        public TlsCertificateRole Role { get; }

        // True when the transport should ask the source by itself, once a minute: a folder or a store source, which
        // looks in the background. False for a callback.
        public bool NeedsPolling => Source.LooksUp;

        // Asked once per connection, when Akka.IO builds the connection's TLS options. Never waits for a look-up. A
        // throw fails this connection only; the node stays up.
        public X509Certificate2 Present(TlsConnectionDirection direction, string remotePeer, ILoggingAdapter log)
        {
            try
            {
                return Source.GetCertificate(Role, log);
            }
            catch (Exception ex)
            {
                // Akka.IO fails this connection with the exception, so say why here, as DotNetty does.
                if (direction == TlsConnectionDirection.Outbound)
                {
                    if (ShouldLogCertificateError("outbound", ex, log))
                        log.Error(ex, "Cannot build a TLS connection to [{0}]: {1}", remotePeer, ex.Message);
                }
                else if (ShouldLogCertificateError("inbound", ex, log))
                {
                    log.Error(ex, "Cannot build a TLS connection for the incoming connection from [{0}]: {1}",
                        remotePeer, ex.Message);
                }

                // Akka.IO fails the connection with this exception. Say it was reported, so Akka.IO does not log it
                // again at Warning for every connection.
                TlsCertificateProviderFailure.MarkReported(ex);
                throw;
            }
        }

        // The startup check: waits for the source's first look-up, until 30 seconds after it started, without
        // blocking a thread. Whatever the source throws (it may be a callback that throws anything) stops the node.
        public async Task CheckAtStartupAsync(ILoggingAdapter log)
        {
            try
            {
                await Source.GetCertificateAsync(Role, log).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is ConfigurationException) && !TlsCertificateSource.IsFatal(ex))
            {
                throw new ConfigurationException(ex.Message, ex);
            }
        }

        // What the transport's timer runs: starts a look-up of the source when one is due and logs what it found.
        public void Poll(ILoggingAdapter log) => Source.Poll(Role, log);

        // True when this direction and cause was not logged in the last minute. Past 100 causes in a minute, a new
        // one is not logged; one line says so instead, at most once a minute. The cause is the message when the
        // certificate source wrote it (or the kind of failure, where the message holds something that differs
        // between calls), and otherwise the exception type: a callback's message may differ on every call.
        private bool ShouldLogCertificateError(string direction, Exception exception, ILoggingAdapter log)
        {
            var cause = TlsCertificateSource.OwnFailureKey(exception) is { } key
                ? "message|" + key
                : "type|" + exception.GetType().FullName;
            var verdict = _certificateErrors.Admit(direction + "|" + cause, DateTime.UtcNow);
            if (verdict == TlsLogLimiter.Verdict.LogSummary)
                log.Error("Some \"Cannot build a TLS connection\" errors were not logged: more than 100 different causes came within a minute.");
            return verdict == TlsLogLimiter.Verdict.Log;
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
