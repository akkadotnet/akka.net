---
uid: storage-plugins
title: Storage plugins
---
# Storage plugins

## Journals

Journal is a specialized type of actor which exposes an API to handle incoming events and store them in backend storage. By default Akka.Persistence uses a `MemoryJournal` which stores all events in memory and therefore it's not persistent storage. A custom journal configuration path may be specified inside *akka.persistence.journal.plugin* path and by default it requires two keys set: *class* and *plugin-dispatcher*. Example configuration:

[!code-json[Main](../../../src/core/Akka.Persistence/persistence.conf#L201-L207)]

## Snapshot store

Snapshot store is a specialized type of actor which exposes an API to handle incoming snapshot-related requests and is able to save snapshots in some backend storage. By default Akka.Persistence uses a `LocalSnapshotStore`, which uses a local file system as storage. A custom snapshot store configuration path may be specified inside *akka.persistence.snapshot-store.plugin* path and by default it requires two keys set: *class* and *plugin-dispatcher*. Example configuration:

[!code-json[Main](../../../src/core/Akka.Persistence/persistence.conf#L209-L215)]

### Eager initialization of persistence plugin

By default, persistence plugins are started on-demand, as they are used. In some case, however, it might be beneficial to start a certain plugin eagerly. In order to do that, specify the IDs of plugins you wish to start automatically under `akka.persistence.journal.auto-start-journals` and `akka.persistence.snapshot-store.auto-start-snapshot-stores`.

For example, if you want eager initialization for the sqlite journal and snapshot store plugin, your configuration should look like this:  

```
akka {
  persistence {
    journal {
      plugin = "akka.persistence.journal.sqlite"
      auto-start-journals = ["akka.persistence.journal.sqlite"]
    }
    snapshot-store {
      plugin = "akka.persistence.snapshot-store.sqlite"
      auto-start-snapshot-stores = ["akka.persistence.snapshot-store.sqlite"]
    }
  }
}
```
