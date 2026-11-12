// -----------------------------------------------------------------------
// <copyright file="MultiNodeTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Net.Quic;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.IO;
using Akka.Remote.Artery;

namespace Akka.Remote.TestKit;

/// <summary>Internal, opt-in transport selection shared by initial and restarted test systems.</summary>
internal static class MultiNodeTransport
{
    internal const string ModeVariable = "AKKA_MNTR_TRANSPORT";
    internal const string CertificateVariable = "AKKA_MNTR_QUIC_CERTIFICATE";
    internal const string PasswordVariable = "AKKA_MNTR_QUIC_CERTIFICATE_PASSWORD";
    internal const string PinsVariable = "AKKA_MNTR_QUIC_PEER_PINS";
    internal const string StreamsVariable = "AKKA_MNTR_QUIC_STREAMS";

    internal static string ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "classic" => "classic",
        "artery" => "artery",
        "artery-quic" => "artery-quic",
        _ => throw new ConfigurationException($"{ModeVariable} must be classic, artery, or artery-quic.")
    };

    internal static Config Defaults(string hostname, int port, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var mode = ParseMode(environment(ModeVariable));
        if (mode == "classic")
            return ConfigurationFactory.Empty;

        var streams = 1;
        if (mode == "artery-quic" && environment(StreamsVariable) is { } value &&
            (!int.TryParse(value, out streams) || streams is < 1 or > 64))
            throw new ConfigurationException($"{StreamsVariable} must be between 1 and 64.");

        // This is a fallback: explicit per-role/spec transport choices retain precedence.
        return ConfigurationFactory.ParseString($$"""
            akka.remote.artery {
                enabled = on
                transport = {{(mode == "artery-quic" ? "quic" : "tcp")}}
                canonical.hostname = {{JsonSerializer.Serialize(hostname)}}
                canonical.port = {{port}}
                {{(mode == "artery-quic" ? $"advanced {{ outbound-lanes = {streams}, inbound-lanes = 1, quic {{ ordinary-streams = {streams}, maximum-inbound-ordinary-streams = {streams} }} }}" : "")}}
            }
            """);
    }

    internal static string EffectiveMode(Config config) => !config.GetBoolean("akka.remote.artery.enabled", false)
        ? "classic"
        : config.GetString("akka.remote.artery.transport", "tcp") switch
        {
            "tcp" => "artery",
            "quic" => "artery-quic",
            _ => throw new ConfigurationException("Unsupported effective Artery transport in multi-node configuration.")
        };

    internal static ActorSystem CreateSystem(string name, Config config, Func<string, string?>? environment = null) =>
        CreateSystem(name, ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config)), environment);

    internal static ActorSystem CreateSystem(string name, ActorSystemSetup setup, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var requested = ParseMode(environment(ModeVariable));
        var bootstrap = setup.Get<BootstrapSetup>();
        var config = bootstrap.FlatSelect(value => value.Config).GetOrElse(ConfigurationFactory.Default());
        var configured = EffectiveMode(config);
        X509Certificate2? certificate = null;
        try
        {
            if (configured == "artery-quic")
            {
                var selection = bootstrap.FlatSelect(value => value.ActorRefProvider)
                    .GetOrElse(ProviderSelection.GetProvider(config.GetString("akka.actor.provider", "local")));
                if (selection is ProviderSelection.Local)
                    throw new ConfigurationException("QUIC multi-node configuration requires a remote or cluster actor provider.");
                if (!setup.Get<ArteryTlsSetup>().HasValue)
                {
                    var path = Required(environment, CertificateVariable);
                    var pins = Required(environment, PinsVariable).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    var inbound = TlsPeerPolicy.PinnedCertificates(pins);
                    var outbound = inbound.And(TlsCertificateValidation.ValidateHostname());
                    // Schannel cannot authenticate with EphemeralKeySet. DefaultKeySet uses
                    // the normal temporary Windows key container; do not request PersistKeySet.
                    var keyStorage = OperatingSystem.IsWindows()
                        ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
                    certificate = TlsCertificateLoader.LoadPkcs12FromFile(path, environment(PasswordVariable), keyStorage);
                    setup = setup.WithSetup(new ArteryTlsSetup(ArteryTlsSettings.Mutual(certificate, inbound, outbound)));
                    setup = setup.WithSetup(new ArteryQuicResourceSetup(new ArteryQuicResourceLifetime()));
                }
                if (!QuicConnection.IsSupported)
                    throw new PlatformNotSupportedException("QUIC multi-node testing requires native QUIC support.");
            }

            ActorSystem system;
            if (certificate is not null)
            {
                // Start can throw after initiating asynchronous native teardown. Transfer ownership
                // before Start so the certificate survives that failure path too.
                var owned = new ActorSystemImpl(name, config, setup);
                // Register the resource boundary before Start. Provider initialization can fail
                // before a transport exists, so termination must not dereference that provider.
                var resources = setup.Get<ArteryQuicResourceSetup>().Value.Lifetime;
                var ownedCertificate = certificate;
                var disposal = new Lazy<Task>(() => DisposeAfterNativeReleaseAsync(ownedCertificate, resources.WhenReleased));
                void ReleaseCertificate()
                {
                    resources.Stop();
                    _ = disposal.Value;
                }
                owned.RegisterOnTermination(ReleaseCertificate);
                certificate = null;
                try
                {
                    owned.Start();
                }
                catch
                {
                    // Very early provider failure can prevent ActorSystem's own termination
                    // callback from running. Stop native admission and release once it is safe.
                    ReleaseCertificate();
                    throw;
                }
                system = owned;
            }
            else
            {
                system = ActorSystem.Create(name, setup);
            }
            var provider = ((ExtendedActorSystem)system).Provider;
            var effective = provider is RemoteActorRefProvider remoteProvider
                ? remoteProvider.Transport is ArteryRemoting artery
                    ? (artery.UsesQuicTransport ? "artery-quic" : "artery") : "classic"
                : "local";
            Console.WriteLine("[MULTINODE-TRANSPORT] " + JsonSerializer.Serialize(new
            {
                requested,
                effective,
                runtime = RuntimeInformation.FrameworkDescription,
                transportType = provider is RemoteActorRefProvider remote ? remote.Transport.GetType().FullName : provider.GetType().FullName,
                ordinaryStreams = effective == "artery-quic" ? config.GetInt("akka.remote.artery.advanced.quic.ordinary-streams", 1) : 1,
                nativeQuicAvailable = effective == "artery-quic" ? (bool?)QuicConnection.IsSupported : null
            }));
            return system;
        }
        finally
        {
            certificate?.Dispose();
        }
    }

    private static string Required(Func<string, string?> environment, string name) =>
        environment(name) is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ConfigurationException($"{name} is required for QUIC multi-node actor systems.");

    internal static async Task DisposeAfterNativeReleaseAsync(X509Certificate2 certificate, Task nativeReleased)
    {
        await nativeReleased.ConfigureAwait(false);
        certificate.Dispose();
    }
}
