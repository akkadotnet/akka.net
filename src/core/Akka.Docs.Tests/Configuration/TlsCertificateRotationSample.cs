//-----------------------------------------------------------------------
// <copyright file="TlsCertificateRotationSample.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.IO;
using Akka.Remote.Transport.DotNetty;
using Dns = System.Net.Dns;

namespace Akka.Docs.Tests.Configuration
{
    /// <summary>
    /// TLS certificate rotation examples for the "Rotating TLS Certificates" article.
    /// These samples only need to compile. They name certificates and folders that do not exist.
    /// </summary>
    public sealed class TlsCertificateRotationSample
    {
        #region RotationStoreInCode
        /// <summary>
        /// Pick the certificate from a certificate store by name, and start an ActorSystem with it.
        /// </summary>
        public static ActorSystem StartWithCertificateStore()
        {
            // Each server looks for the certificate that carries its own name
            var serverName = Dns.GetHostEntry(Dns.GetHostName()).HostName;

            var sslSetup = DotNettySslSetup.FromSource(
                TlsCertificateSource.FromStore(serverName, "My", StoreLocation.LocalMachine),
                suppressValidation: false,
                requireMutualAuthentication: true,
                validateCertificateHostname: false,
                customValidator: null);

            var config = ConfigurationFactory.ParseString(@"
                akka.actor.provider = remote
                akka.remote.dot-netty.tcp.enable-ssl = true
            ");

            return ActorSystem.Create("my-system", BootstrapSetup.Create().WithConfig(config).And(sslSetup));
        }
        #endregion

        #region RotationFolderInCode
        /// <summary>
        /// Pick the certificate from a folder of certificate files, and start an ActorSystem with it.
        /// </summary>
        public static ActorSystem StartWithCertificateFolder()
        {
            var sslSetup = DotNettySslSetup.FromSource(
                TlsCertificateSource.FromFolder("/etc/akka/tls", password: null),
                suppressValidation: false,
                requireMutualAuthentication: true,
                validateCertificateHostname: false,
                customValidator: null);

            var config = ConfigurationFactory.ParseString(@"
                akka.actor.provider = remote
                akka.remote.dot-netty.tcp.enable-ssl = true
            ");

            return ActorSystem.Create("my-system", BootstrapSetup.Create().WithConfig(config).And(sslSetup));
        }
        #endregion

        #region RotationCodeBefore
        public static DotNettySslSetup SetupBeforeRotation(
            X509Certificate2 certificate, CertificateValidationCallback validator)
        {
            // A fixed certificate: loaded once, used until the node restarts
            return new DotNettySslSetup(
                certificate,
                suppressValidation: false,
                requireMutualAuthentication: true,
                validateCertificateHostname: false,
                customValidator: validator);
        }
        #endregion

        #region RotationCodeAfter
        public static DotNettySslSetup SetupWithRotation(CertificateValidationCallback validator)
        {
            // A source: asked for the certificate each time the node builds a connection
            return DotNettySslSetup.FromSource(
                TlsCertificateSource.FromStore("akka-node.contoso.com", "My", StoreLocation.LocalMachine),
                suppressValidation: false,
                requireMutualAuthentication: true,
                validateCertificateHostname: false,
                customValidator: validator);
        }
        #endregion

        #region RotationTrustByName
        public static DotNettySslSetup TrustByName()
        {
            // Accept any certificate from a CA the machine trusts, if it carries the expected name
            var trust = CertificateValidation.Combine(
                CertificateValidation.ValidateChain(),
                CertificateValidation.ValidateHostname("akka-node.contoso.com"));

            return DotNettySslSetup.FromSource(
                TlsCertificateSource.FromStore("akka-node.contoso.com", "My", StoreLocation.LocalMachine),
                suppressValidation: false,
                requireMutualAuthentication: true,
                validateCertificateHostname: false,
                customValidator: trust);
        }
        #endregion

        #region RotationTrustOwnCa
        public static CertificateValidationCallback TrustOwnCa()
        {
            // Accept only certificates that your own CA issued
            return CertificateValidation.Combine(
                CertificateValidation.ValidateChain(),
                CertificateValidation.ValidateIssuer("CN=Contoso Issuing CA*"));
        }
        #endregion

        #region RotationTrustPinnedList
        public static CertificateValidationCallback TrustPinnedList(
            X509Certificate2 currentCertificate, X509Certificate2 nextCertificate)
        {
            // The list is fixed when the node starts
            return CertificateValidation.PinnedCertificate(
                currentCertificate.Thumbprint,
                nextCertificate.Thumbprint);
        }
        #endregion
    }
}
