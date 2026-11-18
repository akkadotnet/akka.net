//-----------------------------------------------------------------------
// <copyright file="BuiltInSerializerSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Serialization;
using Akka.Cluster.Tools.PublishSubscribe.Serialization;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Akka.Remote.Serialization;
using Akka.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Akka.Cluster.Hosting.Tests;

/// <summary>
/// The modules register their built-in serializers from code, with no HOCON rows behind them. An Akka.Hosting
/// app, which builds its HOCON and its <see cref="SerializationSetup"/> from the builder, still gets all of them.
/// </summary>
public class BuiltInSerializerSpecs
{
    private readonly ITestOutputHelper _helper;

    public BuiltInSerializerSpecs(ITestOutputHelper helper)
    {
        _helper = helper;
    }

    private async Task<(IHost Host, ActorSystem System)> StartAsync(string name, Action<AkkaConfigurationBuilder> configure)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var host = new HostBuilder()
            .ConfigureLogging(builder => builder.AddProvider(new XUnitLoggerProvider(_helper, LogLevel.Information)))
            .ConfigureServices(services => services.AddAkka(name, (builder, _) =>
            {
                builder
                    .WithRemoting("localhost", 0)
                    .WithClustering(new ClusterOptions { Roles = ["my-host"] })
                    .WithDistributedPubSub("my-host");
                configure(builder);
            }))
            .Build();

        await host.StartAsync(cts.Token);
        return (host, host.Services.GetRequiredService<ActorSystem>());
    }

    [Fact(DisplayName = "Should_resolve_every_module_serializer_When_a_hosted_app_adds_remoting_clustering_and_pubsub")]
    public async Task Should_resolve_every_module_serializer_When_a_hosted_app_adds_remoting_clustering_and_pubsub()
    {
        var (host, system) = await StartAsync("hosting-builtin-serializers", _ => { });
        try
        {
            // nothing wrote rows for the built-in serializers into the config
            Assert.False(system.Settings.Config.HasPath("akka.actor.serializers.akka-misc"));
            Assert.False(system.Settings.Config.HasPath("akka.actor.serializers.akka-cluster"));
            Assert.False(system.Settings.Config.HasPath("akka.actor.serializers.akka-pubsub"));

            var serialization = system.Serialization;
            Assert.IsType<MiscMessageSerializer>(serialization.FindSerializerForType(typeof(Identify)));
            Assert.IsType<PrimitiveSerializers>(serialization.FindSerializerForType(typeof(string)));
            Assert.IsType<ClusterMessageSerializer>(
                serialization.FindSerializerForType(Type.GetType("Akka.Cluster.IClusterMessage, Akka.Cluster", throwOnError: true)!));
            Assert.IsType<DistributedPubSubMessageSerializer>(
                serialization.FindSerializerForType(typeof(Akka.Cluster.Tools.PublishSubscribe.IDistributedPubSubMessage)));

            // a message goes through the module's serializer and back
            var identify = new Identify("hosted");
            var serializer = serialization.FindSerializerFor(identify);
            var bytes = serializer.ToBinary(identify);
            var roundTripped = Assert.IsType<Identify>(
                serialization.Deserialize(bytes, serializer.Identifier, AkkaSerialization.ManifestFor(serializer, identify)));
            Assert.Equal("hosted", roundTripped.MessageId);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact(DisplayName = "Should_prefer_the_app_binding_When_hosted_HOCON_rebinds_a_built_in_type")]
    public async Task Should_prefer_the_app_binding_When_hosted_HOCON_rebinds_a_built_in_type()
    {
        var (host, system) = await StartAsync("hosting-builtin-override", builder =>
            builder.AddHocon(@"akka.actor.serialization-bindings { ""Akka.Actor.Identify, Akka"" = bytes }", HoconAddMode.Prepend));
        try
        {
            var serialization = system.Serialization;
            Assert.IsType<ByteArraySerializer>(serialization.FindSerializerForType(typeof(Identify)));
            // the rest of the module's bindings are untouched
            Assert.IsType<MiscMessageSerializer>(serialization.FindSerializerForType(typeof(ActorIdentity)));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
