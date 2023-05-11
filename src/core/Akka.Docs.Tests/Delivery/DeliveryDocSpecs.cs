//-----------------------------------------------------------------------
// <copyright file="DeliveryDocSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Delivery;
using Akka.Event;
using Akka.TestKit.Xunit2;
using Akka.Util;

namespace DocsExamples.Delivery;

public class DeliveryDocSpecs : TestKit
{
    // <MessageProtocol>
    public interface ICustomerProtocol{ }
    
    public record PurchaseItem(string ItemName) : ICustomerProtocol;
    // </MessageProtocol>

    // <Consumer>
    public sealed class CustomerActor : UntypedActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly string _customerId;
        private readonly List<string> _purchasedItems = new();

        public CustomerActor(string customerId)
        {
            _customerId = customerId;
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ConsumerController.Delivery<ICustomerProtocol> { Message: PurchaseItem purchase } delivery:
                    _purchasedItems.Add(purchase.ItemName);
                    _log.Info($"'{_customerId}' purchased '{purchase.ItemName}'");
                    delivery.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
                    break;
                default:
                    Unhandled(message);
                    break;
            }
        }
    }
    // </Consumer>
    
    // <Producer>
    public sealed class ProducerActor : UntypedActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly IReadOnlyList<string> _items;

        public ProducerActor(IReadOnlyList<string> items)
        {
            _items = items;
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ProducerController.RequestNext<ICustomerProtocol> requestNext:
                {
                    var item = GetRandomItem();
                    _log.Info($"Producing item '{item}'");
                    
                    // send this to the producer controller
                    requestNext.SendNextTo.Tell(new PurchaseItem(item));
                    break;
                }
            }
        }

        private string GetRandomItem() => _items[ThreadLocalRandom.Current.Next(_items.Count)];
    }
}