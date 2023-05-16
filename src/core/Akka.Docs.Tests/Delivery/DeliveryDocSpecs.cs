//-----------------------------------------------------------------------
// <copyright file="DeliveryDocSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Event;
using Akka.Persistence.Delivery;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;

namespace DocsExamples.Delivery;

public class DeliveryDocSpecs : TestKit
{
    private static readonly Config Config = @"
        akka.reliable-delivery.consumer-controller.flow-control-window = 20
        akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
        akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
        akka.persistence.journal.inmem.test-serialization = on # NOT IMPLEMENTED
        akka.persistence.publish-plugin-commands = on
    ";

    public DeliveryDocSpecs(ITestOutputHelper output) : base(Config, output: output)
    {
    }

    [Fact]
    public async Task ProducerController_and_ConsumerController_should_send_messages()
    {
        // <ProducerRegistration>
        IActorRef producer = Sys.ActorOf(Props.Create(() => new ProducerActor()), "producer");
        IActorRef producerController = Sys.ActorOf(ProducerController.Create<ICustomerProtocol>(Sys,
            producerId: "producerController",
            durableProducerQueue: Option<Props>.None));
        producerController.Tell(new ProducerController.Start<ICustomerProtocol>(producer));
        // </ProducerRegistration>


        // <ConsumerRegistration>
        TestProbe endProbe = CreateTestProbe();

        // stop after 3 messages
        IActorRef consumer = Sys.ActorOf(
            Props.Create(() => new CustomerActor("customer1", endProbe, 3)), "consumer1");
        IActorRef consumerController =
            Sys.ActorOf(ConsumerController.Create<ICustomerProtocol>(Sys, Option<IActorRef>.None),
                "consumerController");

        consumerController.Tell(new ConsumerController.Start<ICustomerProtocol>(consumer));
        consumerController.Tell(
            new ConsumerController.RegisterToProducerController<ICustomerProtocol>(producerController));
        // </ConsumerRegistration>

        await endProbe.ExpectMsgAsync<List<string>>(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ProducerController_and_ConsumerController_should_send_messages_with_Chunking()
    {
        // <ChunkedProducerRegistration>
        IActorRef producer = Sys.ActorOf(Props.Create(() => new ProducerActor()), "producer");

        ProducerController.Settings settings = ProducerController.Settings.Create(Sys) with
        {
            ChunkLargeMessagesBytes = 10 // chunk messages into 10b segments
        };
        IActorRef producerController = Sys.ActorOf(ProducerController.Create<ICustomerProtocol>(Sys,
            producerId: "producerController",
            durableProducerQueue: Option<Props>.None,
            settings: settings));
        producerController.Tell(new ProducerController.Start<ICustomerProtocol>(producer));
        // </ChunkedProducerRegistration>


        TestProbe endProbe = CreateTestProbe();

        // stop after 3 messages
        IActorRef consumer = Sys.ActorOf(
            Props.Create(() => new CustomerActor("customer1", endProbe, 3)), "consumer1");
        IActorRef consumerController =
            Sys.ActorOf(ConsumerController.Create<ICustomerProtocol>(Sys, Option<IActorRef>.None),
                "consumerController");

        consumerController.Tell(new ConsumerController.Start<ICustomerProtocol>(consumer));
        consumerController.Tell(
            new ConsumerController.RegisterToProducerController<ICustomerProtocol>(producerController));

        await endProbe.ExpectMsgAsync<List<string>>(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ProducerController_and_ConsumerController_should_send_messages_with_EventSourcedProducerQueue()
    {
        // <DurableQueueProducer>
        TestProbe producerProbe = CreateTestProbe();

        Props eventSourcedProducerQueue =
            EventSourcedProducerQueue.Create<ICustomerProtocol>(persistentId: "durableQueue-1", Sys);
        IActorRef producerController = Sys.ActorOf(ProducerController.Create<ICustomerProtocol>(Sys,
            producerId: "producerController",
            durableProducerQueue: eventSourcedProducerQueue));
        producerController.Tell(new ProducerController.Start<ICustomerProtocol>(producerProbe.Ref));
        // </DurableQueueProducer>


        TestProbe endProbe = CreateTestProbe();

        // stop after 3 messages
        IActorRef consumer = Sys.ActorOf(
            Props.Create(() => new CustomerActor("customer1", endProbe, 3)), "consumer1");
        IActorRef consumerController =
            Sys.ActorOf(ConsumerController.Create<ICustomerProtocol>(Sys, Option<IActorRef>.None),
                "consumerController");

        consumerController.Tell(new ConsumerController.Start<ICustomerProtocol>(consumer));
        consumerController.Tell(
            new ConsumerController.RegisterToProducerController<ICustomerProtocol>(producerController));
        
        // <ConfirmableMessages>
        ProducerController.RequestNext<ICustomerProtocol> request1 = (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<ICustomerProtocol>>());
        
        // confirm that message was stored in durable queue (so we know it will be redelivered if needed)
        long seqNo1 = await request1.AskNextTo(new PurchaseItem("Burger"));
        // </ConfirmableMessages>
        
        ProducerController.RequestNext<ICustomerProtocol> request2 = (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<ICustomerProtocol>>());
        
        // confirm that message was stored in durable queue (so we know it will be redelivered if needed)
        long seqNo2 = await request2.AskNextTo(new PurchaseItem("Burger"));
        
        ProducerController.RequestNext<ICustomerProtocol> request3 = (await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<ICustomerProtocol>>());
        
        // confirm that message was stored in durable queue (so we know it will be redelivered if needed)
        long seqNo3 = await request1.AskNextTo(new PurchaseItem("Burger"));

        await endProbe.ExpectMsgAsync<List<string>>(TimeSpan.FromSeconds(10));
    }

    // <MessageProtocol>
    public interface ICustomerProtocol
    {
    }

    public record PurchaseItem(string ItemName) : ICustomerProtocol;
    // </MessageProtocol>

    // <Consumer>
    public sealed class CustomerActor : UntypedActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly string _customerId;
        private readonly List<string> _purchasedItems = new();
        private TestProbe _endProbe;
        private readonly int _endMessages;

        public CustomerActor(string customerId, TestProbe endProbe, int endMessages)
        {
            _customerId = customerId;
            _endProbe = endProbe;
            _endMessages = endMessages;
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                // <ConsumerDelivery>
                case ConsumerController.Delivery<ICustomerProtocol> { Message: PurchaseItem purchase } delivery:
                    _purchasedItems.Add(purchase.ItemName);
                    _log.Info($"'{_customerId}' purchased '{purchase.ItemName}'");
                    delivery.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);

                    // terminate after receiving enough messages
                    if (_purchasedItems.Count == _endMessages)
                    {
                        _endProbe.Tell(_purchasedItems);
                        Context.Stop(Self);
                    }

                    break;
                // </ConsumerDelivery>
                default:
                    Unhandled(message);
                    break;
            }
        }
    }
    // </Consumer>

    // <Producer>
    public sealed class ProducerActor : UntypedActor, IWithTimers, IWithStash
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private static readonly IReadOnlyList<string> Items = new[]
        {
            "cheeseburger", "hamburger", "pizza", "cola", "fries", "ice cream"
        };

        private class Tick
        {
            private Tick()
            {
            }

            public static Tick Instance { get; } = new();
        }

        protected override void PreStart()
        {
            Timers.StartPeriodicTimer("Tick", Tick.Instance, TimeSpan.FromSeconds(0.25));
        }

        // <ProducerDelivery>
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ProducerController.RequestNext<ICustomerProtocol> requestNext:
                {
                    // deliver next item once timer Tick is received
                    Become(Active(requestNext.SendNextTo));
                    break;
                }
                case Tick:
                {
                    // ignore;
                    break;
                }
                default:
                    Unhandled(message);
                    break;
            }
        }
        // </ProducerDelivery>

        private Receive Active(IActorRef sendNextTo)
        {
            return msg =>
            {
                switch (msg)
                {
                    case Tick _:
                    {
                        var item = GetRandomItem();
                        _log.Info($"Producing item '{item}'");

                        // send this to the producer controller
                        sendNextTo.Tell(new PurchaseItem(item));
                        Stash.Unstash();
                        Become(OnReceive);
                        return true;
                    }
                    case ProducerController.RequestNext<ICustomerProtocol>:
                        Stash.Stash();
                        return true;
                    default:
                        return false;
                }
            };
        }

        private string GetRandomItem() => Items[ThreadLocalRandom.Current.Next(Items.Count)];
        public ITimerScheduler Timers { get; set; }
        public IStash Stash { get; set; }
    }
    // </Producer>
}