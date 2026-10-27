//-----------------------------------------------------------------------
// <copyright file="GeneratedMemberNamingRuntimeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class GeneratedMemberNamingRuntimeSpec : AkkaSpec
{
    public GeneratedMemberNamingRuntimeSpec(ITestOutputHelper output) : base(output)
    {
    }

    [Fact(DisplayName = "Should round-trip same-named command and event types through nested and union dispatch")]
    public void Should_RoundTrip_When_CommandAndEventShareSimpleName()
    {
        var serializer = new NamingRuntimeSerializer((ExtendedActorSystem)Sys);
        var command = new NamingCommands.LeaveChat(7);
        var @event = new NamingEvents.LeaveChat(9);
        var carrier = new NamingCarrier(command, @event, @event);

        serializer.Manifest(command).Should().Be("command");
        serializer.Manifest(@event).Should().Be("event");
        serializer.FromBinary(serializer.ToBinary(command), "command").Should().Be(command);
        serializer.FromBinary(serializer.ToBinary(@event), "event").Should().Be(@event);
        serializer.FromBinary(serializer.ToBinary(carrier), "carrier").Should().Be(carrier);
        serializer.SizeHint(carrier).Should().Be(serializer.ToBinary(carrier).Length);
        serializer.ToBinary(carrier).Should().Equal(
            0x83, 0x00, 0x81, 0x00, 0x07, 0x01, 0x81, 0x00, 0x09,
            0x02, 0x82, 0x01, 0xa5, 0x65, 0x76, 0x65, 0x6e, 0x74, 0x02, 0x81, 0x00, 0x09);
    }
}

[AkkaUnion(typeof(NamingCommands.LeaveChat), typeof(NamingEvents.LeaveChat))]
public interface INamingProtocol;

public static class NamingCommands
{
    [AkkaSerializable(Manifest = "command")]
    public sealed record LeaveChat([property: AkkaField(0)] int Value) : INamingProtocol;
}

public static class NamingEvents
{
    [AkkaSerializable(Manifest = "event")]
    public sealed record LeaveChat([property: AkkaField(0)] int Value) : INamingProtocol;
}

[AkkaSerializable(Manifest = "carrier")]
public sealed record NamingCarrier(
    [property: AkkaField(0)] NamingCommands.LeaveChat Command,
    [property: AkkaField(1)] NamingEvents.LeaveChat Event,
    [property: AkkaField(2)] INamingProtocol Union) : INamingProtocol;

[AkkaSerializer<INamingProtocol>("naming-runtime", 120902)]
public sealed partial class NamingRuntimeSerializer : AkkaSerializer
{
    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}
