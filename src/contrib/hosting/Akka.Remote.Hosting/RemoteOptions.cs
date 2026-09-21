// -----------------------------------------------------------------------
//  <copyright file="AkkaRemoteOptions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Akka.Configuration;
using Akka.Event;
using Akka.Hosting;
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
        ///     Flag to enable TLS/SSL support. If set to true, <see cref="Ssl"/> property need to be set.
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
            var sb = new StringBuilder();
            Build(sb);

            if (sb.Length > 0)
                builder.AddHocon(sb.ToString(), HoconAddMode.Prepend);

            // SSL configuration strategy:
            // 1. If X509Certificate object is provided -> Use DotNettySslSetup (takes precedence over HOCON)
            // 2. If X509Certificate is null but SSL settings configured -> Use HOCON configuration only
            //
            // Important: DotNettySslSetup ALWAYS takes precedence when present, causing HOCON SSL settings
            // to be completely ignored. We must not emit both to avoid confusion.
            // See: https://github.com/akkadotnet/akka.net/blob/dev/src/core/Akka.Remote/Transport/DotNetty/DotNettyTransportSettings.cs#L163-L164

            if (EnableSsl is false || Ssl.X509Certificate == null)
                return;

            var suppressValidation = Ssl.SuppressValidation ?? false;
            var requireMutualAuth = Ssl.RequireMutualAuthentication ?? true; // Default to true as per v1.5.52
            var validateHostname = Ssl.ValidateCertificateHostname ?? false; // Default to false as per v1.5.53

            // Choose the appropriate constructor based on which settings are provided
            if (Ssl.CustomValidator != null)
            {
                // Use the 5-parameter constructor with custom validator (v1.5.55+)
                builder.AddSetup(new DotNettySslSetup(Ssl.X509Certificate, suppressValidation, requireMutualAuth, validateHostname, Ssl.CustomValidator));
            }
            else if (Ssl.RequireMutualAuthentication.HasValue || Ssl.ValidateCertificateHostname.HasValue)
            {
                // Use the 4-parameter constructor (v1.5.52/v1.5.53+)
                builder.AddSetup(new DotNettySslSetup(Ssl.X509Certificate, suppressValidation, requireMutualAuth, validateHostname));
            }
            else
            {
                // Use legacy 2-parameter constructor for backward compatibility when new settings are not specified
                builder.AddSetup(new DotNettySslSetup(Ssl.X509Certificate, suppressValidation));
            }
        }
    
        private void Build(StringBuilder builder)
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
        
            if (EnableSsl is not null)
            {
                tcpSb.AppendLine($"enable-ssl = {EnableSsl.ToHocon()}");
                if (EnableSsl.Value)
                {
                    if(Ssl is null)
                        throw new ConfigurationException("Ssl property need to be populated when EnableSsl is set to true.");

                    // Only emit HOCON SSL configuration if we're NOT going to create a DotNettySslSetup
                    // When DotNettySslSetup is present, it takes precedence and HOCON SSL settings are ignored
                    // See: https://github.com/akkadotnet/akka.net/issues/7914 and the warning at
                    // https://github.com/akkadotnet/akka.net/blob/dev/src/core/Akka.Remote/Transport/DotNetty/DotNettyTransportSettings.cs#L163-L164
                    if (Ssl.X509Certificate == null)
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
        public bool? SuppressValidation { get; set; }
        public X509Certificate2? X509Certificate { get; set; }
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
        /// <b>Available since:</b> Akka.NET v1.5.55
        /// </summary>
        public Transport.DotNetty.CertificateValidationCallback? CustomValidator { get; set; }

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
        public string? Path { get; set; }
        public string? Password { get; set; }
        public bool? UseThumbprintOverFile { get; set; }
        public string? Thumbprint { get; set; }
        public string? StoreName { get; set; }
        public string? StoreLocation { get; set; }
        
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
            
            if (sb.Length == 0) 
                return;
            
            sb.Insert(0, "certificate {\n");
            sb.AppendLine("}");
            builder.Append(sb);
        }
    }
}