// //-----------------------------------------------------------------------
// // <copyright file="ReliableDeliverySerializerSpecs.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Configuration;
using Akka.Cluster.Serialization;
using Akka.Delivery;
using Akka.Delivery.Internal;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit.Abstractions;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests.Serialization;

public class ReliableDeliverySerializerSpecs : AkkaSpec
{
    public ReliableDeliverySerializerSpecs(ITestOutputHelper outputHelper) : base(ClusterConfigFactory.Default(),
        outputHelper)
    {
        Serializer = new ReliableDeliverySerializer((ExtendedActorSystem)Sys);
        RealActorRef = Sys.ActorOf(BlackHoleActor.Props, "blackhole");
    }

    ReliableDeliverySerializer Serializer { get; }

    public static long Timestamp { get; } = DateTime.UtcNow.Ticks;

    public IActorRef RealActorRef { get; }

    public static IEnumerable<object[]> ReliableDeliveryMsgs()
    {
        yield return new object[]
        {
            "SequencedMessage-1",
            new ConsumerController.SequencedMessage<string>("prod-1", 17L, "msg17", false, false)
        };
        yield return new object[]
        {
            "SequencedMessage-2", new ConsumerController.SequencedMessage<string>("prod-1", 1L, "msg01", true, true)
        };
        yield return new object[] { "Ack", new ProducerController.Ack(5L) };
        yield return new object[] { "Request", new ProducerController.Request(5L, 25L, true, true) };
        yield return new object[] { "Resend", new ProducerController.Resend(5L) };
        yield return new object[]
        {
            "RegisterConsumer",
            new ProducerController.RegisterConsumer<(int, double)>(ActorRefs
                .Nobody) // using a nested tuple type to test the serializer's reflection capabilities
        };
        yield return new object[]
        {
            "DurableProducerQueue.MessageSent-1",
            new DurableProducerQueue.MessageSent<string>(3L, "msg03", false, "", Timestamp)
        };
        yield return new object[]
        {
            "DurableProducerQueue.MessageSent-2",
            new DurableProducerQueue.MessageSent<string>(3L, "msg03", true, "q1", Timestamp)
        };
        yield return new object[]
        {
            "DurableProducerQueue.Confirmed", new DurableProducerQueue.Confirmed(3L, "q2", Timestamp)
        };
        yield return new object[]
        {
            "DurableProducerQueue.State-1", new DurableProducerQueue.State<string>(3L, 2L,
                ImmutableDictionary<string, (long, long)>.Empty,
                ImmutableList<DurableProducerQueue.MessageSent<string>>.Empty)
        };
        yield return new object[]
        {
            "DurableProducerQueue.State-2", new DurableProducerQueue.State<string>(3L, 2L,
                ImmutableDictionary<string, (long, long)>.Empty.Add("", (2L, Timestamp)),
                ImmutableList<DurableProducerQueue.MessageSent<string>>.Empty.Add(new DurableProducerQueue.MessageSent<string>(3L, "msg03", false, "", Timestamp)))
        };
    }

    [Theory]
    [MemberData(nameof(ReliableDeliveryMsgs))]
    public void ReliableDeliveryMsgs_should_be_serializable(string scenario, IDeliverySerializable msg)
    {
        Sys.Log.Info(scenario);
        Sys.Serialization.FindSerializerForType(msg.GetType()).Should().BeOfType<ReliableDeliverySerializer>();
        if (msg is ConsumerController.SequencedMessage<string> sequencedMessage)
        {
            // need to update the IActorRef
            sequencedMessage = sequencedMessage with { ProducerController = RealActorRef };
            VerifySerialization(sequencedMessage);
        }
        else if (msg is ProducerController.RegisterConsumer<(int, double)> registerConsumer)
        {
            // need to update the IActorRef
            registerConsumer = new ProducerController.RegisterConsumer<(int, double)>(ConsumerController: RealActorRef);
            VerifySerialization(registerConsumer);
        }
        else
        {
            VerifySerialization(msg);
        }
    }

    private void VerifySerialization(object msg)
    {
        var m1 = Serializer.FromBinary(Serializer.ToBinary(msg), Serializer.Manifest(msg));
        m1.Should().BeEquivalentTo(msg);
    }
}