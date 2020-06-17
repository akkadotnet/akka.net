# Sharded Daemon Process

> [!WARNING]
>This module is currently marked as [may change](../utilities/may-change.md) because it is a new feature that
>needs feedback from real usage before finalizing the API. This means that API or semantics can change without
>warning or deprecation period. It is also not recommended to use this module in production just yet.

## Introduction

Sharded Daemon Process provides a way to run `N` actors, each given a numeric id starting from `0` that are then kept alive
and balanced across the cluster. When a rebalance is needed the actor is stopped and, triggered by a keep alive running on 
all nodes, started on a new node (the keep alive should be seen as an implementation detail and may change in future versions).

The intended use case is for splitting data processing workloads across a set number of workers that each get to work on a subset
of the data that needs to be processed. This is commonly needed to create projections based on the event streams available
from all the [Persistent Actors](../persistence/event-sourcing.md) in a CQRS application. Events are tagged with one out of `N` tags
used to split the workload of consuming and updating a projection between `N` workers.

For cases where a single actor needs to be kept alive see [Cluster Singleton](cluster-singleton.md)

## Basic example

To set up a set of actors running with Sharded Daemon process each node in the cluster needs to run the same initialization
when starting up:

[!code-csharp[ShardedDaemonProcessSpec.cs](../../../src/contrib/cluster/Akka.Cluster.Sharding.Tests/ShardedDaemonProcessSpec.cs?name=tag-processing)]

## Scalability  

This cluster tool is intended for small numbers of consumers and will not scale well to a large set. In large clusters 
it is recommended to limit the nodes the sharded daemon process will run on using a role.