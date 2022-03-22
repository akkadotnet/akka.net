---
uid: member-roles
title: Member Roles
---

# Why Are Roles Important

A cluster can have multiple Akka.NET applications in it, "roles" help to distinguish different Akka.NET applications within a cluster!

# How Can Roles Help

Not all Akka.NET applications in a cluster need to perform the same function. For example, there might be one sub-set which runs the web front-end, one which runs the data access layer and one for the number-crunching. 
Choosing which actors to start on each node, for example cluster-aware routers, can take member roles into account to achieve this distribution of responsibilities.

# Usage

The member roles are defined in the configuration property named `akka.cluster.roles`:

```
akka {
cluster {
    roles = ["backend"]
  }
}
```

and typically defined in the start script as a system property or environment variable.

```
var settings = ClusterShardingSettings
    .Create(_system)
    .WithRole(Environment.GetEnvironmentVariable("ROLE"));
```

The roles are part of the membership information in `MemberEvent` that you can subscribe to. The roles of the local cluster member are available from the `SelfMember` and that can be used for conditionally starting certain actors:

```csharp
var selfMember = Cluster.Get(_actorSystem).SelfMember;
if (selfMember.HasRole("backend")) 
{
  context.ActorOf(Backend.Prop(), "back");
} 
else if (selfMember.HasRole("front")) 
{
  context.ActorOf(Frontend.Prop(), "front");
}
```
