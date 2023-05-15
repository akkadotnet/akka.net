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
using Akka.Delivery;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Akka.Util;

namespace DocsExamples.Delivery;

public class DeliveryDocSpecs : TestKit
{
    public async Task ProducerController_and_ConsumerController_should_send_messages()
    {
        IActorRef producer = CreateTestProbe();
        IActorRef producerController = Sys.ActorOf(ProducerController.Create<ICustomerProtocol>(Sys, producerId: "producer-1",
            durableProducerQueue: Option<Props>.None));
        producerController.Tell();
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
        private readonly IReadOnlyList<string> _items;

        private class Tick
        {
            private Tick()
            {
            }

            public static Tick Instance { get; } = new();
        }

        public ProducerActor(IReadOnlyList<string> items)
        {
            _items = items;
        }

        protected override void PreStart()
        {
            Timers.StartPeriodicTimer("Tick", Tick.Instance, TimeSpan.FromSeconds(1));
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ProducerController.RequestNext<ICustomerProtocol> requestNext:
                {
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

        private string GetRandomItem() => _items[ThreadLocalRandom.Current.Next(_items.Count)];
        public ITimerScheduler Timers { get; set; }
        public IStash Stash { get; set; }
    }
}