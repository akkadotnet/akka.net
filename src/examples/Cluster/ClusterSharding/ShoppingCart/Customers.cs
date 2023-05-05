//-----------------------------------------------------------------------
// <copyright file="Customers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Delivery;
using Akka.Persistence;

namespace ShoppingCart
{
    #region ActorClass

    public class Customer : ReceiveActor
    {
        /// <summary>
        /// Marker interface used for grouping all Customer-entity messages
        /// </summary>
        public interface ICustomerCommand
        {
        }

        public sealed class PurchaseItem : ICustomerCommand
        {
            public readonly string ItemName;

            public PurchaseItem(string itemName)
            {
                ItemName = itemName;
            }
        }

        private readonly List<string> _purchasedItems = new List<string>();
        private readonly IActorRef _consumerController; // use to guarantee reliable delivery of messages

        public Customer(string persistenceId, IActorRef consumerController)
        {
            _consumerController = consumerController;
            Receive<ConsumerController.Delivery<ICustomerCommand>>(purchase =>
            {
                if (purchase.Message is PurchaseItem p)
                {
                    _purchasedItems.Add(p.ItemName);
                    var name = Uri.UnescapeDataString(Self.Path.Name);
                    Console.WriteLine(
                        @$"'{name}' purchased '{p.ItemName}'.
All items: [{string.Join(", ", _purchasedItems)}]
--------------------------");
                }
                else
                {
                    // unsupported message type
                    Unhandled(purchase.Message);
                }
                
                purchase.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
            });
        }

        protected override void PreStart()
        {
            // signal that we're ready to consume messages
            _consumerController.Tell(new ConsumerController.Start<ICustomerCommand>(Self));
        }
    }

    #endregion
}