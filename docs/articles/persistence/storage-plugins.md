---
uid: storage-plugins
title: Storage plugins
---
# Storage plugins

## Journals

Journal is a specialized type of actor which exposes an API to handle incoming events and store them in backend storage. By default Akka.Persistence uses a `MemoryJournal` which stores all events in memory and therefore it's not persistent storage. A custom journal configuration path may be specified inside *akka.persistence.journal.plugin* path and by default it requires two keys set: *class* and *plugin-dispatcher*. Example configuration:

[!code-json[Main](../../../src/core/Akka.Persistence/persistence.conf#L196-L202)]

## Snapshot store

Snapshot store is a specialized type of actor which exposes an API to handle incoming snapshot-related requests and is able to save snapshots in some backend storage. By default Akka.Persistence uses a `LocalSnapshotStore`, which uses a local file system as storage. A custom snapshot store configuration path may be specified inside *akka.persistence.snapshot-store.plugin* path and by default it requires two keys set: *class* and *plugin-dispatcher*. Example configuration:

[!code-json[Main](../../../src/core/Akka.Persistence/persistence.conf#L204-L219)]

