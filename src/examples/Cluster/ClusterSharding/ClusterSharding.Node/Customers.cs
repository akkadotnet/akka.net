//-----------------------------------------------------------------------
// <copyright file="Customers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Akka.Persistence;

namespace ClusterSharding.Node
{
    public class Customer : ReceivePersistentActor
    {
        #region Messages
        
        public sealed class PurchaseItem
        {
            public readonly string ItemName;

            public PurchaseItem(string itemName)
            {
                ItemName = itemName;
            }
        }

        public sealed class ItemPurchased
        {
            public readonly string ItemName;

            public ItemPurchased(string itemName)
            {
                ItemName = itemName;
            }
        }

        #endregion
        
        public override string PersistenceId { get; } = Context.Parent.Path.Name + "/" + Context.Self.Path.Name;

        public ICollection<string> _purchasedItems = new List<string>();

        public Customer()
        {
            SetReceiveTimeout(TimeSpan.FromSeconds(60));
            Recover<ItemPurchased>(purchased => _purchasedItems.Add(purchased.ItemName));

            Command<PurchaseItem>(purchase =>
            {
                Persist(new ItemPurchased(purchase.ItemName), purchased =>
                {
                    _purchasedItems.Add(purchased.ItemName);
                    var name = Uri.UnescapeDataString(Self.Path.Name);
                    Console.WriteLine($"'{name}' purchased '{purchased.ItemName}'.\nAll items: [{string.Join(", ", _purchasedItems)}]\n--------------------------");
                });
            });
        }
    }
}
