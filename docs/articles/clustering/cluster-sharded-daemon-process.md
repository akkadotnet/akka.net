---
uid: sharded-daemon-process
title: Akka.Cluster.Sharding Daemon Processes - Distributing Workers
---

# Sharded Daemon Process

> [!WARNING]
>This module is currently marked as [may change](../utilities/may-change.md) because it is a new feature that
>needs feedback from real usage before finalizing the API. This means that API or semantics can change without
>warning or deprecation period. It is also not recommended to use this module in production just yet.

## Introduction

Sharded Daemon Process provides a way to run `N` actors, each given a numeric id starting from `0` that are then kept alive
and balanced across the cluster. When a re-balance is needed the actor is stopped and, triggered by a keep alive running on
all nodes, started on a new node (the keep alive should be seen as an implementation detail and may change in future versions).

The intended use case is for splitting data processing workloads across a set number of workers that each get to work on a subset
of the data that needs to be processed. This is commonly needed to create projections based on the event streams available
from all the [Persistent Actors](../persistence/event-sourcing.md) in a CQRS application. Events are tagged with one out of `N` tags
used to split the workload of consuming and updating a projection between `N` workers.

For cases where a single actor needs to be kept alive see [Cluster Singleton](cluster-singleton.md)

## Basic Example

To set up a set of actors running with Sharded Daemon process each node in the cluster needs to run the same initialization
when starting up:

[!code-csharp[ShardedDaemonProcessSpec.cs](../../../src/contrib/cluster/Akka.Cluster.Sharding.Tests/ShardedDaemonProcessSpec.cs?name=tag-processing)]

## Scalability  

This cluster tool is intended for small numbers of consumers and will not scale well to a large set. In large clusters it is recommended to limit the nodes the sharded daemon process will run on using a role.

## Push-Based Communication

[`ShardedDaemonProcess`](xref:Akka.Cluster.Sharding.ShardedDaemonProcess) also supports push-based communication not too dissimilar from a round-robin `Router`:

[!code-csharp[IActorRef returned by ShardedDaemonProcess](../../../src/contrib/cluster/Akka.Cluster.Sharding.Tests/ShardedDaemonProcessProxySpec.cs?name=PushDaemon)]

The `ShardedDaemonProcess.Init` call returns an `IActorRef` - any messages you send to this `IActorRef` will be routed to one of the `n` worker instances you specified.

## Daemon Proxies

You can also interact with `ShardedDaemonProcess` on nodes that don't use the same [Akka.Cluster role](xref:member-roles) as the `ShardedDaemonProcess` host itself.

[!code-csharp[IActorRef returned by ShardedDaemonProcess](../../../src/contrib/cluster/Akka.Cluster.Sharding.Tests/ShardedDaemonProcessProxySpec.cs?name=PushDaemonProxy)]

Under the covers we use a [`ShardRegionProxy`](xref:Akka.Cluster.Sharding.ClusterSharding#Akka_Cluster_Sharding_ClusterSharding_StartProxy_System_String_System_String_Akka_Cluster_Sharding_IMessageExtractor_) to forward any messages you send to the `IActorRef` returned by the `ShardedDaemonProcess.InitProxy` method.
