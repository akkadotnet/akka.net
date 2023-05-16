// //-----------------------------------------------------------------------
// // <copyright file="ReliableDeliveryWithEventSourcedProducerQueueSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Event;
using Akka.Persistence.Delivery;
using Akka.Persistence.Tests.Serialization;
using Akka.Remote.Configuration;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests.Delivery;

/// <summary>
/// Same tests, but with chunked messages
/// </summary>
public class
    ReliableDeliveryWithEventSourcedProducerQueueChunkedSpec : ReliableDeliveryWithEventSourcedProducerQueueSpec
{
    public ReliableDeliveryWithEventSourcedProducerQueueChunkedSpec(ITestOutputHelper output) : base(output, "akka.reliable-delivery.producer-controller.chunk-large-messages = 1b")
    {
    }
}

public class ReliableDeliveryWithEventSourcedProducerQueueSpec : AkkaSpec
{
    public static readonly Config Config = @"
        akka.loglevel = DEBUG
        akka.reliable-delivery.consumer-controller.flow-control-window = 20
        akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
        akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
        akka.persistence.journal.inmem.test-serialization = on # NOT IMPLEMENTED
        akka.persistence.publish-plugin-commands = on
    ";
    
    // NOTE: need the RemoteConfigFactory.Default() here in order to get message chunking to work properly

    public ReliableDeliveryWithEventSourcedProducerQueueSpec(ITestOutputHelper output) : base(Config.WithFallback(RemoteConfigFactory.Default()), output)
    {
        
    }
    
    protected ReliableDeliveryWithEventSourcedProducerQueueSpec(ITestOutputHelper output, Config config) : base(config.WithFallback(Config).WithFallback(RemoteConfigFactory.Default()), output)
    {
        
    }

    [Fact]
    public async Task
        ReliableDelivery_with_EventSourcedProducerQueue_must_deliver_messages_after_full_producer_and_consumer_restart()
    {
        var producerId = "p1";
        var producerProbe = CreateTestProbe();
        
        var producerQueueProps = EventSourcedProducerQueue.Create<string>(producerId, Sys);

        var producerController = Sys.ActorOf(ProducerController.Create<string>(Sys, producerId, producerQueueProps));
        producerController.Tell(new ProducerController.Start<string>(producerProbe));
        producerProbe.Watch(producerController);
        
        var consumerController = Sys.ActorOf(ConsumerController.Create<string>(Sys, Option<IActorRef>.None));
        var consumerProbe = CreateTestProbe();
        consumerController.Tell(new ConsumerController.Start<string>(consumerProbe.Ref));
        consumerController.Tell(new ConsumerController.RegisterToProducerController<string>(producerController));
        consumerProbe.Watch(consumerController);
        
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<string>>()).SendNextTo.Tell("a");
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<string>>()).SendNextTo.Tell("b");
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<string>>()).SendNextTo.Tell("c");
        await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<string>>();

        (await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<string>>()).Message.Should().Be("a");
        Sys.Log.Info("Stopping [{0}]", producerController);
        Sys.Stop(producerController);
        await producerProbe.ExpectTerminatedAsync(producerController);
        Sys.Stop(consumerController);
        await consumerProbe.ExpectTerminatedAsync(consumerController);
        
        var producerController2 = Sys.ActorOf(ProducerController.Create<string>(Sys, producerId, producerQueueProps));
        producerController2.Tell(new ProducerController.Start<string>(producerProbe));
        
        var consumerController2 = Sys.ActorOf(ConsumerController.Create<string>(Sys, Option<IActorRef>.None));
        consumerController2.Tell(new ConsumerController.Start<string>(consumerProbe.Ref));
        consumerController2.Tell(new ConsumerController.RegisterToProducerController<string>(producerController2));

        var delivery1 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<string>>();
        delivery1.Message.Should().Be("a");
        delivery1.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        
        var delivery2 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<string>>();
        delivery2.Message.Should().Be("b");
        delivery2.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        
        var delivery3 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<string>>();
        delivery3.Message.Should().Be("c");
        delivery3.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        
        var requestNext4 = await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<string>>();
        requestNext4.CurrentSeqNr.Should().Be(4);
        requestNext4.SendNextTo.Tell("d");
        
        var delivery4 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<string>>();
        delivery4.Message.Should().Be("d");
        delivery4.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
    }

    [Fact]
    public async Task
        ReliableDelivery_with_EventSourcedProducerQueue_must_deliver_messages_after_producer_restart_keeping_same_ConsumerController()
    {
        var producerId = "p2";
        var producerProbe = CreateTestProbe();
        
        var producerQueueProps = EventSourcedProducerQueue.Create<string>(producerId, Sys);

        var producerController = Sys.ActorOf(ProducerController.Create<string>(Sys, producerId, producerQueueProps));
        producerController.Tell(new ProducerController.Start<string>(producerProbe));
        producerProbe.Watch(producerController);
        
        var consumerController = Sys.ActorOf(ConsumerController.Create<string>(Sys, Option<IActorRef>.None));
        var consumerProbe = CreateTestProbe();
        consumerController.Tell(new ConsumerController.Start<string>(consumerProbe.Ref));
        consumerController.Tell(new ConsumerController.RegisterToProducerController<string>(producerController));
        
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<string>>()).SendNextTo.Tell("a");
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<string>>()).SendNextTo.Tell("b");
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<string>>()).SendNextTo.Tell("c");
        await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<string>>();

        var delivery1 = (await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<string>>());
        delivery1.Message.Should().Be("a");
        Sys.Log.Info("Stopping [{0}]", producerController);
        Sys.Stop(producerController);
        await producerProbe.ExpectTerminatedAsync(producerController);
        
        var producerController2 = Sys.ActorOf(ProducerController.Create<string>(Sys, producerId, producerQueueProps));
        producerController2.Tell(new ProducerController.Start<string>(producerProbe));
        consumerController.Tell(new ConsumerController.RegisterToProducerController<string>(producerController2));
        
        delivery1.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        
        var requestNext4 = await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<string>>();
        requestNext4.CurrentSeqNr.Should().Be(4);
        requestNext4.SendNextTo.Tell("d");
        
        // TODO: try to deduplicate this in the future?
        var redelivery1 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<string>>();
        redelivery1.Message.Should().Be("a");
        redelivery1.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        
        (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<string>>()).SendNextTo.Tell("e");
        
        var redelivery2 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<string>>();
        redelivery2.Message.Should().Be("b");
        redelivery2.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        
        var redelivery3 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<string>>();
        redelivery3.Message.Should().Be("c");
        redelivery3.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        
        var delivery4 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<string>>();
        delivery4.Message.Should().Be("d");
        delivery4.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        
        var delivery5 = await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<string>>();
        delivery5.Message.Should().Be("e");
        delivery5.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
    }
}