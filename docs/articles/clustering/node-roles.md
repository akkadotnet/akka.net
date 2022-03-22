---
uid: node-roles
title: Node Roles
---

# Node Roles

Not all nodes of a cluster need to perform the same function. For example, there might be one sub-set which runs the web front-end, one which runs the data access layer and one for the number-crunching. Choosing which actors to start on each node, for example cluster-aware routers, can take node roles into account to achieve this distribution of responsibilities.

The node roles are defined in the configuration property named `akka.cluster.roles` and typically defined in the start script as a system property or environment variable.

The roles are part of the membership information in `MemberEvent` that you can subscribe to. The roles of the own node are available from the `SelfMember` and that can be used for conditionally starting certain actors:

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
