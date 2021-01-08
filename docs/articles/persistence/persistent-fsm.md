---
uid: persistent-fsm
title: Persistence FSM
---
# Persistence FSM

`PersistentFSM` handles the incoming messages in an FSM like fashion. Its internal state is persisted as a sequence of changes, later referred to as domain events. Relationship between incoming messages, FSM's states and transitions, persistence of domain events is defined by a DSL.

## A Simple Example
To demonstrate the features of the `PersistentFSM` class, consider an actor which represents a Web store customer. The contract of our "`WebStoreCustomerFSMActor`" is that it accepts the following commands:

[!code-csharp[WebStoreCustomerFSMActor.cs](../../../src/core/Akka.Docs.Tests/Persistence/WebStoreCustomerFSMActor.cs?name=persistent-fsm-commands)]

`AddItem` sent when the customer adds an item to a shopping cart `Buy` - when the customer finishes the purchase `Leave` - when the customer leaves the store without purchasing anything `GetCurrentCart` allows to query the current state of customer's shopping cart

The customer can be in one of the following states:
[!code-csharp[WebStoreCustomerFSMActor.cs](../../../src/core/Akka.Docs.Tests/Persistence/WebStoreCustomerFSMActor.cs?name=persistent-fsm-states)]

`LookingAround` customer is browsing the site, but hasn't added anything to the shopping cart `Shopping` customer has recently added items to the shopping cart `Inactive` customer has items in the shopping cart, but hasn't added anything recently `Paid` customer has purchased the items

> [!NOTE]
> `PersistentFSM` states must inherit from trait `PersistentFSM.IFsmState` and implement the `string Identifier` property. This is required in order to simplify the serialization of FSM states. String identifiers should be unique!

Customer's actions are "recorded" as a sequence of "domain events" which are persisted. Those events are replayed on an actor's start in order to restore the latest customer's state:

[!code-csharp[WebStoreCustomerFSMActor.cs](../../../src/core/Akka.Docs.Tests/Persistence/WebStoreCustomerFSMActor.cs?name=persistent-fsm-domain-events)]

Customer state data represents the items in a customer's shopping cart:

[!code-csharp[WebStoreCustomerFSMActor.cs](../../../src/core/Akka.Docs.Tests/Persistence/WebStoreCustomerFSMActor.cs?name=persistent-fsm-domain-messages)]

Side-effects:

[!code-csharp[WebStoreCustomerFSMActor.cs](../../../src/core/Akka.Docs.Tests/Persistence/WebStoreCustomerFSMActor.cs?name=persistent-fsm-side-effects)]

Here is how everything is wired together:
[!code-csharp[WebStoreCustomerFSMActor.cs](../../../src/core/Akka.Docs.Tests/Persistence/WebStoreCustomerFSMActor.cs?name=persistent-fsm-setup)]

> [!NOTE]
> State data can only be modified directly on initialization. Later it's modified only as a result of applying domain events. Override the `ApplyEvent` method to define how state data is affected by domain events, see the example below

[!code-csharp[WebStoreCustomerFSMActor.cs](../../../src/core/Akka.Docs.Tests/Persistence/WebStoreCustomerFSMActor.cs?name=persistent-fsm-apply-event)]

`AndThen` can be used to define actions which will be executed following event’s persistence - convenient for "side effects" like sending a message or logging. Notice that actions defined in andThen block are not executed on recovery:
```cs
GoTo(Paid.Instance).Applying(OrderExecuted.Instance).AndThen(cart =>
{
    if (cart is NonEmptyShoppingCart nonShoppingCart)
    {
        reportActor.Tell(new PurchaseWasMade(nonShoppingCart.Items));
    }
});
```
A snapshot of state data can be persisted by calling the `SaveStateSnapshot()` method:
```cs
Stop().Applying(OrderDiscarded.Instance).AndThen(cart =>
{
    reportActor.Tell(ShoppingCardDiscarded.Instance);
    SaveStateSnapshot();
});
```
On recovery state data is initialized according to the latest available snapshot, then the remaining domain events are replayed, triggering the `ApplyEvent` method.

## Periodical snapshot by snapshot-after

You can enable periodical `SaveStateSnapshot()` calls in `PersistentFSM` if you turn the following flag on in `reference.conf`
```
akka.persistence.fsm.snapshot-after = 1000
```
this means `SaveStateSnapshot()` is called after the sequence number reaches multiple of 1000.

> [!NOTE]
> `SaveStateSnapshot()` might not be called exactly at sequence numbers being multiple of the `snapshot-after` configuration value.
This is because `PersistentFSM` works in a sort of "batch" mode when processing and persisting events, and `SaveStateSnapshot()`
is called only at the end of the "batch". For example, if you set `akka.persistence.fsm.snapshot-after = 1000`,
it is possible that `SaveStateSnapshot()` is called at `lastSequenceNr = 1005, 2003, ... `
A single batch might persist state transition, also there could be multiple domain events to be persisted
if you pass them to `Applying`  method in the `PersistentFSM` DSL.
