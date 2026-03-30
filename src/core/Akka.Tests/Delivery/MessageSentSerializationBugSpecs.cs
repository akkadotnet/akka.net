//-----------------------------------------------------------------------
// <copyright file="MessageSentSerializationBugSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Delivery;
using Akka.Delivery.Internal;
using Akka.Serialization;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Delivery;

/// <summary>
/// Reproduction test for https://github.com/akkadotnet/akka.net/issues/8105
///
/// When MessageSent{T} (where T is a reference type) is serialized using the default
/// Newtonsoft.Json serializer (i.e. without Akka.Cluster providing the ReliableDeliverySerializer),
/// it fails with "Self referencing loop detected" due to broken cross-type equality in
/// MessageOrChunk{T}.Equals(object).
/// </summary>
public class MessageSentSerializationBugSpecs : TestKit.Xunit.TestKit
{
    public MessageSentSerializationBugSpecs(ITestOutputHelper output) : base(output: output)
    {
    }

    // Simple reference type message - this is what triggers the bug
    public sealed class PurchaseItem
    {
        public string ItemName { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    /// <summary>
    /// Reproduces Issue #8105: MessageOrChunk{T}.Equals(object) reports equality between
    /// the wrapper and its inner message T, which causes Newtonsoft.Json's reference tracking
    /// to incorrectly detect a self-referencing loop.
    /// </summary>
    [Fact(DisplayName = "MessageSent with reference type should round-trip through Newtonsoft serialization")]
    public void MessageSent_with_reference_type_should_roundtrip_newtonsoft_serialization()
    {
        // Arrange: create a MessageSent<PurchaseItem> just like EventSourcedProducerQueue would
        var item = new PurchaseItem { ItemName = "Widget", Quantity = 5 };
        var messageSent = new DurableProducerQueue.MessageSent<PurchaseItem>(
            SeqNr: 1,
            Message: new MessageOrChunk<PurchaseItem>(item),
            Ack: false,
            ConfirmationQualifier: DurableProducerQueue.NoQualifier,
            Timestamp: 0);

        // Without Akka.Cluster, MessageSent<T> falls back to the default Newtonsoft.Json serializer.
        // This should work, but currently fails with "Self referencing loop detected" due to
        // broken cross-type equality in MessageOrChunk<T>.Equals(object).
        var serializer = (NewtonSoftJsonSerializer)Sys.Serialization.FindSerializerFor(messageSent);

        var bytes = serializer.ToBinary(messageSent);
        bytes.Should().NotBeEmpty();
    }

    /// <summary>
    /// Demonstrates the underlying equality bug: MessageOrChunk{T} considers itself equal
    /// to its inner T value, violating the symmetry contract of Equals.
    /// </summary>
    [Fact(DisplayName = "MessageOrChunk should not report equality with its inner value")]
    public void MessageOrChunk_should_not_equal_its_inner_value()
    {
        var item = new PurchaseItem { ItemName = "Widget", Quantity = 5 };
        var wrapper = new MessageOrChunk<PurchaseItem>(item);

        // A wrapper type should never be equal to its unwrapped inner value.
        // This currently fails because MessageOrChunk<T>.Equals(object) has a
        // cross-type case that matches on T, violating the symmetry contract of Equals.
        wrapper.Equals((object)item).Should().BeFalse(
            "a MessageOrChunk<T> should not be equal to a raw T instance");
    }
}
