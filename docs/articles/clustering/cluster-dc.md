---
uid: cluster-dc
title: Cluster across multiple data centers
---

# Cluster across multiple data centers

This chapter describes how Akka.Cluster can be used across multiple data centers, availability zones or regions.

The reason for Akka.NET cluster awareness of data center boundaries is that often a communication between nodes living on different data centers has much higher latency and failure rate than the message passing between nodes living inside the same data center. 

Another reason to group your nodes into more isolated regions is to improve stability of large clusters or isolation of certain group of nodes having higher risk of failure.

## Motivation

Some of the reasons to split your cluster to more than one data center, are:

- Improve responsiveness of your services by placing them closer to the requester geographical locations.
- Redundancy and failure tolerance in face of entire data center going down.
- Load balancing.

While it's possible to run Akka.Cluster across multiple physicall data centers, it may lead to some risks like:

- If a network partition occurs, no new nodes can will be able to fully join the cluster (as `MemberStatus.Up` is assigned to a node only after all nodes in the cluster have acknowledged it). It doesn't matter if we're talking about unreachable nodes inside a single data-center or parition of entire data centers. While this may be partially mitigated by allowing `akka.cluster.allow-weakly-up-members = on` and using `MemberStatus.WeaklyUp`, it doesn't fully solve an issue.
- Since we didn't acknowledged charactersitics of cross data center communication, a false positive failure detections ratio would be much higher. It's also hard to distinguish crash of a single node from entire DC going down.
- Downing or removal of nodes inside the same data center can be more aggresive and automatic than the one used across DC boundaries.
- There's a higher risk involved in doing fail over of Cluster Singletons or Cluster Sharding entities across data centers.
- Lack of information about location makes it harder to optimize frequent communication to occur between nearby nodes instead of distant ones. I.e. cluster routers will be more efficient, if the concentrate on communicating only with the nodes living in the same data center.

You could avoid those problems by setting up separate clusters for each data centers, and communicating them either via Cluster Client or by using external transport. This way however we lose cluster membership semantics. It will also make some of the features (like Distributed Data) not working in geo-distributed environment.

## Defining the data centers

The idea behind Multi-DC feature is that we are able to group nodes by setting the `akka.cluster.multi-data-center.self-data-center` configuration property. Node can belong only to a single data center - if none was specified, implicitly a `default` name is used.

It's not mandatory to limit usage of this feature only to reflect physical boundaries of data centers. You can as well use it to isolate group of more "wacky" servers or split huge clusters into smaller groups for better scalability.

## Membership

Some of the cluster membership state transitions are managed by single node called a leader. There is one leader per data center, and all members within that data center have their state transitions managed by this leader, isolated from other DCs. Some of these operations cannot be performed while there are unreachable nodes detected within the same data center. However the unreachability is isolated to the scope of current DC - if some nodes are unreachable in other DCs or maybe there are entire DCs out of reach, they won't affect membership within the scope of DC, where its nodes are able to reach each other.

Some of the operations, that can move across boundaries of data centers, are:

- Actions like Join, Leave or Down - they can be send to any node in any DC.
- Seed node configuration is also global.

> An implementation detail, that you don't need to know, but may be interested about is that data center membership is realized by adding data center name prefixed with `dc-` to the current member roles. Don't worry if you'll see it in logs.

An information about what data center a member belongs to, is accessible from `Member` object:

```csharp
var cluster = Cluster.Get(system);
// data center of a current node
var dc = cluster.SelfDataCenter; 
// all known data centers
var all = cluster.State.AllDataCenters;
// DC of a specific member
var member = cluster.State.Members.First();
var dc = member.DataCenter;
```

## Failure detection

Akka.NET cluster node failure detection is heartbeat-based, both in case of inter-DC and cross-DC connections. However the frequency is much lower between data centers - it should only indicate problems with connection between them. You can configure both failure detectors:

- `akka.cluster.failure-detector` working withing boundaries of current data center.
- `akka.cluster.multi-data-center.failure-detector` for failure detection across data centers.

When subscribing to cluster the `UnreachableMember` and `ReachableMember` events are used for membership observations scoped to a data center where the subscribtion was registered. For cross data center notifications, use `UnreachableDataCenter` and `ReachableDataCenter` events.

Within a data center all nodes are involved in membership gossiping and failure detection. However, cross data center heartbeats and gossip messages are send only between a number of oldest nodes on each side. This number can be configured via `akka.cluster.multi-data-center.cross-data-center-connections`. This also may affect the strategy of rolling cluster upgrades - ideally the oldest nodes should be upgraded as last ones, one by one or few at the time (to avoid turning off all of the cross-DC connections).

## Cluster Singleton

**Cluster singleton will always create a one singleton per data center.** This is by design - it's very hard to select one global singleton and make a consistent choice in face of many leaders spread in their own data centers. If you need only one cluster singleton, just start it only on one data center and communicate with it using cluster singleton proxies. You can specify which DC your proxy is expected to connect to using `ClusterSingletonProxySettings`.

```csharp
var proxy = system.ActorOf(
    ClusterSingletonProxy.Props(
        singletonManagerPath: "/user/consumer",
        settings: ClusterSingletonProxySettings.Create(system)
            .WithRole("worker")
            .WithDataCenter("West US")
    ), name: "consumerProxy");
```

If no data center was defined, cluster singleton proxy will connect to the data center on which it was created.

## Cluster Sharding

Depending on configuration, your cluster sharding may also use cluster singletons - this means, that the singleton limitations also affects sharded entities. Each data center will have its own coordinators and regions, isolated from other data centers and unaware of entities living in them. Therefore **calling an entity with the same name on 3 different DCs will result in creating 3 different instances of it, one on each data center**.

If you need to guarantee a single global entity, you can either choose a single data center to host all entities of given type or to include information about data center in shard/entity routing strategy.

Just like in the case of cluster singletons, a cluster sharding proxy by default will target its own data center. This can be configured during proxy creation:

```csharp
var counterProxy = await ClusterSharding.Get(system).StartProxyAsync(
    typeName: "Counter",
    role: null,
    dataCenter: "North Europe",
    extractEntityId: extractEntityId,
    extractShardId: extractShardId
);
```

This way entities living on different data centers can also communicate with each other.