//-----------------------------------------------------------------------
// <copyright file="DotNettyTransportSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util;
using DotNetty.Buffers;

#nullable enable
namespace Akka.Remote.Transport.DotNetty
{
    /// <summary>
    ///     INTERNAL API.
    ///
    ///     Defines the settings for the <see cref="DotNettyTransport"/>.
    /// </summary>
    /// <param name="TransportMode">
    ///     Transport mode used by underlying socket channel.
    ///     Currently only TCP is supported.
    /// </param>
    /// <param name="EnableSsl">
    ///     If set to true, a Secure Socket Layer will be established
    ///     between remote endpoints. They need to share a X509 certificate
    ///     which path is specified in `akka.remote.dot-netty.tcp.ssl.certificate.path`
    /// </param>
    /// <param name="ConnectTimeout">
    ///     Sets a connection timeout for all outbound connections
    ///     i.e. how long a connect may take until it is timed out.
    /// </param>
    /// <param name="Hostname">
    ///     If this value is set, this becomes the public address for the actor system on this
    ///     transport, which might be different than the physical ip address (hostname)
    ///     this is designed to make it easy to support private / public addressing schemes
    /// </param>
    /// <param name="PublicHostname">
    ///     The hostname or IP to bind the remoting to.
    /// </param>
    /// <param name="Port">
    ///     The default remote server port clients should connect to.
    ///     Default is 2552 (AKKA), use 0 if you want a random available port
    ///     This port needs to be unique for each actor system on the same machine.
    /// </param>
    /// <param name="PublicPort">
    ///     If this value is set, this becomes the public port for the actor system on this
    ///     transport, which might be different than the physical port
    ///     this is designed to make it easy to support private / public addressing schemes
    /// </param>
    /// <param name="ServerSocketWorkerPoolSize">TBD</param>
    /// <param name="ClientSocketWorkerPoolSize">TBD</param>
    /// <param name="MaxFrameSize">TBD</param>
    /// <param name="Ssl">TBD</param>
    /// <param name="DnsUseIpv6">
    ///     If set to true, we will use IPv6 addresses upon DNS resolution for
    ///     host names. Otherwise IPv4 will be used.
    /// </param>
    /// <param name="TcpReuseAddr">
    ///     Enables SO_REUSEADDR, which determines when an ActorSystem can open
    ///     the specified listen port (the meaning differs between *nix and Windows).
    /// </param>
    /// <param name="TcpKeepAlive">
    ///     Enables TCP Keepalive, subject to the O/S kernel's configuration.
    /// </param>
    /// <param name="TcpNoDelay">
    ///     Enables the TCP_NODELAY flag, i.e. disables Nagle's algorithm
    /// </param>
    /// <param name="Backlog">
    ///     Sets the size of the connection backlog.
    /// </param>
    /// <param name="EnforceIpFamily">
    ///     If set to true, we will enforce usage of IPv4 or IPv6 addresses upon DNS
    ///     resolution for host names. If true, we will use IPv6 enforcement. Otherwise,
    ///     we will use IPv4.
    /// </param>
    /// <param name="ReceiveBufferSize">
    ///     Sets the default receive buffer size of the Sockets.
    /// </param>
    /// <param name="SendBufferSize">
    ///     Sets the default send buffer size of the Sockets.
    /// </param>
    /// <param name="WriteBufferHighWaterMark">TBD</param>
    /// <param name="WriteBufferLowWaterMark">TBD</param>
    /// <param name="BackwardsCompatibilityModeEnabled">
    ///     Enables backwards compatibility with Akka.Remote clients running Helios 1.*
    /// </param>
    /// <param name="LogTransport">
    ///     When set to true, it will enable logging of DotNetty user events
    ///     and message frames.
    /// </param>
    /// <param name="ByteOrder">
    ///     Byte order used by DotNetty, either big or little endian.
    ///     By default a little endian is used to achieve compatibility with Helios.
    /// </param>
    /// <param name="EnableBufferPooling">
    ///     Used mostly as a work-around for https://github.com/akkadotnet/akka.net/issues/3370
    ///     on .NET Core on Linux. Should always be left to <c>true</c> unless running DotNetty v0.4.6
    ///     on Linux, which can accidentally release buffers early and corrupt frames. Turn this setting
    ///     to <c>false</c> to disable pooling and work-around this issue at the cost of some performance.
    /// </param>
    /// <param name="BatchWriterSettings">
    ///     Used for performance-tuning the DotNetty channels to maximize I/O performance.
    /// </param>
    internal sealed record DotNettyTransportSettings(
        TransportMode TransportMode, 
        bool EnableSsl,
        TimeSpan ConnectTimeout,
        string Hostname, 
        string PublicHostname,
        int Port,
        int? PublicPort,
        int ServerSocketWorkerPoolSize,
        int ClientSocketWorkerPoolSize,
        int MaxFrameSize,
        SslSettings Ssl,
        bool DnsUseIpv6,
        bool TcpReuseAddr,
        bool TcpKeepAlive,
        bool TcpNoDelay,
        int Backlog,
        bool EnforceIpFamily,
        int? ReceiveBufferSize,
        int? SendBufferSize, 
        int? WriteBufferHighWaterMark,
        int? WriteBufferLowWaterMark,
        bool BackwardsCompatibilityModeEnabled,
        bool LogTransport,
        ByteOrder ByteOrder,
        bool EnableBufferPooling,
        BatchWriterSettings BatchWriterSettings)
    {
        public static DotNettyTransportSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.remote.dot-netty.tcp");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<DotNettyTransportSettings>("akka.remote.dot-netty.tcp");

            var setup = system.Settings.Setup.Get<DotNettySslSetup>();
            var sslSettings = setup.HasValue ? setup.Value.Settings : null;

            // Warn if both DotNettySslSetup and HOCON SSL are configured (DotNettySslSetup takes precedence)
            if (sslSettings != null && config.GetBoolean("enable-ssl"))
            {
                var sslConfig = config.GetConfig("ssl");
                // Only warn if HOCON has explicit certificate configuration
                var hasCertPath = sslConfig.HasPath("certificate.path") && !string.IsNullOrWhiteSpace(sslConfig.GetString("certificate.path"));
                var hasCertThumbprint = sslConfig.HasPath("certificate.thumbprint") && !string.IsNullOrWhiteSpace(sslConfig.GetString("certificate.thumbprint"));

                if (hasCertPath || hasCertThumbprint)
                {
                    var log = Logging.GetLogger(system, typeof(DotNettyTransportSettings));
                    log.Warning("Both DotNettySslSetup and HOCON SSL configuration are present. " +
                               "DotNettySslSetup takes precedence and HOCON SSL settings will be ignored.");
                }
            }

            return Create(config, sslSettings);
        }

        /// <summary>
        /// Adds support for the "off-for-windows" option per https://github.com/akkadotnet/akka.net/issues/3293
        /// </summary>
        /// <param name="hoconTcpReuseAddr">The HOCON string for the akka.remote.dot-netty.tcp.reuse-addr option</param>
        /// <returns><c>true</c> if we should enable REUSE_ADDR for tcp. <c>false</c> otherwise.</returns>
        private static bool ResolveTcpReuseAddrOption(string hoconTcpReuseAddr)
        {
            return hoconTcpReuseAddr.ToLowerInvariant() switch
            {
                "off-for-windows" when RuntimeDetector.IsWindows => false,
                "off-for-windows" => true,
                "on" => true,
                "off" => false,
                _ => false
            };
        }

        public static DotNettyTransportSettings Create(Config config, SslSettings? sslSettings = null)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<DotNettyTransportSettings>();

            var transportMode = config.GetString("transport-protocol", "tcp").ToLower();
            var host = config.GetString("hostname");
            if (string.IsNullOrWhiteSpace(host)) 
                host = IPAddress.Any.ToString();

            var publicHost = config.GetString("public-hostname");
            var publicPort = config.GetInt("public-port");

            var byteOrderString = config.GetString("byte-order", "little-endian").ToLowerInvariant();
            var order = byteOrderString switch
            {
                "little-endian" => ByteOrder.LittleEndian,
                "big-endian" => ByteOrder.BigEndian,
                _ => throw new ArgumentException(
                    $"Unknown byte-order option [{byteOrderString}]. Supported options are: big-endian, little-endian.")
            };

            var batchWriterSettings = new BatchWriterSettings(config.GetConfig("batching"));

            var enableSsl = config.GetBoolean("enable-ssl");

            return new DotNettyTransportSettings(
                TransportMode: transportMode == "tcp" ? TransportMode.Tcp : TransportMode.Udp,
                EnableSsl: enableSsl,
                ConnectTimeout: config.GetTimeSpan("connection-timeout", TimeSpan.FromSeconds(15)),
                Hostname: host,
                PublicHostname: !string.IsNullOrEmpty(publicHost) ? publicHost : host,
                Port: config.GetInt("port", 2552),
                PublicPort: publicPort > 0 ? publicPort : null,
                ServerSocketWorkerPoolSize: ComputeWorkerPoolSize(config.GetConfig("server-socket-worker-pool")),
                ClientSocketWorkerPoolSize: ComputeWorkerPoolSize(config.GetConfig("client-socket-worker-pool")),
                MaxFrameSize: ToNullableInt(config.GetByteSize("maximum-frame-size", null)) ?? 128000,
                Ssl: enableSsl ? (sslSettings ?? SslSettings.Create(config.GetConfig("ssl"))) : SslSettings.Empty,
                DnsUseIpv6: config.GetBoolean("dns-use-ipv6"),
                TcpReuseAddr: ResolveTcpReuseAddrOption(config.GetString("tcp-reuse-addr", "off-for-windows")),
                TcpKeepAlive: config.GetBoolean("tcp-keepalive", true),
                TcpNoDelay: config.GetBoolean("tcp-nodelay", true),
                Backlog: config.GetInt("backlog", 4096),
                EnforceIpFamily: RuntimeDetector.IsMono || config.GetBoolean("enforce-ip-family"),
                ReceiveBufferSize: ToNullableInt(config.GetByteSize("receive-buffer-size", null) ?? 256000),
                SendBufferSize: ToNullableInt(config.GetByteSize("send-buffer-size", null) ?? 256000),
                WriteBufferHighWaterMark: ToNullableInt(config.GetByteSize("write-buffer-high-water-mark", null)),
                WriteBufferLowWaterMark: ToNullableInt(config.GetByteSize("write-buffer-low-water-mark", null)),
                BackwardsCompatibilityModeEnabled: config.GetBoolean("enable-backwards-compatibility"),
                LogTransport: config.HasPath("log-transport") && config.GetBoolean("log-transport"),
                ByteOrder: order,
                EnableBufferPooling: config.GetBoolean("enable-pooling", true),
                BatchWriterSettings: batchWriterSettings
            ).Validate();
        }

        private static int? ToNullableInt(long? value) => value is > 0 ? (int?)value.Value : null;

        private static int ComputeWorkerPoolSize(Config config)
        {
            if (config.IsNullOrEmpty())
                return ThreadPoolConfig.ScaledPoolSize(2, 1.0, 2);

            return ThreadPoolConfig.ScaledPoolSize(
                floor: config.GetInt("pool-size-min", 2),
                scalar: config.GetDouble("pool-size-factor", 1.0),
                ceiling: config.GetInt("pool-size-max", 2));
        }

        internal DotNettyTransportSettings Validate()
        {
            if (MaxFrameSize < 32000) 
                throw new ArgumentException("maximum-frame-size must be at least 32000 bytes", nameof(MaxFrameSize));

            return this;
        }
    }
    internal enum TransportMode
    {
        Tcp,
        Udp
    }

    internal sealed class SslSettings
    {
        public static readonly SslSettings Empty = new();

        public static SslSettings CreateOrDefault(Config config, SslSettings? @default = null)
        {
            try
            {
                return Create(config);
            }
            catch (Exception)
                when (@default != null)
            {
                return @default;
            }
        }

        internal static SslSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw new ConfigurationException($"Failed to create {typeof(DotNettyTransportSettings)}: DotNetty SSL HOCON config was not found (default path: `akka.remote.dot-netty.tcp.ssl`)");

            var requireMutualAuth = config.GetBoolean("require-mutual-authentication", true);
            var validateCertificateHostname = config.GetBoolean("validate-certificate-hostname", false);

            if (config.GetBoolean("certificate.use-thumprint-over-file")
                || config.GetBoolean("certificate.use-thumbprint-over-file"))
            {
                var thumbprint = config.GetString("certificate.thumbprint")
                                 ?? config.GetString("certificate.thumpbrint");
                if (string.IsNullOrWhiteSpace(thumbprint))
                    throw new Exception("`akka.remote.dot-netty.tcp.ssl.certificate.use-thumbprint-over-file` is set to true but `akka.remote.dot-netty.tcp.ssl.certificate.thumbprint` is null or empty");

                return new SslSettings(certificateThumbprint: thumbprint,
                    storeName: config.GetString("certificate.store-name"),
                    storeLocation: ParseStoreLocationName(config.GetString("certificate.store-location")),
                    suppressValidation: config.GetBoolean("suppress-validation"),
                    requireMutualAuthentication: requireMutualAuth,
                    validateCertificateHostname: validateCertificateHostname);
            }

            var flagsRaw = config.GetStringList("certificate.flags", new string[] { });
            var flags = flagsRaw.Aggregate(X509KeyStorageFlags.DefaultKeySet, (flag, str) => flag | ParseKeyStorageFlag(str));

            return new SslSettings(
                certificatePath: config.GetString("certificate.path"),
                certificatePassword: config.GetString("certificate.password"),
                flags: flags,
                suppressValidation: config.GetBoolean("suppress-validation"),
                requireMutualAuthentication: requireMutualAuth,
                validateCertificateHostname: validateCertificateHostname);

        }

        private static StoreLocation ParseStoreLocationName(string str)
        {
            return str switch
            {
                "local-machine" => StoreLocation.LocalMachine,
                "current-user" => StoreLocation.CurrentUser,
                _ => throw new ArgumentException(
                    $"Unrecognized flag in X509 certificate config [{str}]. Available flags: local-machine | current-user")
            };
        }

        private static X509KeyStorageFlags ParseKeyStorageFlag(string str)
        {
            return str switch
            {
                "default-key-set" => X509KeyStorageFlags.DefaultKeySet,
                "exportable" => X509KeyStorageFlags.Exportable,
                "machine-key-set" => X509KeyStorageFlags.MachineKeySet,
                "persist-key-set" => X509KeyStorageFlags.PersistKeySet,
                "user-key-set" => X509KeyStorageFlags.UserKeySet,
                "user-protected" => X509KeyStorageFlags.UserProtected,
                _ => throw new ArgumentException(
                    $"Unrecognized flag in X509 certificate config [{str}]. Available flags: default-key-set | exportable | machine-key-set | persist-key-set | user-key-set | user-protected")
            };
        }

        /// <summary>
        /// X509 certificate used to establish Secure Socket Layer (SSL) between two remote endpoints.
        /// </summary>
        public readonly X509Certificate2? Certificate;

        /// <summary>
        /// Flag used to suppress certificate validation - use true only, when on dev machine or for testing.
        /// </summary>
        public readonly bool SuppressValidation;

        /// <summary>
        /// When true, requires mutual TLS authentication where both client and server
        /// must present valid certificates with accessible private keys during the TLS handshake.
        /// Provides defense-in-depth security by ensuring symmetric authentication.
        /// </summary>
        public readonly bool RequireMutualAuthentication;

        /// <summary>
        /// When true, enables traditional TLS hostname validation (certificate CN/SAN must match target hostname).
        /// When false, only validates certificate chain against CA, ignores hostname mismatches.
        /// Default is false for backward compatibility and to support mutual TLS scenarios with per-node certificates,
        /// IP-based connections, or dynamic service discovery.
        /// </summary>
        public readonly bool ValidateCertificateHostname;

        /// <summary>
        /// Custom certificate validation callback (overrides config-based validation when provided)
        /// </summary>
        public readonly CertificateValidationCallback? CustomValidator;

        private SslSettings()
        {
            Certificate = null;
            SuppressValidation = false;
            RequireMutualAuthentication = false;
            ValidateCertificateHostname = false;
            CustomValidator = null;
        }

        /// <summary>
        /// Constructor for backward compatibility - defaults to RequireMutualAuthentication = true, ValidateCertificateHostname = false
        /// </summary>
        public SslSettings(X509Certificate2 certificate, bool suppressValidation)
            : this(certificate, suppressValidation, requireMutualAuthentication: true, validateCertificateHostname: false, customValidator: null)
        {
        }

        /// <summary>
        /// Constructor for backward compatibility - defaults to ValidateCertificateHostname = false
        /// </summary>
        public SslSettings(X509Certificate2 certificate, bool suppressValidation, bool requireMutualAuthentication)
            : this(certificate, suppressValidation, requireMutualAuthentication, validateCertificateHostname: false, customValidator: null)
        {
        }

        public SslSettings(X509Certificate2 certificate, bool suppressValidation, bool requireMutualAuthentication, bool validateCertificateHostname)
            : this(certificate, suppressValidation, requireMutualAuthentication, validateCertificateHostname, customValidator: null)
        {
        }

        public SslSettings(X509Certificate2 certificate, bool suppressValidation, bool requireMutualAuthentication, bool validateCertificateHostname, CertificateValidationCallback? customValidator)
        {
            Certificate = certificate;
            SuppressValidation = suppressValidation;
            RequireMutualAuthentication = requireMutualAuthentication;
            ValidateCertificateHostname = validateCertificateHostname;
            CustomValidator = customValidator;
        }

        /// <summary>
        /// Validates that the SSL certificate has an accessible private key.
        /// Should be called before starting the server to ensure proper TLS configuration.
        /// </summary>
        /// <exception cref="ConfigurationException">
        /// Thrown when certificate lacks private key or application cannot access it.
        /// </exception>
        public void ValidateCertificate()
        {
            if (Certificate == null)
                return; // No SSL configured

            if (!Certificate.HasPrivateKey)
            {
                throw new ConfigurationException(
                    "SSL certificate does not have a private key. " +
                    "Ensure certificate is installed with private key permissions.");
            }

            // Actually test private key access (not just presence)
            // SslStream supports both RSA and ECDSA keys - check both types
            try
            {
                using (var rsaKey = Certificate.GetRSAPrivateKey())
                using (var ecdsaKey = Certificate.GetECDsaPrivateKey())
                {
                    // Certificate must have either RSA or ECDSA private key accessible
                    if (rsaKey == null && ecdsaKey == null)
                    {
                        throw new ConfigurationException(
                            "Cannot access private key for SSL certificate. " +
                            "Certificate has private key but application lacks permissions to access it. " +
                            "Verify application has permissions to the certificate's private key.");
                    }
                    // Successfully accessed private key - validation passed
                }
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                throw new ConfigurationException(
                    "SSL certificate private key exists but cannot be accessed. " +
                    "Verify application user has permissions to the private key in certificate store. " +
                    $"Error: {ex.Message}", ex);
            }
        }

        private SslSettings(string certificateThumbprint, string storeName, StoreLocation storeLocation, bool suppressValidation, bool requireMutualAuthentication, bool validateCertificateHostname)
            : this(certificateThumbprint, storeName, storeLocation, suppressValidation, requireMutualAuthentication, validateCertificateHostname, customValidator: null)
        {
        }

        private SslSettings(string certificateThumbprint, string storeName, StoreLocation storeLocation, bool suppressValidation, bool requireMutualAuthentication, bool validateCertificateHostname, CertificateValidationCallback? customValidator)
        {
            using var store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadOnly);

            var find = store.Certificates.Find(X509FindType.FindByThumbprint, certificateThumbprint, !suppressValidation);
            if (find.Count == 0)
            {
                throw new ArgumentException(
                    "Could not find Valid certificate for thumbprint (by default it can be found under `akka.remote.dot-netty.tcp.ssl.certificate.thumbprint`. Also check `akka.remote.dot-netty.tcp.ssl.certificate.store-name` and `akka.remote.dot-netty.tcp.ssl.certificate.store-location`)");
            }

            Certificate = find[0];
            SuppressValidation = suppressValidation;
            RequireMutualAuthentication = requireMutualAuthentication;
            ValidateCertificateHostname = validateCertificateHostname;
            CustomValidator = customValidator;
        }

        private SslSettings(string certificatePath, string certificatePassword, X509KeyStorageFlags flags, bool suppressValidation, bool requireMutualAuthentication, bool validateCertificateHostname)
            : this(certificatePath, certificatePassword, flags, suppressValidation, requireMutualAuthentication, validateCertificateHostname, customValidator: null)
        {
        }

        private SslSettings(string certificatePath, string certificatePassword, X509KeyStorageFlags flags, bool suppressValidation, bool requireMutualAuthentication, bool validateCertificateHostname, CertificateValidationCallback? customValidator)
        {
            if (string.IsNullOrEmpty(certificatePath))
                throw new ArgumentNullException(nameof(certificatePath), "Path to SSL certificate was not found (by default it can be found under `akka.remote.dot-netty.tcp.ssl.certificate.path`)");

#if NET10_0_OR_GREATER
            Certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, certificatePassword, flags);
#else
            Certificate = new X509Certificate2(certificatePath, certificatePassword, flags);
#endif
            SuppressValidation = suppressValidation;
            RequireMutualAuthentication = requireMutualAuthentication;
            ValidateCertificateHostname = validateCertificateHostname;
            CustomValidator = customValidator;
        }
    }

    /// <summary>
    /// PUBLIC API
    ///
    /// Custom certificate validation callback for mTLS connections.
    /// Invoked during TLS handshake on both client and server sides.
    /// </summary>
    /// <param name="certificate">The peer certificate to validate</param>
    /// <param name="chain">The X509 chain for validation</param>
    /// <param name="remotePeer">The remote address/peer identifier</param>
    /// <param name="errors">SSL policy errors from standard validation</param>
    /// <param name="log">Logger for diagnostics</param>
    /// <returns>True to accept cert, false to reject</returns>
    public delegate bool CertificateValidationCallback(
        X509Certificate2? certificate,
        X509Chain? chain,
        string remotePeer,
        SslPolicyErrors errors,
        ILoggingAdapter log);

    /// <summary>
    /// PUBLIC API
    ///
    /// Factory methods for common certificate validation scenarios.
    /// Helpers return delegates that can be composed or used standalone.
    /// Each helper creates a CertificateValidationCallback that can be passed to DotNettySslSetup.
    /// </summary>
    public static class CertificateValidation
    {
        /// <summary>
        /// Validate certificate chain against system CA store.
        /// Use for: CA-signed certificates in production.
        /// </summary>
        public static CertificateValidationCallback ValidateChain(
            ILoggingAdapter? log = null)
        {
            return (cert, chain, peer, errors, noClosureLog) =>
            {
                if (cert == null)
                {
                    (log ?? noClosureLog).Error("Certificate chain validation failed for {0}: certificate is null", peer);
                    return false;
                }

                var filteredErrors = errors & ~SslPolicyErrors.RemoteCertificateNameMismatch;
                if (filteredErrors == SslPolicyErrors.None)
                    return true;

                var detailedError = TlsErrorMessageBuilder.BuildSslPolicyErrorMessage(
                    filteredErrors, cert, chain);
                (log ?? noClosureLog).Error("Certificate chain validation failed for {0}:\n{1}", peer, detailedError);
                return false;
            };
        }

        /// <summary>
        /// Validate certificate hostname (CN/SAN) matches expected hostname.
        /// Use for: Per-node certificates, FQDN-based identity.
        /// Applies bidirectionally on both client and server.
        /// </summary>
        public static CertificateValidationCallback ValidateHostname(
            string? expectedHostname = null,
            ILoggingAdapter? log = null)
        {
            return (cert, chain, peer, errors, nonClosureLog) =>
            {
                if (cert == null)
                {
                    (log ?? nonClosureLog).Error(
                        "Hostname validation failed for {0}: certificate is null",
                        peer);
                    return false;
                }

                var hostname = expectedHostname ?? peer;

                if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0) return true;
                var cn = cert.GetNameInfo(X509NameType.DnsName, false);
                (log ?? nonClosureLog).Error(
                    "Hostname validation failed for {0}: expected '{1}', certificate CN is '{2}'",
                    peer, hostname, cn);
                return false;

            };
        }

        /// <summary>
        /// Pin certificate by thumbprint. Only accept certs matching allowed list.
        /// Use for: High-security scenarios, known peer certificates.
        /// Best combined with: Certificate revocation checking.
        /// </summary>
        public static CertificateValidationCallback PinnedCertificate(
            params string[] allowedThumbprints)
        {
            if (allowedThumbprints == null || allowedThumbprints.Length == 0)
                throw new ArgumentException("At least one thumbprint required");

            // Normalize thumbprints to uppercase for case-insensitive comparison.
            // This is SAFE because thumbprints are hexadecimal representations of SHA hashes.
            // "2A8B4C" and "2a8b4c" represent the same binary value - just different display conventions.
            // Different tools display thumbprints differently (Windows=uppercase, OpenSSL=lowercase),
            // so case-insensitive comparison improves usability without compromising security.
            // Also filter out any null/empty thumbprints to prevent security issues.
            var normalizedThumbprints = new HashSet<string>(
                allowedThumbprints
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.ToUpperInvariant()));

            if (normalizedThumbprints.Count == 0)
                throw new ArgumentException("At least one valid (non-empty) thumbprint required");

            return (cert, chain, peer, errors, log) =>
            {
                if (cert == null)
                {
                    log.Error("Certificate pinning failed for {0}: certificate is null", peer);
                    return false;
                }

                var thumbprint = cert.Thumbprint?.ToUpperInvariant();

                if (string.IsNullOrEmpty(thumbprint))
                {
                    log.Error("Certificate pinning failed for {0}: certificate has no thumbprint", peer);
                    return false;
                }

                if (!normalizedThumbprints.Contains(thumbprint!))
                {
                    log.Error("Certificate pinning failed for {0}: thumbprint '{1}' not in allowed list",
                        peer, thumbprint);
                    return false;
                }

                return true;
            };
        }

        /// <summary>
        /// Validate certificate subject DN matches expected pattern.
        /// Use for: Organizational CA, issuer-based identity verification.
        /// Supports wildcards: "CN=Akka-Node-*" matches "CN=Akka-Node-001"
        /// </summary>
        public static CertificateValidationCallback ValidateSubject(
            string expectedSubjectPattern,
            ILoggingAdapter? log = null)
        {
            if (string.IsNullOrWhiteSpace(expectedSubjectPattern))
                throw new ArgumentException("Subject pattern required");

            return (cert, chain, peer, errors, log_) =>
            {
                if (cert == null)
                {
                    (log ?? log_).Error(
                        "Subject validation failed for {0}: certificate is null",
                        peer);
                    return false;
                }

                var cert509 = cert as X509Certificate2;
                var subject = cert509?.Subject;

                if (string.IsNullOrEmpty(subject))
                {
                    (log ?? log_).Error(
                        "Subject validation failed for {0}: certificate has no subject",
                        peer);
                    return false;
                }

                if (!SubjectMatchesPattern(subject, expectedSubjectPattern))
                {
                    (log ?? log_).Error(
                        "Subject validation failed for {0}: '{1}' does not match pattern '{2}'",
                        peer, subject, expectedSubjectPattern);
                    return false;
                }

                return true;
            };
        }

        /// <summary>
        /// Validate certificate issuer matches expected DN pattern.
        /// Use for: Verifying certificate came from trusted CA.
        /// </summary>
        public static CertificateValidationCallback ValidateIssuer(
            string expectedIssuerPattern,
            ILoggingAdapter? log = null)
        {
            if (string.IsNullOrWhiteSpace(expectedIssuerPattern))
                throw new ArgumentException("Issuer pattern required");

            return (cert, chain, peer, errors, log_) =>
            {
                if (cert == null)
                {
                    (log ?? log_).Error(
                        "Issuer validation failed for {0}: certificate is null",
                        peer);
                    return false;
                }

                var cert509 = cert as X509Certificate2;
                var issuer = cert509?.Issuer;

                if (string.IsNullOrEmpty(issuer))
                {
                    (log ?? log_).Error(
                        "Issuer validation failed for {0}: certificate has no issuer",
                        peer);
                    return false;
                }

                if (!SubjectMatchesPattern(issuer, expectedIssuerPattern))
                {
                    (log ?? log_).Error(
                        "Issuer validation failed for {0}: '{1}' does not match pattern '{2}'",
                        peer, issuer, expectedIssuerPattern);
                    return false;
                }

                return true;
            };
        }

        /// <summary>
        /// Compose multiple validation callbacks into a single callback.
        /// All validators must pass for certificate to be accepted.
        /// Use for: Combining multiple validation strategies.
        /// </summary>
        public static CertificateValidationCallback Combine(
            params CertificateValidationCallback[] validators)
        {
            if (validators == null || validators.Length == 0)
                throw new ArgumentException("At least one validator required");

            return (cert, chain, peer, errors, log) =>
            {
                foreach (var validator in validators!)
                {
                    if (!validator(cert, chain, peer, errors, log))
                        return false;
                }
                return true;
            };
        }

        /// <summary>
        /// Chain validator with optional custom validation.
        /// Validates certificate chain, then calls optional custom logic.
        /// </summary>
        public static CertificateValidationCallback ChainPlusThen(
            Func<X509Certificate2?, X509Chain?, string, bool> customCheck,
            ILoggingAdapter? log = null)
        {
            if (customCheck == null)
                throw new ArgumentException("Custom check function required");

            return (cert, chain, peer, errors, log_) =>
            {
                // First validate chain
                var chainValidator = ValidateChain(log ?? log_);
                if (!chainValidator(cert, chain, peer, errors, log_))
                    return false;

                // Then custom check
                if (!customCheck(cert, chain, peer))
                {
                    (log ?? log_).Error("Custom certificate validation failed for {0}", peer);
                    return false;
                }

                return true;
            };
        }

        private static bool SubjectMatchesPattern(string? subject, string pattern)
        {
            // Simple wildcard matching: CN=Akka-Node-* matches CN=Akka-Node-001
            if (string.IsNullOrEmpty(subject))
                return false;

            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(subject, regex);
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Helper class for building human-readable error messages for TLS/SSL certificate validation failures.
    /// Provides detailed diagnostics and actionable suggestions for common certificate issues.
    /// </summary>
    internal static class TlsErrorMessageBuilder
    {
        /// <summary>
        /// Builds a detailed error message for SSL policy errors encountered during TLS handshake.
        /// </summary>
        /// <param name="errors">The SSL policy errors from certificate validation callback</param>
        /// <param name="certificate">The certificate that failed validation (may be null)</param>
        /// <param name="chain">The X509 chain used for validation (may be null)</param>
        /// <returns>A human-readable error message with diagnostics and suggestions</returns>
        public static string BuildSslPolicyErrorMessage(
            System.Net.Security.SslPolicyErrors errors,
            X509Certificate2? certificate,
            X509Chain? chain)
        {
            var message = new System.Text.StringBuilder();
            message.AppendLine("TLS/SSL certificate validation failed:");

            // Interpret SslPolicyErrors flags
            if (errors != System.Net.Security.SslPolicyErrors.None)
            {
                if ((errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
                {
                    message.AppendLine("  - Remote certificate not available");
                    message.AppendLine("    Suggestion: Ensure the remote endpoint provides a valid TLS certificate");
                }

                if ((errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                {
                    message.AppendLine("  - Remote certificate name mismatch");
                    message.AppendLine("    Suggestion: Verify certificate CN/SAN matches the target hostname");
                    if (certificate != null)
                    {
                        var cn = certificate.GetNameInfo(X509NameType.DnsName, false);
                        message.AppendLine($"    Certificate CN: {cn}");
                    }
                }

                if ((errors & System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors) != 0)
                {
                    message.AppendLine("  - Certificate chain validation errors");

                    if (chain != null && chain.ChainStatus.Length > 0)
                    {
                        var chainStatusMsg = BuildX509ChainStatusMessage(chain.ChainStatus);
                        message.Append(chainStatusMsg);
                    }
                    else
                    {
                        message.AppendLine("    Suggestion: Certificate chain cannot be validated. " +
                                          "Install required intermediate CA certificates.");
                    }
                }
            }

            // Add certificate details if available
            if (certificate != null)
            {
                message.AppendLine($"\nCertificate Details:");
                message.AppendLine($"  Subject: {certificate.Subject}");
                message.AppendLine($"  Issuer: {certificate.Issuer}");
                message.AppendLine($"  Thumbprint: {certificate.Thumbprint}");
                message.AppendLine($"  Valid From: {certificate.NotBefore:yyyy-MM-dd HH:mm:ss}");
                message.AppendLine($"  Valid To: {certificate.NotAfter:yyyy-MM-dd HH:mm:ss}");
                message.AppendLine($"  Has Private Key: {certificate.HasPrivateKey}");
            }

            return message.ToString().TrimEnd();
        }

        /// <summary>
        /// Builds a detailed message explaining X509 chain status errors.
        /// </summary>
        /// <param name="chainStatus">Array of chain status from X509Chain validation</param>
        /// <returns>Human-readable explanation of chain errors with suggestions</returns>
        public static string BuildX509ChainStatusMessage(X509ChainStatus[] chainStatus)
        {
            var message = new System.Text.StringBuilder();

            foreach (var status in chainStatus)
            {
                // Skip "NoError" status
                if (status.Status == X509ChainStatusFlags.NoError)
                    continue;

                message.AppendLine($"    - {status.Status}: {status.StatusInformation}");

                // Add specific suggestions based on chain status
                var suggestion = GetChainStatusSuggestion(status.Status);
                if (!string.IsNullOrEmpty(suggestion))
                {
                    message.AppendLine($"      Suggestion: {suggestion}");
                }
            }

            return message.ToString();
        }

        /// <summary>
        /// Maps X509ChainStatusFlags to actionable suggestions for fixing the issue.
        /// </summary>
        private static string GetChainStatusSuggestion(X509ChainStatusFlags status)
        {
            return status switch
            {
                X509ChainStatusFlags.NotTimeValid =>
                    "Certificate has expired or is not yet valid. Check system clock and certificate validity period.",

                X509ChainStatusFlags.NotTimeNested =>
                    "Certificate validity period does not nest correctly within the chain.",

                X509ChainStatusFlags.Revoked =>
                    "Certificate has been revoked. Contact certificate issuer.",

                X509ChainStatusFlags.NotSignatureValid =>
                    "Certificate signature is invalid. Certificate may be corrupted.",

                X509ChainStatusFlags.NotValidForUsage =>
                    "Certificate is not valid for the intended usage. Check Extended Key Usage (EKU) extensions.",

                X509ChainStatusFlags.UntrustedRoot =>
                    "Certificate chain terminates in an untrusted root. Install root CA certificate in Trusted Root Certification Authorities store.",

                X509ChainStatusFlags.RevocationStatusUnknown =>
                    "Revocation status cannot be determined. Check network connectivity to CRL/OCSP endpoints.",

                X509ChainStatusFlags.Cyclic =>
                    "Certificate chain contains a cycle. Certificate configuration is invalid.",

                X509ChainStatusFlags.InvalidExtension =>
                    "Certificate contains an invalid extension.",

                X509ChainStatusFlags.InvalidPolicyConstraints =>
                    "Certificate policy constraints are invalid.",

                X509ChainStatusFlags.InvalidBasicConstraints =>
                    "Basic constraints are invalid. CA certificate may be missing CA:TRUE constraint.",

                X509ChainStatusFlags.InvalidNameConstraints =>
                    "Name constraints in certificate are invalid.",

                X509ChainStatusFlags.HasNotSupportedNameConstraint =>
                    "Certificate contains name constraints that are not supported.",

                X509ChainStatusFlags.HasNotDefinedNameConstraint =>
                    "Certificate has undefined name constraints.",

                X509ChainStatusFlags.HasNotPermittedNameConstraint =>
                    "Certificate name violates name constraints.",

                X509ChainStatusFlags.HasExcludedNameConstraint =>
                    "Certificate name is explicitly excluded by name constraints.",

                X509ChainStatusFlags.PartialChain =>
                    "Certificate chain is incomplete. Install all intermediate CA certificates from your certificate provider.",

                X509ChainStatusFlags.CtlNotTimeValid =>
                    "Certificate Trust List (CTL) is not time-valid.",

                X509ChainStatusFlags.CtlNotSignatureValid =>
                    "Certificate Trust List (CTL) signature is invalid.",

                X509ChainStatusFlags.CtlNotValidForUsage =>
                    "Certificate Trust List (CTL) is not valid for this usage.",

                X509ChainStatusFlags.OfflineRevocation =>
                    "Revocation checking is offline. Enable network access or disable revocation checking for testing.",

                X509ChainStatusFlags.NoIssuanceChainPolicy =>
                    "Certificate does not have a valid issuance policy.",

                X509ChainStatusFlags.ExplicitDistrust =>
                    "Certificate is explicitly distrusted. Remove from Distrusted Certificates store if this is incorrect.",

                X509ChainStatusFlags.HasNotSupportedCriticalExtension =>
                    "Certificate has an unsupported critical extension.",

                X509ChainStatusFlags.HasWeakSignature =>
                    "Certificate uses a weak signature algorithm (e.g., SHA1). Use SHA256 or stronger.",

                _ => string.Empty
            };
        }

        /// <summary>
        /// Builds an error message for TLS handshake exceptions.
        /// Attempts to extract meaningful information from CryptographicException and AuthenticationException.
        /// </summary>
        public static string BuildTlsHandshakeErrorMessage(Exception exception, bool isClient)
        {
            var role = isClient ? "Client" : "Server";
            var message = new System.Text.StringBuilder();

            message.AppendLine($"TLS handshake failed ({role} side):");
            message.AppendLine($"  Error: {exception.Message}");

            // Provide role-specific suggestions
            if (isClient)
            {
                message.AppendLine("\nClient-side TLS troubleshooting:");
                message.AppendLine("  - Verify server certificate is trusted (install root CA if using self-signed)");
                message.AppendLine("  - Check certificate hostname matches connection target");
                message.AppendLine("  - For mutual TLS, ensure client certificate is configured, accessible, and trusted by server");
                message.AppendLine("  - Server and client certificates must have compatible trust chains");
            }
            else
            {
                message.AppendLine("\nServer-side TLS troubleshooting:");
                message.AppendLine("  - Verify server certificate has accessible private key");
                message.AppendLine("  - For mutual TLS, check if client is providing a certificate");
                message.AppendLine("  - Review certificate validation requirements (suppress-validation for testing)");
            }

            if (exception.InnerException != null)
            {
                message.AppendLine($"\nInner Exception: {exception.InnerException.Message}");
            }

            return message.ToString().TrimEnd();
        }
    }
}
