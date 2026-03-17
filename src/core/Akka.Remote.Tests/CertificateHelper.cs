//-----------------------------------------------------------------------
// <copyright file="CertificateHelper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;

namespace Akka.Remote.Tests
{
    /// <summary>
    /// Helper for loading X509 certificates in a way that's compatible with both
    /// pre-.NET 10 and .NET 10+ runtimes.
    /// </summary>
    internal static class CertificateHelper
    {
        public static X509Certificate2 LoadPkcs12(
            string path,
            string? password,
            X509KeyStorageFlags flags = X509KeyStorageFlags.DefaultKeySet)
        {
#if NET10_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12FromFile(path, password, flags);
#else
            return new X509Certificate2(path, password, flags);
#endif
        }
    }
}
