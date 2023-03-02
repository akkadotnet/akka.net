---
uid: akkadotnet-v15-upgrade-advisories
title: Akka.NET v1.5 Upgrade Advisories
---

# Akka.NET v1.5 Upgrade Advisories

This document contains specific upgrade suggestions, warnings, and notices that you will want to pay attention to when upgrading between versions within the Akka.NET v1.5 roadmap.

## Upgrading From Akka.NET v1.4 to v1.5

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

#### Migrating to New Sharding Storage From Akka.Persistence

> [!NOTE]
> This section applies to users who are using `remember-entities=on` and want to migrate to using the low-latency event-sourced based storage. All other users should just migrate to `state-store-mode=ddata`.

Switching over to using `remember-entities-store = eventsourced` will cause an initial migration of data from the `ShardCoordinator`'s journal into separate event journals going forward.

Upgrading to Akka.NET v1.5 will **cause an irreversible migration of Akka.Cluster.Sharding data** for users who were previously running `akka.cluster.state-store-mode=persistence`, so follow the steps below carefully:

> [!IMPORTANT]
> This migration is intended to be performed via upgrading Akka.NET to v1.5 and applying the recommended configuration changes below - **it will require a full restart of your cluster any time you change the `state-store-mode` setting**.

#### Upgrade to Akka.NET v1.5 Sharding

Update your Akka.Cluster.Sharding HOCON to look like the following (adjust as necessary for your custom settings):

```hocon
akka.cluster.sharding {
    remember-entities = on
    remember-entities-store = eventsourced
    state-store-mode = ddata

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

To deploy this upgrade:

1. Take your cluster offline and
2. Roll out the changes with the new version of Akka.NET installed and these HOCON changes.

It should less than 10 seconds to fully migrate over to the new format and the Akka.Cluster.Sharding system will continue to start normally while it takes place.

> [!NOTE]
> If you don't run Akka.Cluster.Sharding with `remember-entities=on` normally then _there is no need to turn it on here_.

With these HOCON settings in-place the following will happen:

1. The old `PersitentShardCoordinator` state will be broken up - `remember-entities=on` data will be distributed to each of the `PersistentShard` actors, who will now use the new `remember-entities-store = "eventsourced"` setting going forward;
2. Old `Akka.Cluster.Sharding.ShardCoordinator+IDomainEvent` will be upgraded to a new storage format via the `coordinator-migration` Akka.Persistence event adapter; and
3. No more data will be persisted by the `ShardCoordinator` - instead it will all be replicated on the fly by DData, which is vastly preferable.

> [!IMPORTANT]
> This migration is irreversible once completed.

If you run into any trouble upgrading, [please file an issue with Akka.NET](https://github.com/akkadotnet/akka.net/issues/new/choose).

### Breaking Logging Changes

In v1.5, we've re-engineering the `ILoggingAdapter` construct to be more extensible and performant. Unfortunately this necessitate some breaking changes that will affect end-user code - but the remedy for those changes is trivial.

After installing the v1.5 NuGet packages into your applications or libraries, you will need to add the following to all of your source files where you previously made calls to the `ILoggingAdapter`:

```csharp
using Akka.Event;
```

That `using` statement will pull in the extension methods that match all of the v1.4 API `ILoggingAdapter` signatures in v1.5.

_Even better_ - if you can take advantage of [`global using` statements in C#10](https://blog.jetbrains.com/dotnet/2021/11/18/global-usings-in-csharp-10/), then this is a one-liner as either the MSBuild or project level:

In `Directory.Build.props`:

```xml
<Project>
    <ItemGroup>
        <PackageReference Include="Akka" />
        <Using Include="Akka.Event" />
    </ItemGroup>
</Project>
```
