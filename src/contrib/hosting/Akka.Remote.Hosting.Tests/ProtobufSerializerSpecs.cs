#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using Akka.Remote.Serialization;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Remote.Hosting.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DynamicTypeLoadingCollection
{
    public const string Name = "Akka.DynamicTypeLoading";
}

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ProtobufSerializerSpecs
{
    [Fact(DisplayName = "WithProtobufSerializer should register parsers when dynamic type loading is off")]
    public async Task Should_register_parsers_When_dynamic_type_loading_is_disabled()
    {
        const string switchName = "Akka.DynamicTypeLoading";
        var hadSwitch = AppContext.TryGetSwitch(switchName, out var previous);
        AppContext.SetSwitch(switchName, false);
        try
        {
            using var host = new HostBuilder().ConfigureServices(services =>
                services.AddAkka("protobuf-hosting", builder =>
                {
                    builder.WithRemoting(hostname: "127.0.0.1", port: 0);
                    builder.WithProtobufSerializer(StringValue.Descriptor).Should().BeSameAs(builder);
                })).Build();
            await host.StartAsync();
            try
            {
                var system = host.Services.GetRequiredService<ActorSystem>();
                var message = new StringValue { Value = "hosting" };
                var serializer = system.Serialization.FindSerializerFor(message);
                serializer.Should().BeOfType<ProtobufSerializer>();
                system.Serialization.Deserialize(serializer.ToBinary(message), serializer.Identifier, serializer.Manifest(message))
                    .Should().Be(message);
            }
            finally
            {
                await host.StopAsync();
            }
        }
        finally
        {
            AppContext.SetSwitch(switchName, !hadSwitch || previous);
        }
    }
}
