// -----------------------------------------------------------------------
// <copyright file="BenchmarkTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.IO;
using Akka.Remote.Artery;

namespace RemotePingPong
{
    /// <summary>Owns benchmark credentials until all actor systems have terminated.</summary>
    internal sealed class BenchmarkTransport : IDisposable
    {
        private readonly X509Certificate2? _certificate;
        private readonly ArteryTlsSettings? _tls;
        private readonly string _credentialSource;

        private BenchmarkTransport(string kind, X509Certificate2? certificate = null,
            ArteryTlsSettings? tls = null, string credentialSource = "none")
        {
            Kind = kind;
            _certificate = certificate;
            _tls = tls;
            _credentialSource = credentialSource;
        }

        internal static BenchmarkTransport Default() => new("dotnetty");
        internal string Kind { get; }
        internal bool IsArtery => Kind != "dotnetty";
        internal string DisplayName => Kind switch
        {
            "dotnetty" => "DotNetty",
            "artery-tcp" => "Artery.Tcp",
            "artery-tls" => "Artery.Tls",
            _ => throw new InvalidOperationException($"Unknown transport [{Kind}].")
        };

        internal static BenchmarkTransport Create(string kind, bool splitMode, string? certificatePath,
            string? passwordEnvironmentVariable, string? peerPins)
        {
            if (kind is not ("dotnetty" or "artery-tcp" or "artery-tls"))
                throw new ArgumentException("Transport must be dotnetty, artery-tcp, artery-tls.");
            var encrypted = kind is "artery-tls";
            if (!encrypted)
            {
                if (certificatePath is not null || passwordEnvironmentVariable is not null || peerPins is not null)
                    throw new ArgumentException("Certificate options require --transport artery-tls.");
                return new BenchmarkTransport(kind);
            }
            if (splitMode && (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(peerPins)))
                throw new ArgumentException("Encrypted server/client modes require --cert and --peer-pin on both processes.");
            if (certificatePath is null && (passwordEnvironmentVariable is not null || peerPins is not null))
                throw new ArgumentException("--cert-password-env and --peer-pin require --cert.");

            var password = passwordEnvironmentVariable is null ? null
                : Environment.GetEnvironmentVariable(passwordEnvironmentVariable)
                    ?? throw new ArgumentException($"Certificate password environment variable [{passwordEnvironmentVariable}] is not set.");
            var certificate = certificatePath is null ? CreateLoopbackCertificate()
                : TlsCertificateLoader.LoadPkcs12FromFile(certificatePath, password, CertificateKeyStorage);
            try
            {
                // Pins establish trust; normal outbound hostname validation is an additional check.
                // Without explicit pins, both loopback systems use this same process-owned identity.
                var pins = peerPins is null ? new[] { certificate.Thumbprint }
                    : peerPins.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (pins.Length == 0)
                    throw new ArgumentException("--peer-pin must contain at least one certificate thumbprint.");
                var policy = TlsPeerPolicy.PinnedCertificates(pins).And(TlsCertificateValidation.ValidateHostname());
                var tls = ArteryTlsSettings.Mutual(certificate, policy).WithProtocols(SslProtocols.Tls13);
                return new BenchmarkTransport(kind, certificate, tls,
                    certificatePath is null ? "ephemeral self-signed loopback identity (benchmark only)" : "supplied PKCS#12 identity");
            }
            catch
            {
                certificate.Dispose();
                throw;
            }
        }

        internal ActorSystemSetup Setup(Config config) => _tls is null
            ? ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config))
            : ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), new ArteryTlsSetup(_tls));

        // Schannel cannot use an ephemeral imported private key for server authentication.
        // Retain ephemeral key storage on platforms whose TLS implementation supports it.
        private static X509KeyStorageFlags CertificateKeyStorage => OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.DefaultKeySet
            : X509KeyStorageFlags.EphemeralKeySet;

        internal void PrintSecurity()
        {
            Console.WriteLine("Security:                          {0}", _tls is null ? "plaintext" : "TLS 1.3, mutual authentication, pinned trust + hostname validation");
            if (_tls is not null)
            {
                Console.WriteLine("Credentials:                       {0}", _credentialSource);
                Console.WriteLine("Local certificate thumbprint:      {0}", _certificate!.Thumbprint);
            }
        }

        private static X509Certificate2 CreateLoopbackCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            names.AddIpAddress(IPAddress.IPv6Loopback);
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            var usages = new OidCollection
            {
                new("1.3.6.1.5.5.7.3.1"),
                new("1.3.6.1.5.5.7.3.2")
            };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(4));
            var bytes = generated.Export(X509ContentType.Pkcs12);
            try
            {
                return TlsCertificateLoader.LoadPkcs12(bytes, null, CertificateKeyStorage);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        public void Dispose() => _certificate?.Dispose();
    }
}
