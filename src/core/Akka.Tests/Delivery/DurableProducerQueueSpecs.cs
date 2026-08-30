//-----------------------------------------------------------------------
// <copyright file="DurableProducerQueueSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using Akka.Delivery.Internal;
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
                new ChunkedMessage(Encoding.UTF8.GetBytes("a").AsMemory(), true, true, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(2,
                new ChunkedMessage(Encoding.UTF8.GetBytes("b").AsMemory(), true, false, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(3,
                new ChunkedMessage(Encoding.UTF8.GetBytes("c").AsMemory(), false, false, 20, ""), false, "", 0));
        // last chunk was never stored

        var state2 = state1.CleanUpPartialChunkedMessages();
        state2.Unconfirmed.Count.Should().Be(1);
        state2.Unconfirmed.First().Message.Chunk!.Value.SerializedMessage.ToArray()
            .Should().Equal(Encoding.UTF8.GetBytes("a"));
        state2.CurrentSeqNr.Should().Be(2);

        // replace the 2 incomplete chunks with complete ones
        var state3 = state1.AddMessageSent(MessageSent<string>.FromChunked(2,
                new ChunkedMessage(Encoding.UTF8.GetBytes("d").AsMemory(), true, false, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(3,
                new ChunkedMessage(Encoding.UTF8.GetBytes("e").AsMemory(), false, true, 20, ""), false, "", 0));

        var state4 = state3.CleanUpPartialChunkedMessages();
        state4.Unconfirmed.Count.Should().Be(3);
        state4.Unconfirmed.First().Message.Chunk!.Value.SerializedMessage.ToArray()
            .Should().Equal(Encoding.UTF8.GetBytes("a"));
        state4.Unconfirmed[1].Message.Chunk!.Value.SerializedMessage.ToArray()
            .Should().Equal(Encoding.UTF8.GetBytes("d"));
        state4.Unconfirmed[2].Message.Chunk!.Value.SerializedMessage.ToArray()
            .Should().Equal(Encoding.UTF8.GetBytes("e"));

        var state5 = state3.AddMessageSent(MessageSent<string>.FromChunked(4,
                new ChunkedMessage(Encoding.UTF8.GetBytes("f").AsMemory(), true, false, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(5,
                new ChunkedMessage(Encoding.UTF8.GetBytes("g").AsMemory(), false, false, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(4,
                new ChunkedMessage(Encoding.UTF8.GetBytes("h").AsMemory(), true, true, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(5,
                new ChunkedMessage(Encoding.UTF8.GetBytes("i").AsMemory(), true, false, 20, ""), false, "", 0))
            .AddMessageSent(MessageSent<string>.FromChunked(6,
                new ChunkedMessage(Encoding.UTF8.GetBytes("j").AsMemory(), false, false, 20, ""), false, "", 0));

        var state6 = state5.CleanUpPartialChunkedMessages();
        state6.Unconfirmed.Count.Should().Be(4);
        state6.Unconfirmed.First().Message.Chunk!.Value.SerializedMessage.ToArray()
            .Should().Equal(Encoding.UTF8.GetBytes("a"));
        state6.Unconfirmed[1].Message.Chunk!.Value.SerializedMessage.ToArray()
            .Should().Equal(Encoding.UTF8.GetBytes("d"));
        state6.Unconfirmed[2].Message.Chunk!.Value.SerializedMessage.ToArray()
            .Should().Equal(Encoding.UTF8.GetBytes("e"));
        state6.Unconfirmed[3].Message.Chunk!.Value.SerializedMessage.ToArray()
            .Should().Equal(Encoding.UTF8.GetBytes("h"));
        state6.CurrentSeqNr.Should().Be(5);
    }
}
