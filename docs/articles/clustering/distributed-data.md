---
uid: distributed-data
title: Distributed Data
---
# Distributed data

Akka.DistributedData plugin can be used as in-memory, highly-available, distributed key-value store, where values conform to so called [Conflict-Free Replicated Data Types](http://hal.upmc.fr/inria-00555588/document) (CRDT). Those data types can have replicas across multiple nodes in the cluster, where DistributedData plugin has been initialized. We are free to perform concurrent updates on replicas with the same corresponding key without need of coordination (distributed locks or transactions) - all state changes will eventually converge with conflicts being automatically resolved, thanks to the nature of CRDTs. To use distributed data plugin, simply install it via NuGet:

```
install-package Akka.DistributedData
```

Keep in mind, that CRDTs are intended for high-availability, non-blocking read/write scenarios. However they are not a good fit, when you need strong consistency or are operating on big data. If you want to have millions of data entries, this is NOT a way to go. Keep in mind, that all data is kept in memory and, as state-based CRDTs, whole object state is replicated remotely across the nodes, when an update happens. A more efficient implementations (delta-based CRDTs) are considered for the future implementations.

## Basic operations

Each CRDT defines few core operations, which are: reads, upserts and deletes. There's no explicit distinction between inserting a value and updating it.

### Reads

To retrieve current data value stored under expected key, you need to send a `Replicator.Get` request directly to a replicator actor reference. You can use `Dsl.Get` helper too:

```csharp
using Akka.DistributedData;
using static Akka.DistributedData.Dsl;

var replicator = DistributedData.Get(system).Replicator;
var key = new ORSetKey<string>("keyA");
var readConsistency = ReadLocal.Instance;

var response = await replicator.Ask<Replicator.IGetResponse>(Get(key, readConsistency));
if (response.IsSuccessful) 
{
    var data = response.Get(key);    
}
```

In response, you should receive `Replicator.IGetResponse` message. There are several types of possible responses:

- `GetSuccess` when a value for provided key has been received successfully. To get the value, you need to call `response.Get(key)` with the key, you've sent with the request.
- `NotFound` when no value was found under provided key.
- `GetFailure` when a replicator failed to retrieve value within specified consistency and timeout constraints.
- `DataDeleted` when a value for the provided key has been deleted.

All `Get` requests follows the read-your-own-write rule - if you updated the data, and want to read the state under the same key immediately after, you'll always retrieve modified value, even if the `IGetResponse` message will arrive before `IUpdateResponse`.

#### Read consistency

What is a mentioned read consistency? As we said at the beginning, all updates performed within distributed data module will eventually converge. This means, we're not speaking about immediate consistency of a given value across all nodes. Therefore we can precise, what degree of consistency are we expecting:

- `ReadLocal` - we take value based on replica living on a current node.
- `ReadFrom` - value will be merged from states retrieved from some number of nodes, including local one.
- `ReadMajority` - value will be merged from more than a half of cluster nodes replicas (or nodes given a configured role).
- `ReadAll` - before returning, value will be read and merged from all cluster nodes (or the ones with configured role).

### Upserts

To update and replicate the data, you need to send `Replicator.Update` request directly to a replicator actor reference. You can use `Dsl.Update` helper too:

```csharp
using System;
using Akka.Cluster;
using Akka.DistributedData;
using static Akka.DistributedData.Dsl;

var cluster = Cluster.Get(system);
var replicator = DistributedData.Get(system).Replicator;
var key = new ORSetKey<string>("keyA");
var set = ORSet.Create(cluster, "value");
var writeConsistency = new WriteTo(3, TimeSpan.FromSeconds(1));

var response = await replicator.Ask<Replicator.IUpdateResponse>(Update(key, set, writeConsistency, old => old.Merge(set)));
```

Just like in case of reads, there are several possible responses:

- `UpdateSuccess` when a value for provided key has been replicated successfully within provided write consistency constraints.
- `ModifyFailure` when update failed because of an exception within modify function used inside `Update` command.
- `UpdateTimeout` when a write consistency constraints has not been fulfilled on time. **Warning**: this doesn't mean, that update has been rolled back! Provided value will eventually propagate its replicas across nodes using gossip protocol, causing the altered state to eventually converge across all of them.
- `DataDeleted` when a value under provided key has been deleted.

You'll always see updates done on local node. When you perform two updates on the same key, second modify function will always see changes done by the first one.

#### Write consistency

Just like in case of reads, write consistency allows us to specify level of certainty of our updates before proceeding:

- `WriteLocal` - while value will be disseminated later using gossip, the response will return immediately after local replica update has been acknowledged.
- `WriteTo` - update will immediately be propagated to a given number of replicas, including local one.
- `WriteMajority` - update will propagate to more than a half nodes in a cluster (or nodes given a configured role) before response will be emitted.
- `WriteAll` - update will propagate to all nodes in a cluster (or nodes given a configured role) before response will be emitted.

### Deletes

Any data can be deleted by sending a `Replicator.Delete` request to a local replicator actor reference. You can use `Dsl.Delete` helper too:

```csharp
using Akka.DistributedData;
using static Akka.DistributedData.Dsl;

var replicator = DistributedData.Get(system).Replicator;
var key = new ORSetKey<string>("keyA");
var writeConsistency = WriteLocal.Instance;

var response = await replicator.Ask<Replicator.IDeleteResponse>(Delete(key, writeConsistency))
```

Delete may return one of the 3 responses:

- `DeleteSuccess` when key deletion succeeded within provided consistency constraints.
- `DataDeleted` when data has been deleted already. Once deleted, key can no longer be reused and `DataDeleted` response will be send to all subsequent requests (either reads, updates or deletes). This message will also be used as notification for subscribers.
- `ReplicationDeleteFailure` when operation failed to satisfy specified consistency constraints. **Warning**: this doesn't mean, that delete has been rolled back! Provided operation will eventually propagate its replicas across nodes using gossip protocol, causing the altered state to eventually converge across all of them.

Deletes doesn't specify it's own consistency - it uses the same `IWriteConsistency` interface as updates.

Delete operation doesn't mean, that the entry for specified key has been completely removed. It will still occupy portion of a memory. In case of frequent updates and removals consider to use remove-aware data types such as `ORSet` or `ORDictionary`.

## Subscriptions

You may decide to subscribe to eventual notifications about possible updates by sending `Replicator.Subscribe` message to a local replicator actor reference. All subscribers will be notified periodically (accordingly to `akka.cluster.distributed-data.notify-subscribers-interval` setting, which is 0.5 sec by default). You can also provoke immediate notification of all subscribers by sending `Replicator.FlushChanges` request to the replicator. Example of actor subscription:

```csharp
using System;
using Akka.DistributedData;
using static Akka.DistributedData.Dsl;

class Subscriber : ReceiveActor 
{
    public Subscriber() 
    {
        var replicator = DistributedData.Get(Context.System).Replicator;
        var key = new ORSetKey<string>("keyA");
        replicator.Tell(Subscribe(key, Self));

        Receive<Replicator.Changed>(changed => 
        {
            var newValue = changed.Get(key);
            Console.WriteLine($"Received updated value for key '{key}': {newValue}");
        });
    }    
}
```

All subscribers are removed automatically when terminated. This can be also done explicitly by sending `Replicator.Unsubscribe` message.

## Available replicated data types

Akka.DistributedData specifies several data types, sharing the same `IReplicatedData` interface. All of them share some common members, such as (default) empty value or `Merge` method used to merge two replicas of the same data with automatic conflict resolution. All of those values are also immutable - this means, that any operations, which are supposed to change their state, produce new instance in result:

- `Flag` is a boolean CRDT flag, which default value is always `false`. When a merging replicas have conflicting state, `true` will always win over `false`.
- `GCounter` (also known as growing-only counter) allows only for addition/increment of its state. Trying to add a negative value is forbidden here. Total value of the counter is a sum of values across all of the replicas. In case of conflicts during merge operation, a copy of replica with greater value will always win.
- `PNCounter` allows for both increments and decrements. A total value of the counter is a sum of increments across all replicas decreased by the sum of all decrements.
- `GSet` is an add-only set, which disallows to remove elements once added to it. Merges of GSets are simple unions of their elements. This data type doesn't produce any garbage.
- `ORSet` is implementation of an observed remove add-wins set. It allows to both add and remove its elements any number of times. In case of conflicts when merging replicas, added elements always wins over removed ones.
- `ORDictionary` (also known as OR-Map or Observed Remove Map) has similar semantics to OR-Set, however it allows to merge values (which must be CRDTs themselves) in case of concurrent updates.
- `ORMultiDictionary` is a multi-map implementation based on `ORDictionary`, where values are represented as OR-Sets. Use `AddItem` or `RemoveItem` to add or remove elements to the bucket under specified keys.
- `PNCounterDictionary` is a dictionary implementation based on `ORDictionary`, where values are represented as PN-Counters.
- `LWWRegister` (Last Write Wins Register) is a cell for any data type, that implements CRDT semantics. Each modification updates register's timestamp (timestamp generation can be customized, by default it's using UTC date time ticks). In case of merge conflicts, the value with highest update timestamp always wins.
- `LWWDictionary` is a dictionary implementation, which internally uses LLW-Registers to allow to store data of any type to be stored in it. Dictionary entry additions/removals are solved with add-wins semantics, while in-place entry value updates are resolved using last write wins semantics.

Keep in mind, that most of the replicated collections add/remove methods require to provide local instance of the cluster in order to correctly track, to which replica update is originally assigned to.

## Tombstones

One of the issue of CRDTs, is that they accumulate history of changes (including removed elements), producing a garbage, that effectively pile up in memory. While this is still a problem, it can be limited by replicator, which is able to remove data associated with nodes, that no longer exist in the cluster. This process is known as a pruning.

## Settings 

There are several different HOCON settings, that can be used to configure distributed data plugin. By default, they all live under `akka.cluster.distributed-data` node:

- `name` of replicator actor. Default: *ddataReplicator*.
- `role` used to limit expected DistributedData capability to nodes having that role. None by default.
- `gossip-interval` tells replicator, how often replicas should be gossiped over the cluster. Default: *2 seconds*
- `notify-subscribers-interval` tells, how often replicator subscribers should be notified with replica state changes. Default: *0.5 second*
- `max-delta-elements` limits a maximum number of entries (key-value pairs) to be send in a single gossip information. If there are more modified entries waiting to be gossiped, they will be send in the next round. Default: *1000*
- `use-dispatcher` can be used to specify custom replicator actor message dispatcher. By default it uses an actor system default dispatcher.
- `pruning-interval` tells, how often replicator will check if pruning should be performed. Default: *30 seconds*
- `max-pruning-dissemination` informs, what is the worst expected time for the pruning process to inform whole cluster about pruned node data. Default: *60 seconds*
- `serializer-cache-time-to-live` is used by custom distributed data serializer to determine, for how long serialized replicas should be cached. When sending replica over multiple nodes, it will reuse data already serialized, if it was found in a cache. Default: *10 seconds*.