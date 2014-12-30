#   Akka.Persistence

## Architecture

-   **PersistentActor**: Is a persistent, stateful actor. It is able to persist events to a journal and can react to them in a thread-safe manner. It can be used to implement both command as well as event sourced actors. When a persistent actor is started or restarted, journaled messages are replayed to that actor, so that it can recover internal state from these messages.
-   **PersistentView**: A view is a persistent, stateful actor that receives journaled messages that have been written by another persistent actor. A view itself does not journal new messages, instead, it updates internal state only from a persistent actor's replicated message stream.
-   **GuaranteedDelivery**: To send messages with at-least-once delivery semantics to destinations, also in case of sender and receiver virtual machine crashes.
-   **Journal**: A journal stores the sequence of messages sent to a persistent actor. An application can control which messages are journaled and which are received by the persistent actor without being journaled. The storage backend of a journal is pluggable. The default journal storage plugin writes to the operating system's memory, replicated journals are available as Community plugins.
-   **SnapshotStore**: A snapshot store persists snapshots of a persistent actor's or a view's internal state. Snapshots are used for optimizing recovery times. The storage backend of a snapshot store is pluggable. The default snapshot storage plugin writes to the local filesystem.

## Technical Overview

*TODO: describe recovery cycle for PersistentViews and Eventsourced FSMs*