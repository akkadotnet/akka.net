// -----------------------------------------------------------------------
//  <copyright file="AkkaRemoteHostingExtensions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Actor;
using Akka.Hosting;

namespace Akka.Remote.Hosting
{
    public static class AkkaRemoteHostingExtensions
    {
        /// <summary>
        /// Adds Akka.Remote support to this <see cref="ActorSystem"/>.
        /// </summary>
        /// <param name="builder">A configuration delegate.</param>
        /// <param name="hostname">Optional. The hostname to bind Akka.Remote upon. <b>Default</b>: "0.0.0.0"</param>
        /// <param name="port">Optional. The port to bind Akka.Remote upon. <b>Default</b>: 2552</param>
        /// <param name="publicHostname">Optional. If using hostname aliasing, this is the host we will advertise.</param>
        /// <param name="publicPort">Optional. If using port aliasing, this is the port we will advertise.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        public static AkkaConfigurationBuilder WithRemoting(
            this AkkaConfigurationBuilder builder,
            string? hostname = null,
            int? port = null,
            string? publicHostname = null,
            int? publicPort = null)
            => builder.WithRemoting(new RemoteOptions
            {
                HostName = hostname,
                Port = port,
                PublicHostName = publicHostname,
                PublicPort = publicPort
            });

        /// <summary>
        /// Adds Akka.Remote support to this <see cref="ActorSystem"/>.
        /// </summary>
        /// <param name="builder">A configuration delegate.</param>
        /// <param name="configure">
        ///     An <see cref="Action{T}"/> delegate used to configure an <see cref="RemoteOptions"/>
        ///     instance to configure Akka.Remote
        /// </param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        public static AkkaConfigurationBuilder WithRemoting(
            this AkkaConfigurationBuilder builder,
            Action<RemoteOptions> configure)
        {
            var options = new RemoteOptions();
            configure(options);
            return builder.WithRemoting(options);
        }

        /// <summary>
        /// Adds Akka.Remote support to this <see cref="ActorSystem"/>.
        /// </summary>
        /// <param name="builder">A configuration delegate.</param>
        /// <param name="options">
        ///     A <see cref="RemoteOptions"/> instance to configure Akka.Remote
        /// </param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        public static AkkaConfigurationBuilder WithRemoting(
            this AkkaConfigurationBuilder builder,
            RemoteOptions options)
        {
            options.Build(builder);

            if (builder.ActorRefProvider.HasValue)
            {
                switch (builder.ActorRefProvider.Value)
                {
                    case ProviderSelection.Cluster _:
                    case ProviderSelection.Remote _:
                    case ProviderSelection.Custom _:
                        return builder; // no-op
                }
            }

            return builder.WithActorRefProvider(ProviderSelection.Remote.Instance);
        }
    }
}