// -----------------------------------------------------------------------
//  <copyright file="ProducerControllerSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Util;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Tests.Delivery.TestConsumer;

namespace Akka.Tests.Delivery;

public class ProducerControllerSpec : TestKit.Xunit2.TestKit
{
    private static readonly Config Config = "akka.reliable-delivery.consumer-controller.flow-control-window = 20";

    public ProducerControllerSpec(ITestOutputHelper output) : base(
        Config.WithFallback(TestSerializer.Config), output: output)
    {
    }

    private int _idCount = 0;
    private int NextId() => _idCount++;

    private string ProducerId => $"p-{_idCount}";

    [Fact]
    public async Task ProducerController_must_resend_lost_initial_SequencedMessage()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None),
            $"producerController-{_idCount}");
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));
        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe.Ref));

        var sendTo = (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo;
        sendTo.Tell(new Job("msg-1"));

        var seqMsg = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();

        seqMsg.ProducerId.Should().Be(ProducerId);
        seqMsg.SeqNr.Should().Be(1);
        seqMsg.ProducerController.Should().Be(producerController);

        // the ConsumerController will send initial `Request` back, but if that is lost or if the first
        // `SequencedMessage` is lost the ProducerController will resend the SequencedMessage
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 1, producerController));

        producerController.Tell(new ProducerController.Request(1L, 10L, true, false));
        await consumerControllerProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(1100));
    }

    [Fact]
    public async Task ProducerController_should_resend_lost_SequencedMessage_when_receiving_Resend()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None),
            $"producerController-{_idCount}");
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));

        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe.Ref));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo.Tell(new Job("msg-1"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 1, producerController));

        producerController.Tell(new ProducerController.Request(1L, 10L, true, false));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-2"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 2, producerController));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-3"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 3, producerController));
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-4"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 4, producerController));

        // let's say 3 is lost, when 4 is received the ConsumerController detects the gap and sends Resend(3)
        producerController.Tell(new ProducerController.Resend(3L));

        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 3, producerController));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 4, producerController));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-5"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 5, producerController));
    }

    [Fact]
    public async Task ProducerController_should_resend_last_SequencedMessage_when_receiving_Request()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None),
            $"producerController-{_idCount}");
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));

        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe.Ref));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo.Tell(new Job("msg-1"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 1, producerController));

        producerController.Tell(new ProducerController.Request(1L, 10L, true, false));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-2"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 2, producerController));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-3"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 3, producerController));
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-4"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 4, producerController));

        // let's say 3 and 4 are lost, and no more messages are sent from producer
        // ConsumerController will resend Request periodically
        producerController.Tell(new ProducerController.Request(2L, 10L, true, true));

        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 3, producerController));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 4, producerController));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-5"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 5, producerController));
    }

    [Fact]
    public async Task ProducerController_should_support_registration_of_new_ConsumerController()
    {
        NextId();
        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None),
            $"producerController-{_idCount}");
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));

        var consumerControllerProbe1 = CreateTestProbe();
        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe1.Ref));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo.Tell(new Job("msg-1"));
        await consumerControllerProbe1.ExpectMsgAsync(SequencedMessage(ProducerId, 1, producerController));

        producerController.Tell(new ProducerController.Request(1L, 10L, true, false));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo.Tell(new Job("msg-2"));
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo.Tell(new Job("msg-3"));

        var consumerControllerProbe2 = CreateTestProbe();
        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe2.Ref));

        await consumerControllerProbe2.ExpectMsgAsync(SequencedMessage(ProducerId, 2, producerController).AsFirst());
        await consumerControllerProbe2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        // if no Request confirming the first (seqNr=2) it will resend it
        await consumerControllerProbe2.ExpectMsgAsync(SequencedMessage(ProducerId, 2, producerController).AsFirst());

        producerController.Tell(new ProducerController.Request(2L, 10L, true, false));
        // then the other unconfirmed should be resent
        await consumerControllerProbe2.ExpectMsgAsync(SequencedMessage(ProducerId, 3, producerController));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo.Tell(new Job("msg-4"));
        await consumerControllerProbe2.ExpectMsgAsync(SequencedMessage(ProducerId, 4, producerController));
    }

    [Fact]
    public async Task ProducerController_should_reply_to_MessageWithConfirmation()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None),
            $"producerController-{_idCount}");
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));

        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe.Ref));

        var replyTo = CreateTestProbe();

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .AskNextTo(new ProducerController.MessageWithConfirmation<Job>(new Job("msg-1"), replyTo.Ref));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 1, producerController, ack: true));
        producerController.Tell(new ProducerController.Request(1L, 10L, true, false));
        await replyTo.ExpectMsgAsync(1L);

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .AskNextTo(new ProducerController.MessageWithConfirmation<Job>(new Job("msg-2"), replyTo.Ref));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 2, producerController, ack: true));
        producerController.Tell(new ProducerController.Ack(2L));
        await replyTo.ExpectMsgAsync(2L);

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .AskNextTo(new ProducerController.MessageWithConfirmation<Job>(new Job("msg-3"), replyTo.Ref));
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .AskNextTo(new ProducerController.MessageWithConfirmation<Job>(new Job("msg-4"), replyTo.Ref));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 3, producerController, ack: true));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 4, producerController, ack: true));
        // Ack(3 lost, but Ack(4) triggers reply for 3 and 4
        producerController.Tell(new ProducerController.Ack(4L));
        await replyTo.ExpectMsgAsync(3L);
        await replyTo.ExpectMsgAsync(4L);

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .AskNextTo(new ProducerController.MessageWithConfirmation<Job>(new Job("msg-5"), replyTo.Ref));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 5, producerController, ack: true));
        // Ack(5) lost, but eventually a Request will trigger the reply
        producerController.Tell(new ProducerController.Request(5L, 15L, true, false));
        await replyTo.ExpectMsgAsync(5L);
    }
    
    [Fact]
    public async Task ProducerController_should_reply_to_MessageWithConfirmation_when_Chunking()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var producerController =
            Sys.ActorOf(
                ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None,
                    ProducerController.Settings.Create(Sys) with { ChunkLargeMessagesBytes = 1}),
                $"producerController-{_idCount}");
        
        var producerProbe = CreateTestProbe();
        var replyProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));
        
        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe.Ref));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .AskNextTo(new ProducerController.MessageWithConfirmation<Job>(new Job("abc"), replyProbe.Ref));
        
        // gather all 3 chunks
        var seqMsg1 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        seqMsg1.Message.IsMessage.Should().BeFalse();
        seqMsg1.Message.Chunk.HasValue.Should().BeTrue();
        seqMsg1.IsFirstChunk.Should().BeTrue();
        seqMsg1.IsLastChunk.Should().BeFalse();
        seqMsg1.SeqNr.Should().Be(1);
        
        producerController.Tell(new ProducerController.Request(0L, 10L, true, false));
        await replyProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100)); // no reply yet

        var seqMsg2 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        seqMsg2.Message.IsMessage.Should().BeFalse();
        seqMsg2.Message.Chunk.HasValue.Should().BeTrue();
        seqMsg2.IsFirstChunk.Should().BeFalse();
        seqMsg2.IsLastChunk.Should().BeFalse();
        seqMsg2.SeqNr.Should().Be(2);
        
        producerController.Tell(new ProducerController.Request(0L, 10L, true, false));
        
        var seqMsg3 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        seqMsg3.Message.IsMessage.Should().BeFalse();
        seqMsg3.Message.Chunk.HasValue.Should().BeTrue();
        seqMsg3.IsFirstChunk.Should().BeFalse();
        seqMsg3.IsLastChunk.Should().BeTrue();
        seqMsg3.SeqNr.Should().Be(3);
        
        // confirm all 3
        producerController.Tell(new ProducerController.Request(3L, 10L, true, false));
        
        // expect confirmation to come to reply probe
        await replyProbe.ExpectMsgAsync(3L);
    }

    [Fact]
    public async Task ProducerController_should_allow_restart_of_producer()
    {
        NextId();
        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None),
            $"producerController-{_idCount}");
        var producerProbe1 = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe1.Ref));

        var consumerControllerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe.Ref));

        (await producerProbe1.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo.Tell(new Job("msg-1"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 1, producerController));

        producerController.Tell(new ProducerController.Request(1L, 10L, true, false));

        (await producerProbe1.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo.Tell(new Job("msg-2"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 2, producerController));

        (await producerProbe1.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).CurrentSeqNr.Should().Be(3);

        // restart producer, new Start
        var producerProbe2 = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe2.Ref));

        (await producerProbe2.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-3"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 3, producerController));

        (await producerProbe2.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-4"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 4, producerController));
    }

    [Fact]
    public async Task ProducerController_must_chunk_large_messages()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var producerController =
            Sys.ActorOf(
                ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None,
                    ProducerController.Settings.Create(Sys) with { ChunkLargeMessagesBytes = 1}),
                $"producerController-{_idCount}");
        
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));
        
        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe.Ref));
        
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo.Tell(new Job("abc"));
        var seqMsg1 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        seqMsg1.Message.IsMessage.Should().BeFalse();
        seqMsg1.Message.Chunk.HasValue.Should().BeTrue();
        seqMsg1.IsFirstChunk.Should().BeTrue();
        seqMsg1.IsLastChunk.Should().BeFalse();
        seqMsg1.SeqNr.Should().Be(1);
        
        producerController.Tell(new ProducerController.Request(0L, 10L, true, false));
        
        var seqMsg2 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        seqMsg2.Message.IsMessage.Should().BeFalse();
        seqMsg2.Message.Chunk.HasValue.Should().BeTrue();
        seqMsg2.IsFirstChunk.Should().BeFalse();
        seqMsg2.IsLastChunk.Should().BeFalse();
        seqMsg2.SeqNr.Should().Be(2);
        
        producerController.Tell(new ProducerController.Request(0L, 10L, true, false));
        
        var seqMsg3 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        seqMsg3.Message.IsMessage.Should().BeFalse();
        seqMsg3.Message.Chunk.HasValue.Should().BeTrue();
        seqMsg3.IsFirstChunk.Should().BeFalse();
        seqMsg3.IsLastChunk.Should().BeTrue();
        seqMsg3.SeqNr.Should().Be(3);
        
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo.Tell(new Job("d"));
        var seqMsg4 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        seqMsg4.Message.IsMessage.Should().BeFalse();
        seqMsg4.Message.Chunk.HasValue.Should().BeTrue();
        seqMsg4.IsFirstChunk.Should().BeTrue();
        seqMsg4.IsLastChunk.Should().BeTrue();
        seqMsg4.SeqNr.Should().Be(4);
    }

    [Fact]
    public async Task
        ProducerController_without_resends_must_not_resend_last_lost_SequencedMessage_when_receiving_Request()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None),
            $"producerController-{_idCount}");
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));

        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe.Ref));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo.Tell(new Job("msg-1"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 1, producerController));

        producerController.Tell(new ProducerController.Request(1L, 10L, false, false));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-2"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 2, producerController));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-3"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 3, producerController));
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-4"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 4, producerController));

        // let's say 3 and 4 are lost, and no more messages are sent from producer
        // ConsumerController will resend Request periodically
        producerController.Tell(new ProducerController.Request(2L, 10L, false, true));
        
        // but 3 and 4 are not resent because supportResend=false
        await consumerControllerProbe.ExpectNoMsgAsync(100.Milliseconds());
        
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-5"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 5, producerController));
    }

    [Fact]
    public async Task
        ProducerController_without_resends_must_reply_to_MessageWithConfirmation_for_lost_messages()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None),
            $"producerController-{_idCount}");
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));

        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe.Ref));
        
        var replyTo = CreateTestProbe();
        
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .AskNextTo(new ProducerController.MessageWithConfirmation<Job>(new Job("msg-1"), replyTo.Ref));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 1, producerController, ack:true));
        producerController.Tell(new ProducerController.Request(1L, 10L, false, false));
        await replyTo.ExpectMsgAsync(1L);
        
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .AskNextTo(new ProducerController.MessageWithConfirmation<Job>(new Job("msg-2"), replyTo.Ref));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 2, producerController, ack:true));
        producerController.Tell(new ProducerController.Ack(2L));
        await replyTo.ExpectMsgAsync(2L);
        
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .AskNextTo(new ProducerController.MessageWithConfirmation<Job>(new Job("msg-3"), replyTo.Ref));
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .AskNextTo(new ProducerController.MessageWithConfirmation<Job>(new Job("msg-4"), replyTo.Ref));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 3, producerController, ack:true));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 4, producerController, ack:true));
        // Ack(3 lost, but Ack(4) triggers reply for 3 and 4
        producerController.Tell(new ProducerController.Ack(4L));
        await replyTo.ExpectMsgAsync(3L);
        await replyTo.ExpectMsgAsync(4L);
        
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .AskNextTo(new ProducerController.MessageWithConfirmation<Job>(new Job("msg-5"), replyTo.Ref));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 5, producerController, ack:true));
        // Ack(5) lost, but eventually a Request will trigger the reply
        producerController.Tell(new ProducerController.Request(5L, 15L, false, false));
        await replyTo.ExpectMsgAsync(5L);
    }

    /// <summary>
    /// Reproduction for https://github.com/akkadotnet/akka.net/issues/6754
    /// </summary>
    [Fact]
    public async Task Repro6754()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var msg = new string('*', 10_000); // 10k char length string
        var producerController =
            Sys.ActorOf(
                ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None,
                    ProducerController.Settings.Create(Sys) with { ChunkLargeMessagesBytes = 1024}),
                $"producerController-{_idCount}");
        
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));
        
        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe.Ref));
        
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
            .SendNextTo.Tell(new Job(msg));
        var seqMsg1 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        seqMsg1.Message.IsMessage.Should().BeFalse();
        seqMsg1.Message.Chunk.HasValue.Should().BeTrue();
        seqMsg1.IsFirstChunk.Should().BeTrue();
        seqMsg1.IsLastChunk.Should().BeFalse();
        seqMsg1.SeqNr.Should().Be(1);
        
        producerController.Tell(new ProducerController.Request(0L, 10L, true, false));
        
        var seqMsg2 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        seqMsg2.Message.IsMessage.Should().BeFalse();
        seqMsg2.Message.Chunk.HasValue.Should().BeTrue();
        seqMsg2.IsFirstChunk.Should().BeFalse();
        seqMsg2.IsLastChunk.Should().BeFalse();
        seqMsg2.SeqNr.Should().Be(2);
        
        // producerController.Tell(new ProducerController.Request(0L, 10L, true, false));
        //
        // var seqMsg3 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        // seqMsg3.Message.IsMessage.Should().BeFalse();
        // seqMsg3.Message.Chunk.HasValue.Should().BeTrue();
        // seqMsg3.IsFirstChunk.Should().BeFalse();
        // seqMsg3.IsLastChunk.Should().BeTrue();
        // seqMsg3.SeqNr.Should().Be(3);
        //
        // (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>())
        //     .SendNextTo.Tell(new Job("d"));
        // var seqMsg4 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        // seqMsg4.Message.IsMessage.Should().BeFalse();
        // seqMsg4.Message.Chunk.HasValue.Should().BeTrue();
        // seqMsg4.IsFirstChunk.Should().BeTrue();
        // seqMsg4.IsLastChunk.Should().BeTrue();
        // seqMsg4.SeqNr.Should().Be(4);
    }
}