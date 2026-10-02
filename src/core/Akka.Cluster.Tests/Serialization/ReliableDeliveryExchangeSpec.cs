//-----------------------------------------------------------------------
// <copyright file="ReliableDeliveryExchangeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Cluster.Configuration;
using Akka.Configuration;
using Akka.Delivery;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests.Serialization;

/// <summary>
/// <see cref="ReliableDeliveryExchange"/> against the real legacy <c>ReliableDeliverySerializer</c>: with
/// <c>serialize-messages = on</c> every Delivery wire message crosses the serializer, and the consumer side sees copies.
/// A V2 port of that serializer runs the same exchange after it is switched on.
/// </summary>
public sealed class ReliableDeliveryExchangeSpec : AkkaSpec
{
    public sealed record WorkItem(string JobId, int Priority);

    public ReliableDeliveryExchangeSpec(ITestOutputHelper output) : base(output, ConfigurationFactory.ParseString("akka.actor.serialize-messages = on").WithFallback(ClusterConfigFactory.Default()))
    {
    }

    [Fact(DisplayName = "Should_BindDeliveryMessagesToTheClusterSerializer_When_ClusterConfigIsLoaded")]
    public void Should_BindDeliveryMessagesToTheClusterSerializer_When_ClusterConfigIsLoaded()
    {
        var sequenced = new ConsumerController.SequencedMessage<WorkItem>("p", 1, new WorkItem("j", 1), true, false);

        // the serializer is internal to Akka.Cluster, so it is named, not typed
        Sys.Serialization.FindSerializerFor(sequenced).GetType().FullName
            .Should().Be("Akka.Cluster.Serialization.ReliableDeliverySerializer");
    }

    [Fact(DisplayName = "Should_DeliverCopiesInOrder_When_ProducerAndConsumerExchangeUnderSerializeMessages")]
    public async Task Should_DeliverCopiesInOrder_When_ProducerAndConsumerExchangeUnderSerializeMessages()
    {
        Sys.Settings.SerializeAllMessages.Should().BeTrue("the exchange only proves the wire path with serialize-messages on");
        var sent = Enumerable.Range(1, 5).Select(i => new WorkItem($"job-{i}", i)).ToList();

        var result = await ReliableDeliveryExchange.RunAsync(this, sent, TimeSpan.FromSeconds(10));

        result.Received.Should().Equal(sent);
        result.Received.Zip(sent, (received, original) => ReferenceEquals(received, original))
            .Should().OnlyContain(same => !same, "serialize-messages hands the consumer side a deserialized copy");
    }
}
