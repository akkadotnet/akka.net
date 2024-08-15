// -----------------------------------------------------------------------
//  <copyright file="WebStoreCustomerFSMActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Persistence.Fsm;

namespace DocsExamples.Persistence.PersistentFSM;

#region persistent-fsm-commands

public interface ICommand
{
}

public class AddItem : ICommand
{
    public AddItem(Item item)
    {
        Item = item;
    }

    public Item Item { get; set; }
}

public class Buy : ICommand
{
    private Buy()
    {
    }

    public static Buy Instance { get; } = new();
}

public class Leave : ICommand
{
    private Leave()
    {
    }

    public static Leave Instance { get; } = new();
}

public class GetCurrentCart : ICommand
{
    private GetCurrentCart()
    {
    }

    public static GetCurrentCart Instance { get; } = new();
}

#endregion

#region persistent-fsm-states

public interface IUserState : Akka.Persistence.Fsm.PersistentFSM.IFsmState
{
}

public class LookingAround : IUserState
{
    private LookingAround()
    {
    }

    public static LookingAround Instance { get; } = new();
    public string Identifier => "Looking Around";
}

public class Shopping : IUserState
{
    private Shopping()
    {
    }

    public static Shopping Instance { get; } = new();
    public string Identifier => "Shopping";
}

public class Inactive : IUserState
{
    private Inactive()
    {
    }

    public static Inactive Instance { get; } = new();
    public string Identifier => "Inactive";
}

public class Paid : IUserState
{
    private Paid()
    {
    }

    public static Paid Instance { get; } = new();
    public string Identifier => "Paid";
}

#endregion

#region persistent-fsm-domain-events

public interface IDomainEvent
{
}

public class ItemAdded : IDomainEvent
{
    public ItemAdded(Item item)
    {
        Item = item;
    }

    public Item Item { get; set; }
}

public class OrderExecuted : IDomainEvent
{
    private OrderExecuted()
    {
    }

    public static OrderExecuted Instance { get; } = new();
}

public class OrderDiscarded : IDomainEvent
{
    private OrderDiscarded()
    {
    }

    public static OrderDiscarded Instance { get; } = new();
}

#endregion

#region persistent-fsm-domain-messages

public class Item
{
    public Item(string id, string name, double price)
    {
        Id = id;
        Name = name;
        Price = price;
    }

    public string Id { get; }

    public string Name { get; }

    public double Price { get; }
}

public interface IShoppingCart
{
    IShoppingCart AddItem(Item item);
    IShoppingCart Empty();
}

public class EmptyShoppingCart : IShoppingCart
{
    public IShoppingCart AddItem(Item item)
    {
        return new NonEmptyShoppingCart(ImmutableList.Create(item));
    }

    public IShoppingCart Empty()
    {
        return this;
    }
}

public class NonEmptyShoppingCart : IShoppingCart
{
    public NonEmptyShoppingCart(ImmutableList<Item> items)
    {
        Items = items;
    }

    public ImmutableList<Item> Items { get; }

    public IShoppingCart AddItem(Item item)
    {
        return new NonEmptyShoppingCart(Items.Add(item));
    }

    public IShoppingCart Empty()
    {
        return new EmptyShoppingCart();
    }
}

#endregion

#region persistent-fsm-side-effects

public interface IReportEvent
{
}

public class PurchaseWasMade : IReportEvent
{
    public PurchaseWasMade(IEnumerable<Item> items)
    {
        Items = items;
    }

    public IEnumerable<Item> Items { get; }
}

public class ShoppingCardDiscarded : IReportEvent
{
    private ShoppingCardDiscarded()
    {
    }

    public static ShoppingCardDiscarded Instance { get; } = new();
}

#endregion

internal class WebStoreCustomerFSMActor : PersistentFSM<IUserState, IShoppingCart, IDomainEvent>
{
    public WebStoreCustomerFSMActor(string persistenceId, IActorRef reportActor)
    {
        PersistenceId = persistenceId;

        #region persistent-fsm-setup

        StartWith(LookingAround.Instance, new EmptyShoppingCart());

        When(LookingAround.Instance, (evt, _) =>
        {
            if (evt.FsmEvent is AddItem addItem)
                return GoTo(Shopping.Instance)
                    .Applying(new ItemAdded(addItem.Item))
                    .ForMax(TimeSpan.FromSeconds(1));
            if (evt.FsmEvent is GetCurrentCart) return Stay().Replying(evt.StateData);

            return Stay();
        });

        When(Shopping.Instance, (evt, _) =>
        {
            if (evt.FsmEvent is AddItem addItem)
                return Stay()
                    .Applying(new ItemAdded(addItem.Item))
                    .ForMax(TimeSpan.FromSeconds(1));
            if (evt.FsmEvent is Buy)
                return GoTo(Paid.Instance).Applying(OrderExecuted.Instance)
                    .AndThen(cart =>
                    {
                        if (cart is NonEmptyShoppingCart nonShoppingCart)
                        {
                            reportActor.Tell(new PurchaseWasMade(nonShoppingCart.Items));
                            SaveStateSnapshot();
                        }
                        else if (cart is EmptyShoppingCart)
                        {
                            SaveStateSnapshot();
                        }
                    });
            if (evt.FsmEvent is Leave)
                return Stop().Applying(OrderDiscarded.Instance)
                    .AndThen(_ =>
                    {
                        reportActor.Tell(ShoppingCardDiscarded.Instance);
                        SaveStateSnapshot();
                    });
            if (evt.FsmEvent is GetCurrentCart)
                return Stay().Replying(evt.StateData);
            if (evt.FsmEvent is FSMBase.StateTimeout) return GoTo(Inactive.Instance).ForMax(TimeSpan.FromSeconds(2));

            return Stay();
        });

        When(Inactive.Instance, (evt, _) =>
        {
            if (evt.FsmEvent is AddItem addItem)
                return GoTo(Shopping.Instance)
                    .Applying(new ItemAdded(addItem.Item))
                    .ForMax(TimeSpan.FromSeconds(1));
            if (evt.FsmEvent is FSMBase.StateTimeout)
                return Stop()
                    .Applying(OrderDiscarded.Instance)
                    .AndThen(_ => reportActor.Tell(ShoppingCardDiscarded.Instance));

            return Stay();
        });

        When(Paid.Instance, (evt, _) =>
        {
            if (evt.FsmEvent is Leave)
                return Stop();
            if (evt.FsmEvent is GetCurrentCart) return Stay().Replying(evt.StateData);

            return Stay();
        });

        #endregion
    }

    public override string PersistenceId { get; }

    internal static Props Props(string name, IActorRef dummyReportActorRef)
    {
        return Akka.Actor.Props.Create(() => new WebStoreCustomerFSMActor(name, dummyReportActorRef));
    }

    #region persistent-fsm-apply-event

    protected override IShoppingCart ApplyEvent(IDomainEvent evt, IShoppingCart cartBeforeEvent)
    {
        switch (evt)
        {
            case ItemAdded itemAdded: return cartBeforeEvent.AddItem(itemAdded.Item);
            case OrderExecuted _: return cartBeforeEvent;
            case OrderDiscarded _: return cartBeforeEvent.Empty();
            default: return cartBeforeEvent;
        }
    }

    #endregion
}