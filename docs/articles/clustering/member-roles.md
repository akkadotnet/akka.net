---
uid: member-roles
title: Member Roles
---

# Member Roles

![cluster roles](/images/cluster/cluster-roles.png)

A cluster can have multiple nodes(machines/servers/vms) with different capabilities.
When you require an application to run on a node(machine/server/vm) with certain capabilities, roles helps you to distinguish the nodes so that application can be deployed on that node.
Specifying cluster role(s) is a best practice; you don't want an application that requires less computational power running and consuming resources meant for a mission-critical and resource-intensive application.
Even if you only have a single type of node in your cluster, you should still use roles for it so you can leverage this infrastructure as your cluster expands in the future; and they add zero overhead in any conceivable way.

# How To Configure Cluster Roles

Below I will show you how the cluster above can be reproduced. I will create a five-nodes(ActorSystems - all having same name, though, but living on different machine/server/vm) cluster with different roles applied:

**Node1**: All of my code that receives requests from users and push same to the cluster will be deployed here!

```hocon
akka
{
  cluster
  {
    roles = ["web"]
  }
}
```

**Node2**: All of my code handling fraud detections will be deployed on this node

```hocon
akka
{
  cluster
  {
    roles = ["fraud"]
  }
}
```

**Node3**: All me code that retrieves, stores data will be deployed on this node

```hocon
akka
{
  cluster
  {
    roles = ["storage"]
  }
}
```

**Node4**: All my code that handles customer orders will be deployed on this node

```hocon
akka
{
  cluster
  {
    roles = ["order"]
  }
}
```

**Node5**: All my code that handles customer billing will be deployed on this node

```hocon
akka
{
  cluster
  {
    roles = ["billing"]
  }
}
```

Now that we have laid the foundation for what is to follow, Akka.Cluster is made of various ready-made extensions(or modules) you can deploy.
I will show you how you can deploy them on any of the nodes. Apart from the Akka.Cluster modules, if you just want to use the Akka.Cluster core, I will show you how you can deploy your own actor to the cluster node with the required role:

**Cluster Sharding**: Sharding be will deployed on the nodes with the `order` role, `node4`

```hocon
akka
{
  cluster
  {
    roles = ["order"]
    sharding
    {
      role = "order"
    }
  }
}
```

**Distributed Pub-Sub**: DistributedPubSub will be deployed on the nodes with the `web` role, `node1`.

```hocon
akka
{
  cluster
  {
    roles = ["web"]
    pub-sub
    {
      role = "web"
    }
  }
}
```

**Distributed Data**: DistributedData will be deployed on the node with the `storage` role, `node3`.

```hocon
akka
{
  cluster
  {
    roles = ["storage"]
    distributed-data
    {
      role = "storage"
    }
  }
}
```

**Cluster Singleton**: To avoid over charging a customer more than once, my code will be deployed with `ClusterSingleton` on the node with the `billing` role, `node5`

```hocon
akka
{
  cluster
  {
    roles = ["billing"]
    singleton 
    {
      role = "billing"
    }
  }
}
```

I have one more node, `node2`, with nothing running in it. I will deploy my custom fraud detection code there, and the way to do that is:

```csharp
var selfMember = Cluster.Get(_actorSystem).SelfMember;
if (selfMember.HasRole("fraud"))
{
    context.ActorOf(Billing.Prop(), "bill-gate");
}
else
{
    //sleep, probably!
}
```

Using the Cluster `SelfMember`, I am checking if the current node has the `billing` role and if yes, create the `Billing` actor.

## Cluster-Aware Router

Cluster-Aware routers automate how actors are deployed on the cluster and also how messages are routed based on the role specified! Routers in Akka.NET can be either grouped or pooled and you can read up on them [Routers](https://getakka.net/articles/actors/routers.html)

**Router Group**: I will create Cluster-Aware Router Group for all my applications above!

```hocon
akka
{
   actor
   {
      provider = "Akka.Cluster.ClusterActorRefProvider, Akka.Cluster"
      deployment
      {
         /webdispatcher
         {
            router = consistent-hashing-group # routing strategy
            routees.paths = ["/user/web"] # path of routee on each node
            nr-of-instances = 3 # max number of total routees
            cluster
            {
               enabled = on
               use-role = "web"
            }
         }
         /frauddispatcher
         {
            router = consistent-hashing-group # routing strategy
            routees.paths = ["/user/fraud"] # path of routee on each node
            nr-of-instances = 3 # max number of total routees
            cluster
            {
               enabled = on
               use-role = "fraud"
            }
         }
         /billingdispatcher
         {
            router = consistent-hashing-group # routing strategy
            routees.paths = ["/user/billing"] # path of routee on each node
            nr-of-instances = 3 # max number of total routees
            cluster
            {
               enabled = on
               use-role = "billing"
            }
         }
         /orderdispatcher
         {
            router = consistent-hashing-group # routing strategy
            routees.paths = ["/user/order"] # path of routee on each node
            nr-of-instances = 3 # max number of total routees
            cluster
            {
               enabled = on
               use-role = "order"
            }
         }
         /storagedispatcher
         {
            router = consistent-hashing-group # routing strategy
            routees.paths = ["/user/storage"] # path of routee on each node
            nr-of-instances = 3 # max number of total routees
            cluster
            {
               enabled = on
               use-role = "storage"
            }
         }
      }
   }
}
```

```csharp
var web = system.ActorOf<Web>("web");
var fraud = system.ActorOf<Fraud>("fraud");
var order = system.ActorOf<Order>("order");
var billing = system.ActorOf<Billing>("billing");
var storage = system.ActorOf<Storage>("storage");

var webRouter = system.ActorOf(Props.Empty.WithRouter(FromConfig.Instance),"webdispatcher");
var fraudRouter = system.ActorOf(Props.Empty.WithRouter(FromConfig.Instance),"frauddispatcher");
var orderRouter = system.ActorOf(Props.Empty.WithRouter(FromConfig.Instance),"orderdispatcher");
var billingRouter = system.ActorOf(Props.Empty.WithRouter(FromConfig.Instance),"billingdispatcher");
var storageRouter = system.ActorOf(Props.Empty.WithRouter(FromConfig.Instance),"storagedispatcher");
```

**Router Pool**: I will create Cluster-Aware Router Pool for all my applications above!

```hocon
akka
{
   actor
   {
      provider = "Akka.Cluster.ClusterActorRefProvider, Akka.Cluster"
      deployment
      {
         /webdispatcher
         {
            router = round-robin-pool # routing strategy
            max-nr-of-instances-per-node = 5
            cluster
            {
               enabled = on
               use-role = "web"
            }
         }
         /frauddispatcher
         {
            router = round-robin-pool # routing strategy
            max-nr-of-instances-per-node = 5
            cluster
            {
               enabled = on
               use-role = "fraud"
            }
         }
         /billingdispatcher
         {
            router = round-robin-pool # routing strategy
            max-nr-of-instances-per-node = 5
            cluster
            {
               enabled = on
               use-role = "billing"
            }
         }
         /orderdispatcher
         {
            router = round-robin-pool # routing strategy
            max-nr-of-instances-per-node = 5
            cluster
            {
               enabled = on
               use-role = "order"
            }
         }
         /storagedispatcher
         {
            router = round-robin-pool # routing strategy
            max-nr-of-instances-per-node = 5
            cluster
            {
               enabled = on
               use-role = "storage"
            }
         }
      }
   }
}
```

```csharp
var routerProps = Props.Empty.WithRouter(FromConfig.Instance);
```
