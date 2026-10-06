//-----------------------------------------------------------------------
// <copyright file="TlsExamples.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Event;
using Akka.IO;

namespace DocsExamples.Networking.IO
{
    public static class TlsExamples
    {
        #region tlsServerOnly
        public static void ConfigureServerOnlyTls(
            IActorRef tcpManager,
            IActorRef serverHandler,
            IActorRef serverBindCommander,
            IActorRef clientCommander,
            X509Certificate2 serverCertificate)
        {
            var serverTls = TlsServerSettings.ServerOnly(serverCertificate);
            var clientTls = TlsClientSettings.ServerOnly(
                    TlsPeerPolicy.SystemTrust().And(TlsCertificateValidation.ValidateHostname()))
                .WithHandshakeTimeout(TimeSpan.FromSeconds(5));

            tcpManager.Tell(new Tcp.Bind(serverHandler, new IPEndPoint(IPAddress.Any, 8443))
            {
                Tls = serverTls
            }, serverBindCommander);

            tcpManager.Tell(new Tcp.Connect(new DnsEndPoint("server.example.net", 8443))
            {
                Tls = clientTls
            }, clientCommander);
        }
        #endregion

        #region tlsMutual
        public static void ConfigureMutualTls(
            IActorRef tcpManager,
            IActorRef serverHandler,
            IActorRef serverBindCommander,
            IActorRef clientCommander,
            X509Certificate2 serverCertificate,
            X509Certificate2 clientCertificate)
        {
            var serverTls = TlsServerSettings.Mutual(serverCertificate, TlsPeerPolicy.SystemTrust());
            var clientTls = TlsClientSettings.Mutual(
                    clientCertificate,
                    TlsPeerPolicy.SystemTrust().And(TlsCertificateValidation.ValidateHostname()));

            tcpManager.Tell(new Tcp.Bind(serverHandler, new IPEndPoint(IPAddress.Any, 8443))
            {
                Tls = serverTls
            }, serverBindCommander);

            tcpManager.Tell(new Tcp.Connect(new DnsEndPoint("server.example.net", 8443))
            {
                Tls = clientTls
            }, clientCommander);
        }
        #endregion

        #region tlsPinnedCertificate
        public static TlsClientSettings CreateSelfSignedPinnedClientSettings(string serverThumbprint)
        {
            var serverTrust = TlsPeerPolicy.PinnedCertificates(serverThumbprint)
                .And(TlsCertificateValidation.ValidateHostname("localhost"));

            return TlsClientSettings.ServerOnly(serverTrust);
        }
        #endregion

        #region tlsCustomTrust
        public static TlsClientSettings CreateCustomTrustClientSettings(
            string expectedSubject,
            string expectedIssuer,
            string expectedServerName)
        {
            var trust = TlsPeerPolicy.CustomTrust((certificate, chain, remotePeer, errors, log) =>
            {
                var chainErrors = errors & ~System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch;
                var accepted = certificate is not null &&
                               chainErrors == System.Net.Security.SslPolicyErrors.None &&
                               string.Equals(certificate.Subject, expectedSubject, StringComparison.Ordinal) &&
                               string.Equals(certificate.Issuer, expectedIssuer, StringComparison.Ordinal);
                if (!accepted)
                {
                    log.Warning("TLS custom trust rejected peer {0}; policy errors: {1}; chain elements: {2}",
                        remotePeer, errors, chain?.ChainElements.Count ?? 0);
                }

                return accepted;
            }).And(TlsCertificateValidation.ValidateHostname(expectedServerName));

            return TlsClientSettings.ServerOnly(trust).WithTargetHost(expectedServerName);
        }
        #endregion

        #region tlsCertificateLoading
        public static X509Certificate2 LoadCertificateFromPkcs12File(string filePath, string password)
        {
            return TlsCertificateLoader.LoadPkcs12FromFile(
                filePath, password, X509KeyStorageFlags.DefaultKeySet);
        }

        public static X509Certificate2 LoadCertificateFromPkcs12Bytes(byte[] pkcs12Bytes, string password)
        {
            return TlsCertificateLoader.LoadPkcs12(
                pkcs12Bytes, password, X509KeyStorageFlags.DefaultKeySet);
        }

        public static X509Certificate2 LoadCertificateFromStore(string thumbprint)
        {
            return TlsCertificateLoader.LoadFromStore(
                thumbprint, storeName: "My", storeLocation: StoreLocation.CurrentUser, validOnly: true);
        }
        #endregion

        #region tlsTrustPolicyComposition
        public static TlsPeerPolicy CreatePrivateCaPeerPolicy(string issuerPattern, string expectedHostname)
        {
            return TlsPeerPolicy.SystemTrust().And(
                TlsCertificateValidation.ValidateIssuer(issuerPattern),
                TlsCertificateValidation.ValidateHostname(expectedHostname));
        }

        public static TlsPeerPolicy CreatePinnedPeerPolicy(string thumbprint, string expectedHostname)
        {
            return TlsPeerPolicy.PinnedCertificates(thumbprint)
                .And(TlsCertificateValidation.ValidateHostname(expectedHostname));
        }
        #endregion
    }
}
