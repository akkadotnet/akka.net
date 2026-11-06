//-----------------------------------------------------------------------
// <copyright file="MauiAkkaHostingExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Hosting;

namespace Akka.Hosting.Maui
{
    /// <summary>
    /// Akka.Hosting for .NET MAUI applications.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AddAkka</c> relies on the host starting its <see cref="IHostedService"/>, and <c>MauiApp</c> never does
    /// (https://github.com/dotnet/maui/issues/2244), so <c>AddAkka</c> throws inside a MAUI app. <c>AddAkkaMaui</c>
    /// registers the same <see cref="ActorSystem"/>, <see cref="ActorRegistry"/> and <see cref="IRequiredActor{TKey}"/>
    /// services, and starts the system from an <see cref="IMauiInitializeService"/> instead. MAUI runs those inside
    /// <c>MauiAppBuilder.Build()</c>, so by the time <c>Build()</c> returns the <see cref="ActorSystem"/> is up and every
    /// <c>WithActors</c> / <c>StartActors</c> callback has run - the same guarantee <c>IHost.StartAsync</c> gives.
    /// </para>
    /// <para>
    /// Shutdown: the <see cref="ActorSystem"/> is a singleton in the app's service provider, so disposing the
    /// <c>MauiApp</c> terminates it, and <see cref="CoordinatedShutdown"/> also runs on process exit by default
    /// (<c>akka.coordinated-shutdown.run-by-clr-shutdown-hook</c>). Mobile platforms can kill an app without either;
    /// call <c>CoordinatedShutdown.Get(system).Run(...)</c> from your app's lifecycle events if you need a clean stop.
    /// </para>
    /// </remarks>
    public static class MauiAkkaHostingExtensions
    {
        /// <summary>
        /// Registers an <see cref="ActorSystem"/> for a .NET MAUI app and starts it while <c>MauiAppBuilder.Build()</c> runs.
        /// </summary>
        /// <param name="services">The MAUI app's service collection (<c>MauiAppBuilder.Services</c>).</param>
        /// <param name="actorSystemName">The name of the <see cref="ActorSystem"/> that will be instantiated.</param>
        /// <param name="builder">A configuration delegate.</param>
        /// <returns>The <see cref="IServiceCollection"/> instance.</returns>
        public static IServiceCollection AddAkkaMaui(this IServiceCollection services, string actorSystemName,
            Action<AkkaConfigurationBuilder> builder)
        {
            return services.AddAkkaMaui(actorSystemName, (configurationBuilder, _) => builder(configurationBuilder));
        }

        /// <summary>
        /// Registers an <see cref="ActorSystem"/> for a .NET MAUI app and starts it while <c>MauiAppBuilder.Build()</c> runs.
        /// </summary>
        /// <param name="services">The MAUI app's service collection (<c>MauiAppBuilder.Services</c>).</param>
        /// <param name="actorSystemName">The name of the <see cref="ActorSystem"/> that will be instantiated.</param>
        /// <param name="builder">A configuration delegate that accepts an <see cref="IServiceProvider"/>.</param>
        /// <returns>The <see cref="IServiceCollection"/> instance.</returns>
        public static IServiceCollection AddAkkaMaui(this IServiceCollection services, string actorSystemName,
            Action<AkkaConfigurationBuilder, IServiceProvider> builder)
        {
            AkkaHostingExtensions.RegisterActorSystem(services, actorSystemName, builder);

            // Not registered as an IHostedService: MAUI would not start it, and if a future MAUI version does, the
            // ActorSystem must not be started twice.
            services.AddSingleton<MauiAkkaService>(sp => new MauiAkkaService(
                sp.GetRequiredService<AkkaConfigurationBuilder>(),
                sp,
                sp.GetService<ILoggerFactory>()?.CreateLogger<AkkaHostedService>() ?? NullLogger<AkkaHostedService>.Instance,
                // MAUI registers no IHostApplicationLifetime; null means "don't stop the app when the ActorSystem terminates"
                sp.GetService<IHostApplicationLifetime>()));

            services.TryAddEnumerable(
                ServiceDescriptor.Transient<IMauiInitializeService, MauiAkkaInitializer>(_ => new MauiAkkaInitializer()));

            return services;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <remarks>
    /// The <see cref="AkkaHostedService"/> that Akka.Hosting.Maui starts. Its own type so it can never be confused
    /// with an <see cref="IHostedService"/> registration.
    /// </remarks>
    internal sealed class MauiAkkaService : AkkaHostedService
    {
        public MauiAkkaService(AkkaConfigurationBuilder configurationBuilder, IServiceProvider serviceProvider,
            ILogger<AkkaHostedService> logger, IHostApplicationLifetime? applicationLifetime)
            : base(configurationBuilder, serviceProvider, logger, applicationLifetime)
        {
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <remarks>
    /// Runs inside <c>MauiAppBuilder.Build()</c>, on the UI thread, before any page exists.
    /// </remarks>
    internal sealed class MauiAkkaInitializer : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services)
        {
            var akka = services.GetRequiredService<MauiAkkaService>();

            // Blocks Build() until the ActorSystem is up, like IHost.StartAsync does before an app runs. Task.Run keeps
            // the start off the UI thread's SynchronizationContext, so a continuation inside StartAsync that is not
            // ConfigureAwait(false) cannot deadlock against this wait. A failed start throws out of Build().
            Task.Run(() => akka.StartAsync(CancellationToken.None)).GetAwaiter().GetResult();
        }
    }
}
