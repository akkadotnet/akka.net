//-----------------------------------------------------------------------
// <copyright file="ClassicRemotingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Integration proof for openspec task 6.4 (and, together with the assertions below, task 8.5):
/// a source-generated <see cref="AkkaSerializer"/> registered the same way a real
/// application would -- via the classic HOCON <c>akka.actor.serializers</c> /
/// <c>serialization-bindings</c> blocks, mirroring how <c>Akka.Remote.Artery.ArteryControlMessageSerializer</c>
/// is registered in <c>Remote.conf</c> (see <c>Akka.Remote.Tests.Artery.ArteryControlMessageSerializerSpec</c>)
/// -- must round-trip a generated payload between two independent <see cref="ActorSystem"/>s over
/// classic (non-Artery) <c>akka.remote.dot-netty.tcp</c> remoting. This exercises the real
/// <c>RemoteActorRefProvider</c> transport pipeline, not just direct calls into
/// <c>Serialization.cs</c> (already covered by task 6.3 / <see cref="GeneratedMessagePackSerializerSpec"/>).
/// </summary>
public class ClassicRemotingSpec : AkkaSpec
{
    private static readonly Config RemotingConfig = ConfigurationFactory.ParseString(@"
        akka.actor.provider = remote
        akka.remote.dot-netty.tcp {
            hostname = localhost
            port = 0
        }
        akka.actor {
            serializers {
                classic-remoting-test = ""Akka.Serialization.V2.Tests.ClassicRemotingSerializer, Akka.Serialization.V2.Tests""
            }
            serialization-bindings {
                ""Akka.Serialization.V2.Tests.IClassicRemotingProtocol, Akka.Serialization.V2.Tests"" = classic-remoting-test
            }
        }");

    public ClassicRemotingSpec(ITestOutputHelper output) : base(RemotingConfig, output)
    {
    }

    [Fact(DisplayName = "Generated V2 payload should round-trip over classic Akka.Remote between two ActorSystems")]
    public async Task Generated_payload_should_round_trip_over_classic_remoting()
    {
        // second, independent ActorSystem -- same HOCON (dynamic port), separate process boundary emulated
        // via a real dot-netty TCP transport rather than an in-process shortcut.
        var sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        try
        {
            sys2.ActorOf(Props.Create(() => new RemoteEchoActor()), "echo");
            var sys2Address = ((ExtendedActorSystem)sys2).Provider.DefaultAddress;

            var selection = Sys.ActorSelection(new RootActorPath(sys2Address) / "user" / "echo");
            var request = new RemotePing("corr-1", 42, TestActor);

            selection.Tell(request);

            var reply = await ExpectMsgAsync<RemotePong>(TimeSpan.FromSeconds(10));
            reply.CorrelationId.Should().Be("corr-1");
            reply.Sequence.Should().Be(42);

            // prove the wire path actually used the generated V2 serializer, not some fallback.
            Sys.Serialization.FindSerializerFor(request).Should().BeOfType<ClassicRemotingSerializer>();
        }
        finally
        {
            Shutdown(sys2);
        }
    }

    [Fact(DisplayName = "Generated V2 payload with nested value object should round-trip over classic Akka.Remote")]
    public async Task Generated_payload_with_nested_value_object_should_round_trip_over_classic_remoting()
    {
        var sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        try
        {
            sys2.ActorOf(Props.Create(() => new RemoteEchoActor()), "echo2");
            var sys2Address = ((ExtendedActorSystem)sys2).Provider.DefaultAddress;

            var selection = Sys.ActorSelection(new RootActorPath(sys2Address) / "user" / "echo2");
            var request = new RemoteShipmentPing("corr-2", new RemoteShippingAddress("1 Main St", "Seattle"), TestActor);

            selection.Tell(request);

            var reply = await ExpectMsgAsync<RemoteShipmentPong>(TimeSpan.FromSeconds(10));
            reply.CorrelationId.Should().Be("corr-2");
            reply.Address.Should().Be(request.Address);
        }
        finally
        {
            Shutdown(sys2);
        }
    }
}

/// <summary>
/// Echoes generated V2 payloads back to the sender embedded in the message (rather than
/// <c>Sender</c>) so the test also proves the deserialized <see cref="IActorRef"/> field is a
/// genuinely usable remote reference, not just a structurally-equal value.
/// </summary>
internal sealed class RemoteEchoActor : ReceiveActor
{
    public RemoteEchoActor()
    {
        Receive<RemotePing>(ping => ping.ReplyTo?.Tell(new RemotePong(ping.CorrelationId, ping.Sequence)));
        Receive<RemoteShipmentPing>(ping => ping.ReplyTo?.Tell(new RemoteShipmentPong(ping.CorrelationId, ping.Address)));
    }
}

public interface IClassicRemotingProtocol
{
}

[AkkaSerializer<IClassicRemotingProtocol>("classic-remoting-test", 120310)]
public sealed partial class ClassicRemotingSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

[AkkaSerializable(Manifest = "remote-ping-v1")]
public sealed record RemotePing(
    [property: AkkaField(1)] string CorrelationId,
    [property: AkkaField(2)] int Sequence,
    [property: AkkaField(3)] IActorRef? ReplyTo) : IClassicRemotingProtocol;

[AkkaSerializable(Manifest = "remote-pong-v1")]
public sealed record RemotePong(
    [property: AkkaField(1)] string CorrelationId,
    [property: AkkaField(2)] int Sequence) : IClassicRemotingProtocol;

[AkkaSerializable(Manifest = "remote-shipment-ping-v1")]
public sealed record RemoteShipmentPing(
    [property: AkkaField(1)] string CorrelationId,
    [property: AkkaField(2)] RemoteShippingAddress Address,
    [property: AkkaField(3)] IActorRef? ReplyTo) : IClassicRemotingProtocol;

[AkkaSerializable(Manifest = "remote-shipment-pong-v1")]
public sealed record RemoteShipmentPong(
    [property: AkkaField(1)] string CorrelationId,
    [property: AkkaField(2)] RemoteShippingAddress Address) : IClassicRemotingProtocol;

[AkkaSerializable]
public sealed record RemoteShippingAddress(
    [property: AkkaField(1)] string Street,
    [property: AkkaField(2)] string City);
