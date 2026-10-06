// -----------------------------------------------------------------------
// <copyright file="TlsCertificateLoaderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Akka.IO;
using Xunit;

namespace Akka.Tests.IO
{
    public sealed class TlsCertificateLoaderSpec
    {
        private const string Password = "test-password";

        [Fact(DisplayName = "Should_Load_Caller_Owned_Private_Key_Certificates_From_PKCS12_File_And_Bytes")]
        public void Should_load_caller_owned_private_key_certificates_from_pkcs12_file_and_bytes()
        {
            using var source = CreateCertificate("loader.example.net");
            var pfx = source.Export(X509ContentType.Pkcs12, Password);
            var path = Path.Combine(Path.GetTempPath(), $"akka-tls-{Guid.NewGuid():N}.p12");

            try
            {
                File.WriteAllBytes(path, pfx);
                using var fromFile = TlsCertificateLoader.LoadPkcs12FromFile(
                    path, Password, X509KeyStorageFlags.DefaultKeySet);
                using var fromBytes = TlsCertificateLoader.LoadPkcs12(
                    pfx, Password, X509KeyStorageFlags.DefaultKeySet);

                Assert.True(fromFile.HasPrivateKey);
                Assert.True(fromBytes.HasPrivateKey);
                Assert.Equal(source.Thumbprint, fromFile.Thumbprint);
                Assert.Equal(source.Thumbprint, fromBytes.Thumbprint);
                using var fileKey = fromFile.GetRSAPrivateKey();
                using var byteKey = fromBytes.GetRSAPrivateKey();
                Assert.NotNull(fileKey);
                Assert.NotNull(byteKey);
            }
            finally
            {
                File.Delete(path);
                CryptographicOperations.ZeroMemory(pfx);
            }
        }

        [Fact(DisplayName = "Should_Reject_Invalid_PKCS12_Sources_And_Missing_Private_Keys")]
        public void Should_reject_invalid_pkcs12_sources_and_missing_private_keys()
        {
            using var certificate = CreateCertificate("loader.example.net");
            var pfx = certificate.Export(X509ContentType.Pkcs12, Password);
            var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
            var publicOnlyPfx = publicOnly.Export(X509ContentType.Pkcs12);
            publicOnly.Dispose();

            try
            {
                Assert.Throws<ArgumentException>(() =>
                    TlsCertificateLoader.LoadPkcs12(Array.Empty<byte>(), Password, X509KeyStorageFlags.DefaultKeySet));
                Assert.Throws<CryptographicException>(() =>
                    TlsCertificateLoader.LoadPkcs12(new byte[] { 1, 2, 3 }, Password, X509KeyStorageFlags.DefaultKeySet));
                Assert.Throws<CryptographicException>(() =>
                    TlsCertificateLoader.LoadPkcs12(pfx, "wrong-password", X509KeyStorageFlags.DefaultKeySet));
                Assert.Throws<ArgumentException>(() =>
                    TlsCertificateLoader.LoadPkcs12(publicOnlyPfx, null, X509KeyStorageFlags.DefaultKeySet));
                var missingFileError = Assert.Throws<CryptographicException>(() => TlsCertificateLoader.LoadPkcs12FromFile(
                    Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.p12"),
                    Password, X509KeyStorageFlags.DefaultKeySet));
                Assert.IsType<FileNotFoundException>(missingFileError.InnerException);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pfx);
                CryptographicOperations.ZeroMemory(publicOnlyPfx);
            }
        }

        [Fact(DisplayName = "Should_Return_An_Independent_Store_Certificate_For_A_Normalized_Thumbprint")]
        public void Should_return_an_independent_store_certificate_with_a_normalized_thumbprint()
        {
            using var certificate = CreateCertificate("store.example.net");
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);

            try
            {
                var normalizedThumbprint = string.Join(" ", certificate.Thumbprint.ToLowerInvariant().ToCharArray());
                Assert.Throws<InvalidOperationException>(() => TlsCertificateLoader.LoadFromStore(normalizedThumbprint));
                using var loaded = TlsCertificateLoader.LoadFromStore(normalizedThumbprint, validOnly: false);
                Assert.Equal(certificate.Thumbprint, loaded.Thumbprint);
                using var loadedKey = loaded.GetRSAPrivateKey();
                Assert.NotNull(loadedKey);

                store.Remove(certificate);
                using var retainedKey = loaded.GetRSAPrivateKey();
                Assert.NotNull(retainedKey);
            }
            finally
            {
                store.Remove(certificate);
            }
        }

        [Fact(DisplayName = "Should_Reject_Invalid_Or_Missing_Store_Certificate_Thumbprints")]
        public void Should_reject_invalid_or_missing_store_certificate_thumbprints()
        {
            using var certificate = CreateCertificate("missing-thumbprint.example.net");
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);

            try
            {
                Assert.Throws<ArgumentNullException>(() => TlsCertificateLoader.LoadFromStore(null!));
                Assert.Throws<ArgumentException>(() => TlsCertificateLoader.LoadFromStore("not-a-thumbprint"));
                Assert.Throws<InvalidOperationException>(() =>
                    TlsCertificateLoader.LoadFromStore("0000000000000000000000000000000000000000"));
            }
            finally
            {
                store.Remove(certificate);
            }
        }

        private static X509Certificate2 CreateCertificate(string subject)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={subject}", rsa, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            using var generatedCertificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var exportedCertificate = generatedCertificate.Export(X509ContentType.Pkcs12);
            try
            {
                return X509CertificateLoader.LoadPkcs12(
                    exportedCertificate,
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.DefaultKeySet);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(exportedCertificate);
            }
        }
    }
}
