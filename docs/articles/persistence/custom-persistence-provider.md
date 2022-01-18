---
uid: custom-persistent-provider
title: Writing A Custom Persistent Provider
---

# Writing A Custom Akka.Persistence Provider

## Implementing Akka.Persistence Journal

* TODO: Discuss design goals
* TODO: Discuss Journal responsibility

Akka.Persistence journal is responsible for storing all events for playback in the event of a recovery.

All of the code examples in this documentation will assume a SQLite database and the `Journal` table schemas we will be using are:

[!code-csharp[Schema](../../../src/examples/Akka.Persistence.Custom/Journal/SqliteJournal.cs?name=schema "Journal table schemas")]

### Required Method Implementations

#### ReplayMessagesAsync

```c#
Task ReplayMessagesAsync(
    IActorContext context, 
    string persistenceId, 
    long fromSequenceNr, 
    long toSequenceNr, 
    long max, 
    Action<IPersistentRepresentation> recoveryCallback)
```

* **`context`**: The contextual information about the actor processing the replayed messages.
* **`persistenceId`**: Persistent actor identifier
* **`fromSequenceNr`**: Inclusive sequence number where replay should start
* **`toSequenceNr`**: Inclusive sequence number where replay should end
* **`max`**: Maximum number of messages to be replayed
* **`recoveryCallback`**: Called to replay a message, may be called from any thread.

This call is __NOT__ protected with a circuit-breaker.

This method should asynchronously replay persistent messages:

* Implementations replay a message by calling `recoveryCallback`.
* The returned `Task` must be completed when all messages (matching the sequence number bounds) have been replayed.
* The `Task` must be completed with a failure if any of the persistent messages could not be replayed.

The `toSequenceNr` is the lowest of what was returned by `ReadHighestSequenceNrAsync` and what the user specified as the recovery `Recovery` parameter. This does imply that this call is always preceded by reading the highest sequence number for the given `persistenceId`.

This call is NOT protected with a circuit-breaker because it may take a long time to replay all events. The plugin implementation itself must protect against an unresponsive backend store and make sure that the returned `Task` is completed with success or failure within reasonable time. It is not allowed to ignore completing the `Task`.

##### Code Sample

`ByPersistenceIdSql` in this example refers to this SQL query statement:

[!code-csharp[ByPersistenceIdSql](../../../src/examples/Akka.Persistence.Custom/Journal/SqliteJournal.cs?name=ByPersistenceIdSql "ByPersistenceIdSql SQL Statement")]

SQLite code example:

[!code-csharp[ReplayMessagesAsync](../../../src/examples/Akka.Persistence.Custom/Journal/SqliteJournal.cs?name=ReplayMessagesAsync "ReplayMessagesAsync method implementation")]

#### ReadHighestSequenceNrAsync

```c#
Task<long> ReadHighestSequenceNrAsync(
    string persistenceId, 
    long fromSequenceNr)
```

* **`persistenceId`**: Persistent actor identifier
* **`fromSequenceNr`**: Hint where to start searching for the highest sequence number. When a persistent actor is recovering this `fromSequenceNr` will the sequence number of the used snapshot, or `0L` if no snapshot is used.

This method should asynchronously reads the highest stored sequence number for provided `persistenceId`. The persistent actor will use the highest sequence number after recovery as the starting point when persisting new events. This sequence number is also used as `toSequenceNr` in subsequent calls to `ReplayMessagesAsync` unless the user has specified a lower `toSequenceNr`. Journal must maintain the highest sequence number and never decrease it.

This call is protected with a circuit-breaker.

Please also note that requests for the highest sequence number may be made concurrently to writes executing for the same `persistenceId`, in particular it is possible that a restarting actor tries to recover before its outstanding writes have completed.

`HighestSequenceNrSql` in this example refers to this SQL query statement:

[!code-csharp[HighestSequenceNrSql](../../../src/examples/Akka.Persistence.Custom/Journal/SqliteJournal.cs?name=HighestSequenceNrSql "HighestSequenceNrSql SQL Statement")]

SQLite code example:

[!code-csharp[ReadHighestSequenceNrAsync](../../../src/examples/Akka.Persistence.Custom/Journal/SqliteJournal.cs?name=ReadHighestSequenceNrAsync "ReadHighestSequenceNrAsync method implementation")]

#### WriteMessagesAsync

```c#
protected abstract Task<IImmutableList<Exception>> WriteMessagesAsync(
    IEnumerable<AtomicWrite> messages)
```

Asynchronously writes a batch of persistent messages to the journal.

This call is protected with a circuit-breaker.

Calls to this method are serialized by the enclosing journal actor. If you spawn work in asynchronous tasks it is alright that they complete the futures in any order, but the actual writes for a specific persistenceId should be serialized to avoid issues such as events of a later write are visible to consumers (query side, or replay) before the events of an earlier write are visible. A `PersistentActor` will not send a new `WriteMessages` request before the previous one has been completed.

Please note that the `IPersistentRepresentation.Sender` of the contained `IPersistentRepresentation` objects has been set to null (i.e. set to `ActorRefs.NoSender`) in order to save space in the journal and to prevent saving a sender reference that will likely be obsolete during replay.

Please also note that requests for the highest sequence number may be made concurrently to this call executing for the same `persistenceId`, in particular it is possible that a restarting actor tries to recover before its outstanding writes have completed. In the latter case it is highly desirable to defer reading the highest sequence number until all outstanding writes have completed, otherwise the `PersistentActor` may reuse sequence numbers.

##### Batching

The batch is only for performance reasons, i.e. all messages don't have to be written atomically. Higher throughput can typically be achieved by using batch inserts of many records compared to inserting records one-by-one, but this aspect depends on the underlying data store and a journal implementation can implement it as efficiently as possible.

Journals should aim to persist events in-order for a given `persistenceId` as otherwise in case of a failure, the persistent state may end up being inconsistent.

##### AtomicWrite

Each `AtomicWrite` message contains:

* a single `IPersistentRepresentation` that corresponds to the event that was passed to the `Eventsourced.Persist<TEvent>(TEvent, Action<TEvent>)` method of the `PersistentActor`, or 
* it contains several `IPersistentRepresentation` that correspond to the events that were passed to the `Eventsourced.PersistAll<TEvent>(IEnumerable<TEvent>, Action<TEvent>)` method of the `PersistentActor`.

All `Persistent` of the `AtomicWrite` must be written to the data store atomically, i.e. all or none must be stored. 

If the journal (data store) cannot support atomic writes of multiple events it should reject such writes with a `NotSupportedException` describing the issue. This limitation should also be documented by the journal plugin.

##### Handling Failures

If there are failures when storing any of the messages in the batch the returned `Task` must be completed with failure. The `Task` must only be completed with success when all messages in the batch have been confirmed to be stored successfully, i.e. they will be readable, and visible, in a subsequent replay. If there is uncertainty about if the messages were stored or not the `Task` must be completed with failure.

Data store connection problems must be signaled by completing the `Task` with failure.

##### Handling `AtomicWrite` Result

The journal can also signal that it rejects individual messages (`AtomicWrite`) by the returned `Task`.

* It is possible but not mandatory to reduce the number of allocations by returning null for the happy path, i.e. when no messages are rejected.
* Otherwise the returned list must have as many elements as the input `messages`.
* Each result element signals if the corresponding `AtomicWrite` is rejected or not, with an exception describing the problem.
* Rejecting a message means it was not stored, i.e. it must not be included in a later replay.
* Rejecting a message is typically done before attempting to store it, e.g. because of serialization error.
* Data store connection problems must not be signaled as rejections.

##### Code Sample

`InsertEventSql` in this example refers to this SQL query statement:

[!code-csharp[InsertEventSql](../../../src/examples/Akka.Persistence.Custom/Journal/SqliteJournal.cs?name=InsertEventSql "InsertEventSql SQL Statement")]

SQLite code example:

[!code-csharp[WriteMessagesAsync](../../../src/examples/Akka.Persistence.Custom/Journal/SqliteJournal.cs?name=WriteMessagesAsync "WriteMessagesAsync method implementation")]

#### DeleteMessagesToAsync

```c#
protected abstract Task DeleteMessagesToAsync(
    string persistenceId, 
    long toSequenceNr)
```

* **`persistenceId`**: TODO: Add this
* **`toSequenceNr`**: TODO: Add this

This call is protected with a circuit-breaker.

Asynchronously deletes all persistent messages up to inclusive `toSequenceNr` bound. This operation has to be done atomically.

##### Code Sample

`DeleteBatchSql` in this example refers to this SQL query statement:

[!code-csharp[DeleteBatchSql](../../../src/examples/Akka.Persistence.Custom/Journal/SqliteJournal.cs?name=DeleteBatchSql "DeleteBatchSql SQL Statement")]

`HighestSequenceNrSql` in this example refers to this SQL query statement:

[!code-csharp[HighestSequenceNrSql](../../../src/examples/Akka.Persistence.Custom/Journal/SqliteJournal.cs?name=HighestSequenceNrSql "HighestSequenceNrSql SQL Statement")]

`UpdateSequenceNrSql` in this example refers to this SQL query statement:

[!code-csharp[UpdateSequenceNrSql](../../../src/examples/Akka.Persistence.Custom/Journal/SqliteJournal.cs?name=UpdateSequenceNrSql "UpdateSequenceNrSql SQL Statement")]

SQLite code example:

[!code-csharp[DeleteMessagesToAsync](../../../src/examples/Akka.Persistence.Custom/Journal/SqliteJournal.cs?name=DeleteMessagesToAsync "DeleteMessagesToAsync method implementation")]

### Detail Implementation

#### Reading HOCON Settings

#### Pre-Start Requirement

* TODO: Making sure all messages are processed during start-up
* TODO: Discuss delay during database connection
* TODO: Discuss stashing and initialization steps

#### Creating And Processing Custom Commands

* TODO: Discuss ReceivePluginInternal and IJournalRequest

#### Best Practices

## Implementing Akka.Persistence SnapshotStore

### Required Method Implementations

#### LoadAsync

* TODO: Discuss what this method is for

#### SaveAsync

* TODO: Discuss what this method is for

#### DeleteAsync

* TODO: Discuss what this method is for

#### DeleteAsync

* TODO: Discuss what this method is for

### Detail Implementation

* TODO: This should be very similar to Journal

## Unit Testing Journal and SnapshotStore
