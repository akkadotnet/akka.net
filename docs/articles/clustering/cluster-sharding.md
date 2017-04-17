---
uid: cluster-sharding
title: Akka.Cluster.Sharding module
---
# Akka.Cluster.Sharding module

Cluster sharding is useful in cases when you want to contact with cluster actors using their logical id's, but don't want to care about their physical location inside the cluster or manage their creation. Moreover it's able to rebalance them, as nodes join/leave the cluster. It's often used to represent i.e. Aggregate Roots in Domain Driven Design terminology.

> Cluster sharding depends on Akka.Persistence module. In order to use it, you'll need to specify an event journal accessible by all of the participating nodes.

Actors managed by cluster sharding are called **entities** and can be automatically distributed across multiple nodes inside the cluster. One entity instance may live only at one node at the time, and can be communicated with via `ShardRegion` without need to know, what it's exact node location is.

Example:

```csharp
// define envelope used to message routing
public sealed class Envelope
{
    public readonly int ShardId;
    public readonly int EntityId;
    public readonly object Message;

    ...
}

// define, how shard id, entity id and message itself should be resolved
public sealed class MessageExtractor : IMessageExtractor
{
    public string EntityId(object message)
    {
        return (message as Envelope)?.EntityId.ToString();
    }

    public string ShardId(object message)
    {
        return (message as Envelope)?.ShardId.ToString();
    }

    public object EntityMessage(object message)
    {
        return (message as Envelope)?.Message;
    }
}

// register actor type as a sharded entity
var region = ClusterSharding.Get(system).Start(
    typeName: "my-actor",
    entityProps: Props.Create<MyActor>(),
    settings: ClusterShardingSettings.Create(system),
    messageExtractor: new MessageExtractor());

// send message to entity through shard region
region.Tell(new Envelope(shardId: 1, entityId: 1, message: "hello"))
```

In this example, we first specify way to resolve our messages recipients in context of sharded entities. For this, specialized message type called `Envelope` and resolution strategy called `MessageExtractor` have been specified. That part can be customized, and shared among many different shard regions, but it needs to be uniform among all nodes.

Second part of an example is registering custom actor type as sharded entity using `ClusterSharding.Start` or `ClusterSharding.StartAsync` methods. Result is the `IActorRef` to shard region used to communicate between current actor system and target entities. Shard region must be specified once per each type on each node, that is expected to participate in sharding entities of that type.

In case when you want to send message to entities from specific node, but you don't want that node to participate in sharding itself, you can use `ShardRegionProxy` for that.

Example:

```csharp
var proxy = ClusterSharding.Get(system).StartProxy(
    typeName: "my-actor",
    role: null,
    messageExtractor: new MessageExtractor());
```

## Shards

Entities are located and managed automatically. They can also be recreated on the other nodes, as new nodes join the cluster or old ones are leaving it. This process is called rebalancing and for performance reasons it never works on a single entity. Instead all entities are organized and managed in so called shards.

As you may have seen in the examples above shard resolution algorithm is one of the choices you have to make. Good uniform distribution is not an easy task - too small number shards may result in not even distribution of entities across all nodes, while too many of them may increase message routing latency and rebalancing overhead. As a rule of thumb, you may decide to have a number of shards ten times greater than expected maximum number of cluster nodes.

By default rebalancing process always happens from nodes with the highest number of shards, to the ones with the smallest one. This can be configured into by specifying custom implementation of the `IShardAllocationStrategy` interface in `ClusterSharding.Start` parameters.

## Passivation

To reduce memory consumption, you may decide to stop entities after some period of inactivity using `Context.SetReceiveTimeout(timeout)`. In order to make cluster sharding aware of stopping entities, **DON'T use `Context.Stop(Self)` on the entities**, as this may result in losing messages. Instead you may send `Passivate` message message to current entity `Context.Parent` (which is shard itself in this case). This will trigger shard to stop forwarding messages to target entity, and buffer them instead until it's terminated. Once that happens, if there are still some messages buffered, entity will be reincarnated and messages flushed to it automatically.

## Retrieving sharding state

You can inspect current sharding stats by using following messages:

- On `GetShardRegionState` shard region will reply with `ShardRegionState` containing data about shards living in the current actor system and what entities are alive on each one of them.
- On `GetClusterShardingStats` shard region will reply with `ClusterShardingStats` having information about shards living in the whole cluster and how many entities alive in each one of them.

## Integrating cluster sharding with persistent actors

One of the most common scenarios, where cluster sharding is used, is to combine them with eventsourced persistent actors from [Akka.Persistence](xref:persistence-architecture) module. However as the entities are incarnated automatically based on provided props, specifying a dedicated, static unique `PersistenceId` for each entity may seem troublesome.

This can be resolved by getting information about shard/entity ids directly from actor's path and constructing unique id from it. For each entity actor path will follow */user/{typeName}/{shardId}/{entityId}* pattern, where *{typeName}* was the parameter provided to `ClusterSharding.Start` method, while *{shardId}* and *{entityId}* where strings returned by message extractor logic. Given these values we can build consistent, unique `PersistenceId`s on the fly like on the following example:

```csharp
public class Aggregate : PersistentActor
{
    public override string PersistenceId { get; }

    public Aggregate()
    {
        PersistenceId = Context.Parent.Path.Name + "-" + Self.Path.Name;
    }

    ...
}
```
