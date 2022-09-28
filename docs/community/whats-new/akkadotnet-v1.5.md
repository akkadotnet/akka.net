---
uid: akkadotnet-v15-whats-new
title: What's new in Akka.NET v1.5.0?
---

# Akka.NET v1.5 Plans and Goals

Beginning with our [March 9th, 2022 Akka.NET Community Standup](xref:community-standups) we've shared our plans for the roadmap of Akka.NET v1.5.

While not every technical detail is finalized yet, we do want to share the goals and some of the specific plans of what's included in this next minor version release of Akka.NET!

## Goals

<!-- markdownlint-disable MD033 -->
<iframe width="560" height="315" src="https://www.youtube.com/embed/pZN1ugrJtJU" title="YouTube video player" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture" allowfullscreen></iframe>
<!-- markdownlint-enable MD033 -->

Based on feedback from Akka.NET users, here are the top priorities:

1. **Better documentation and examples** - after simplifying the Akka.NET APIs around configuration and startup, modernizing all samples and deployment guidance is the biggest issue affecting end-users.
2. **Improved Akka.Remote + Cluster.Sharding performance** - we want to go from ~350,000 msg/s to 1m+ msg/s in our `RemotePingPong` benchmark by changing the design of Akka.Remote's actors and optionally, replacing our transports.
3. **Improved default serialization options** - Newtonsoft.Json sucks. Full stop.
4. **Improved Akka.Cluster formation experience** - in layman's terms this means [releasing Akka.Management out of beta](https://github.com/akkadotnet/Akka.Management/discussions/417) and incorporating it into the Akka.Cluster literature and code samples.
5. **Leveraging .NET 6+ primitives for improved performance** - thread execution, zero-allocation System.Memory data structures, and more.
6. **Make CQRS a priority in Akka.Persistence** - tags are an especially weak area for Akka.Persistence right now, given that they require table scans.
7. **Leverage Microsoft.Extensions more closely** - consume Microsoft.Extensions.Configuration from HOCON, allow Microsoft.Extensions.Logging interplay, and more.
8. **Cleanup** - there are a lot of old constructs and that can be removed from Akka.NET.

## How to Contribute

Contributing to the v1.5 effort is somewhat different than our [normal maintenance contributions to Akka.NET](xref:contributing-to-akkadotnet),in the following ways:

1. **Breaking binary compatibility changes are allowed so long as they are cost-justified** - contributors must be able to explain both the benefits and trade-offs involved in making this change in order for it to be accepted;
2. **Wire compatibility must include a viable upgrade path** - if we're to introduce changes to the wire format of Akka.NET, contributors must demonstrate and document a viable upgrade path for end-users; and
3. **Bigger bets are encouraged** - now is a good time to take some risks on the code base. So long as contributors can offer sound arguments for taking those bets (and this includes acknowledging and being realistic about trade-offs) then they should feel free to propose new changes that align with the 1.5 goals.

### Good Areas for Contribution

1. .NET 6 dual-targeting - there will be lots of areas where we can take advantage of new .NET 6 APIs for improved performance;
2. Performance optimization - performance optimization around Akka.Remote, Akka.Persistence, Akka.Cluster.Sharding, DData, and core Akka will be prioritized in this release and there are _many_ areas for improvement;
3. Akka.Hosting - there are lots of extension points and possibilities for making the APIs as expressive + concise as possible - we will need input from contributors to help make this possible;
4. API Cleanup - there are lots of utility classes or deprecated APIs that can be deleted from the code base with minimal impact. Less is more; and
5. New ideas and possibilities - this is a _great_ time to consider adding new features to Akka.NET. Be bold.

> [!IMPORTANT]
> Familiarize yourself with our [Akka.NET contribution guidelines](xref:contributing-to-akkadotnet) for best experience.

## Designs & Major Changes

### Akka.Cluster.Sharding State Storage

One of the most significant upgrades we've made in Akka.NET v1.5 is a complete rewrite of Akka.Cluster.Sharding's state storage system.

> [!NOTE]
> You can watch [our discussion of this Akka.Cluster.Sharding upgrade during our September, 2022 Akka.NET Community Standup for more details](https://www.youtube.com/watch?v=rTBgxeHf91M&t=359s).

In Akka.NET v1.5 we've split Akka.Cluster.Sharding's `state-store-mode` into two parts:

* CoordinatorStore (`akka.cluster.sharding.state-store-mode`) and
* ShardStore (`akka.cluster.sharding.remember-entities-store`.)

Which can use different persistence modes configured via `akka.cluster.sharding.state-store-mode` & `akka.cluster.sharding.remember-entities-store`.

> [!IMPORTANT]
> The goal behind this split was to remove the `ShardCoordinator` as a single point of bottleneck during `remember-entities=on` recovery - in Akka.NET v1.4 all remember-entity state is concentrated in the journal of the `PersistentShardCoordinator` or the CRDT used by the `DDataCoordinator`. In v1.5 the responsibility for remembering entities has been pushed to the `Shard` actors themselves, which allows for remembered-entities to be recovered in parallel for all shards.

Possible combinations:

state-store-mode | remember-entities-store | CoordinatorStore mode | ShardStore mode
------------------ | ------------------------- | ------------------------ | ------------------
persistence (default) | - (ignored) | persistence | persistence
ddata | ddata | ddata | ddata
ddata | eventsourced (new) | ddata | persistence

There should be no breaking changes from user perspective. Only some internal messages/objects were moved. There should be no change in the `PersistentId` behavior and default persistent configuration (`akka.cluster.sharding.state-store-mode`)

This change is designed to speed up the performance of Akka.Cluster.Sharding coordinator recovery by moving `remember-entities` recovery into separate actors - this also solves major performance problems with the `ddata` recovery mode overall.

The recommended settings for maximum ease-of-use for Akka.Cluster.Sharding in new applications going forward will be:

```hocon
akka.cluster.sharding{
  state-store-mode = ddata
  remember-entities-store = eventsourced
}
```

However, for the sake of backwards compatibility the Akka.Cluster.Sharding defaults have been left as-is:

```hocon
akka.cluster.sharding{
  state-store-mode = persistence
  # remember-entities-store (not set - also uses legacy Akka.Persistence)
}
```

#### Migrating to New Sharding Storage from Akka.Persistence

> [!NOTE]
> This section applies only to users who were using `akka.cluster.sharding.state-store-mode = persistence`. If you were using `akka.cluster.sharding.state-store-mode`

Switching over to using `remember-entities-store = eventsourced` will cause an initial migration of data from the `ShardCoordinator`'s journal into separate event journals going forward.

Upgrading to Akka.NET v1.5 will **cause an irreversible migration of Akka.Cluster.Sharding data** for users who were previously running `akka.cluster.state-store-mode=persistence`, so follow the steps below carefully:

> [!IMPORTANT]
> This migration is intended to be performed via upgrading Akka.NET to v1.5 and applying HOCON configuration changes - it requires no downtime.

##### Step 1 - Upgrade to Akka.NET v1.5 with Updated Persistence HOCON

Update your Akka.Cluster.Sharding HOCON to look like the following (adjust as necessary for your custom settings):

```hocon
akka.cluster.sharding {
    remember-entities = on
    remember-entities-store = "eventsourced"
    state-store-mode = "persistence"

    # fail if upgrade doesn't succeed
    fail-on-invalid-entity-state-transition = on
}

 akka.persistence.journal.{your-journal-implementation} {
    event-adapters {
        coordinator-migration = ""Akka.Cluster.Sharding.OldCoordinatorStateMigrationEventAdapter, Akka.Cluster.Sharding""
    }

    event-adapter-bindings {
        ""Akka.Cluster.Sharding.ShardCoordinator+IDomainEvent, Akka.Cluster.Sharding"" = coordinator-migration
    }
}
```

> [!NOTE]
> If you don't run Akka.Cluster.Sharding with `remember-entities=on` normally then _there is no need to turn it on here_.

With these HOCON settings in-place the following will happen:

1. The old `PersitentShardCoordinator` state will be broken up - `remember-entities=on` data will be distributed to each of the `PersistentShard` actors, who will now use the new `remember-entities-store = "eventsourced"` setting going forward;
2. Old `Akka.Cluster.Sharding.ShardCoordinator+IDomainEvent` will be upgraded to a new storage format via the `coordinator-migration` Akka.Persistence event adapter; and
3. The `PersistentShardCoordinator` will migrate its journal to the new format as well.

##### Step 2 - Migrating Away from Persistence to DData

Once your cluster has successfully booted up with these settings, you can now optionally move to using `DData` as your `akka.cluster.sharding.state-store-mode` by deploying a second time with the following HOCON:

```hocon
akka.cluster.sharding {
    remember-entities = on
    remember-entities-store = "eventsourced"
    state-store-mode = "ddata"

    # fail if upgrade doesn't succeed
    fail-on-invalid-entity-state-transition = on
}

 akka.persistence.journal.{your-journal-implementation} {
    event-adapters {
        coordinator-migration = ""Akka.Cluster.Sharding.OldCoordinatorStateMigrationEventAdapter, Akka.Cluster.Sharding""
    }

    event-adapter-bindings {
        ""Akka.Cluster.Sharding.ShardCoordinator+IDomainEvent, Akka.Cluster.Sharding"" = coordinator-migration
    }
}
```

Now you'll be running Akka.Cluster.Sharding with the recommended settings.

#### Migrating to New Sharding Storage from Akka.DistributedData

The migration process onto Akka.NET v1.5's new Cluster.Sharding storage system is less involved for users who were already using `akka.cluster.sharding.state-store-mode=ddata`.

All these users need to do this:

1. Setup an `akka.persistence.journal` and `akka.persistence.snapshot-store` to use with `akka.cluster.sharding.remember-entities-store = eventsourced`;
2. Deploy using the following HOCON:

```hocon
akka.cluster.sharding {
    remember-entities = on
    remember-entities-store = "eventsourced"
    state-store-mode = "ddata"

    # fail if upgrade doesn't succeed
    fail-on-invalid-entity-state-transition = on
}

 akka.persistence.journal.{your-journal-implementation} {
    event-adapters {
        coordinator-migration = ""Akka.Cluster.Sharding.OldCoordinatorStateMigrationEventAdapter, Akka.Cluster.Sharding""
    }

    event-adapter-bindings {
        ""Akka.Cluster.Sharding.ShardCoordinator+IDomainEvent, Akka.Cluster.Sharding"" = coordinator-migration
    }
}
```

If you run into any trouble upgrading, [please file an issue with Akka.NET](https://github.com/akkadotnet/akka.net/issues/new/choose).

### Akka.Hosting

We want to make Akka.NET something that can be instantiated more typically per the patterns often used with the Microsoft.Extensions.Hosting APIs that are common throughout .NET.

```csharp
using Akka.Hosting;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Cluster.Hosting;
using Akka.Remote.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .WithRemoting("localhost", 8110)
        .WithClustering(new ClusterOptions(){ Roles = new[]{ "myRole" }, 
            SeedNodes = new[]{ Address.Parse("akka.tcp://MyActorSystem@localhost:8110")}})
        .WithActors((system, registry) =>
    {
        var echo = system.ActorOf(act =>
        {
            act.ReceiveAny((o, context) =>
            {
                context.Sender.Tell($"{context.Self} rcv {o}");
            });
        }, "echo");
        registry.TryRegister<Echo>(echo); // register for DI
    });
});

var app = builder.Build();

app.MapGet("/", async (context) =>
{
    var echo = context.RequestServices.GetRequiredService<ActorRegistry>().Get<Echo>();
    var body = await echo.Ask<string>(context.TraceIdentifier, context.RequestAborted).ConfigureAwait(false);
    await context.Response.WriteAsync(body);
});

app.Run();
```

No HOCON. Automatically runs all Akka.NET application lifecycle best practices behind the scene. Automatically binds the `ActorSystem` and the `ActorRegistry`, another new 1.5 feature, to the `IServiceCollection` so they can be safely consumed via both actors and non-Akka.NET parts of users' .NET applications.

This should be open to extension in other child plugins, such as Akka.Persistence.SqlServer:

```csharp
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .WithRemoting("localhost", 8110)
        .WithClustering(new ClusterOptions()
        {
            Roles = new[] { "myRole" },
            SeedNodes = new[] { Address.Parse("akka.tcp://MyActorSystem@localhost:8110") }
        })
        .WithSqlServerPersistence(builder.Configuration.GetConnectionString("sqlServerLocal"))
        .WithShardRegion<UserActionsEntity>("userActions", s => UserActionsEntity.Props(s),
            new UserMessageExtractor(),
            new ShardOptions(){ StateStoreMode = StateStoreMode.DData, Role = "myRole"})
        .WithActors((system, registry) =>
        {
            var userActionsShard = registry.Get<UserActionsEntity>();
            var indexer = system.ActorOf(Props.Create(() => new Indexer(userActionsShard)), "index");
            registry.TryRegister<Index>(indexer); // register for DI
        });
})
```

#### `ActorRegistry`

As part of Akka.Hosting, we need to provide a means of making it easy to pass around top-level `IActorRef`s via dependency injection both within the `ActorSystem` and outside of it.

The `ActorRegistry` will fulfill this role through a set of generic, typed methods that make storage and retrieval of long-lived `IActorRef`s easy and coherent:

```csharp
var registry = ActorRegistry.For(myActorSystem); // fetch from ActorSystem
registry.TryRegister<Index>(indexer); // register for DI
registry.Get<Index>(); // use in DI
```
