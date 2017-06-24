---
uid: persistent-fsm
title: Persistence FSM
---
# Persistence FSM

`PersistentFSM` handles the incoming messages in an FSM like fashion. Its internal state is persisted as a sequence of changes, later referred to as domain events. Relationship between incoming messages, FSM's states and transitions, persistence of domain events is defined by a DSL.

> [!WARNING]
> PersistentFSM is marked as `experimental`.

To demonstrate the features of the `PersistentFSM` class, consider an actor which represents a Web store customer. The contract of our "`WebStoreCustomerFSMActor`" is that it accepts the following commands:

[!code-csharp[Main](../../examples/DocsExamples/Persistence/PersistentFSM/PersistentFSM.Commands.cs?range=3-27)]

`AddItem` sent when the customer adds an item to a shopping cart `Buy` - when the customer finishes the purchase `Leave` - when the customer leaves the store without purchasing anything `GetCurrentCart` allows to query the current state of customer's shopping cart

The customer can be in one of the following states:
[!code-csharp[Main](../../examples/DocsExamples/Persistence/PersistentFSM/PersistentFSM.State.cs?range=3-9)]

`LookingAround` customer is browsing the site, but hasn't added anything to the shopping cart `Shopping` customer has recently added items to the shopping cart `Inactive` customer has items in the shopping cart, but hasn't added anything recently `Paid` customer has purchased the items

Customer's actions are "recorded" as a sequence of "domain events" which are persisted. Those events are replayed on an actor's start in order to restore the latest customer's state:

[!code-csharp[Main](../../examples/DocsExamples/Persistence/PersistentFSM/PersistentFSM.Events.cs?range=3-23)]

Customer state data represents the items in a customer's shopping cart:

[!code-csharp[Main](../../examples/DocsExamples/Persistence/PersistentFSM/PersistentFSM.Messages.cs?range=5-65)]

Side-effects:

[!code-csharp[Main](../../examples/DocsExamples/Persistence/PersistentFSM/PersistentFSM.SideEffects.cs?range=3-13)]

Here is how everything is wired together:
[!code-csharp[Main](../../examples/DocsExamples/Persistence/PersistentFSM/ExamplePersistentFSM.cs?range=16-109)]

> [!NOTE]
> State data can only be modified directly on initialization. Later it's modified only as a result of applying domain events. Override the `ApplyEvent` method to define how state data is affected by domain events, see the example below

[!code-csharp[Main](../../examples/DocsExamples/Persistence/PersistentFSM/ExamplePersistentFSM.cs?range=112-125)]

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

