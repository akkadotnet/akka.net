#nullable enable

using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class OmittedPropertyRoundTripSpec : AkkaSpec
{
    public OmittedPropertyRoundTripSpec(ITestOutputHelper output) : base(ConfigurationFactory.Empty, output)
    {
    }

    [Fact(DisplayName = "Should preserve opted-in fields and reset intentionally omitted state on round-trip")]
    public void Should_ResetOmittedProperty_When_MessageRoundTrips()
    {
        var serializer = new OmittedStateSerializer((ExtendedActorSystem)Sys);
        var original = new OmittedStateMessage { Content = "hello", Author = "alice" };
        var restored = (OmittedStateMessage)serializer.FromBinary(serializer.ToBinary(original), serializer.Manifest(original));
        restored.Content.Should().Be("hello");
        restored.Author.Should().Be("unknown");
    }
}

public interface IOmittedStateProtocol;

[AkkaSerializable(Manifest = "omitted-state")]
public sealed record OmittedStateMessage : IOmittedStateProtocol
{
    [AkkaField(0)] public string Content { get; init; } = string.Empty;
#pragma warning disable AKKASG046
    public string Author { get; init; } = "unknown";
#pragma warning restore AKKASG046
}

[AkkaSerializer<IOmittedStateProtocol>("omitted-state", 120903)]
public sealed partial class OmittedStateSerializer : AkkaSerializer
{
    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}
