// -----------------------------------------------------------------------
// <copyright file="ArteryRemoteHostingExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Remote.Artery;

namespace Akka.Remote.Hosting
{
    /// <summary>Akka.Hosting extensions for configuring Artery remoting.</summary>
    public static class ArteryRemoteHostingExtensions
    {
        /// <summary>Adds Artery remoting and optional node-level TLS settings to this actor system.</summary>
        /// <param name="builder">The Akka.Hosting builder.</param>
        /// <param name="configure">A delegate that configures Artery host, port, and optional TLS settings.</param>
        /// <returns>The same builder instance.</returns>
        public static AkkaConfigurationBuilder WithArteryRemoting(
            this AkkaConfigurationBuilder builder, Action<ArteryRemoteOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            var options = new ArteryRemoteOptions();
            configure(options);
            Validate(options);

            var existingConfig = builder.Configuration.GetOrElse(Config.Empty);
            var hocon = new StringBuilder("akka.remote.artery.enabled = on\n");
            if (options.HostName is not null)
                hocon.Append("akka.remote.artery.canonical.hostname = \"")
                    .Append(EscapeHoconString(options.HostName))
                    .AppendLine("\"");
            if (options.Port.HasValue)
                hocon.Append("akka.remote.artery.canonical.port = ").AppendLine(options.Port.Value.ToString());

            builder.AddHocon(ConfigurationFactory.ParseString(hocon.ToString()), HoconAddMode.Prepend);

            if (options.Tls is not null)
            {
                builder.Setups.RemoveWhere(setup => setup is ArteryTlsSetup);
                builder.AddSetup(new ArteryTlsSetup(options.Tls));
            }

            if (builder.ActorRefProvider.HasValue)
            {
                if (builder.ActorRefProvider.Value is ProviderSelection.Local)
                    builder.WithActorRefProvider(ProviderSelection.Remote.Instance);
            }
            else if (!existingConfig.HasPath("akka.actor.provider") ||
                     IsLocalProvider(existingConfig.GetString("akka.actor.provider")))
            {
                builder.WithActorRefProvider(ProviderSelection.Remote.Instance);
            }

            return builder;
        }

        private static bool IsLocalProvider(string provider)
        {
            if (string.Equals(provider, "local", StringComparison.OrdinalIgnoreCase))
                return true;

            var comma = provider.IndexOf(',');
            var typeName = (comma < 0 ? provider : provider.Substring(0, comma)).Trim();
            return string.Equals(typeName, "Akka.Actor.LocalActorRefProvider", StringComparison.Ordinal);
        }

        private static void Validate(ArteryRemoteOptions options)
        {
            if (options.HostName is not null)
            {
                if (string.IsNullOrWhiteSpace(options.HostName))
                    throw new ArgumentException("The Artery hostname cannot be empty or whitespace.", nameof(options));

                foreach (var character in options.HostName)
                {
                    if (char.IsControl(character))
                        throw new ArgumentException("The Artery hostname cannot contain control characters.", nameof(options));
                }
            }

            if (options.Port is < 0 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(options), options.Port,
                    "The Artery port must be between 0 and 65535.");
        }

        private static string EscapeHoconString(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
