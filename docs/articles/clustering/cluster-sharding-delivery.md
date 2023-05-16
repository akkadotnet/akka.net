---
uid: cluster-sharding-delivery
title: Reliable Delivery over Akka.Cluster.Sharding
---

# Reliable Delivery over Akka.Cluster.Sharding

> [!TIP]
> Please see "[Reliable Message Delivery with Akka.Delivery](xref:reliable-delivery)" before reading this documentation. Akka.Cluster.Sharding.Delivery builds upon all of the concepts and tools implemented in the base Akka.Delivery APIs.

If you're using [Akka.Cluster.Sharding](xref:cluster-sharding) to distribute state via one or more `ShardRegion`s across your Akka.Cluster, Akka.Cluster.Sharding.Delivery can help you guarantee delivery of messages from the rest of your `ActorSystem`s to each of your entity actors.

## Point to Point Delivery

Akka.Cluster.Sharding.Delivery only uses [point-to-point delivery mode from Akka.Delivery](xref:reliable-delivery) and **message chunking is not supported** in this mode.

### Typed Messaging Protocol

Akka.Cluster.ShardingDelivery uses a .NET generic-typed protocol and the `ShardingProducerController` and `ShardingConsumerController` are also both strongly typed. This means that end-users need to organize their messages into "protocol groups" in order to be effective, like so:

[!code-csharp[Message Protocol](../../../src/core/Akka.Docs.Tests/Delivery/DeliveryDocSpecs.cs?name=MessageProtocol)]

The common interface that all of the messages in this protocol implement is typically the type you'll want to use for your generic argument `T` in the Akka.Delivery or [Akka.Cluster.Sharding.Delivery](xref:cluster-sharding-delivery) method calls and types, as shown below:

[!code-csharp[Starting Typed Actors](../../../src/core/Akka.Docs.Tests/Delivery/DeliveryDocSpecs.cs?name=ProducerRegistration)]

### Built-in Actors and Messages

![Overview of built-in Akka.Cluster.Sharding.Delivery actors](/images/cluster/delivery/1-sharding-delivery-registration.png)

The Akka.Cluster.Sharding.Delivery relationship consists of the following actors:

* **`Producer`** - this is a user-defined actor that is responsible for the production of messages. It receives [`ShardingProducerController.RequestNext<T>`](xref:Akka.Cluster.Sharding.Delivery.ShardingProducerController.RequestNext`1) messages from the `ShardingProducerController` when capacity is available to deliver additional messages.
* **`ShardingProducerController`** - this actor is built into Akka.Cluster.ShardingDelivery and does most of the work. **You typically only need a single `ShardingProducerController` per-`ActorSystem` / per-`ShardRegion`** (or you can use [Sharded Daemon Processes](xref:sharded-daemon-process) to host a fixed number of producers per-cluster.) The `ShardingProducerController` is responsible for spawning a `ProducerController` per-entity and delivering those messages to the `ShardRegion` `IActorRef`.
* **`ShardingConsumerController`** - this actor is built into Akka.Cluster.Sharding.Delivery and typically resides on the opposite site of the network from the `ShardingProducerController`. This actor wraps around your normal Akka.Cluster.Sharding entity actors and is created directly by the `ShardRegion` each time an entity is messaged. The `ShardingConsumerController` will spawn your entity actor directly and will additionally spawn a `ConsumerController` for each unique `ProducerId` detected in the message stream. Each of the `ConsumerController`s spawned by the `ShardingConsumerController` will deliver messages via their usual [`ConsumerController.Delivery<T>`](xref:Akka.Delivery.ConsumerController.Delivery`1) to the `Consumer`.
* **`Consumer`** - this is your entity actor hosted via Akka.Cluster.Sharding. The `Consumer` processes messages of type `T` and must send `ConsumerController.Confirmation` messages back to the `ConsumerController` once it has successfully processed each `ConsumerController.Delivery<T>`. The `Consumer` actor is spawned by the `ShardingConsumerController`.

#### Integration with ShardRegions

In order to make use of Akka.Cluster.Sharding.Delivery, we have to change the way we spawn our `ShardRegion`'s entity actors:

[!code-csharp[Launching ShardRegion with ShardingConsumerController configured](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/Program.cs?name=LaunchShardRegion)]

1. The `ShardingConsumerController` needs to be the actor initially created by the `ShardRegion` each time an entity is spawned;
2. The `ShardingConsumerController.Create` method takes an argument of type `Func<IActorRef, Props>` - this allows you to pass in the `IActorRef` of the `ShardingConsumerController` itself down to your entity actor, the `Props` of which should be returned by this function.
3. The `Consumer` actor must send a `ConsumerController.Start<T>` message, typically during `PreStart`, to the `ShardingConsumerController` in order to trigger message delivery.

[!code-csharp[Consumer signalling to ShardingConsumerController that it's ready to receive messages](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/Customers.cs?name=ShardingConsumerRegistration)]

Do this for each entity type / `ShardRegion` you wish to guarantee delivery for via the `ShardingProducerController`.

Next, we have to create our `Producer` and `ShardingProducerController` instances:

[!code-csharp[Launching ShardRegion with ShardingConsumerController configured](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/Program.cs?name=StartSendingMessages)]

1. We have to launch our `ShardingProducerController` and our `Producer` actors - each `ShardingProducerController` must have a *globally unique* `ProducerId` value (similar to a `PersistentId`).
2. `ShardingProducerController`s can be optionally made persistent via the same [`EventSourcedProducerQueue`](xref:Akka.Persistence.Delivery.EventSourcedProducerQueue) that can be used by an individual `ProducerController`.
3. The `ShardingProducerController` must have a reference to the `IActorRef` of the `ShardRegion` to which it will be delivering messages.
4. The `ShardingProducerController` must receive a [`ShardingProducerController.Start<T>`](xref:Akka.Cluster.Sharding.Delivery.ShardingProducerController.Start`1) message that contains the `Producer`'s `IActorRef` in order to begin message production.

> [!TIP]
> Unlike Akka.Delivery, there is no need to have the `ProducerController` and `ConsumerController` explicitly register with the other - this will be handled automatically by the Akka.Cluster.Sharding messaging system.

### Message Production

Once the `Producer` has been successfully registered with its `ProducerController`, it will begin to receive `ShardingProducerController.RequestNext<T>` messages - each time it receives one of these messages the `Producer` can send a burst of messages to the `ShardingProducerController`.

[!code-csharp[Launching ShardRegion with ShardingConsumerController configured](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/Producer.cs?name=MessageProduction)]

> [!IMPORTANT]
> It is crucial that the `Prodcuer` send its messages of type `T` wrapped inside a [`ShardingEnvelope`](xref:Akka.Cluster.Sharding.ShardingEnvelope) - otherwise the `ShardingProducerController` won't know which messages should be routed to which unique `entityId`. Additionally - your `HashCodeMessageExtractor` that you use with your `ShardRegion` must also be able to handle the `ShardingEnvelope` to ensure that this message is processed correctly on the receiving side. There is a proposal in-place to automate some of this work in a future release of Akka.NET: [#6717](https://github.com/akkadotnet/akka.net/issues/6717).

One important distinction between [`ShardingProducerController.RequestNext<T>`](xref:Akka.Cluster.Sharding.Delivery.ShardingProducerController.RequestNext`1) and [`ProducerController.RequestNext<T>`](xref:Akka.Delivery.ProducerController.RequestNext`1) - because the `ShardingProducerController` has to deliver to multiple `Consumer`s, it retains a much larger outbound delivery buffer. You can check the status of which entities are currently buffered or which ones have active demand by inspecting the `ShardingProducerController.RequestNext<T>.BufferedForEntitiesWithoutDemand` or `ShardingProducerController.RequestNext<T>.EntitiesWithDemand` properties respectively.

![Akka.Cluster.Sharding.Delivery message production cycle.](/images/cluster/delivery/2-sharding-message-production.png)

Once the `ShardingProducerController` begins receiving messages of type `T` (wrapped in a `ShardingEnvelope`) from the `Producer`, it will spawn `ProducerController`s for each unique entity and begin routing those messages to the `ShardRegion`.

### Message Consumption

The `ProducerController`s will all send `ConsumerController.SequencedMessage<T>` over the wire, wrapped inside `ShardingEnvelope`s - the `ShardRegion` must be programmed to handle these types:

![Akka.Cluster.Sharding.Delivery message consumption process.](/images/cluster/delivery/3-sharding-message-consumption.png)

[!code-csharp[ShardRegion message exctractor](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/MessageExtractor.cs?name=ExtractorClass)]

As the `ConsumerController.SequencedMessage<T>`s are delivered, the `ShardRegion` will spawn the `ShardingConsumerController` for each entity, which will in turn spawn the entity actor itself (the `Consumer`) as well as one `ConsumerController` per unique `ProducerId`. These actors are all cheap and maintain a finite amount of buffer space.

1. The `Consumer` receives the `ConsumerController.Delivery<T>` message from the `ShardingConsumerController`;
2. The `Consumer` replies to the `IActorRef` stored inside `ConsumerController.Delivery<T>.DeliverTo` with a `ConsumerController.Confirmed` message - this marks the message as "processed;"
3. The `ConsumerController` marks the message as delivered, removes it from the buffer, and requests additional messages from the `ProducerController`; and
4. This in turn causes the `ShardingProducerController` to update its aggregate state and send additional `ShardingProducerController.RequestNext<T>` demand to the `Producer`.

### Guarantees, Constraints, and Caveats

See ["Reliable Message Delivery with Akka.Delivery - Guarantees, Constraints, and Caveats"](xref:reliable-delivery#guarantees-constraints-and-caveats) - these are the same in Akka.Cluster.Sharding.Delivery.

With one notable exception: **Akka.Cluster.Sharding.Delivery does not support message chunking** and there are no plans to add it at this time.

## Durable Reliable Delivery over Akka.Cluster.Sharding

By default the `ShardingProducerController` will run using without any persistent storage - however, if you reference the [Akka.Persistence library](xref:persistence-architecture) in your Akka.NET application then you can make use of the [`EventSourcedProducerQueue`](xref:Akka.Persistence.Delivery.EventSourcedProducerQueue) to ensure that your `ShardingProducerController` saves and un-acknowledged messages to your Akka.Persistence Journal and SnapshotStore.

[!code-csharp[Durable ShardingProducerController configuration](../../../src/contrib/Akka.Cluster.Sharding.Tests/Delivery/DurableShardingSpec.cs?name=SpawnDurableProducer)]

> [!TIP]
> The `EventSourcedProducerQueue` can be customized via the [`EventSourcedProducerQueue.Settings` class](xref:Akka.Persistence.Delivery.EventSourcedProducerQueue.Settings) - for instance, you can customize it to use a separate Akka.Persistence Journal and SnapshotStore.

Each time a message is sent to the `ShardingProducerController` it will persist a copy of the message to the Akka.Persistence journal.

![ShardingProducerController persistence with EventSourcedProducerQueue.](/images/cluster/delivery/4-sharding-message-persistence.png)

> [!TIP]
> All messages for all entities are stored in the same `EventSourcedProducerQueue` instance.

### Confirmation of Outbound Messages Persisted

If the `Producer` needs to confirm that all of its outbound messages have been successfully persisted, this can be accomplished via the `ShardingProducerController.RequestNext<T>.AskNextTo` method:

[!code-csharp[Starting ProducerController with EventSourcedProducerQueue enabled](../../../src/core/Akka.Docs.Tests/Delivery/DeliveryDocSpecs.cs?name=ConfirmableMessages)]

The `AskNextTo` method will return a `Task<long>` that will be completed once the message has been confirmed as stored inside the `EventSourcedProducerQueue` - the `long` in this case is the sequence number that has been assigned to this message via the `ShardingProducerController`'s outbound queue.

In addition to outbound deliveries, confirmation messages from the `ShardingConsumerController` will also be persisted - and these will cause the `EventSourcedProducerQueue` to gradually compress its footprint in the Akka.Persistence journal by taking snapshots.

> [!TIP]
> By default the `EventSourcedProducerQueue` will take a new snapshot every 1000 events but this can be configured via the [`EventSourcedProducerQueue.Settings` class](xref:Akka.Persistence.Delivery.EventSourcedProducerQueue.Settings).
