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

[!code-csharp[Customers](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/Customers.cs?name=MessageProtocol)]

The common interface that all of the messages in this protocol implement is typically the type you'll want to use for your generic argument `T` in the Akka.Delivery or [Akka.Cluster.Sharding.Delivery](xref:cluster-sharding-delivery) method calls and types, as shown below:

[!code-csharp[Customers](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/Program.cs?name=StartSendingMessage)]

### Registration Flow

In order for the `ProducerController` and the `ConsumerController` to begin exchanging messages with each other, the respective actors must register with each other:

![Producer registers with Akka.Delivery.ProducerController, Consumer registers with Akka.Delivery.ConsumerController](/images/actor/delivery/2-delivery-registration.png)

1. The `Producer` must send a [`ProducerController.Start<T>`](xref:Akka.Delivery.ProducerController.Start`1) message to the `ProducerController` in order to begin receiving `ProducerController.RequestNext<T>` messages;
2. The `Consumer` must send a [`ConsumerController.Start<T>`](xref:Akka.Delivery.ConsumerController.Start`1) message to the `ConsumerController` in order to receive `ConsumerController.Delivery<T>` messages; and
3. The `ConsumerController` needs to register with the `ProducerController` - this can be accomplished either by sending a reference to the `ProducerController` to the `ConsumerController` via a [`ConsumerController.RegisterToProducerController<T>`](xref:Akka.Delivery.ConsumerController.RegisterToProducerController`1) message or by sending the `ProducerController` a [`ProducerController.RegisterConsumer<T>`](xref:Akka.Delivery.ProducerController.RegisterConsumer`1) message. Either one will work.

### Messaging Flow

Once the registration flow is completed, all that's needed is for the `Producer` to begin delivering messages to the `ProducerController` after it receives an initial [`ProducerController.RequestNext<T>` message](xref:Akka.Delivery.ProducerController.RequestNext`1).

![Akka.Delivery message production flow](/images/actor/delivery/3-delivery-message-production.png)

The `ProducerController` and `Producer` are both responsible for managing backpressure inside the 