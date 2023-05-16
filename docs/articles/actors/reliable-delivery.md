---
uid: reliable-delivery
title: Reliable Akka.NET Message Delivery with Akka.Delivery
---

# Reliable Message Delivery with Akka.Delivery

By [default Akka.NET uses "at most once" message delivery between actors](xref:message-delivery-reliability) - this is sufficient for most purposes within Akka.NET, but there are instances where users may require strong delivery guarantees.

This is where Akka.Delivery can be helpful - it provides a robust set of tools for ensuring message delivery over the network, across actor restarts, and even across process restarts.

Akka.Delivery will ultimately support two different modes of reliable delivery:

* **Point-to-point delivery** - this works similarly to [Akka.Persistence's `AtLeastOnceDeliveryActor`](xref:at-least-once-delivery); messages are pushed by the producer to the consumer in real-time.
* **Pull-based delivery** - this is not yet implemented in Akka.Delivery.

## Point to Point Delivery

Point-to-point delivery is a great option for users who desire reliable, ordered delivery of messages over the network or across process restarts. Additionally, point to point delivery mode also supports "message chunking" - the ability to break up large messages into smaller, sequenced chunks that can be delivered over [Akka.Remote connections without head-of-line blocking](https://petabridge.com/blog/large-messages-and-sockets-in-akkadotnet/).

### Built-in Actors and Messages

![Overview of built-in Akka.Delivery actors](/images/actor/delivery/1-delivery-actors-overview.png)

An Akka.Delivery relationship consists of 4 actors typically:

* **`Producer`** - this is a user-defined actor that is responsible for the production of messages. It receives [`ProducerController.RequestNext<T>`](xref:Akka.Delivery.ProducerController.RequestNext`1) messages from the `ProducerController` when capacity is available to deliver additional messages.
* **`ProducerController`** - this actor is built into Akka.Delivery and does most of the work. The `ProducerController` sequences all incoming messages from the `Producer`, delivers them to the `ConsumerController`, waits for acknowledgements that messages have been processed, and subsequently requests more messages from the `Producer`.
* **`ConsumerController`** - this actor is also built into Akka.Delivery and typically resides on the opposite site of the network from the `ProducerController`. The `ConsumerController` is responsible for buffering unprocessed messages, delivering messages for processing via [`ConsumerController.Delivery<T>`](xref:Akka.Delivery.ConsumerController.Delivery`1) to the `Consumer`, receiving confirmation that the `Consumer` has successfully processed the most recent message, and then subsequently requesting additional messages from the `ProducerController`.
* **`Consumer`** - this is a user-defined actor that is ultimately responsible for consuming messages of type `T` and sending `ConsumerController.Confirmation` messages back to the `ConsumerController` once it has successfully processed each `ConsumerController.Delivery<T>`.

#### Typed Messaging Protocol

Akka.Delivery uses a .NET generic-typed protocol and the `ProducerController` and `ConsumerController` are also both strongly typed. This means that end-users need to organize their messages into "protocol groups" in order to be effective, like so:

[!code-csharp[Message Protocol](../../../src/core/Akka.Docs.Tests/Delivery/DeliveryDocSpecs.cs?name=MessageProtocol)]

The common interface that all of the messages in this protocol implement is typically the type you'll want to use for your generic argument `T` in the Akka.Delivery or [Akka.Cluster.Sharding.Delivery](xref:cluster-sharding-delivery) method calls and types, as shown below:

[!code-csharp[Starting Typed Actors](../../../src/core/Akka.Docs.Tests/Delivery/DeliveryDocSpecs.cs?name=ProducerRegistration)]

### Registration Flow

In order for the `ProducerController` and the `ConsumerController` to begin exchanging messages with each other, the respective actors must register with each other:

![Producer registers with Akka.Delivery.ProducerController, Consumer registers with Akka.Delivery.ConsumerController](/images/actor/delivery/2-delivery-registration.png)

1. The `Producer` must send a [`ProducerController.Start<T>`](xref:Akka.Delivery.ProducerController.Start`1) message to the `ProducerController` in order to begin receiving `ProducerController.RequestNext<T>` messages;
2. The `Consumer` must send a [`ConsumerController.Start<T>`](xref:Akka.Delivery.ConsumerController.Start`1) message to the `ConsumerController` in order to receive `ConsumerController.Delivery<T>` messages; and
3. The `ConsumerController` needs to register with the `ProducerController` - this can be accomplished either by sending a reference to the `ProducerController` to the `ConsumerController` via a [`ConsumerController.RegisterToProducerController<T>`](xref:Akka.Delivery.ConsumerController.RegisterToProducerController`1) message or by sending the `ProducerController` a [`ProducerController.RegisterConsumer<T>`](xref:Akka.Delivery.ProducerController.RegisterConsumer`1) message. Either one will work.

The `ProducerController` registration flow is pretty simple:

[!code-csharp[Starting Typed Actors](../../../src/core/Akka.Docs.Tests/Delivery/DeliveryDocSpecs.cs?name=ProducerRegistration)]

As is the `ConsumerController` registration flow:

[!code-csharp[Starting Typed Actors](../../../src/core/Akka.Docs.Tests/Delivery/DeliveryDocSpecs.cs?name=ConsumerRegistration)]


### Message Production

Once the registration flow is completed, all that's needed is for the `Producer` to begin delivering messages to the `ProducerController` after it receives an initial [`ProducerController.RequestNext<T>` message](xref:Akka.Delivery.ProducerController.RequestNext`1).

![Akka.Delivery message production flow](/images/actor/delivery/3-delivery-message-production.png)

1. The `ProducerController` sends a `ProducerController.RequestNext<T>` message to the `Producer` when delivery capacity is available;
2. The `Producer` messages the `IActorRef` stored at `ProducerController.RequestNext<T>.SendNextTo` with a message of type `T`; 
3. The `ProducerController` will sequence (and optionally chunk) the incoming messages and deliver them to the `ConsumerController` on the other side of the network;
4. The `ConsumerController` will transmit the received messages in the order in which they were received to the `Consumer` via a `ConsumerController.Delivery<T>` message.


The `Producer` actor is ultimately responsible for managing back-pressure inside the Akka.Delivery system - a message of type `T` cannot be sent to the `ProducerController` until the `Producer` receives a message of type `ProducerController.RequestNext<T>`:

[!code-csharp[Akka.Delivery Message Production](../../../src/core/Akka.Docs.Tests/Delivery/DeliveryDocSpecs.cs?name=ProducerDelivery)]

> [!IMPORTANT]
> If a `Producer` tries to send a message of type `T` to the `ProducerController` before receiving a `ProducerController.RequestNext<T>` message, the `ProducerController` will log an error and crash. The appropriate way to handle this backpressure is typically through a combination of [actor behavior switching](xref:receive-actor-api#becomeunbecome) and [stashing](xref:receive-actor-api#stash).

The `ProducerController` maintains a buffer of unacknowledged messages - in the event that the `ConsumerController` restarts the `ProducerController` will re-deliver all unacknowledged messages to it and restart the delivery process.

> [!NOTE]
> `ProducerController.RequestNext<T>` messages aren't sent 1:1 with messages successfully consumed by the `Consumer` - the Akka.Delivery system allows for some buffering inside the `ProducerController` and `ConsumerController`, but the system constrains it to 10s of messages in order to conserve memory across a large number of concurrent Akka.Delivery instances. In practice this means that the flow control of Akka.Delivery will always allow the `Producer` to run slightly ahead of a slower `Consumer`.

### Message Consumption

It's the job of the `Consumer`, a user-defined actor, to mark the messages it's received as processed so the reliable delivery system can move forward and deliver the next message in the sequence:

![Akka.Delivery message consumption flow](/images/actor/delivery/4-delivery-message-consumption.png)

1. The `Consumer` receives the `ConsumerController.Delivery<T>` message from the `ConsumerController`;
2. The `Consumer` replies to the `IActorRef` stored inside `ConsumerController.Delivery<T>.DeliverTo` with a `ConsumerController.Confirmed` message - this marks the message as "processed;" and
3. The `ConsumerController` marks the message as delivered, removes it from the buffer, and requests additional messages from the `ProducerController`.

[!code-csharp[Akka.Delivery Message Consumption](../../../src/core/Akka.Docs.Tests/Delivery/DeliveryDocSpecs.cs?name=ConsumerDelivery)]

The `ConsumerController` also maintains a buffer of unacknowledged messages received from the `ProducerController` - those messages will be delivered to the `Consumer` each time a `ConsumerController.Confirmed` is received by the `ConsumerController`. 

There's no time limit on how long the `Consumer` has to process each message - any additional messages sent or resent by the `ProducerController` to the `ConsumerController` will be buffered until the `Consumer` acknowledges the current message.

> [!IMPORTANT]
> In the event that the `Consumer` actor terminates, the `ConsumerController` actor delivering messages to it will also self-terminate. The `ProducerController`, however, will continue running while it waits for a new `ConsumerController` instance to register.

### Guarantees, Constraints, and Caveats

It's the goal of Akka.Delivery to ensure that all messages are successfully processed by the `Consumer` in the order that the `Producer` originally sent. 

1. The [`ConsumerController.Settings.ResendIntervalMin`](xref:Akka.Delivery.ConsumerController.Settings) and `ConsumerController.Settings.ResendIntervalMax` will determine how often the `ConsumerController` re-requests messages from the `ProducerController`.
2. The `ProducerController` buffers all messages (and can optionally persist them) until they are marked as consumed; and
3. The `ProducerController` and the `ConsumerController` can both restart independently from each other - if the `ProducerController` has a durable (persistent) queue configured there will be no message loss in this event.
4. The only scenario in which duplicates will be delivered to the `Consumer`: `ProducerController` with durable queue restarts before it can process confirmations from the `ConsumerController` - in that scenario (an edge case of an edge case) it's possible that previously confirmed messages will be re-delivered. However, they will be the first messages immediately received by the consumer upon the `ProducerController` restarting.
5. The `Producer` can restart independently from the `ProducerController`.

> [!NOTE]
> The `Producer` and `ProducerController` must reside inside the same `ActorSystem` (the `ProducerController`) asserts this. Likewise, the `Consumer` and the `ConsumerController` must reside inside the same `ActorSystem` (the `ConsumerController` asserts this.) 

### Chunking Large Messages

One very useful feature of point-to-point message delivery is message chunking: the ability to break up large messages into an ordered sequence of fixed-size byte arrays. Akka.Delivery guarantees that chunks will be transmitted and received in-order over the wire - and when using the `DurableQueue` with the `ProducerController` chunked messages will be re-chunked and re-transmitted upon restart.

**Chunking of messages is disabled by default**.

> [!TIP]
> This feature is _very_ useful for [eliminating head-of-line-blocking in Akka.Remote](https://petabridge.com/blog/large-messages-and-sockets-in-akkadotnet/).

To enable message chunking you can set the following HOCON value in your `ActorSystem` configuration:

```hocon
akka.reliable-delvery.producer-controller.chunk-large-messages = 100b #100b chunks, off by default
```

Or by setting the [`ProducerController.Settiongs.ChunkLargeMessagesBytes`](xref:Akka.Delivery.ProducerController.Settings) property to a value greater than 0.

[!code-csharp[Akka.Delivery ProducerController message chunking](../../../src/core/Akka.Docs.Tests/Delivery/DeliveryDocSpecs.cs?name=ChunkedProducerRegistration)]

![ProducerController chunking messages into byte arrays](/images/actor/delivery/chunking-step-1.png)

When chunking is enabled, the `ProducerController` will use [the Akka.NET serialization system](xref:serialization) to convert each message sent by the `Producer` into a `byte[]` which will be chunked into [1,N] `ProducerController.Settiongs.ChunkLargeMessagesBytes`-sized arrays.

![ConsumerController receiving initial message chunks from ProducerController](/images/actor/delivery/chunking-step-2.png)

Each of these chunks will be transmitted individually, in-order and subsequently buffered by the `ConsumerController`.

![ConsumerController receiving final chunk from ProducerController](/images/actor/delivery/chunking-step-3.png)

Once the last chunk has been received by the `ConsumerController` the original message will be deserialized using Akka.NET serialization again. Provided that serialization was successful, the message will be delivered just like a normal `ConsumerController.Delivery<T>` to the `Consumer`.

![Consumer marking chunked message as Confirmed, ProducerController marks all chunks as delivered.](/images/actor/delivery/chunking-step-4.png)

Once the `Consumer` has recieved the `ConsumerController.Delivery<T>` and sent a `ConsumerController.Confirmed` to the `ConsumerController`, all chunks will be marked as received and freed from memory on both ends of the network.

> [!TIP]
> Your `akka.reliable-delvery.producer-controller.chunk-large-messages` size should always be smaller than your [`akka.remote.dot-netty.tcpmaximum-frame-size`](xref:).

## Durable Reliable Delivery

By default the `ProducerController` will run using without any persistent storage - however, if you reference the [Akka.Persistence library](xref:persistence-architecture) in your Akka.NET application then you can make use of the [`EventSourcedProducerQueue`](xref:Akka.Persistence.Delivery.EventSourcedProducerQueue) to ensure that your `ProducerController` saves and un-acknowledged messages to your Akka.Persistence Journal and SnapshotStore.

[!code-csharp[Starting ProducerController with EventSourcedProducerQueue enabled](../../../src/core/Akka.Docs.Tests/Delivery/DeliveryDocSpecs.cs?name=DurableQueueProducer)]

> [!TIP]
> The `EventSourcedProducerQueue` can be customized via the [`EventSourcedProducerQueue.Settings` class](xref:Akka.Persistence.Delivery.EventSourcedProducerQueue.Settings) - for instance, you can customize it to use a separate Akka.Persistence Journal and SnapshotStore.

