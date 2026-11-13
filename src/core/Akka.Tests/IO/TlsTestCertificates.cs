// -----------------------------------------------------------------------
// <copyright file="TlsTestCertificates.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Akka.Tests.IO
{
    /// <summary>Mints the short-lived certificates the specs use, self-signed or signed by a test CA.</summary>
    internal static class TlsTestCertificates
    {
        public const string ServerAuth = "1.3.6.1.5.5.7.3.1";
        public const string ClientAuth = "1.3.6.1.5.5.7.3.2";
        public const string AnyPurpose = "2.5.29.37.0";
        private const string EkuExtensionOid = "2.5.29.37"; // the enhanced key usage extension

        /// <summary>
        /// Creates a self-signed certificate with an exportable private key. Times are minutes from now.
        /// No <paramref name="ekus"/> means no enhanced key usage extension.
        /// </summary>
        public static X509Certificate2 Create(string commonName, int notBeforeMinutes, int notAfterMinutes,
            bool rsa, params string[] ekus) =>
            Create(null, commonName, notBeforeMinutes, notAfterMinutes, rsa, ekus);

        /// <summary>Like <see cref="Create(string,int,int,bool,string[])"/>, but signed by <paramref name="issuer"/>.</summary>
        public static X509Certificate2 CreateIssued(X509Certificate2 issuer, string commonName, int notBeforeMinutes,
            int notAfterMinutes, params string[] ekus) =>
            Create(issuer, commonName, notBeforeMinutes, notAfterMinutes, false, ekus);

        /// <summary>Like <see cref="CreateIssued"/>, but the subject alternative name is <paramref name="dnsName"/> instead of <c>localhost</c>.</summary>
        public static X509Certificate2 CreateIssuedForDnsName(X509Certificate2 issuer, string commonName,
            int notBeforeMinutes, int notAfterMinutes, string dnsName, params string[] ekus) =>
            Create(issuer, new X500DistinguishedName($"CN={commonName}"), DateTimeOffset.UtcNow.AddMinutes(notBeforeMinutes),
                DateTimeOffset.UtcNow.AddMinutes(notAfterMinutes), false, ekus, dnsName: dnsName);

        /// <summary>Like <see cref="Create(string,int,int,bool,string[])"/>, with exact validity times.</summary>
        public static X509Certificate2 Create(string commonName, DateTimeOffset notBefore, DateTimeOffset notAfter,
            bool rsa, params string[] ekus) =>
            Create(null, new X500DistinguishedName($"CN={commonName}"), notBefore, notAfter, rsa, ekus);

        /// <summary>Like <see cref="Create(string,DateTimeOffset,DateTimeOffset,bool,string[])"/>, with any subject.</summary>
        public static X509Certificate2 Create(X500DistinguishedName subject, DateTimeOffset notBefore,
            DateTimeOffset notAfter, bool rsa, params string[] ekus) =>
            Create(null, subject, notBefore, notAfter, rsa, ekus);

        /// <summary>
        /// Like <see cref="Create(string,int,int,bool,string[])"/>, but the enhanced key usage extension holds bytes
        /// that are not a valid list of usages, so reading <c>EnhancedKeyUsages</c> throws.
        /// </summary>
        public static X509Certificate2 CreateWithUndecodableEku(string commonName, int notBeforeMinutes,
            int notAfterMinutes, bool rsa) =>
            Create(null, new X500DistinguishedName($"CN={commonName}"), DateTimeOffset.UtcNow.AddMinutes(notBeforeMinutes),
                DateTimeOffset.UtcNow.AddMinutes(notAfterMinutes), rsa, Array.Empty<string>(), undecodableEku: true);

        /// <summary>
        /// Like <see cref="Create(string,int,int,bool,string[])"/>, with this key usage (<c>null</c> for no key usage
        /// extension) and, when <paramref name="authority"/> is true, the basic constraints of a CA.
        /// </summary>
        public static X509Certificate2 Create(string commonName, int notBeforeMinutes, int notAfterMinutes, bool rsa,
            X509KeyUsageFlags? keyUsage, bool authority, params string[] ekus) =>
            Create(null, new X500DistinguishedName($"CN={commonName}"), DateTimeOffset.UtcNow.AddMinutes(notBeforeMinutes),
                DateTimeOffset.UtcNow.AddMinutes(notAfterMinutes), rsa, ekus, keyUsage: keyUsage, authority: authority);

        public static byte[] NewSerial()
        {
            var serial = Guid.NewGuid().ToByteArray();
            serial[0] = 0x01; // positive
            return serial;
        }

        private static X509Certificate2 Create(X509Certificate2? issuer, string commonName, int notBeforeMinutes,
            int notAfterMinutes, bool rsa, string[] ekus) =>
            Create(issuer, new X500DistinguishedName($"CN={commonName}"), DateTimeOffset.UtcNow.AddMinutes(notBeforeMinutes),
                DateTimeOffset.UtcNow.AddMinutes(notAfterMinutes), rsa, ekus);

        private static X509Certificate2 Create(X509Certificate2? issuer, X500DistinguishedName subject,
            DateTimeOffset notBefore, DateTimeOffset notAfter, bool rsa, string[] ekus, bool undecodableEku = false,
            X509KeyUsageFlags? keyUsage = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            bool authority = false, string dnsName = "localhost")
        {
            using var key = rsa ? (AsymmetricAlgorithm)RSA.Create(2048) : ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = key is RSA rsaKey
                ? new CertificateRequest(subject, rsaKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                : new CertificateRequest(subject, (ECDsa)key, HashAlgorithmName.SHA256);

            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName(dnsName);
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(authority, false, 0, authority));
            if (keyUsage is not null)
                request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsage.Value, true));
            if (undecodableEku)
                request.CertificateExtensions.Add(new X509Extension(new Oid(EkuExtensionOid), new byte[] { 0x04, 0x02, 0xDE, 0xAD }, false));
            if (ekus.Length > 0)
            {
                var usages = new OidCollection();
                foreach (var eku in ekus)
                    usages.Add(new Oid(eku));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
            }

            using var generated = issuer is null
                ? request.CreateSelfSigned(notBefore, notAfter)
                : WithKey(request.Create(issuer, notBefore, notAfter, NewSerial()), key);
            var pkcs12 = generated.Export(X509ContentType.Pkcs12);
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(pkcs12, null,
                X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable);
#else
            return new X509Certificate2(pkcs12, (string?)null,
                X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable);
#endif
        }

        // Attaches the key to a certificate that a CA signed, and disposes the keyless one.
        private static X509Certificate2 WithKey(X509Certificate2 signed, AsymmetricAlgorithm key)
        {
            using (signed)
                return key is RSA rsa ? signed.CopyWithPrivateKey(rsa) : signed.CopyWithPrivateKey((ECDsa)key);
        }

        public static X509Certificate2 WithoutKey(X509Certificate2 certificate) =>
#if NET9_0_OR_GREATER
            X509CertificateLoader.LoadCertificate(certificate.RawData);
#else
            new X509Certificate2(certificate.RawData);
#endif
    }
}
