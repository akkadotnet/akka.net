//-----------------------------------------------------------------------
// <copyright file="ReliableDeliveryV2RemotingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Configuration;
using Akka.Cluster.Serialization;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Delivery.Internal;
using Akka.Remote;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using ActorTestKit = Akka.TestKit.Xunit.TestKit;
using PocoPayload = Akka.Cluster.Tests.Serialization.ReliableDeliveryMessagePackSerializerSpecs.PocoPayload;

namespace Akka.Cluster.Tests.Serialization;

/// <summary>
/// Sends the complete Delivery corpus over actual TCP connections between a legacy writer and a V2 writer.
/// Both systems run the same binaries in one process; this does not establish mixed-binary or native interoperability.
/// </summary>
[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ReliableDeliveryV2RemotingSpec : AkkaSpec
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly ITestOutputHelper _output;

    public ReliableDeliveryV2RemotingSpec(ITestOutputHelper output) : base(output)
    {
        _output = output;
    }

    [Theory(DisplayName = "Should_RoundTripEveryDeliveryManifest_When_RemoteSystemsUseOpposingWriteBindings")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_RoundTripEveryDeliveryManifest_When_RemoteSystemsUseOpposingWriteBindings(bool artery)
    {
        var transport = artery ? "artery" : "classic";
        await using var client = new ActorTestKit(RemoteConfig(artery, serializationV2: false), $"delivery-{transport}-legacy", _output);
        try
        {
            await using var server = new ActorTestKit(RemoteConfig(artery, serializationV2: true), $"delivery-{transport}-v2", _output);
            try
            {
                server.Sys.ActorOf(Props.Create(() => new Echo()), "echo");
                var clientAddress = ((ExtendedActorSystem)client.Sys).Provider.DefaultAddress;
                var serverAddress = ((ExtendedActorSystem)server.Sys).Provider.DefaultAddress;
                clientAddress.Port.Should().BeGreaterThan(0);
                serverAddress.Port.Should().BeGreaterThan(0);
                serverAddress.Should().NotBe(clientAddress);

                var echoPath = new RootActorPath(serverAddress) / "user" / "echo";
                var remoteEcho = await client.Sys.ActorSelection(echoPath).ResolveOne(Timeout);
                remoteEcho.Should().BeOfType<RemoteActorRef>("the payload must cross the remote transport");
                var probe = client.CreateTestProbe();

                AssertBothReaders(client.Sys);
                AssertBothReaders(server.Sys);
                foreach (var entry in ReliableDeliveryMessagePackSerializerSpecs.ReliableDeliveryMsgs())
                {
                    var scenario = (string)entry[0];
                    var message = WithControllerRef((IDeliverySerializable)entry[1], probe.Ref);
                    _output.WriteLine($"{transport}: {scenario}");
                    client.Sys.Serialization.FindSerializerFor(message).Identifier.Should().Be(36, scenario);
                    server.Sys.Serialization.FindSerializerFor(message).Identifier.Should().Be(76, scenario);

                    // The outgoing send uses id 36. Echo's reply traverses the reverse connection with id 76.
                    remoteEcho.Tell(message, probe.Ref);
                    var received = await probe.ExpectMsgAsync<IDeliverySerializable>(Timeout);
                    received.GetType().Should().Be(message.GetType(), scenario);
                    received.Should().BeEquivalentTo((object)message, scenario);
                }
            }
            finally
            {
                await server.ShutdownAsync(Timeout, verifySystemShutdown: true);
            }
        }
        finally
        {
            await client.ShutdownAsync(Timeout, verifySystemShutdown: true);
        }
    }

    private static Config RemoteConfig(bool artery, bool serializationV2) => ConfigurationFactory.ParseString($$"""
        akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
        akka.actor.serialization-v2 = {{(serializationV2 ? "on" : "off")}}
        akka.remote.dot-netty.tcp {
            hostname = "127.0.0.1"
            port = 0
        }
        akka.remote.artery {
            enabled = {{(artery ? "on" : "off")}}
            canonical.hostname = "127.0.0.1"
            canonical.port = 0
        }
        """).WithFallback(ClusterConfigFactory.Default());

    private static void AssertBothReaders(ActorSystem system)
    {
        system.Serialization.GetSerializerById(36).Should().BeOfType<ReliableDeliverySerializer>();
        system.Serialization.GetSerializerById(76).Should().BeOfType<ReliableDeliveryMessagePackSerializer>();
    }

    private static IDeliverySerializable WithControllerRef(IDeliverySerializable message, IActorRef controller) => message switch
    {
        ConsumerController.SequencedMessage<string> sequenced => sequenced with { ProducerController = controller },
        ConsumerController.SequencedMessage<PocoPayload> sequenced => sequenced with { ProducerController = controller },
        ProducerController.RegisterConsumer<(int, double)> => new ProducerController.RegisterConsumer<(int, double)>(controller),
        _ => message
    };

    private sealed class Echo : ReceiveActor
    {
        public Echo()
        {
            ReceiveAny(message => Sender.Tell(message, Self));
        }
    }
}
