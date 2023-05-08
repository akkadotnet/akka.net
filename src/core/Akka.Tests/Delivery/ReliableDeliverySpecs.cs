// -----------------------------------------------------------------------
//  <copyright file="ReliableDeliverySpecs.cs" company="Akka.NET Project">
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
using static Akka.Tests.Delivery.TestProducer;

namespace Akka.Tests.Delivery;

public class ReliableDeliverySpecs : TestKit.Xunit2.TestKit
{
    internal static readonly Config Config = @"akka.reliable-delivery.consumer-controller.flow-control-window = 20";

    public ReliableDeliverySpecs(ITestOutputHelper output) : this(output, Config)
    {
    }
    
    protected ReliableDeliverySpecs(ITestOutputHelper output, Config config) : base(
        config.WithFallback(TestSerializer.Config), output: output)
    {
    }
    
    private bool Chunked => ProducerController.Settings.Create(Sys).ChunkLargeMessagesBytes != null &&
                            ProducerController.Settings.Create(Sys).ChunkLargeMessagesBytes > 0;
    
    private int _idCount = 0;
    private int NextId() => _idCount++;

    private string ProducerId => $"p-{_idCount}";

    [Fact]
    public async Task ReliableDelivery_must_illustrate_point_to_point_usage()
    {
        NextId();
        var consumerEndProbe = CreateTestProbe();
        var consumerController = Sys.ActorOf(ConsumerController.Create<Job>(Sys, Option<IActorRef>.None), $"consumerController-{_idCount}");
        var testConsumer = Sys.ActorOf(TestConsumer.PropsFor(DefaultConsumerDelay, 42, consumerEndProbe.Ref, consumerController), $"destination-{_idCount}");
        
        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None), $"producerController-{_idCount}");
        var producer = Sys.ActorOf(Props.Create(() => new TestProducer(DefaultProducerDelay, producerController)), $"producer-{_idCount}");
        
        consumerController.Tell(new ConsumerController.RegisterToProducerController<Job>(producerController));

        var collected = await consumerEndProbe.ExpectMsgAsync<Collected>(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReliableDelivery_must_illustrate_point_to_point_usage_with_ask()
    {
        NextId();
        var consumerEndProbe = CreateTestProbe();
        var consumerController = Sys.ActorOf(ConsumerController.Create<Job>(Sys, Option<IActorRef>.None), $"consumerController-{_idCount}");
        var testConsumer = Sys.ActorOf(TestConsumer.PropsFor(DefaultConsumerDelay, 42, consumerEndProbe.Ref, consumerController), $"destination-{_idCount}");

        var replyProbe = CreateTestProbe();
        
        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None), $"producerController-{_idCount}");
        var producer = Sys.ActorOf(Props.Create(() => new TestProducerWithAsk(DefaultProducerDelay, replyProbe.Ref, producerController)), $"producer-{_idCount}");
        
        consumerController.Tell(new ConsumerController.RegisterToProducerController<Job>(producerController));

        var messageCount = (await consumerEndProbe.ExpectMsgAsync<Collected>(TimeSpan.FromSeconds(5))).MessageCount;
        if (Chunked)
            replyProbe.ReceiveN(messageCount, 5.Seconds());
        else
            replyProbe.ReceiveN(messageCount, 5.Seconds()).Should().BeEquivalentTo(Enumerable.Range(1, messageCount));
        
    }

    private async Task TestWithDelays(TimeSpan producerDelay, TimeSpan consumerDelay)
    {
        NextId();
        var consumerEndProbe = CreateTestProbe();
        var consumerController = Sys.ActorOf(ConsumerController.Create<Job>(Sys, Option<IActorRef>.None), $"consumerController-{_idCount}");
        var testConsumer = Sys.ActorOf(TestConsumer.PropsFor(consumerDelay, 42, consumerEndProbe.Ref, consumerController), $"destination-{_idCount}");
        
        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None), $"producerController-{_idCount}");
        var producer = Sys.ActorOf(Props.Create(() => new TestProducer(producerDelay, producerController)), $"producer-{_idCount}");
        
        consumerController.Tell(new ConsumerController.RegisterToProducerController<Job>(producerController));

        await consumerEndProbe.ExpectMsgAsync<Collected>(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReliableDelivery_must_work_with_slow_producer_and_fast_consumer()
    {
        await TestWithDelays(producerDelay: TimeSpan.FromMilliseconds(30), consumerDelay: TimeSpan.Zero);
    }
    
    [Fact]
    public async Task ReliableDelivery_must_work_with_fast_producer_and_slow_consumer()
    {
        await TestWithDelays(producerDelay: TimeSpan.Zero, consumerDelay: TimeSpan.FromMilliseconds(30));
    }
    
    [Fact]
    public async Task ReliableDelivery_must_work_with_fast_producer_and_fast_consumer()
    {
        await TestWithDelays(producerDelay: TimeSpan.Zero, consumerDelay: TimeSpan.Zero);
    }

    [Fact]
    public async Task ReliableDelivery_must_allow_replacement_of_destination()
    {
        NextId();
        var consumerEndProbe = CreateTestProbe();
        var consumerController = Sys.ActorOf(ConsumerController.Create<Job>(Sys, Option<IActorRef>.None), $"consumerController-{_idCount}");
        var testConsumer = Sys.ActorOf(TestConsumer.PropsFor(DefaultConsumerDelay, 42, consumerEndProbe.Ref, consumerController), $"destination-{_idCount}");
        
        var producerController = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None), $"producerController-{_idCount}");
        var producer = Sys.ActorOf(Props.Create(() => new TestProducer(DefaultProducerDelay, producerController)), $"producer-{_idCount}");

        Watch(consumerController);
        consumerController.Tell(new ConsumerController.RegisterToProducerController<Job>(producerController));

        await consumerEndProbe.ExpectMsgAsync<Collected>(TimeSpan.FromSeconds(5));
        await ExpectTerminatedAsync(consumerController);
        
        var consumerEndProbe2 = CreateTestProbe();
        var consumerController2 = Sys.ActorOf(ConsumerController.Create<Job>(Sys, Option<IActorRef>.None), $"consumerController2-{_idCount}");
        var testConsumer2 = Sys.ActorOf(TestConsumer.PropsFor(DefaultConsumerDelay, 42, consumerEndProbe2.Ref, consumerController2), $"destination2-{_idCount}");
        
        consumerController2.Tell(new ConsumerController.RegisterToProducerController<Job>(producerController));
        
        await consumerEndProbe2.ExpectMsgAsync<Collected>(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReliableDelivery_must_allow_replacement_of_producer()
    {
        NextId();
        var consumerProbe = CreateTestProbe();
        var consumerController = Sys.ActorOf(ConsumerController.Create<Job>(Sys, Option<IActorRef>.None), $"consumerController-{_idCount}");
        consumerController.Tell(new ConsumerController.Start<Job>(consumerProbe.Ref));

        var producerController1 = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None), $"producerController-{_idCount}");
        var producerProbe1 = CreateTestProbe();
        
        producerController1.Tell(new ProducerController.Start<Job>(producerProbe1.Ref));
        
        consumerController.Tell(new ConsumerController.RegisterToProducerController<Job>(producerController1));

        (await producerProbe1.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-1"));
        var delivery1 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery1.Message.Should().Be(new Job("msg-1"));
        delivery1.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        
        (await producerProbe1.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-2"));
        var delivery2 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery2.Message.Should().Be(new Job("msg-2"));
        delivery2.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        
        // replace producer
        Sys.Stop(producerController1);
        var producerController2 = Sys.ActorOf(ProducerController.Create<Job>(Sys, ProducerId, Option<Props>.None), $"producerController2-{_idCount}");
        var producerProbe2 = CreateTestProbe();
        producerController2.Tell(new ProducerController.Start<Job>(producerProbe2.Ref));
        producerController2.Tell(new ProducerController.RegisterConsumer<Job>(consumerController));
        
        (await producerProbe2.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-3"));
        var delivery3 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery3.Message.Should().Be(new Job("msg-3"));
        delivery3.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        
        (await producerProbe2.ExpectMsgAsync<ProducerController.RequestNext<Job>>()).SendNextTo.Tell(new Job("msg-4"));
        var delivery4 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery4.Message.Should().Be(new Job("msg-4"));
        delivery4.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
    }
}

/// <summary>
/// Re-run the tests with chunking enabled
/// </summary>
public class ReliableDeliveryChunkedSpecs : ReliableDeliverySpecs
{
    public ReliableDeliveryChunkedSpecs(ITestOutputHelper output) : this(output, 
        ConfigurationFactory.ParseString("akka.reliable-delivery.producer-controller.chunk-large-messages = 1b")
            .WithFallback(ReliableDeliverySpecs.Config))
    {
    }

    protected ReliableDeliveryChunkedSpecs(ITestOutputHelper output, Config config) : base(output, config)
    {
    }
}