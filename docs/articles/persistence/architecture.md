---
uid: persistence-architecture
title: Persistence
---
# Persistence

Akka.Persistence plugin enables stateful actors to persist their internal state so that it can be recovered when an actor is started, restarted after a CLR crash or by a supervisor, or migrated in a cluster. The key concept behind Akka persistence is that only changes to an actor's internal state are persisted but never its current state directly (except for optional snapshots). These changes are only ever appended to storage, nothing is ever mutated, which allows for very high transaction rates and efficient replication. Stateful actors are recovered by replaying stored changes to these actors from which they can rebuild internal state. This can be either the full history of changes or starting from a snapshot which can dramatically reduce recovery times. Akka persistence also provides point-to-point communication with at-least-once message delivery semantics.

## Architecture

Akka.Persistence features are available through new set of actor base classes:

- `UntypedPersistentActor` - is a persistent, stateful actor. It is able to persist events to a journal and can react to them in a thread-safe manner. It can be used to implement both command as well as event sourced actors. When a persistent actor is started or restarted, journaled messages are replayed to that actor so that it can recover internal state from these messages.
- `PersistentView` is a persistent, stateful actor that receives journaled messages that have been written by another persistent actor. A view itself does not journal new messages, instead, it updates internal state only from a persistent actor's replicated message stream. Note: `PersistentView` is deprecated.
- `AtLeastOnceDeliveryActor` - is an actor which sends messages with at-least-once delivery semantics to destinations, also in case of sender and receiver CLR crashes.
- `AsyncWriteJournal` stores the sequence of messages sent to a persistent actor. An application can control which messages are journaled and which are received by the persistent actor without being journaled. Journal maintains highestSequenceNr that is increased on each message. The storage backend of a journal is pluggable. By default it uses an in-memory message stream and is NOT a persistent storage.
- `SnapshotStore` is used to persist snapshots of either persistent actor's or view's internal state. They can be used to reduce recovery times in case when a lot of events needs to be replayed for specific persistent actor. Storage backend of the snapshot store is pluggable. By default it uses local file system.

Receive Actors
- `ReceivePersistentActor` - receive actor version of `UntypedPersistentActor`.
- `AtLeastOnceDeliveryReceiveActor` - receive actor version of `AtLeastOnceDeliveryActor`.

## Persistent Actors

Unlike the default `UntypedActor` class, `UntypedPersistentActor` and its derivatives requires the setup of a few more additional members:

- `PersistenceId` is a persistent actor's identifier that doesn't change across different actor incarnations. It's used to retrieve an event stream required by the persistent actor to recover its internal state.
- `OnRecover` is a method invoked during an actor's recovery cycle. Incoming objects may be user-defined events as well as system messages, for example `SnapshotOffer` which is used to deliver latest actor state saved in the snapshot store.
- `OnCommand` is an equivalent of the basic `Receive` method of default Akka.NET actors.

Persistent actors also offer a set of specialized members:

- `Persist` and `PersistAsync` methods can be used to send events to the event journal in order to store them inside. The second argument is a callback invoked when the journal confirms that events have been stored successfully.
- `DeferAsync` is used to perform various operations *after* events will be persisted and their callback handlers will be invoked. Unlike the persist methods, defer won't store an event in persistent storage. Defer method may NOT be invoked in case when the actor is restarted even though the journal will successfully persist events sent.
- `DeleteMessages` will order attached journal to remove part of its events. It can be only physical deletion, when the messages are removed physically from the journal.
- `LoadSnapshot` will send a request to the snapshot store to resend the current actor's snapshot.
- `SaveSnapshot` will send the current actor's internal state as a snapshot to be saved by the configured snapshot store.
- `DeleteSnapshot` and `DeleteSnapshots` methods may be used to specify snapshots to be removed from the snapshot store in cases where they are no longer needed.
- `OnReplaySuccess` is a virtual method which will be called when the recovery cycle ends successfully.
- `OnReplayFailure` is a virtual method which will be called when the recovery cycle fails unexpectedly from some reason.
- `IsRecovering` property determines if the current actor is performing a recovery cycle at the moment.
- `SnapshotSequenceNr` property may be used to determine the sequence number used for marking persisted events. This value changes in a monotonically increasing manner.

In case a manual recovery cycle initialization is necessary, it may be invoked by sending a `Recover` message to a persistent actor.