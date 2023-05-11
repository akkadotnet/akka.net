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

### Registration Flow

In order for the `ProducerController` and the `ConsumerController` to begin exchanging messages with each other, the respective actors must register with each other:

![Producer registers with Akka.Delivery.ProducerController, Consumer registers with Akka.Delivery.ConsumerController](/images/actor/delivery/2-delivery-registration.png)