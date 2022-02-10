//-----------------------------------------------------------------------
// <copyright file="Customers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Persistence;

namespace ShoppingCart
{
    #region ActorClass
    public class Customer : ReceiveActor
    {
        public sealed class PurchaseItem
        {
            public readonly string ItemName;

            public PurchaseItem(string itemName)
            {
                ItemName = itemName;
            }
        }
        
        private readonly List<string> _purchasedItems = new List<string>();

        public Customer(string persistenceId)
        {
            Receive<PurchaseItem>(purchase =>
            {
                _purchasedItems.Add(purchase.ItemName);
                var name = Uri.UnescapeDataString(Self.Path.Name);
                Console.WriteLine(
@$"'{name}' purchased '{purchase.ItemName}'.
All items: [{string.Join(", ", _purchasedItems)}]
--------------------------");
            });
        }
    }
    #endregion
}
