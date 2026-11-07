//-----------------------------------------------------------------------
// <copyright file="ReliableDeliveryV2CompatibilitySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Configuration;
using Akka.Cluster.Serialization;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Delivery.Internal;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.Cluster.Tests.Serialization;

/// <summary>
/// Exercises the real module table and immutable wire fixtures across the write-binding switch.
/// These are serializer-boundary checks; two local systems do not establish remoting interoperability.
/// </summary>
[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ReliableDeliveryV2CompatibilitySpec : AkkaSpec
{
    private const int LegacyId = 36;
    private const int V2Id = 76;

    public ReliableDeliveryV2CompatibilitySpec(ITestOutputHelper output)
        : base(ClusterConfigFactory.Default(), output)
    {
    }

    [Theory(DisplayName = "Should_SelectExpectedWriterAndReadBothFormats_When_UsingTheRealModuleTable")]
    [InlineData("", 36)]
    [InlineData("akka.actor.serialization-v2 = off", 36)]
    [InlineData("akka.actor.serialization-v2 = on", 76)]
    [InlineData("akka.actor.serialization-v2 = on\nakka.actor.serialization-bindings { \"Akka.Delivery.Internal.IDeliverySerializable, Akka\" = reliable-delivery }", 36)]
    [InlineData("akka.actor.serialization-v2 = off\nakka.actor.serialization-bindings { \"Akka.Delivery.Internal.IDeliverySerializable, Akka\" = reliable-delivery-v2 }", 76)]
    public async Task Should_SelectExpectedWriterAndReadBothFormats_When_UsingTheRealModuleTable(string settings, int expectedId)
    {
        await WithSystemAsync("delivery-binding", settings, system =>
        {
            foreach (var sample in Corpus())
            {
                system.Serialization.FindSerializerFor(sample.Message).Identifier.Should().Be(expectedId, sample.Name);
                AssertFixtureReads(system, sample);
            }
        });
    }

    [Fact(DisplayName = "Should_ExchangeBothFormats_When_SystemsUseOpposingWriteBindings")]
    public async Task Should_ExchangeBothFormats_When_SystemsUseOpposingWriteBindings()
    {
        await WithSystemAsync("delivery-v2-writer", "akka.actor.serialization-v2 = on", v2System =>
        {
            foreach (var sample in Corpus())
            {
                AssertExchange(Sys, v2System, sample, LegacyId);
                AssertExchange(v2System, Sys, sample, V2Id);
            }
        });
    }

    [Fact(DisplayName = "Should_PreserveEveryGoldenRecord_When_WritingOrReadingEitherFormat")]
    public void Should_PreserveEveryGoldenRecord_When_WritingOrReadingEitherFormat()
    {
        var legacy = new ReliableDeliverySerializer((ExtendedActorSystem)Sys);
        var v2 = new ReliableDeliveryMessagePackSerializer((ExtendedActorSystem)Sys);
        var corpus = Corpus().ToArray();
        ReliableDeliveryV2GoldenBytes.Cases.Should().HaveCount(corpus.Length);

        foreach (var sample in corpus)
        {
            var fixture = ReliableDeliveryV2GoldenBytes.Cases[sample.Name];
            fixture.Manifest.Should().Be(sample.Manifest);
            legacy.Manifest(sample.Message).Should().Be(sample.Manifest);
            v2.Manifest(sample.Message).Should().Be(sample.Manifest);
            Convert.ToHexString(legacy.ToBinary(sample.Message)).Should().Be(fixture.LegacyHex, sample.Name);
            Convert.ToHexString(v2.ToBinary(sample.Message)).Should().Be(fixture.V2Hex, sample.Name);
            AssertFixtureReads(Sys, sample);
        }
    }

    private static void AssertFixtureReads(ActorSystem system, Sample sample)
    {
        var fixture = ReliableDeliveryV2GoldenBytes.Cases[sample.Name];
        var legacyBytes = Convert.FromHexString(fixture.LegacyHex);
        var legacy = new ReliableDeliverySerializer((ExtendedActorSystem)system);
        var expected = legacy.FromBinary(legacyBytes, sample.Manifest);
        var legacyRead = system.Serialization.Deserialize(legacyBytes, LegacyId, sample.Manifest);
        var v2Read = system.Serialization.Deserialize(Convert.FromHexString(fixture.V2Hex), V2Id, sample.Manifest);
        Describe(legacyRead).Should().BeEquivalentTo(Describe(expected), sample.Name);
        Describe(v2Read).Should().BeEquivalentTo(Describe(expected), sample.Name);
    }

    private static void AssertExchange(ActorSystem writer, ActorSystem reader, Sample sample, int expectedWriterId)
    {
        var serializer = writer.Serialization.FindSerializerFor(sample.Message);
        serializer.Identifier.Should().Be(expectedWriterId, sample.Name);
        var manifest = AkkaSerialization.ManifestFor(serializer, sample.Message);
        manifest.Should().Be(sample.Manifest);
        var received = reader.Serialization.Deserialize(serializer.ToBinary(sample.Message), serializer.Identifier, manifest);
        var legacy = new ReliableDeliverySerializer((ExtendedActorSystem)reader);
        var expected = legacy.FromBinary(Convert.FromHexString(ReliableDeliveryV2GoldenBytes.Cases[sample.Name].LegacyHex), sample.Manifest);
        Describe(received).Should().BeEquivalentTo(Describe(expected), sample.Name);
    }

    private static async Task WithSystemAsync(string name, string settings, Action<ActorSystem> action)
    {
        var config = ConfigurationFactory.ParseString(settings).WithFallback(ClusterConfigFactory.Default());
        var system = ActorSystem.Create(name, config);
        try
        {
            action(system);
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    // Fixed inputs: no generated actor uid, clock, multi-entry dictionary, or set ordering enters the golden bytes.
    private static IEnumerable<Sample> Corpus()
    {
        yield return new("sequenced", "a", new ConsumerController.SequencedMessage<string>("producer", 3, "payload", true, true, ActorRefs.Nobody));
        yield return new("ack", "b", new ProducerController.Ack(3));
        yield return new("request", "c", new ProducerController.Request(3, 8, true, false));
        yield return new("resend", "d", new ProducerController.Resend(3));
        yield return new("register-consumer", "e", new ProducerController.RegisterConsumer<string>(ActorRefs.Nobody));
        yield return new("message-sent", "f", new DurableProducerQueue.MessageSent<string>(3, "payload", true, "q", 123456));
        yield return new("confirmed", "g", new DurableProducerQueue.Confirmed(3, "q", 123456));
        yield return new("state", "h", new DurableProducerQueue.State<string>(4, 2,
            ImmutableDictionary<string, (long, long)>.Empty.Add("q", (2, 123455)),
            ImmutableList.Create(new DurableProducerQueue.MessageSent<string>(3, "payload", true, "q", 123456))));
        yield return new("cleanup", "i", new DurableProducerQueue.Cleanup(ImmutableHashSet.Create("q")));
    }

    private static object Describe(object message) => message switch
    {
        ConsumerController.ISequencedMessage m => new { m.PayloadType, m.ProducerId, m.SeqNr, m.First, m.Ack, Controller = m.ProducerControllerRef.Path.ToString(), m.Payload, Chunk = DescribeChunk(m.Chunk) },
        ProducerController.Ack m => new { m.ConfirmedSeqNr },
        ProducerController.Request m => new { m.ConfirmedSeqNr, m.RequestUpToSeqNr, m.SupportResend, m.ViaTimeout },
        ProducerController.Resend m => new { m.FromSeqNr },
        ProducerController.IRegisterConsumer m => new { m.ConsumerType, Controller = m.ConsumerController.Path.ToString() },
        DurableProducerQueue.IMessageSent m => new { m.MessageType, m.SeqNr, m.Ack, m.ConfirmationQualifier, m.Timestamp, m.Payload, Chunk = DescribeChunk(m.Chunk) },
        DurableProducerQueue.Confirmed m => new { m.SeqNr, m.Qualifier, m.Timestamp },
        DurableProducerQueue.IState m => new { m.MessageType, m.CurrentSeqNr, m.HighestConfirmedSeqNr, m.ConfirmedSeqNr, Unconfirmed = m.UnconfirmedMessages.Select(Describe).ToArray() },
        DurableProducerQueue.Cleanup m => new { m.ConfirmationQualifiers },
        _ => throw new ArgumentException($"Unexpected fixture message {message.GetType()}", nameof(message))
    };

    private static object? DescribeChunk(ChunkedMessage? chunk) => chunk is { } value
        ? new { Bytes = value.SerializedMessage.ToArray(), value.FirstChunk, value.LastChunk, value.SerializerId, value.Manifest }
        : null;

    private sealed record Sample(string Name, string Manifest, IDeliverySerializable Message);

}
