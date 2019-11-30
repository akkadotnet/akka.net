---
uid: streams-dynamic-handling
title: Dynamic stream handling
---

# Dynamic stream handling

## Controlling graph completion with KillSwitch

A `KillSwitch` allows the completion of graphs of `FlowShape` from the outside. It consists of a flow element that
can be linked to a graph of `FlowShape` needing completion control. The `IKillSwitch` interface allows to:

* complete the graph(s) via `Shutdown()`
* fail the graph(s) via `Abort(Exception cause)`

```C#
public interface IKillSwitch
{
    /// <summary>
    /// After calling <see cref="Shutdown"/> the linked <see cref="IGraph{TShape}"/>s of <see cref="FlowShape{TIn,TOut}"/> are completed normally.
    /// </summary>
    void Shutdown();

    /// <summary>
    /// After calling <see cref="Abort"/> the linked <see cref="IGraph{TShape}"/>s of <see cref="FlowShape{TIn,TOut}"/> are failed.
    /// </summary>
    void Abort(Exception cause);
}
```

After the first call to either `Shutdown` and `Abort`, all subsequent calls to any of these methods will
be ignored. Graph completion is performed by both

* completing its downstream
* cancelling (in case of `Shutdown`) or failing (in case of `Abort`) its upstream.

A `IKillSwitch` can control the completion of one or multiple streams, and therefore comes in two different flavours.

### UniqueKillSwitch

``UniqueKillSwitch`` allows to control the completion of **one** materialized ``Graph`` of ``FlowShape``. Refer to the
below for usage examples.

* **Shutdown**

[!code-csharp[KillSwitchDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/KillSwitchDocTests.cs?name=unique-shutdown)]

* **Abort**

[!code-csharp[KillSwitchDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/KillSwitchDocTests.cs?name=unique-abort)]

### SharedKillSwitch

A `SharedKillSwitch` allows to control the completion of an arbitrary number graphs of `FlowShape`. It can
be materialized multiple times via its `Flow` method, and all materialized graphs linked to it are controlled
by the switch. Refer to the below for usage examples.

* **Shutdown**

[!code-csharp[KillSwitchDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/KillSwitchDocTests.cs?name=shared-shutdown)]

* **Abort**

[!code-csharp[KillSwitchDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/KillSwitchDocTests.cs?name=shared-abort)]

> [!NOTE]
> A `UniqueKillSwitch` is always a result of a materialization, whilst `SharedKillSwitch` needs to be constructed before any materialization takes place.

### Using `CancellationToken`s as kill switches

Plain old .NET cancellation tokens can also be used as kill switch stages via extension method: `cancellationToken.AsFlow(cancelGracefully: true)`. Their behavior is very similar to what a `SharedKillSwitch` has to offer with one exception - while normal kill switch recognizes difference between closing a stream gracefully (via. `Shutdown()`) and abruptly (via. `Abort(exception)`), .NET cancellation tokens have no such distinction. 

Therefore you need to explicitly specify at the moment of defining a flow stage, if cancellation token call should cause stream to close with completion or failure, by using `cancelGracefully` parameter. If it's set to `false`, calling cancel on a token's source will cause stream to fail with an `OperationCanceledException`.

## Dynamic fan-in and fan-out with MergeHub and BroadcastHub

There are many cases when consumers or producers of a certain service (represented as a Sink, Source, or possibly Flow) are dynamic and not known in advance. The Graph DSL does not allow to represent this, all connections of the graph must be known in advance and must be connected upfront. To allow dynamic fan-in and fan-out streaming, the Hubs should be used. They provide means to construct Sink and Source pairs that are “attached” to each other, but one of them can be materialized multiple times to implement dynamic fan-in or fan-out.

### Using the MergeHub
A `MergeHub` allows to implement a dynamic fan-in junction point in a graph where elements coming from different producers are emitted in a First-Comes-First-Served fashion. If the consumer cannot keep up then all of the producers are backpressured. The hub itself comes as a Source to which the single consumer can be attached. It is not possible to attach any producers until this `Source` has been materialized (started). This is ensured by the fact that we only get the corresponding `Sink` as a materialized value. Usage might look like this:

[!code-csharp[HubsDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/HubsDocTests.cs?name=merge-hub)]

This sequence, while might look odd at first, ensures proper startup order. Once we get the `Sink`, we can use it as many times as wanted. Everything that is fed to it will be delivered to the consumer we attached previously until it cancels.

### Using the BroadcastHub

A `BroadcastHub` can be used to consume elements from a common producer by a dynamic set of consumers. The rate of the producer will be automatically adapted to the slowest consumer. In this case, the hub is a `Sink` to which the single producer must be attached first. Consumers can only be attached once the Sink has been materialized (i.e. the producer has been started). One example of using the `BroadcastHub`:

[!code-csharp[HubsDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/HubsDocTests.cs?name=broadcast-hub)]

The resulting `Source` can be materialized any number of times, each materialization effectively attaching a new subscriber. If there are no subscribers attached to this hub then it will not drop any elements but instead backpressure the upstream producer until subscribers arrive. This behavior can be tweaked by using the combinators `Buffer` for example with a drop strategy, or just attaching a subscriber that drops all messages. If there are no other subscribers, this will ensure that the producer is kept drained (dropping all elements) and once a new subscriber arrives it will adaptively slow down, ensuring no more messages are dropped.

### Combining dynamic stages to build a simple Publish-Subscribe service

The features provided by the Hub implementations are limited by default. This is by design, as various combinations can be used to express additional features like unsubscribing producers or consumers externally. We show here an example that builds a `Flow` representing a publish-subscribe channel. The input of the `Flow` is published to all subscribers while the output streams all the elements published.

First, we connect a `MergeHub` and a `BroadcastHub` together to form a publish-subscribe channel. Once we materialize this small stream, we get back a pair of `Source` and `Sink` that together define the publish and subscribe sides of our channel.

[!code-csharp[HubsDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/HubsDocTests.cs?name=pub-sub-1)]

We now use a few tricks to add more features. First of all, we attach a `Sink.Ignore<string>()` at the broadcast side of the channel to keep it drained when there are no subscribers. If this behavior is not the desired one this line can be simply dropped.

[!code-csharp[HubsDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/HubsDocTests.cs?name=pub-sub-2)]

We now wrap the `Sink` and `Source` in a Flow using `Flow.FromSinkAndSource`. This bundles up the two sides of the channel into one and forces users of it to always define a publisher and subscriber side (even if the subscriber side is just dropping). It also allows us to very simply attach a `KillSwitch` as a `BidiStage` which in turn makes it possible to close both the original Sink and Source at the same time. Finally, we add `backpressureTimeout` on the consumer side to ensure that subscribers that block the channel for more than 3 seconds are forcefully removed (and their stream failed).

[!code-csharp[HubsDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/HubsDocTests.cs?name=pub-sub-3)]

The resulting `Flow` now has a type of `Flow<string, string, UniqueKillSwitch>` representing a publish-subscribe channel which can be used any number of times to attach new producers or consumers. In addition, it materializes to a `UniqueKillSwitch` (see [UniqueKillSwitch](xref:streams-dynamic-handling#uniquekillswitch)) that can be used to deregister a single user externally:

[!code-csharp[HubsDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/HubsDocTests.cs?name=pub-sub-4)]


### Using the PartitionHub

**This is a [may change](xref:may-change) feature**

A `PartitionHub` can be used to route elements from a common producer to a dynamic set of consumers.
The selection of consumer is done with a function. Each element can be routed to only one consumer. 

The rate of the producer will be automatically adapted to the slowest consumer. In this case, the hub is a `Sink`
to which the single producer must be attached first. Consumers can only be attached once the `Sink` has
been materialized (i.e. the producer has been started). One example of using the `PartitionHub`:

[!code-csharp[HubsDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/HubsDocTests.cs?name=partition-hub)]

The `partitioner` function takes two parameters; the first is the number of active consumers and the second
is the stream element. The function should return the index of the selected consumer for the given element, 
i.e. `int` greater than or equal to 0 and less than number of consumers.

The resulting `Source` can be materialized any number of times, each materialization effectively attaching
a new consumer. If there are no consumers attached to this hub then it will not drop any elements but instead
backpressure the upstream producer until consumers arrive. This behavior can be tweaked by using the combinators
`.Buffer` for example with a drop strategy, or just attaching a consumer that drops all messages. If there
are no other consumers, this will ensure that the producer is kept drained (dropping all elements) and once a new
consumer arrives and messages are routed to the new consumer it will adaptively slow down, ensuring no more messages
are dropped.

It is possible to define how many initial consumers that are required before it starts emitting any messages
to the attached consumers. While not enough consumers have been attached messages are buffered and when the
buffer is full the upstream producer is backpressured. No messages are dropped.

The above example illustrate a stateless partition function. For more advanced stateful routing the `StatefulSink` can be used. Here is an example of a stateful round-robin function:

[!code-csharp[HubsDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/HubsDocTests.cs?name=partition-hub-stateful)]

Note that it is a factory of a function to to be able to hold stateful variables that are 
unique for each materialization.


The function takes two parameters; the first is information about active consumers, including an array of 
consumer identifiers and the second is the stream element. The function should return the selected consumer
identifier for the given element. The function will never be called when there are no active consumers, i.e. 
there is always at least one element in the array of identifiers.

Another interesting type of routing is to prefer routing to the fastest consumers. The `IConsumerInfo`
has an accessor `QueueSize` that is approximate number of buffered elements for a consumer.
Larger value than other consumers could be an indication of that the consumer is slow.
Note that this is a moving target since the elements are consumed concurrently. Here is an example of
a hub that routes to the consumer with least buffered elements:

[!code-csharp[HubsDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/HubsDocTests.cs?name=partition-hub-fastest)]