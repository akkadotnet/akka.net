---
uid: member-roles
title: Member Roles
---

# Member Roles

![cluster roles](/images/cluster-roles.png)

A cluster can have multiple Akka.NET applications in it, "roles" help to distinguish different Akka.NET applications within a cluster!
Not all Akka.NET applications in a cluster need to perform the same function. For example, there might be one sub-set which runs the web front-end, one which runs the data access layer and one for the number-crunching.
Choosing which actors to start on each node, for example cluster-aware routers, can take member roles into account to achieve this distribution of responsibilities.

# How to Use Role Information in Akka.NET

The member roles are defined in the configuration property named `akka.cluster.roles` and typically defined in the start script as a system property or environment variable.:

```hocon
akka {
cluster {
    roles = ["backend"]
  }
}
```
# Programming Against Akka.Cluster Events

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

# Akka.Cluster.Sharding

Specifies that entities runs on cluster nodes with a specific role. If the role is not specified (or empty) all nodes in the cluster are used.

```hocon
akka {
	cluster {
    roles = ["worker", "notifier", "credit", "storage"]
    sharding {
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

# `DistributedPubSub`

Start the mediator on members tagged with this role. All members are used if undefined or empty.

```hocon
akka {
	cluster {
    roles = ["worker", "notifier", "credit", "storage"]
    pub-sub {
			role = "notifier"
		}
  }
}
```

# `DData`

Replicas are running on members tagged with this role. All members are used if undefined or empty

```hocon
akka {
	cluster {
    roles = ["worker", "notifier", "credit", "storage"]
    distributed-data {
			role = "storage"
		}
  }
}
```

# `ClusterSingleton`

Singleton among the nodes tagged with specified role. If the role is not specified it's a singleton among all nodes in the cluster.

```hocon
akka {
	cluster {
    roles = ["worker", "notifier", "credit", "storage"]
    singleton {
			role = "credit"
		}
  }
}
```

# Putting It All Together

From the above, you can see that it is possible to have different .NET applications (or actors) in a cluster all performing different function:

```hocon
akka {
	cluster {
    roles = ["worker", "notifier", "credit", "storage"]
    singleton {
			role = "credit"
		}
	distributed-data {
			role = "storage"
		}
	pub-sub {
			role = "notifier"
		}
	sharding {
			role = "worker"
		}
  }

```