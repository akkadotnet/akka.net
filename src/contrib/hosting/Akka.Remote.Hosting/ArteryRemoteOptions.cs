// -----------------------------------------------------------------------
// <copyright file="ArteryRemoteOptions.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

namespace Akka.Remote.Hosting
{
    /// <summary>Options for configuring Artery remoting through Akka.Hosting.</summary>
    public sealed class ArteryRemoteOptions
    {
        /// <summary>The hostname or IP address Artery binds to.</summary>
        public string? HostName { get; set; }

        /// <summary>The local Artery port. Use zero to request an available port.</summary>
        public int? Port { get; set; }

        /// <summary>The optional node-level TLS profile shared by all Artery TCP channels.</summary>
        public Akka.Remote.Artery.ArteryTlsSettings? Tls { get; set; }
    }
}
