---
uid: cluster-sharding
title: Akka.Cluster.Sharding module
---
# Akka.Cluster.Sharding module

Cluster sharding is useful in cases when you want to contact with cluster actors using their logical id's, but don't want to care about their physical location inside the cluster or manage their creation. Moreover it's able to rebalance them, as nodes join/leave the cluster. It's often used to represent i.e. Aggregate Roots in Domain Driven Design terminology.

Cluster sharding can operate in 2 modes, configured via `akka.cluster.sharding.state-store-mode` HOCON configuration:

1. `persistence` (**default**) depends on Akka.Persistence module. In order to use it, you'll need to specify an event journal accessible by all of the participating nodes. An information about the particular shard placement is stored in a persistent cluster singleton actor known as *coordinator*. In order to guarantee consistent state between different incarnations, coordinator stores its own state using Akka.Persistence event journals.
2. `ddata` depends on Akka.DistributedData module. It uses Conflict-free Replicated Data Types (CRDT) to ensure eventually consistent shard placement and global availability via node-to-node replication and automatic conflict resolution. In this mode event journals don't have to be configured. 

Cluster sharding may be active only on nodes in `Up` status - so the ones fully recognized and acknowledged by every other node in a cluster.

## QuickStart

Actors managed by cluster sharding are called **entities** and can be automatically distributed across multiple nodes inside the cluster. One entity instance may live only at one node at the time, and can be communicated with via `ShardRegion` without need to know, what it's exact node location is.

Example:

```csharp
// define envelope used to message routing
public sealed class ShardEnvelope
{
    public readonly int ShardId;
    public readonly int EntityId;
    public readonly object Message;

    ...
}

// define, how shard id, entity id and message itself should be resolved
public sealed class MessageExtractor : IMessageExtractor
{
    public string EntityId(object message) => (message as ShardEnvelope)?.EntityId.ToString();

    public string ShardId(object message) => (message as ShardEnvelope)?.ShardId.ToString();

    public object EntityMessage(object message) => (message as ShardEnvelope)?.Message;
}

// register actor type as a sharded entity
var region = await ClusterSharding.Get(system).StartAsync(
    typeName: "my-actor",
    entityProps: Props.Create<MyActor>(),
    settings: ClusterShardingSettings.Create(system),
    messageExtractor: new MessageExtractor());

// send message to entity through shard region
region.Tell(new ShardEnvelope(shardId: 1, entityId: 1, message: "hello"))
```

In this example, we first specify way to resolve our message recipients in context of sharded entities. For this, specialized message type called `ShardEnvelope` and resolution strategy called `MessageExtractor` have been specified. That part can be customized, and shared among many different shard regions, but it needs to be uniform among all nodes.

Second part of an example is registering custom actor type as sharded entity using `ClusterSharding.Start` or `ClusterSharding.StartAsync` methods. Result is the `IActorRef` to shard region used to communicate between current actor system and target entities. Shard region must be specified once per each type on each node, that is expected to participate in sharding entities of that type. Keep in mind, that it's recommended to wait for the current node to first fully join the cluster before initializing a shard regions in order to avoid potential timeouts.

In some cases, the actor may need to know the `entityId` associated with it. This can be achieved using the `entityPropsFactory` parameter to `ClusterSharding.Start` or `ClusterSharding.StartAsync`. The entity ID will be passed to the factory as a parameter, which can then be used in the creation of the actor.

In case when you want to send message to entities from specific node, but you don't want that node to participate in sharding itself, you can use `ShardRegionProxy` for that.

Example:

```csharp
var proxy = ClusterSharding.Get(system).StartProxy(
    typeName: "my-actor",
    role: null,
    messageExtractor: new MessageExtractor());
```

## Shards

Entities are located and managed automatically. They can also be recreated on the other nodes, as new nodes join the cluster or old ones are leaving it. This process is called rebalancing and for performance reasons it never works over a single entity. Instead all entities are organized and managed in so called shards.

As you may have seen in the examples above shard resolution algorithm is one of the choices you have to make. Good uniform distribution is not an easy task - too small number shards may result in not even distribution of entities across all nodes, while too many of them may increase message routing latency and rebalancing overhead. As a rule of thumb, you may decide to have a number of shards ten times greater than expected maximum number of cluster nodes.

By default rebalancing process always happens from nodes with the highest number of shards, to the ones with the smallest one. This can be configured into by specifying custom implementation of the `IShardAllocationStrategy` interface in `ClusterSharding.Start` parameters.

## Passivation

To reduce memory consumption, you may decide to stop entities after some period of inactivity using `Context.SetReceiveTimeout(timeout)`. In order to make cluster sharding aware of stopping entities, **DON'T use `Context.Stop(Self)` on the entities**, as this may result in losing messages. Instead send a `ShardRegion.Passivate` message to current entity `Context.Parent` (which is shard itself in this case). This will inform shard to stop forwarding messages to target entity, and buffer them instead until it's terminated. Once that happens, if there are still some messages buffered, entity will be reincarnated and messages flushed to it automatically.

### Automatic Passivation

The entities can be configured to be automatically passivated if they haven't received a message for a while using the `akka.cluster.sharding.passivate-idle-entity-after` setting, or by explicitly setting `ClusterShardingSettings.PassivateIdleEntityAfter` to a suitable time to keep the actor alive. Note that only messages sent through sharding are counted, so direct messages to the `ActorRef` of the actor or messages that it sends to itself are not counted as activity. Passivation can be disabled by setting `akka.cluster.sharding.passivate-idle-entity-after = off`. It is always disabled if [Remembering Entities](#remembering-entities) is enabled.

## Remembering entities

By default, when a shard is rebalanced to another node, the entities it stored before migration, are NOT started immediately after. Instead they are recreated ad-hoc, when new messages are incoming. This behavior can be modified by `akka.cluster.sharding.remember-entities = true` configuration. It will instruct shards to keep their state between rebalances - it also comes with extra cost due to necessity of persisting information about started/stopped entities. Additionally a message extractor logic must be aware of `ShardRegion.StartEntity` message:

```csharp
public sealed class ShardEnvelope
{
    public readonly int EntityId;
    public readonly object Message;

    ...
}

public sealed class MessageExtractor : HashCodeMessageExtractor
{
    public MessageExtractor() : base(maxNumberOfShards: 100) { }

    public string EntityId(object message) 
    {
        switch(message)
        {
            case ShardEnvelope e: return e.EntityId;
            case ShardRegion.StartEntity start: return start.EntityId;
        }
    } 
    public object EntityMessage(object message) => (message as ShardEnvelope)?.Message ?? message;
}
```

Using `ShardRegion.StartEntity` implies, that you're able to infer a shard id given an entity id alone. For this reason, in example above we modified a cluster sharding routing logic to make use of `HashCodeMessageExtractor` - in this variant, shard id doesn't have to be provided explicitly, as it will be computed from the hash of entity id itself. Notice a `maxNumberOfShards`, which is the maximum available number of shards allowed for this type of an actor - this value must never change during a single lifetime of a cluster. 

## Retrieving sharding state

You can inspect current sharding stats by using following messages:

- On `GetShardRegionState` shard region will reply with `ShardRegionState` containing data about shards living in the current actor system and what entities are alive on each one of them.
- On `GetClusterShardingStats` shard region will reply with `ClusterShardingStats` having information about shards living in the whole cluster and how many entities alive in each one of them.

## Integrating cluster sharding with persistent actors

One of the most common scenarios, where cluster sharding is used, is to combine them with eventsourced persistent actors from [Akka.Persistence](xref:persistence-architecture) module. However as the entities are incarnated automatically based on provided props, specifying a dedicated, static unique `PersistenceId` for each entity may seem troublesome.

This can be resolved by getting information about shard/entity ids directly from actor's path and constructing unique id from it. For each entity actor path will follow */system/{typeName}/{shardId}/{entityId}* pattern, where *{typeName}* was the parameter provided to `ClusterSharding.Start` method, while *{shardId}* and *{entityId}* where strings returned by message extractor logic. 

> N.B. Sharded entity actors are automatically created by the Akka.Cluster.Sharding guardian actor hierarchy, hence why they live under the `/system` portion of the actor hierarchy. This is done intentionally - in the event of an `ActorSystem` termination the `/user` side of the actor hierachy is always terminated first before the `/system` actors are. 
>
> Therefore, this design gives the sharding system a chance to hand over all of the sharded entity actors running on the terminating node over to the other remaining nodes in the cluster.

Given these values we can build consistent, unique `PersistenceId`s on the fly using the `entityId` (the expectation is that `entityId` are globally unique) as in the following example:

```csharp
public class Aggregate : PersistentActor
{
    public override string PersistenceId { get; }

    public Aggregate()
    {
        PersistenceId = Self.Path.Name;
    }

    ...
}
```