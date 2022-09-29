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
> This section applies only to users who were using `akka.cluster.sharding.state-store-mode = persistence`. If you were using `akka.cluster.sharding.state-store-mode`

Switching over to using `remember-entities-store = eventsourced` will cause an initial migration of data from the `ShardCoordinator`'s journal into separate event journals going forward.

Upgrading to Akka.NET v1.5 will **cause an irreversible migration of Akka.Cluster.Sharding data** for users who were previously running `akka.cluster.state-store-mode=persistence`, so follow the steps below carefully:

> [!IMPORTANT]
> This migration is intended to be performed via upgrading Akka.NET to v1.5 and applying HOCON configuration changes - it requires no downtime.

##### Step 1 - Upgrade to Akka.NET v1.5 With Updated Persistence HOCON

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

##### Step 2 - Migrating Away From Persistence to DData

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

#### Migrating to New Sharding Storage From Akka.DistributedData

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
