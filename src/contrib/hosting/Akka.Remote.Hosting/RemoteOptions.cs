// -----------------------------------------------------------------------
//  <copyright file="AkkaRemoteOptions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Akka.Configuration;
using Akka.Event;
using Akka.Hosting;
using Akka.IO;
using Akka.Remote.Transport.DotNetty;

namespace Akka.Remote.Hosting
{
    public class RemoteOptions
    {
        /// <summary>
        ///     The hostname or ip to bind akka remoting to, <see cref="IPAddress.Any"/> is used if empty
        /// </summary>
        public string? HostName { get; set; }
        
        /// <summary>
        ///     The default remote server port clients should connect to.
        ///     Default is 2552, use 0 if you want a random available port.
        ///     This port needs to be unique for each actor system on the same machine.
        /// </summary>
        public int? Port { get; set; }
        
        /// <summary>
        ///     If this value is set, this becomes the public address for the actor system on this
        ///     transport, which might be different than the physical ip address (hostname).
        ///     This is designed to make it easy to support private / public addressing schemes
        /// </summary>
        public string? PublicHostName { get; set; }
        
        /// <summary>
        ///     Similar to <see cref="PublicHostName"/>, this allows Akka.Remote users
        ///     to alias the port they're listening on. The socket will actually listen on the
        ///     <see cref="Port"/> property, but when connecting to other ActorSystems this node will advertise
        ///     itself as being connected to the "public-port". This is helpful when working with 
        ///     hosting environments that rely on address translation and port-forwarding, such as Docker.
        /// </summary>
        public int? PublicPort { get; set; }

        /// <summary>
        ///     <para>
        ///         Sets the send buffer size of the Sockets, set to 0 for platform default.
        ///     </para>
        ///     <b>Default:</b> 256000
        /// </summary>
        public long? SendBufferSize { get; set; }
        
        /// <summary>
        ///     <para>
        ///         Sets the send buffer size of the Sockets, set to 0 for platform default.
        ///     </para>
        ///     <b>Default:</b> 256000
        /// </summary>
        public long? ReceiveBufferSize { get; set; }
        
        /// <summary>
        ///     <para>
        ///         Maximum message size the transport will accept, but at least 32000 bytes.
        ///         Please note that UDP does not support arbitrary large datagrams,
        ///         so this setting has to be chosen carefully when using UDP.
        ///         Both <see cref="SendBufferSize"/> and <see cref="ReceiveBufferSize"/> settings has to
        ///         be adjusted to be able to buffer messages of maximum size.
        ///     </para>
        ///     <b>Default:</b> 128000
        /// </summary>
        public long? MaxFrameSize { get; set; }
        
        /// <summary>
        ///     Turns TLS on for the remote transport. When true, name a certificate on <see cref="Ssl"/>.
        ///     Setting <see cref="SslOptions.CertificateSource"/> turns TLS on without this flag, unless this is false.
        ///     <see cref="SslOptions.X509Certificate"/> and <see cref="SslOptions.CertificateOptions"/> do not:
        ///     with those, TLS is on only when this is true.
        /// </summary>
        public bool? EnableSsl { get; set; }
    
        /// <summary>
        ///     The TLS/SSL option for the remote transport.
        /// </summary>
        public SslOptions Ssl { get; set; } = new ();
        
        /// <summary>
        ///     <para>
        ///         Failure detection algorithm used to detect remote transport failure condition.
        ///     </para>
        /// </summary>
        public DeadlineFailureDetectorOptions? TransportFailureDetector { get; set; }
        
        /// <summary>
        ///     <para>
        ///         Failure detection algorithm used to detect remote death watch.
        ///     </para>
        /// </summary>
        public PhiAccrualFailureDetectorOptions? WatchFailureDetector { get; set; }
        
        internal void Build(AkkaConfigurationBuilder builder)
        {
            // A certificate source (set directly, or built from a common name or folder when a custom
            // validator needs it) is applied through DotNettySslSetup, like an X509Certificate.
            var source = ResolveCertificateSource();

            var sb = new StringBuilder();
            Build(sb, source);

            if (sb.Length > 0)
                builder.AddHocon(sb.ToString(), HoconAddMode.Prepend);

            // SSL configuration strategy:
            // 1. If there is a certificate source (see above) -> Use DotNettySslSetup.FromSource
            // 2. If X509Certificate object is provided -> Use DotNettySslSetup (takes precedence over HOCON)
            // 3. If X509Certificate is null but CustomValidator is set -> Load the certificate named by
            //    CertificateOptions (file or thumbprint) and use DotNettySslSetup, because HOCON cannot carry a delegate
            // 4. Otherwise -> Use HOCON configuration only
            //
            // Important: DotNettySslSetup ALWAYS takes precedence when present, causing HOCON SSL settings
            // to be completely ignored. We must not emit both to avoid confusion.
            // See: https://github.com/akkadotnet/akka.net/blob/dev/src/core/Akka.Remote/Transport/DotNetty/DotNettyTransportSettings.cs#L163-L164

            if (EnableSsl is false)
                return;

            if (source != null)
            {
                builder.AddSetup(DotNettySslSetup.FromSource(
                    source,
                    Ssl.SuppressValidation ?? false,
                    Ssl.RequireMutualAuthentication ?? true,
                    Ssl.ValidateCertificateHostname ?? false,
                    Ssl.CustomValidator));
                return;
            }

            var certificate = Ssl.X509Certificate;
            if (certificate == null)
            {
                if (EnableSsl is not true || Ssl.CustomValidator == null)
                    return;

                certificate = Ssl.CertificateOptions.Load(Ssl.SuppressValidation ?? false);
            }

            var suppressValidation = Ssl.SuppressValidation ?? false;
            var requireMutualAuth = Ssl.RequireMutualAuthentication ?? true; // Default to true as per v1.5.52
            var validateHostname = Ssl.ValidateCertificateHostname ?? false; // Default to false as per v1.5.53

            // Choose the appropriate constructor based on which settings are provided
            if (Ssl.CustomValidator != null)
            {
                // Use the 5-parameter constructor with custom validator (v1.5.55+)
                builder.AddSetup(new DotNettySslSetup(certificate, suppressValidation, requireMutualAuth, validateHostname, Ssl.CustomValidator));
            }
            else if (Ssl.RequireMutualAuthentication.HasValue || Ssl.ValidateCertificateHostname.HasValue)
            {
                // Use the 4-parameter constructor (v1.5.52/v1.5.53+)
                builder.AddSetup(new DotNettySslSetup(certificate, suppressValidation, requireMutualAuth, validateHostname));
            }
            else
            {
                // Use legacy 2-parameter constructor for backward compatibility when new settings are not specified
                builder.AddSetup(new DotNettySslSetup(certificate, suppressValidation));
            }
        }
    
        private TlsCertificateSource? ResolveCertificateSource()
        {
            // Ssl is not null in normal use. When it is null, leave the existing behaviour untouched.
            if (EnableSsl is false || Ssl is null)
                return null;

            return Ssl.ResolveCertificateSource(EnableSsl == true);
        }

        private void Build(StringBuilder builder, TlsCertificateSource? source)
        {
            var sb = new StringBuilder();
            
            if (TransportFailureDetector is not null)
            {
                var tsb = TransportFailureDetector.ToHocon();
                if (tsb.Length > 0)
                {
                    sb.AppendLine("transport-failure-detector {\n");
                    sb.Append(tsb);
                    sb.AppendLine("}");
                }
            }
            
            if (WatchFailureDetector is not null)
            {
                var wsb = WatchFailureDetector.ToHocon();
                if (wsb.Length > 0)
                {
                    sb.AppendLine("watch-failure-detector {\n");
                    sb.Append(wsb);
                    sb.AppendLine("}");
                }
            }
            
            var tcpSb = new StringBuilder();
        
            if (!string.IsNullOrWhiteSpace(HostName))
                tcpSb.AppendLine($"hostname = {HostName.ToHocon()}");
        
            if (Port is not null)
                tcpSb.AppendLine($"port = {Port}");
        
            if (!string.IsNullOrWhiteSpace(PublicHostName))
                tcpSb.AppendLine($"public-hostname = {PublicHostName.ToHocon()}");
        
            if (PublicPort is not null)
                tcpSb.AppendLine($"public-port = {PublicPort}");

            if (SendBufferSize is not null)
                tcpSb.AppendLine($"send-buffer-size = {SendBufferSize.ToHocon()}");

            if (ReceiveBufferSize is not null)
                tcpSb.AppendLine($"receive-buffer-size = {ReceiveBufferSize.ToHocon()}");

            if (MaxFrameSize is not null)
                tcpSb.AppendLine($"maximum-frame-size = {MaxFrameSize.ToHocon()}");
        
            // A certificate source turns TLS on, unless EnableSsl is false.
            var enableSsl = EnableSsl ?? (Ssl?.CertificateSource != null ? true : (bool?)null);
            if (enableSsl is not null)
            {
                tcpSb.AppendLine($"enable-ssl = {enableSsl.ToHocon()}");
                if (enableSsl.Value)
                {
                    if(Ssl is null)
                        throw new ConfigurationException("Ssl property need to be populated when EnableSsl is set to true.");

                    // Only emit HOCON SSL configuration if we're NOT going to create a DotNettySslSetup
                    // When DotNettySslSetup is present, it takes precedence and HOCON SSL settings are ignored
                    // See: https://github.com/akkadotnet/akka.net/issues/7914 and the warning at
                    // https://github.com/akkadotnet/akka.net/blob/dev/src/core/Akka.Remote/Transport/DotNetty/DotNettyTransportSettings.cs#L163-L164
                    if (Ssl.X509Certificate == null && source == null && Ssl.CustomValidator == null)
                    {
                        Ssl.Build(tcpSb);
                    }
                }
            }

            if(tcpSb.Length > 0)
            {
                tcpSb.Insert(0, "dot-netty.tcp {\n");
                tcpSb.Append("}");
                sb.Append(tcpSb);
            }
            
            if(sb.Length == 0)
                return;
            
            sb.Insert(0, "akka.remote {\n");
            sb.Append("}");
            
            builder.Append(sb);
        }
        
    }

    public sealed class SslOptions
    {
        /// <summary>
        /// When true, the node does not validate the certificate chain of its peers. For development and
        /// testing only, for example with self-signed certificates.
        /// </summary>
        public bool? SuppressValidation { get; set; }

        /// <summary>
        /// One certificate, loaded by you, that the node presents for as long as it runs. It is not checked for
        /// the usages a <see cref="CertificateSource"/> checks. To renew a certificate with no restart, set
        /// <see cref="CertificateSource"/> in place of this.
        /// </summary>
        public X509Certificate2? X509Certificate { get; set; }

        /// <summary>
        /// <para>
        /// Supplies the certificate this node presents. Akka.NET asks the source each time it builds a
        /// connection, so a renewed certificate is used by new connections with no restart.
        /// Create one with <see cref="TlsCertificateSource.FromStore"/>,
        /// <see cref="TlsCertificateSource.FromFolder(string, string)"/> or <see cref="TlsCertificateSource.FromCallback"/>.
        /// </para>
        /// <para>
        /// Setting a source turns TLS on, unless <see cref="RemoteOptions.EnableSsl"/> is false. The source is used and
        /// <see cref="CertificateOptions"/> is ignored, as it is for <see cref="X509Certificate"/>.
        /// If <see cref="RemoteOptions.EnableSsl"/> is false, TLS is off and the source is not used.
        /// Set either this or <see cref="X509Certificate"/>, not both.
        /// </para>
        /// <para>
        /// A source only uses a certificate that is in date, has a private key and is allowed for the job. With
        /// <see cref="RequireMutualAuthentication"/> on, which is the default, that means both server and client
        /// authentication. A certificate that is not allowed is skipped with a warning, and a node with no other
        /// certificate does not start.
        /// </para>
        /// <b>Default:</b> null
        /// </summary>
        public TlsCertificateSource? CertificateSource { get; set; }

        public SslCertificateOptions CertificateOptions { get; set; } = new ();

        /// <summary>
        /// <para>
        /// When set to true, enables mutual TLS (mTLS) authentication where both client and server
        /// must present valid certificates with accessible private keys during the TLS handshake.
        /// </para>
        /// <para>
        /// This provides defense-in-depth security by ensuring bidirectional authentication and
        /// preventing asymmetric connectivity issues in peer-to-peer Akka.Remote connections.
        /// </para>
        /// <b>Default:</b> true (as of Akka.NET v1.5.52)
        /// </summary>
        public bool? RequireMutualAuthentication { get; set; }

        /// <summary>
        /// <para>
        /// Controls whether certificate hostname validation is performed during TLS handshake.
        /// </para>
        /// <para>
        /// When enabled (true): Traditional TLS hostname validation is performed - certificate CN/SAN must match the target hostname.
        /// When disabled (false): Only validates certificate chain against CA, ignores hostname mismatches.
        /// </para>
        /// <para>
        /// Disabling hostname validation may be necessary for:
        /// - Mutual TLS with per-node certificates in P2P clusters
        /// - IP-based connections where certificates use DNS names
        /// - Service discovery with dynamic addresses
        /// </para>
        /// <b>Default:</b> false (as of Akka.NET v1.5.53)
        /// </summary>
        public bool? ValidateCertificateHostname { get; set; }

        /// <summary>
        /// <para>
        /// Custom certificate validation callback for advanced validation scenarios.
        /// When provided, this callback takes precedence over config-based validation.
        /// </para>
        /// <para>
        /// Use this to implement custom validation logic such as certificate pinning,
        /// subject/issuer matching, or other business-specific validation rules.
        /// </para>
        /// <para>
        /// The callback parameters are:
        /// - X509Certificate2?: The peer certificate to validate
        /// - X509Chain?: The X509 chain for validation
        /// - string: The remote peer identifier
        /// - SslPolicyErrors: SSL policy errors from standard validation
        /// - ILoggingAdapter: Logger for diagnostics
        /// </para>
        /// <para>
        /// Returns true to accept the certificate, false to reject it.
        /// </para>
        /// <para>
        /// The validator applies however the certificate is named: <see cref="X509Certificate"/>,
        /// <see cref="CertificateSource"/> or <see cref="CertificateOptions"/>. For a file or a thumbprint in
        /// <see cref="CertificateOptions"/>, the certificate is then loaded when <c>WithRemoting</c> runs. If TLS is
        /// on and the options name no certificate, <c>WithRemoting</c> throws a <see cref="ConfigurationException"/>.
        /// </para>
        /// <b>Available since:</b> Akka.NET v1.5.55
        /// </summary>
        public Transport.DotNetty.CertificateValidationCallback? CustomValidator { get; set; }

        /// <summary>
        /// Returns the certificate source to apply through <see cref="DotNettySslSetup"/>, or null when the
        /// certificate comes from <see cref="X509Certificate"/> or from HOCON.
        /// </summary>
        internal TlsCertificateSource? ResolveCertificateSource(bool sslEnabled)
        {
            if (X509Certificate != null && CertificateSource != null)
                throw new ConfigurationException(
                    "Both Ssl.X509Certificate and Ssl.CertificateSource are set. Set one of them, not both.");

            if (CertificateSource != null)
                return CertificateSource;

            // HOCON cannot carry a custom validator. Only when one is set do we build the source here.
            if (X509Certificate != null || CustomValidator == null || !sslEnabled)
                return null;

            return CertificateOptions?.CreateSourceOrNull();
        }

        internal void Build(StringBuilder builder)
        {
            var sb = new StringBuilder();

            if (SuppressValidation is not null)
                sb.AppendLine($"suppress-validation = {SuppressValidation.ToHocon()}");

            if (RequireMutualAuthentication is not null)
                sb.AppendLine($"require-mutual-authentication = {RequireMutualAuthentication.ToHocon()}");

            if (ValidateCertificateHostname is not null)
                sb.AppendLine($"validate-certificate-hostname = {ValidateCertificateHostname.ToHocon()}");

            CertificateOptions.Build(sb);

            if(sb.Length == 0)
                return;

            sb.Insert(0, "ssl {");
            sb.AppendLine("}");
            builder.Append(sb);
        }
    }

    public sealed class SslCertificateOptions
    {
        /// <summary>
        /// <para>
        /// The certificate file, or a folder of certificate files.
        /// </para>
        /// <para>
        /// A file is loaded once, when the node starts. A folder may hold PKCS#12 files and PEM files. The node uses
        /// the usable certificate in it that was issued last and looks again in the
        /// background, so new connections use a renewed certificate within about a minute, with no restart.
        /// </para>
        /// <para>
        /// A certificate in a folder must be allowed for the job: with mutual TLS on, which is the default, both
        /// server and client authentication. A single file is not checked this way, so check the certificate
        /// before you change a file path into a folder.
        /// </para>
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// The password of the certificate file. For a folder, the one password for every PKCS#12 file and every
        /// encrypted PEM key in it.
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// When true, the node loads the certificate from the store by <see cref="Thumbprint"/>, and
        /// <see cref="Path"/> is ignored.
        /// </summary>
        public bool? UseThumbprintOverFile { get; set; }

        /// <summary>
        /// The thumbprint of one certificate in the store. It names that certificate only, so a renewed
        /// certificate needs a new thumbprint and a restart. To renew with no restart, use <see cref="CommonName"/>.
        /// </summary>
        public string? Thumbprint { get; set; }

        /// <summary>
        /// The certificate store to read, such as <c>My</c>. Used with <see cref="Thumbprint"/> and
        /// <see cref="CommonName"/>. With <see cref="CommonName"/>, an empty name means <c>My</c>.
        /// </summary>
        public string? StoreName { get; set; }

        /// <summary>
        /// Where the store is: <c>current-user</c> (the default) or <c>local-machine</c>.
        /// </summary>
        public string? StoreLocation { get; set; }

        /// <summary>
        /// The common name (CN) of the certificate to pick from the store named by <see cref="StoreName"/>
        /// and <see cref="StoreLocation"/>. The node uses the usable match that was issued last and looks again
        /// in the background, so new connections use a renewed certificate added to the store within about a
        /// minute, with no restart. The certificate must be allowed for the job: with mutual TLS on, which is the
        /// default, both server and client authentication. A certificate named by <see cref="Thumbprint"/> is not
        /// checked this way. Takes priority over <see cref="Path"/> and <see cref="Thumbprint"/>.
        /// </summary>
        public string? CommonName { get; set; }

        /// <summary>
        /// Builds a source for the two lookups that rotate (common name, or a folder), or returns null.
        /// </summary>
        internal TlsCertificateSource? CreateSourceOrNull()
        {
            if (!string.IsNullOrWhiteSpace(CommonName))
            {
                return TlsCertificateSource.FromStore(
                    CommonName!,
                    string.IsNullOrWhiteSpace(StoreName) ? "My" : StoreName!,
                    ParseStoreLocation(StoreLocation));
            }

            // The thumbprint switch wins over a path, as it does in the transport.
            if (UseThumbprintOverFile != true && !string.IsNullOrEmpty(Path) && Directory.Exists(Path))
                return TlsCertificateSource.FromFolder(Path!, string.IsNullOrEmpty(Password) ? null : Password);

            return null;
        }

        // Same values and error as the transport's `store-location` key. An empty value means current user.
        private static StoreLocation ParseStoreLocation(string? value)
        {
            return value switch
            {
                null or "" => System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser,
                "local-machine" => System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine,
                "current-user" => System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser,
                _ => throw new ArgumentException(
                    $"Unrecognized flag in X509 certificate config [{value}]. Available flags: local-machine | current-user")
            };
        }
        
        /// <summary>
        /// Loads the one certificate these options name by file or thumbprint, for use with a
        /// <see cref="DotNettySslSetup"/>. A common name or a folder gives a source instead; see
        /// <see cref="CreateSourceOrNull"/>.
        /// Mirrors how Akka.Remote loads it from HOCON: a thumbprint wins over a file when
        /// <see cref="UseThumbprintOverFile"/> is set; the store name defaults to "" and the store
        /// location to "current-user"; the file is loaded with the default key storage flags.
        /// </summary>
        internal X509Certificate2 Load(bool suppressValidation)
        {
            if (UseThumbprintOverFile is true)
            {
                if (string.IsNullOrWhiteSpace(Thumbprint))
                    throw NoCertificate();

                using var store = new X509Store(StoreName ?? string.Empty, ParseStoreLocation(StoreLocation));
                store.Open(OpenFlags.ReadOnly);

                var found = store.Certificates.Find(X509FindType.FindByThumbprint, Thumbprint, !suppressValidation);
                if (found.Count == 0)
                    throw new ArgumentException(
                        "Could not find Valid certificate for thumbprint (set by `SslCertificateOptions.Thumbprint`). " +
                        "Also check `SslCertificateOptions.StoreName` and `SslCertificateOptions.StoreLocation`");

                return found[0];
            }

            if (string.IsNullOrEmpty(Path))
                throw NoCertificate();

            return X509CertificateLoader.LoadPkcs12FromFile(Path, Password ?? string.Empty, X509KeyStorageFlags.DefaultKeySet);
        }

        private static ConfigurationException NoCertificate()
            => new("SslOptions.CustomValidator needs the certificate to be set through the options " +
                   "(X509Certificate, CertificateSource, or CertificateOptions with a Path, a Thumbprint or a CommonName), " +
                   "because it would otherwise be ignored.");

        internal void Build(StringBuilder builder)
        {
            var sb = new StringBuilder();
            
            if (!string.IsNullOrEmpty(Path))
                sb.AppendLine($"path = {Path.ToHocon()}");
            
            if (!string.IsNullOrEmpty(Password))
                sb.AppendLine($"password = {Password.ToHocon()}");
            
            if (UseThumbprintOverFile is not null)
                sb.AppendLine($"use-thumbprint-over-file = {UseThumbprintOverFile.ToHocon()}");
            
            if (!string.IsNullOrEmpty(Thumbprint))
                sb.AppendLine($"thumbprint = {Thumbprint.ToHocon()}");
            
            if (!string.IsNullOrEmpty(StoreName))
                sb.AppendLine($"store-name = {StoreName.ToHocon()}");
            
            if (!string.IsNullOrEmpty(StoreLocation))
                sb.AppendLine($"store-location = {StoreLocation.ToHocon()}");

            if (!string.IsNullOrEmpty(CommonName))
                sb.AppendLine($"common-name = {CommonName.ToHocon()}");
            
            if (sb.Length == 0) 
                return;
            
            sb.Insert(0, "certificate {\n");
            sb.AppendLine("}");
            builder.Append(sb);
        }
    }
}