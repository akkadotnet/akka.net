//-----------------------------------------------------------------------
// <copyright file="ReliableDeliveryIntegrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Serialization.V2;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests;

/// <summary>
/// Integration proof for openspec task 6.9: a source-generated V2 MessagePack payload used as the
/// user message type for Akka.Delivery (ReliableDelivery) must survive the real
/// <c>Akka.Cluster.Serialization.ReliableDeliverySerializer</c> -- the same wrapper serializer
/// production remote/cluster deployments use to carry <see cref="ConsumerController.SequencedMessage{T}"/>
/// (and friends) across process boundaries. That serializer is <c>internal</c> to <c>Akka.Cluster</c>,
/// so it can't be constructed directly from this test project; instead this proves the wrapper the
/// same way an application observes it -- through <c>Serialization.FindSerializerFor</c> +
/// <c>Serialize</c>/<c>Deserialize</c> -- and, in the second test, implicitly via
/// <c>akka.actor.serialize-messages = on</c> forcing every local <see cref="ProducerController"/> -&gt;
/// <see cref="ConsumerController"/> <c>Tell</c> through the same wire path a remote deployment would use.
///
/// <para>
/// Setting <c>akka.actor.provider</c> to <see cref="ClusterActorRefProvider"/> is what pulls in
/// Akka.Cluster's <c>Cluster.conf</c> (via <c>Settings.InjectTopLevelFallback</c>, the same mechanism
/// <c>Akka.Remote.RemoteActorRefProvider</c> uses for <c>Remote.conf</c>), which is where the
/// <c>Akka.Delivery.Internal.IDeliverySerializable -&gt; ReliableDeliverySerializer</c> binding lives.
/// No actual cluster is formed (no seed nodes, nothing joins) -- this mirrors
/// <c>Akka.DistributedData.Tests.Serialization.ReplicatedDataSerializerSpec</c>'s approach for the
/// equivalent DistributedData proof (task 6.10).
/// </para>
/// </summary>
public class ReliableDeliveryIntegrationSpec : AkkaSpec
{
    private static readonly Config DeliveryConfig = ConfigurationFactory.ParseString(@"
        akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
        akka.remote.dot-netty.tcp.port = 0
        akka.actor.serialize-messages = on
        akka.actor {
            serializers {
                reliable-delivery-test = ""Akka.Cluster.Tests.ReliableDeliveryIntegrationSerializer, Akka.Cluster.Tests""
            }
            serialization-bindings {
                ""Akka.Cluster.Tests.IReliableDeliveryIntegrationProtocol, Akka.Cluster.Tests"" = reliable-delivery-test
            }
        }");

    public ReliableDeliveryIntegrationSpec(ITestOutputHelper output) : base(DeliveryConfig, output)
    {
    }

    [Fact(DisplayName = "Generated V2 payload inside ConsumerController.SequencedMessage should round-trip through the real ReliableDeliverySerializer")]
    public void Generated_payload_should_round_trip_through_ReliableDeliverySerializer()
    {
        var payload = new WorkItem("job-1", 7);
        var sequenced = new ConsumerController.SequencedMessage<IReliableDeliveryIntegrationProtocol>("producer-1", 1, payload, true, false);

        var serializer = Sys.Serialization.FindSerializerFor(sequenced);
        // ReliableDeliverySerializer is internal to Akka.Cluster -- assert by name rather than typeof().
        serializer.GetType().FullName.Should().Be("Akka.Cluster.Serialization.ReliableDeliverySerializer");

        var bytes = Sys.Serialization.Serialize(sequenced);
        var manifest = global::Akka.Serialization.Serialization.ManifestFor(serializer, sequenced);
        var recovered = (ConsumerController.SequencedMessage<IReliableDeliveryIntegrationProtocol>)
            Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest);

        recovered.ProducerId.Should().Be("producer-1");
        recovered.SeqNr.Should().Be(1);
        recovered.Message.IsMessage.Should().BeTrue();
        recovered.Message.Message.Should().Be(payload);
        ReferenceEquals(recovered.Message.Message, payload).Should().BeFalse();
    }

    [Fact(DisplayName = "Generated V2 payload should survive a live ProducerController/ConsumerController exchange over the wire")]
    public async Task Generated_payload_should_survive_live_producer_consumer_exchange()
    {
        var consumerControllerProbe = CreateTestProbe();
        var producerProbe = CreateTestProbe();

        var producerController = Sys.ActorOf(
            ProducerController.Create<IReliableDeliveryIntegrationProtocol>(Sys, "producer-2", Option<Props>.None));
        producerController.Tell(new ProducerController.Start<IReliableDeliveryIntegrationProtocol>(producerProbe.Ref));
        producerController.Tell(new ProducerController.RegisterConsumer<IReliableDeliveryIntegrationProtocol>(consumerControllerProbe.Ref));

        var sendTo = (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<IReliableDeliveryIntegrationProtocol>>(TimeSpan.FromSeconds(10)))
            .SendNextTo;
        var payload = new WorkItem("job-2", 9);
        sendTo.Tell(payload);

        var seqMsg = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<IReliableDeliveryIntegrationProtocol>>(TimeSpan.FromSeconds(10));

        seqMsg.ProducerId.Should().Be("producer-2");
        seqMsg.Message.IsMessage.Should().BeTrue();
        seqMsg.Message.Message.Should().Be(payload);
        // serialize-messages=on means the ConsumerController actually received a deserialized copy,
        // not the original in-process reference -- proof the generated V2 serializer ran for real.
        ReferenceEquals(seqMsg.Message.Message, payload).Should().BeFalse();
    }
}

public interface IReliableDeliveryIntegrationProtocol
{
}

[AkkaSerializer<IReliableDeliveryIntegrationProtocol>("reliable-delivery-test", 120410)]
public sealed partial class ReliableDeliveryIntegrationSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

[AkkaSerializable(Manifest = "work-item-v1")]
public sealed record WorkItem(
    [property: AkkaField(1)] string JobId,
    [property: AkkaField(2)] int Priority) : IReliableDeliveryIntegrationProtocol;
