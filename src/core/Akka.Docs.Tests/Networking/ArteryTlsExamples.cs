// -----------------------------------------------------------------------
// <copyright file="ArteryTlsExamples.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Hosting;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.Remote.Hosting;

namespace DocsExamples.Networking.Artery
{
    public static class ArteryTlsExamples
    {
        #region arteryTlsDirectSetup
        public static ActorSystemSetup CreateMutualTlsSetup(
            Config arteryConfig,
            X509Certificate2 nodeCertificate,
            TlsPeerPolicy inboundClientPolicy,
            TlsPeerPolicy outboundServerPolicy)
        {
            var tls = ArteryTlsSettings.Mutual(
                nodeCertificate, inboundClientPolicy, outboundServerPolicy)
                .WithHandshakeTimeout(TimeSpan.FromSeconds(10));

            return ActorSystemSetup.Create(
                BootstrapSetup.Create().WithConfig(arteryConfig),
                new ArteryTlsSetup(tls));
        }
        #endregion

        #region arteryTlsHosting
        public static AkkaConfigurationBuilder ConfigureArteryTls(
            AkkaConfigurationBuilder builder,
            X509Certificate2 nodeCertificate,
            TlsPeerPolicy peerPolicy)
        {
            var tls = ArteryTlsSettings.Mutual(nodeCertificate, peerPolicy);

            return builder.WithArteryRemoting(options =>
            {
                options.HostName = "node.example.net";
                options.Port = 25520;
                options.Tls = tls;
            });
        }
        #endregion
    }
}
