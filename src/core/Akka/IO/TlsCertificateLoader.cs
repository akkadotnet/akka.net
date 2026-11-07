// -----------------------------------------------------------------------
// <copyright file="TlsCertificateLoader.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Akka.IO
{
    /// <summary>
    /// Loads caller-owned certificates with an accessible RSA or ECDSA private key for use by Akka.IO TLS.
    /// </summary>
    public static class TlsCertificateLoader
    {
        /// <summary>Loads a PKCS#12 certificate with its private key from a file.</summary>
        /// <param name="path">Path to a PKCS#12 file.</param>
        /// <param name="password">Password used to open the file, or <c>null</c> when no password is set.</param>
        /// <param name="keyStorageFlags">Flags that control how the private key is imported.</param>
        /// <returns>A caller-owned certificate with an accessible RSA or ECDSA private key.</returns>
        /// <remarks>The caller must dispose the returned certificate after its Akka.IO TLS connections have stopped.</remarks>
        public static X509Certificate2 LoadPkcs12FromFile(string path, string? password,
            X509KeyStorageFlags keyStorageFlags)
        {
            ArgumentNullException.ThrowIfNull(path);
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("The certificate file path cannot be empty or whitespace.", nameof(path));

            var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password, keyStorageFlags);
            return ValidateAndReturn(certificate);
        }

        /// <summary>Loads a PKCS#12 certificate with its private key from a byte array.</summary>
        /// <param name="data">PKCS#12 data.</param>
        /// <param name="password">Password used to open the data, or <c>null</c> when no password is set.</param>
        /// <param name="keyStorageFlags">Flags that control how the private key is imported.</param>
        /// <returns>A caller-owned certificate with an accessible RSA or ECDSA private key.</returns>
        /// <remarks>The caller must dispose the returned certificate after its Akka.IO TLS connections have stopped.</remarks>
        public static X509Certificate2 LoadPkcs12(byte[] data, string? password,
            X509KeyStorageFlags keyStorageFlags)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Length == 0)
                throw new ArgumentException("The PKCS#12 data cannot be empty.", nameof(data));

            var certificate = X509CertificateLoader.LoadPkcs12(data, password, keyStorageFlags);
            return ValidateAndReturn(certificate);
        }

        /// <summary>Finds a certificate by thumbprint in a certificate store.</summary>
        /// <param name="thumbprint">The certificate thumbprint. Spaces and other whitespace are ignored.</param>
        /// <param name="storeName">The store name; defaults to <c>My</c>.</param>
        /// <param name="storeLocation">The store location; defaults to the current user.</param>
        /// <param name="validOnly">Whether the store search should filter certificates to valid certificates.</param>
        /// <returns>A caller-owned matching certificate with an accessible RSA or ECDSA private key.</returns>
        /// <remarks>The caller must dispose the returned certificate after its Akka.IO TLS connections have stopped.</remarks>
        public static X509Certificate2 LoadFromStore(string thumbprint, string storeName = "My",
            StoreLocation storeLocation = StoreLocation.CurrentUser, bool validOnly = true)
        {
            ArgumentNullException.ThrowIfNull(thumbprint);
            ArgumentNullException.ThrowIfNull(storeName);
            if (string.IsNullOrWhiteSpace(storeName))
                throw new ArgumentException("The certificate store name cannot be empty or whitespace.", nameof(storeName));

            var normalizedThumbprint = NormalizeThumbprint(thumbprint);
            if (!IsSha1Thumbprint(normalizedThumbprint))
                throw new ArgumentException("The certificate thumbprint must contain 40 hexadecimal characters.", nameof(thumbprint));

            using var store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var sourceCertificates = store.Certificates;
            X509Certificate2Collection certificates;
            try
            {
                certificates = sourceCertificates.Find(X509FindType.FindByThumbprint, normalizedThumbprint, validOnly);
            }
            finally
            {
                foreach (var certificate in sourceCertificates)
                    certificate.Dispose();
            }

            X509Certificate2? selected = null;
            Exception? keyAccessFailure = null;
            try
            {
                foreach (var certificate in certificates)
                {
                    try
                    {
                        TlsSettingsValidation.ValidateCertificate(certificate, nameof(thumbprint));
                    }
                    catch (Exception exception) when (exception is ArgumentException or CryptographicException or InvalidOperationException)
                    {
                        keyAccessFailure = exception;
                        continue;
                    }

                    selected = certificate;
                    return selected;
                }

                var message = $"No certificate with thumbprint '{normalizedThumbprint}' and an accessible RSA or ECDSA private key was found in store '{storeName}' at '{storeLocation}'.";
                throw new InvalidOperationException(message, keyAccessFailure);
            }
            finally
            {
                foreach (var certificate in certificates)
                {
                    if (!ReferenceEquals(certificate, selected))
                        certificate.Dispose();
                }
            }
        }

        private static X509Certificate2 ValidateAndReturn(X509Certificate2 certificate)
        {
            try
            {
                TlsSettingsValidation.ValidateCertificate(certificate, nameof(certificate));
                return certificate;
            }
            catch
            {
                certificate.Dispose();
                throw;
            }
        }

        private static string NormalizeThumbprint(string thumbprint)
        {
            var builder = new StringBuilder(thumbprint.Length);
            foreach (var character in thumbprint)
            {
                if (!char.IsWhiteSpace(character))
                    builder.Append(char.ToUpperInvariant(character));
            }

            return builder.ToString();
        }

        private static bool IsSha1Thumbprint(string value)
        {
            if (value.Length != 40)
                return false;

            foreach (var character in value)
            {
                if (!Uri.IsHexDigit(character))
                    return false;
            }

            return true;
        }
    }
}
