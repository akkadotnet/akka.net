// -----------------------------------------------------------------------
//  <copyright file="DurableProducerQueueSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using Akka.Delivery.Internal;
using Akka.IO;
using FluentAssertions;
using Xunit;
using static Akka.Delivery.DurableProducerQueue;

namespace Akka.Tests.Delivery;

public class DurableProducerQueueSpecs
{
    [Fact]
    public void DurableProducerQueueState_must_addMessageSent()
    {
        var state1 = State<string>.Empty.AddMessageSent(new MessageSent<string>(1, "a", false, "", 0));
        state1.Unconfirmed.Count.Should().Be(1);
        state1.Unconfirmed.First().Message.Equals("a").Should().BeTrue();
        state1.CurrentSeqNr.Should().Be(2);

        var state2 = state1.AddMessageSent(new MessageSent<string>(2, "b", false, "", 0));
        state2.Unconfirmed.Count.Should().Be(2);
        state2.Unconfirmed.Last().Message.Equals("b").Should().BeTrue();
        state2.CurrentSeqNr.Should().Be(3);
    }

    [Fact]
    public void DurableProducerQueueState_must_confirm()
    {
        var state1 = State<string>.Empty.AddMessageSent(new MessageSent<string>(1, "a", false, "", 0))
            .AddMessageSent(new MessageSent<string>(2, "b", false, "", 0));
        var state2 = state1.AddConfirmed(1L, "", 0);
        state2.Unconfirmed.Count.Should().Be(1);
        state2.Unconfirmed.First().Message.Equals("b").Should().BeTrue();
        state2.CurrentSeqNr.Should().Be(3);
    }

    [Fact]
    public void DurableProducerQueueState_must_filterPartiallyStoredChunkedMessages()
    {
        var state1 = State<string>.Empty.AddMessageSent(MessageSent<string>.FromChunked(1,
                new ChunkedMessage(ByteString.FromString("a"), true, true, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(2,
                new ChunkedMessage(ByteString.FromString("b"), true, false, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(3,
                new ChunkedMessage(ByteString.FromString("c"), false, false, 20, ""), false, "", 0));
        // last chunk was never stored

        var state2 = state1.CleanUpPartialChunkedMessages();
        state2.Unconfirmed.Count.Should().Be(1);
        state2.Unconfirmed.First().Message.Chunk!.Value.SerializedMessage.Should()
            .BeEquivalentTo(ByteString.FromString("a"));
        state2.CurrentSeqNr.Should().Be(2);

        // replace the 2 incomplete chunks with complete ones
        var state3 = state1.AddMessageSent(MessageSent<string>.FromChunked(2,
                new ChunkedMessage(ByteString.FromString("d"), true, false, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(3,
                new ChunkedMessage(ByteString.FromString("e"), false, true, 20, ""), false, "", 0));

        var state4 = state3.CleanUpPartialChunkedMessages();
        state4.Unconfirmed.Count.Should().Be(3);
        state4.Unconfirmed.First().Message.Chunk!.Value.SerializedMessage.Should()
            .BeEquivalentTo(ByteString.FromString("a"));
        state4.Unconfirmed[1].Message.Chunk!.Value.SerializedMessage.Should()
            .BeEquivalentTo(ByteString.FromString("d"));
        state4.Unconfirmed[2].Message.Chunk!.Value.SerializedMessage.Should()
            .BeEquivalentTo(ByteString.FromString("e"));

        var state5 = state3.AddMessageSent(MessageSent<string>.FromChunked(4,
                new ChunkedMessage(ByteString.FromString("f"), true, false, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(5,
                new ChunkedMessage(ByteString.FromString("g"), false, false, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(4,
                new ChunkedMessage(ByteString.FromString("h"), true, true, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(5,
                new ChunkedMessage(ByteString.FromString("i"), true, false, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(6,
                new ChunkedMessage(ByteString.FromString("j"), false, false, 20, ""), false, "", 0));

        var state6 = state5.CleanUpPartialChunkedMessages();
        state6.Unconfirmed.Count.Should().Be(4);
        state6.Unconfirmed.First().Message.Chunk!.Value.SerializedMessage.Should()
            .BeEquivalentTo(ByteString.FromString("a"));
        state6.Unconfirmed[1].Message.Chunk!.Value.SerializedMessage.Should()
            .BeEquivalentTo(ByteString.FromString("d"));
        state6.Unconfirmed[2].Message.Chunk!.Value.SerializedMessage.Should()
            .BeEquivalentTo(ByteString.FromString("e"));
        state6.Unconfirmed[3].Message.Chunk!.Value.SerializedMessage.Should()
            .BeEquivalentTo(ByteString.FromString("h"));
        state6.CurrentSeqNr.Should().Be(5);
    }
}