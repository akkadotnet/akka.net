---
uid: lease
title: Distributed Locks with Akka.Coordination
---
# Distributed Locks with Akka.Coordination

## General Definition

`Akka.Coordination` provides a generalized "distributed lock" implementation called a `Lease` that uses a unique resource identifier inside a backing store (such as Azure Blob Storage or Kubernetes Custom Resource Definitions) to only allow one current "holder" of the lease to perform an action at any given time.

Akka.NET uses leases internally inside [Split Brain Resolver](../clustering/split-brain-resolver.md), [Cluster.Sharding](../clustering/cluster-sharding.md), and [Cluster Singletons](../clustering/cluster-singleton.md) for this purpose - and in this document you can learn how to call and create leases in your own Akka.NET applications if needed.

### Officially Supported Lease Implementations

There are currently two officially supported lease implementations:

* [Akka.Coordination.KubernetesApi](https://github.com/akkadotnet/Akka.Management/tree/dev/src/coordination/kubernetes/Akka.Coordination.KubernetesApi)
* [Akka.Coordination.Azure](https://github.com/akkadotnet/Akka.Management/tree/dev/src/coordination/azure/Akka.Coordination.Azure)

All lease implementations in Akka.NET supports automatic expiry or renewal mechanisms. Expiry ensures that leases do not remain active indefinitely, which can prevent resource deadlock or starvation scenarios.

### Key Characteristics and Components

* **Lease Name**: A unique identifier for the lease, which specifies the resource to be protected.
* **Owner Name**: A unique identifier for the entity (usually an actor or node) that is attempting to acquire the lease.
* **Lease Timeout**: May also be called "Time To Live" or TTL. A duration parameter that specifies how long the lease should last. Leases may be renewed or revoked depending on the implementation.

### Public API

The `Akka.Coordination.Lease` API provides the following methods:

* **`Task<bool> Acquire()`**
* **`Task<bool> Acquire(Action<Exception> leaseLostCallback)`**

  These asynchronous methods attempts to acquire the lease for the resource. It returns a `Task<bool>`, indicating if the acquisition was successful or not. Parameters may include callback delegate method that will be invoked when a granted lease have been revoked for some reason.

* **`Task<bool> Release()`**

  This asynchronous method releases the lease, relinquishing the access rights to the resource. It returns a `Task<bool>`, where true indicates successful release. This method is important for ensuring that resources are freed up for other actors or nodes once a task is completed.

* **`bool CheckLease()`**

  This method checks whether the lease is still valid, typically returning a Boolean. `CheckLease()` is useful for verifying if a lease has expired or been revoked, ensuring that processes do not operate under an invalid lease.

## Example

The full code for this example can be seen inside the [Akka.NET repo](https://github.com/akkadotnet/akka.net/blob/dev/src/core/Akka.Docs.Tests/Utilities/LeaseActorDocSpec.cs)

### Internal Messages

The actor using `Lease` will need a few boilerplate internal messages:

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Utilities/LeaseActorDocSpec.cs?name=messages)]

### Obtaining Reference To Lease Implementation

To obtain a reference to the `Lease` implementation, you will need 4 things:

* **Lease Name**: A unique identifier for the lease, which specifies the resource to be protected.
* **Owner Name**: A unique identifier for the entity (usually an actor or node) that is attempting to acquire the lease.
* **Configuration Path**: A full HOCON configuration path containing the definition of the lease implementation.
* **Retry Interval**: A time duration needed for failed lease acquisition retry.

A `Lease` reference is then obtained by calling `LeaseProvider.Get(Context.System).GetLease()`

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Utilities/LeaseActorDocSpec.cs?name=constructor)]

### Actor States

The actor leverages actor states to separate the lease acquisition and actual working state of the actor.

* **`AcquiringLease` State**
  In this state, the actor will only handle the required internal messages related to lease acquisition. Any other messages not related to lease acquisition will be stashed until the lease is acquired/granted. The actor will automatically retry lease acquisition by calling `AcquireLease()` on a regular basis if it failed to acquire a lease.
* **`Active` State**
  In this state, the actor is active and is allowed to process all received messages normally. The only lease related message being processed is the `LeaseLost` internal message that signals lease revocation.

In the event of a lease revocation, the actor will forcefully shuts down to prevent resource contention. This may be modified to suit user needs.

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Utilities/LeaseActorDocSpec.cs?name=actor-states)]

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Utilities/LeaseActorDocSpec.cs?name=lease-acquisition)]

### Lease Lifecycle

Lease needs to be granted before an actor can perform any of its message handling and the actor needs to stop, forcefully or gracefully, if the lease is revoked. Attention must be taken so that, in the event of revoked lease, there would be no resource contention, or at least with minimal impact.

In the example code, lease would be acquired inside the `PreStart()` method override by calling `AcquireLease()` and it will be released inside the `PostStop()` method override.

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Utilities/LeaseActorDocSpec.cs?name=lease-lifecycle)]
