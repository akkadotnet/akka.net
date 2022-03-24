---
uid: member-roles
title: Member Roles
---

# Member Roles

![cluster roles](/images/cluster/cluster-roles.png)

A cluster can have multiple Akka.NET applications in it, "roles" help to distinguish different Akka.NET applications within a cluster!
Not all Akka.NET applications in a cluster need to perform the same function. For example, there might be one sub-set which runs the web front-end, one which runs the data access layer and one for the number-crunching.
Choosing which actors to start on each node, for example cluster-aware routers, can take member roles into account to achieve this distribution of responsibilities.

# How to Use Roles

The member roles are defined in the configuration property named `akka.cluster.roles` and typically defined in the start script as a system property or environment variable.:

```hocon
akka
{
  cluster
  {
    roles = ["backend"]
  }
}
```

## Using Roles Within Your Cluster

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

## Akka.Cluster.Sharding

Cluster Sharding uses its own Distributed Data Replicator per node. If using roles with sharding there is one Replicator per role, which enables a subset of all nodes for some entity types and another subset for other entity types. Each replicator has a name that contains the node role and therefore the role configuration must be the same on all nodes in the cluster, for example you can’t change the roles when performing a rolling update. Changing roles requires a full cluster restart.

```hocon
akka
{
  cluster
  {
    roles = ["worker", "notifier", "credit", "storage"]
    sharding
    {
      role = "worker"
    }
  }
}
```

```csharp
var sharding = ClusterSharding.Get(system);
var shardRegion = await sharding.StartAsync(
    typeName: "customer",
    entityPropsFactory: e => Props.Create(() => new Customer(e)),
    settings: ClusterShardingSettings.Create(system).WithRole("worker"),
    messageExtractor: new MessageExtractor(10));
```

## `DistributedPubSub`

Start the mediator on members tagged with this role. All members are used if undefined or empty.

```hocon
akka
{
  cluster
  {
    roles = ["worker", "notifier", "credit", "storage"]
    pub-sub
    {
      role = "notifier"
    }
  }
}
```

## `DData`

Replicas are running on members tagged with this role. All members are used if undefined or empty

```hocon
akka
{
  cluster
  {
    roles = ["worker", "notifier", "credit", "storage"]
    distributed-data
    {
      role = "storage"
    }
  }
}
```

## `ClusterSingleton`

Singleton among the nodes tagged with specified role. If the role is not specified it's a singleton among all nodes in the cluster.

```hocon
akka
{
  cluster
  {
    roles = ["worker", "notifier", "credit", "storage"]
    singleton 
    {
      role = "credit"
    }
  }
}
```

## Cluster-Aware Router

The major benefit of Akka.Cluster is that you can scale out your actor system to more nodes as load on the system increases - in other words, during peak period, Akka.Cluster can scale out your, for instance, order processor. 
You can further make the most of the scaling benefits, with Cluster-aware routers to simplify developing scalable applications.

### Creating Cluster-Aware Router Groups

While the standard Router Groups lets you direct messages to a selected actor paths, in Akka.Cluster, Cluster-Aware Router Groups, you can send the messages across a cluster of machines instead!

Create Cluster-Aware Router Groups with HOCON file:

```hocon
akka
{
   actor
   {
      provider = "Akka.Cluster.ClusterActorRefProvider, Akka.Cluster"
      deployment
      {
         /workdispatcher
         {
            router = consistent-hashing-group # routing strategy
            routees.paths = ["/user/worker"] # path of routee on each node
            nr-of-instances = 3 # max number of total routees
            cluster
            {
               enabled = on
               allow-local-routees = on
            }
         }
      }
   }
   cluster
   {
     # your cluster configuration here
   }
}
```
```csharp
var worker = system.ActorOf<Worker>("worker");
var router = system.ActorOf(Props.Empty.WithRouter(FromConfig.Instance),"workdispatcher");
```

Create Cluster-Aware Router Groups with `code`:

```csharp
var routeePaths = new List<string> { "/user/worker" };
var clusterRouterSettings = new ClusterRouterGroupSettings(3, routeePaths, true);
var clusterGroupProps = Props.Empty.WithRouter(new ClusterRouterGroup(new Akka.Routing.ConsistentHashingGroup("/user/worker"), clusterRouterSettings));
```

### Creating Cluster-Aware Router Pool

Cluster-Aware Router Pool, lets you create actors across a cluster of nodes. Any time a new node joins existing cluster, the router deploys actors onto the new node and makes the actors available by adding it to the list of routees. 
If a node becomes unresponsive, due to network outage or it is shut down abruptly, it’s removed from the list of available routees.

Create Cluster-Aware Router Pool with HOCON file:

```hocon
akka
{
   actor
   {
      provider = "Akka.Cluster.ClusterActorRefProvider, Akka.Cluster"
      deployment
      {
         /workdispatcher
         {
            router = round-robin-pool # routing strategy
            max-nr-of-instances-per-node = 5
            cluster
            {
               enabled = on
               allow-local-routees = on
            }
         }
      }
   }
   cluster
   {
     # your cluster configuration here
   }
}
```
```csharp
var routerProps = Props.Empty.WithRouter(FromConfig.Instance);
```
Create Cluster-Aware Router Pool with `code`:

```csharp
var clusterPoolSettings = new ClusterRouterPoolSettings(1000, 5, true);
var clusterPoolProps = Props.Create<Worker>().WithRouter(new ClusterRouterPool(new RoundRobinPool(5), clusterPoolSettings));
```

## Putting It All Together

From the above, you can see that it is possible to have different .NET applications (or actors) in a cluster all performing different function:

```hocon
akka 
{
  cluster 
  {
    roles = ["worker", "notifier", "credit", "storage"]
    singleton
    {
      role = "credit"
    }
    distributed-data
    {
      role = "storage"
    }
    pub-sub
    {
      role = "notifier"
    }
    sharding
    {
      role = "worker"
    }
}
```
