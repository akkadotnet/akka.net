// -----------------------------------------------------------------------
//  <copyright file="DurableProducerControllerSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Delivery.Internal;
using Akka.IO;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Tests.Delivery.TestConsumer;
using static Akka.Delivery.DurableProducerQueue;
using static Akka.Tests.Delivery.TestDurableProducerQueue;

namespace Akka.Tests.Delivery;

public class DurableProducerControllerSpecs : TestKit.Xunit2.TestKit
{
    private static readonly Config Config = @"akka.reliable-delivery.consumer-controller.flow-control-window = 20
     akka.reliable-delivery.consumer-controller.resend-interval-min = 1s";

    public DurableProducerControllerSpecs(ITestOutputHelper output) : base(
        Config.WithFallback(TestSerializer.Config), output: output)
    {
    }

    private int _idCount = 0;
    private int NextId() => _idCount++;

    private string ProducerId => $"p-{_idCount}";

    [Fact]
    public async Task ProducerController_with_durable_queue_must_load_initial_state_resend_unconfirmed()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var durable = CreateProps(TimeSpan.Zero,
            new DurableProducerQueue.State<Job>(CurrentSeqNr: 5, HighestConfirmedSeqNr: 2,
                ConfirmedSeqNr: ImmutableDictionary<string, (long, long)>.Empty.Add(NoQualifier, (2L, TestTimestamp)),
                Unconfirmed: ImmutableList<MessageSent<Job>>.Empty
                    .Add(new MessageSent<Job>(3, new Job("msg-3"), false, NoQualifier, TestTimestamp))
                    .Add(new MessageSent<Job>(4, new Job("msg-4"), false, NoQualifier, TestTimestamp))), _ => false);

        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, durable),
            $"producerController-{_idCount}");
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));

        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe));

        // no request to producer since it has unconfirmed to begin with
        await producerProbe.ExpectNoMsgAsync(100);

        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 3, producerController).AsFirst());
        await consumerControllerProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        producerController.Tell(new ProducerController.Request(3L, 13L, true, false));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 4, producerController));

        var sendTo = (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo;
        sendTo.Tell(new Job("msg-5"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 5, producerController));
    }

    [Fact]
    public async Task ProducerController_with_durable_queue_must_store_confirmations()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var stateHolder = new AtomicReference<DurableProducerQueueStateHolder<Job>>(State<Job>.Empty);
        var durable = CreateProps(TimeSpan.Zero,
            stateHolder, _ => false);
        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, durable),
            $"producerController-{_idCount}");
        var producerProbe = CreateTestProbe();

        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));

        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-1"));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 1, producerController));

        await AwaitAssertAsync(() =>
        {
            stateHolder.Value.State.Should().Be(new State<Job>(2, 0, ImmutableDictionary<string, (long, long)>.Empty,
                ImmutableList.Create<MessageSent<Job>>()
                    .Add(new MessageSent<Job>(1, new Job("msg-1"), false, NoQualifier, TestTimestamp))));
            return Task.CompletedTask;
        });

        producerController.Tell(new ProducerController.Request(1L, 10L, true, false));
        await AwaitAssertAsync(() =>
        {
            stateHolder.Value.State.Should().Be(new State<Job>(2, 1,
                ImmutableDictionary<string, (long, long)>.Empty
                    .Add(NoQualifier, (1L, TestTimestamp)),
                ImmutableList<MessageSent<Job>>.Empty));
            return Task.CompletedTask;
        });

        var replyTo = CreateTestProbe();
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).AskNextTo(
            new ProducerController.MessageWithConfirmation<Job>(new Job("msg-2"), replyTo.Ref));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 2, producerController, ack: true));
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).AskNextTo(
            new ProducerController.MessageWithConfirmation<Job>(new Job("msg-3"), replyTo.Ref));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 3, producerController, ack: true));
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).AskNextTo(
            new ProducerController.MessageWithConfirmation<Job>(new Job("msg-4"), replyTo.Ref));
        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 4, producerController, ack: true));
        producerController.Tell(new ProducerController.Ack(3L));

        await AwaitAssertAsync(() =>
        {
            stateHolder.Value.State.Should().Be(new State<Job>(5, 3,
                ImmutableDictionary<string, (long, long)>.Empty
                    .Add(NoQualifier, (3L, TestTimestamp)),
                ImmutableList<MessageSent<Job>>.Empty.Add(new MessageSent<Job>(4, new Job("msg-4"), true, NoQualifier,
                    TestTimestamp))));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ProducerController_with_durable_queue_must_reply_to_MessageWithConfirmation_after_storage()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var durable = CreateProps(TimeSpan.Zero,
            State<Job>.Empty, _ => false);

        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, durable),
            $"producerController-{_idCount}");
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));

        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe));

        var replyTo = CreateTestProbe();

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).AskNextTo(
            new ProducerController.MessageWithConfirmation<Job>(new Job("msg-1"), replyTo.Ref));
        await replyTo.ExpectMsgAsync(1L);

        await consumerControllerProbe.ExpectMsgAsync(SequencedMessage(ProducerId, 1, producerController, ack: true));
        producerController.Tell(new ProducerController.Request(1L, 10L, true, false));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).AskNextTo(
            new ProducerController.MessageWithConfirmation<Job>(new Job("msg-2"), replyTo.Ref));
        replyTo.ExpectMsg(2L);
    }

    [Fact]
    public async Task ProducerController_with_durable_queue_must_store_chunked_messages()
    {
        NextId();
        var consumerControllerProbe = CreateTestProbe();

        var stateHolder = new AtomicReference<DurableProducerQueueStateHolder<Job>>(State<Job>.Empty);
        var durable = CreateProps(TimeSpan.Zero,
            stateHolder, _ => false);
        var producerController = Sys.ActorOf(
            ProducerController.Create<Job>(Sys, ProducerId, durable,
                ProducerController.Settings.Create(Sys) with { ChunkLargeMessagesBytes = 1}),
            $"producerController-{_idCount}");
        var producerProbe = CreateTestProbe();

        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));

        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe));

        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("abc"));
        await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();

        await AwaitAssertAsync(() =>
        {
            var durableState = stateHolder.Value.State;
            durableState.CurrentSeqNr.Should().Be(2);
            durableState.Unconfirmed.Count.Should().Be(1);
            durableState.Unconfirmed.First().Message.IsMessage.Should().BeFalse();
            return Task.CompletedTask;
        });

        producerController.Tell(new ProducerController.Request(0L, 10L, true, false));

        await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();

        var seqMsg3 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        seqMsg3.Message.IsMessage.Should().BeFalse();
        seqMsg3.IsFirstChunk.Should().BeFalse();
        seqMsg3.IsLastChunk.Should().BeTrue();
        seqMsg3.SeqNr.Should().Be(3);

        await AwaitAssertAsync(() =>
        {
            var durableState = stateHolder.Value.State;
            durableState.CurrentSeqNr.Should().Be(4);
            durableState.Unconfirmed.Count.Should().Be(3);
            durableState.Unconfirmed.First().Message.IsMessage.Should().BeFalse();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task
        ProducerController_with_durable_queue_must_load_initial_state_but_dont_resend_partially_stored_chunked_messages()
    {
        // may happen if crashed before all chunked messages have been stored,
        // should be treated as if none of them were stored (they were not confirmed)
        NextId();

        var consumerControllerProbe = CreateTestProbe();

        var durable = CreateProps(TimeSpan.Zero,
            new DurableProducerQueue.State<Job>(CurrentSeqNr: 5, HighestConfirmedSeqNr: 2,
                ConfirmedSeqNr: ImmutableDictionary<string, (long, long)>.Empty.Add(NoQualifier, (2L, TestTimestamp)),
                Unconfirmed: ImmutableList<MessageSent<Job>>.Empty
                    .Add(MessageSent<Job>.FromChunked(3,
                        new ChunkedMessage(ByteString.FromString("abc"), true, true, 20, ""), false, NoQualifier,
                        TestTimestamp))
                    .Add(MessageSent<Job>.FromChunked(4,
                        new ChunkedMessage(ByteString.FromString("d"), true, false, 20, ""), false, NoQualifier,
                        TestTimestamp))
                    .Add(MessageSent<Job>.FromChunked(5,
                        new ChunkedMessage(ByteString.FromString("e"), false, false, 20, ""), false, NoQualifier,
                        TestTimestamp)))
            // missing last chunk
            , _ => false);

        var producerController =
            Sys.ActorOf(
                ProducerController.Create<Job>(Sys, ProducerId, durable,
                    ProducerController.Settings.Create(Sys) with { ChunkLargeMessagesBytes = 1}),
                $"producerController-{_idCount}");
        
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ProducerController.Start<Job>(producerProbe.Ref));
        
        producerController.Tell(new ProducerController.RegisterConsumer<Job>(consumerControllerProbe));

        var seqMsg3 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        seqMsg3.SeqNr.Should().Be(3);
        seqMsg3.IsFirstChunk.Should().BeTrue();
        seqMsg3.IsLastChunk.Should().BeTrue();
        
        producerController.Tell(new ProducerController.Request(0L, 10L, true, false));
        
        // 4 and 5 discarded because missing last chunk
        await consumerControllerProbe.ExpectNoMsgAsync();
        
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("g"));
        
        var seqMsg4 = await consumerControllerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<Job>>();
        seqMsg4.SeqNr.Should().Be(4);
        seqMsg4.IsFirstChunk.Should().BeTrue();
        seqMsg4.IsLastChunk.Should().BeTrue();
    }
}