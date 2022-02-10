---
uid: cluster-sharding
title: Akka.Cluster.Sharding module
---
# Akka.Cluster.Sharding Module

Cluster sharding is useful in cases when you want to contact with cluster actors using their logical id's, but don't want to care about their physical location inside the cluster or manage their creation. Moreover it's able to re-balance them, as nodes join/leave the cluster. It's often used to represent i.e. Aggregate Roots in Domain Driven Design terminology.

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
    entityPropsFactory: s => Props.Create(() => new MyActor(s)),
    settings: ClusterShardingSettings.Create(system),
    messageExtractor: new MessageExtractor());

// send message to entity through shard region
region.Tell(new ShardEnvelope(shardId: 1, entityId: 1, message: "hello"))
```

In this example, we first specify way to resolve our message recipients in context of sharded entities. For this, specialized message type called `ShardEnvelope` and resolution strategy called `MessageExtractor` have been specified. That part can be customized, and shared among many different shard regions, but it needs to be uniform among all nodes.

Second part of an example is registering custom actor type as sharded entity using `ClusterSharding.Start` or `ClusterSharding.StartAsync` methods. Result is the `IActorRef` to shard region used to communicate between current actor system and target entities. Shard region must be specified once per each type on each node, that is expected to participate in sharding entities of that type. Keep in mind, that it's recommended to wait for the current node to first fully join the cluster before initializing a shard regions in order to avoid potential timeouts.

> N.B. Sharded entity actors are automatically created by the Akka.Cluster.Sharding guardian actor hierarchy, hence why they live under the `/system` portion of the actor hierarchy. This is done intentionally - in the event of an `ActorSystem` termination the `/user` side of the actor hierarchy is always terminated first before the `/system` actors are.
>
> Therefore, this design gives the sharding system a chance to hand over all of the sharded entity actors running on the terminating node over to the other remaining nodes in the cluster.

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

Entities are located and managed automatically. They can also be recreated on the other nodes, as new nodes join the cluster or old ones are leaving it. This process is called re-balancing and for performance reasons it never works over a single entity. Instead all entities are organized and managed in so called shards.

As you may have seen in the examples above shard resolution algorithm is one of the choices you have to make. Good uniform distribution is not an easy task - too small number shards may result in not even distribution of entities across all nodes, while too many of them may increase message routing latency and re-balancing overhead. As a rule of thumb, you may decide to have a number of shards ten times greater than expected maximum number of cluster nodes.

By default re-balancing process always happens from nodes with the highest number of shards, to the ones with the smallest one. This can be configured into by specifying custom implementation of the `IShardAllocationStrategy` interface in `ClusterSharding.Start` parameters.

## Passivation

To reduce memory consumption, you may decide to stop entities after some period of inactivity using `Context.SetReceiveTimeout(timeout)`. In order to make cluster sharding aware of stopping entities, **DON'T use `Context.Stop(Self)` on the entities**, as this may result in losing messages. Instead send a `ShardRegion.Passivate` message to current entity `Context.Parent` (which is shard itself in this case). This will inform shard to stop forwarding messages to target entity, and buffer them instead until it's terminated. Once that happens, if there are still some messages buffered, entity will be reincarnated and messages flushed to it automatically.

### Automatic Passivation

The entities can be configured to be automatically passivated if they haven't received a message for a while using the `akka.cluster.sharding.passivate-idle-entity-after` setting, or by explicitly setting `ClusterShardingSettings.PassivateIdleEntityAfter` to a suitable time to keep the actor alive. Note that only messages sent through sharding are counted, so direct messages to the `ActorRef` of the actor or messages that it sends to itself are not counted as activity. Passivation can be disabled by setting `akka.cluster.sharding.passivate-idle-entity-after = off`. It is always disabled if [Remembering Entities](#remembering-entities) is enabled.

## Remembering Entities

By default, when a shard is re-balanced to another node, the entities it stored before migration, are NOT started immediately after. Instead they are recreated ad-hoc, when new messages are incoming. This behavior can be modified by `akka.cluster.sharding.remember-entities = true` configuration. It will instruct shards to keep their state between re-balances - it also comes with extra cost due to necessity of persisting information about started/stopped entities. Additionally a message extractor logic must be aware of `ShardRegion.StartEntity` message:

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

### Remember Entities Store

There are two options for the remember entities store:

1. Distributed data
2. Persistence

#### Remember Entities Persistence Mode

You can enable persistence mode (enabled by default) with:

```hocon
akka.cluster.sharding.state-store-mode = persistence
```

This mode uses [persistence](../persistence/event-sourcing.md) to store the active shards and active entities for each shard.
By default, cluster sharding will use the journal and snapshot store plugin defined in `akka.persistence.journal.plugin` and
`akka.persistence.snapshot-store.plugin` respectively; to change this behavior, you can use these configuration:

```hocon
akka.cluster.sharding.journal-plugin-id = <plugin>
akka.cluster.sharding.snapshot-plugin-id = <plugin>
```

#### Remember Entities Distributed Data Mode

You can enable DData mode by setting these configuration:

```hocon
akka.cluster.sharding.state-store-mode = ddata
```

To support restarting entities after a full cluster restart (non-rolling) the remember entities store
is persisted to disk by distributed data. This can be disabled if not needed:

```hocon
akka.cluster.sharding.distributed-data.durable.keys = []
```

Possible reasons for disabling remember entity storage are:

* No requirement for remembering entities after a full cluster shutdown
* Running in an environment without access to disk between restarts e.g. Kubernetes without persistent volumes

For supporting remembered entities in an environment without disk storage but with access to a database, use persistence mode instead.

> [!NOTE]
> Currently, Lightning.NET library, the storage solution used to store DData in disk, is having problem
> deploying native library files in [Linux operating system operating in x64 and ARM platforms]
> (<https://github.com/CoreyKaylor/Lightning.NET/issues/141>).
>
> You will need to install LightningDB in your Linux distribution manually if you wanted to use the durable DData feature.

### Terminating Remembered Entities

One complication that  `akka.cluster.sharding.remember-entities = true` introduces is that your sharded entity actors can no longer be terminated through the normal Akka.NET channels, i.e. `Context.Stop(Self)`, `PoisonPill.Instance`, and the like. This is because as part of the `remember-entities` contract - the sharding system is going to insist on keeping all remembered entities alive until explicitly told to stop.

To terminate a remembered entity, the sharded entity actor needs to send a [`Passivate` command](xref:Akka.Cluster.Sharding.Passivate) *to its parent actor* in order to signal to the sharding system that we no longer need to remember this particular entity.

```csharp
protected override bool ReceiveCommand(object message)
{
    switch (message)
    {
        case Increment _:
            Persist(new CounterChanged(1), UpdateState);
            return true;
        case Decrement _:
            Persist(new CounterChanged(-1), UpdateState);
            return true;
        case Get _:
            Sender.Tell(_count);
            return true;
        case ReceiveTimeout _:
            // send Passivate to parent (shard actor) to stop remembering this entity.
            // shard actor will send us back a `Stop.Instance` message
            // as our "shutdown" signal - at which point we can terminate normally.
            Context.Parent.Tell(new Passivate(Stop.Instance));
            return true;
        case Stop _:
            Context.Stop(Self);
            return true;
    }
    return false;
}
```

It is common to simply use `Context.Parent.Tell(new Passivate(PoisonPill.Instance));` to passivate and shutdown remembered-entity actors.

To recreate a remembered entity actor after it has been passivated all you have to do is message the `ShardRegion` actor with a message containing the entity's `EntityId` again just like how you instantiated the actor the first time.

## Retrieving Sharding State

You can inspect current sharding stats by using following messages:

* On `GetShardRegionState` shard region will reply with `ShardRegionState` containing data about shards living in the current actor system and what entities are alive on each one of them.
* On `GetClusterShardingStats` shard region will reply with `ClusterShardingStats` having information about shards living in the whole cluster and how many entities alive in each one of them.

## Integrating Cluster Sharding with Persistent Actors

One of the most common scenarios, where cluster sharding is used, is to combine them with event-sourced persistent actors from [Akka.Persistence](xref:persistence-architecture) module.

Entity actors are instantiated automatically by Akka.Cluster.Sharding - but in order for persistent actors to recover and persist their state correctly they must be given a globally unique `PersistentId`. This can be most easily accomplished using the `entityPropsFactory` overload on the `Sharding.Start` call used to create a new `ShardRegion`:

```csharp
// register actor type as a sharded entity
var region = ClusterSharding.Get(system).Start(
    typeName: "aggregate",
    entityPropsFactory: s => Props.Create(() => new Aggregate(s)),
    settings: ClusterShardingSettings.Create(system),
    messageExtractor: new MessageExtractor());

```

Given these values we can build consistent, unique `PersistenceId`s on the fly using the `entityId` (the expectation is that `entityId` are globally unique) as in the following example:

```csharp
public class Aggregate : PersistentActor
{
    public override string PersistenceId { get; }

    // Passed in via entityPropsFactory via the ShardRegion
    public Aggregate(string persistentId)
    {
        PersistenceId = persistentId;
    }

    // rest of class
}
```

## Cleaning Up Akka.Persistence Shard State

In the normal operation of an Akka.NET cluster, the sharding system automatically terminates and re-balances Akka.Cluster.Sharding regions gracefully whenever an `ActorSystem` terminates.

However, in the event that an `ActorSystem` is aborted as a result of a process / hardware failure it's possible that when using `akka.cluster.sharding.state-store-mode=persistence` leftover sharding data can still be present inside the Akka.Persistence journal and snapshot store - which will prevent the Akka.Cluster.Sharding system from recovering and starting up correctly the next time it's launched.

This is a *rare*, but not impossible occurrence. In the event that this happens you'll need to purge the old Akka.Cluster.Sharding data before restarting the sharding system. You can purge this data automatically by [using the Akka.Cluster.Sharding.RepairTool](https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool) produced by [Petabridge](https://petabridge.com/).

## Tutorial

In this tutorial, we will be making a very simple non-persisted shopping cart implementation using cluster sharding.
All the code used in this tutorial can be found in the [GitHub repository](https://github.com/akkadotnet/akka.net/tree/dev/src/examples/Cluster/ClusterSharding/ShoppingCart)

For a distributed data backed persistent example, please see [this example project](https://github.com/akkadotnet/akka.net/tree/dev/src/examples/Cluster/ClusterSharding/ClusterSharding.Node)
instead.

### Setting Up the Roles

In a sharded cluster, it is common for the shards to be assigned their own specialized role inside the
cluster to distribute their actors in. In this tutorial we will have a single frontend node that will
feed three backend nodes with purchasing data. Usually, these nodes will be separated into different
specialized projects but in this example, we will roll them into a single project and control their
roles using an environment variable.

[!code-csharp[Program.cs](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/Program.cs?name=RoleSetup "Setting up node roles")]

### Starting Up Cluster Sharding

Cluster sharding can be added by using the `Akka.Cluster.Sharding` NuGet package, it already contains
references to the other required packages. Note that the `ClusterSharding.Get()` call is very important
as it contains all the initialization code needed by cluster sharding to start. Note that all nodes that
participates or interacts with the sharded cluster will need to initialize ClusterSharding.

[!code-csharp[Program.cs](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/Program.cs?name=StartSharding "Start Akka.Cluster.Sharding")]

### Starting the Sharding Coordinator Actors

There are two types of sharding coordinator actors:

* **Regular coordinator**: coordinates messages and instantiates sharded actors in their correct shard.
* **Proxy coordinator**: only coordinates messages to the proper sharded actors. This coordinator actor is
  used on nodes that needs to talk to the shard region but does not host any of the sharded actors.

Note that you only need one of these coordinator actors to be able to communicate with the actors
inside the shard region, you don't need a proxy if you already created a regular coordinator.
We will use the proxy coordinator for the front end and the normal coordinator on the backend nodes.

[!code-csharp[Program.cs](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/Program.cs?name=StartShardRegion "Start sharding region")]

### Sending Messages To the Sharded Actors

Finally we can start sending messages from the front end node to the sharded actors in the back end
through the proxy coordinator actor.

[!code-csharp[Program.cs](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/Program.cs?name=StartSendingMessage "Start sending messages")]

Note that the message need to contain the entity and shard id information so that cluster sharding
will know where to send the message to the correct shard and actor. You can do this by directly
embedding the ids inside all of your shard messages, or you can wrap them inside an envelope.
Cluster sharding will extract the information it needs by using a message extractor. We will discuss
this later in the tutorial.

### Sharded Actor

Sharded actors are usually persisted using `Akka.Persistence` so that it can be restored after it is
terminated, but a regular `ReceiveActor` would also work. In this example, we will be using a regular
`ReceiveActor` for brevity. For a distributed data backed cluster sharding example, please see
[this example](https://github.com/akkadotnet/akka.net/tree/dev/src/examples/Cluster/ClusterSharding/ClusterSharding.Node)
in the GitHub repository.

[!code-csharp[Customers.cs](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/Customers.cs?name=ActorClass "Actor class")]

### Message Envelope and Message Extractor

The shard coordinator would need to know which shard and entity it needs to send the messages to,
we do that by embedding the entity and shard id information in the message itself, or inside the
envelope we send the message in. The shard coordinator will then use the message extractor to extract
the shard and entity id from the envelope.

To be recognized as a message extractor, the class needs to implement the `IMessageExtractor` interface.
In this example, we will use the built-in `HashCodeMessageExtractor`; this extractor will derive the
shard id by applying murmur hash algorithm on the entity id so we don't need to create our own.

[!code-csharp[MessageExtractor.cs](../../../src/examples/Cluster/ClusterSharding/ShoppingCart/MessageExtractor.cs?name=ExtractorClass "Message envelope and extractor class")]
