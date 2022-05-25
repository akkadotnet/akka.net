---
uid: custom-persistent-provider
title: Writing A Custom Persistent Provider
---

# Writing A Custom Akka.Persistence Provider

For an introduction to event sourcing, you can read this [article](https://docs.microsoft.com/en-us/previous-versions/msp-n-p/jj591559%28v=pandp.10%29) at MSDN.

Persistence or event source provider in Akka.NET are divided into two plugins, the journal and snapshot store plugins, each with their own API and responsibility. The goal of `Akka.Persistence` event sourcing plugin is to provide a consistent event sourcing API that provides an abstraction layer over the underlying storage mechanism.

## Implementing Akka.Persistence Journal

Akka.Persistence journal is responsible for storing all events for playback in the event of a recovery.

All of the code examples in this documentation will assume a SQLite database. You can view the complete sample project in `/src/examples/Akka.Persistence.Custom`.

The `Journal` table schemas we will be using are:

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

This call is **NOT** protected with a circuit-breaker.

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

This call is protected with a circuit-breaker.

This method should asynchronously reads the highest stored sequence number for provided `persistenceId`. The persistent actor will use the highest sequence number after recovery as the starting point when persisting new events. This sequence number is also used as `toSequenceNr` in subsequent calls to `ReplayMessagesAsync` unless the user has specified a lower `toSequenceNr`. Journal must maintain the highest sequence number and never decrease it.

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

Each `AtomicWrite` message contains a `Payload` property that contains:

* a single `IPersistentRepresentation` that corresponds to the event that was passed to the `Eventsourced.Persist<TEvent>(TEvent, Action<TEvent>)` method of the `PersistentActor`, or
* it contains several `IPersistentRepresentation` that correspond to the events that were passed to the `Eventsourced.PersistAll<TEvent>(IEnumerable<TEvent>, Action<TEvent>)` method of the `PersistentActor`.

Note that while `AtomicWrite.Payload` is declared as an object property, the type of this object instance will always be an `ImmutableList<IPersistentRepresentation>`.

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

* **`persistenceId`**: Persistent actor identifier
* **`toSequenceNr`**: Inclusive sequence number of the last event to be deleted

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

There are some HOCON settings that are by default loaded by the journal base class and these can be overriden in your HOCON settings. The minimum HOCON settings that need to be defined for your custom plugin are:

```hocon
akka.persistence.journal {
  plugin = "akka.persistence.journal.custom-sqlite"
  custom-sqlite {
    # qualified type name of the SQLite persistence journal actor
    class = "Akka.Persistence.Custom.Journal.SqliteJournal, Akka.Persistence.Custom"
  }
}
```

The default HOCON settings are:

```hocon
# Fallback settings for journal plugin configurations.
# These settings are used if they are not defined in plugin config section.
akka.persistence.journal-plugin-fallback {
  # Fully qualified class name providing journal plugin api implementation.
  # It is mandatory to specify this property.
  # The class must have a constructor without parameters or constructor with
  # one `Akka.Configuration.Config` parameter.
  class = ""

  # Dispatcher for the plugin actor.
  plugin-dispatcher = "akka.persistence.dispatchers.default-plugin-dispatcher"

  # Dispatcher for message replay.
  replay-dispatcher = "akka.persistence.dispatchers.default-replay-dispatcher"

  # Default serializer used as manifest serializer when applicable 
  # and payload serializer when no specific binding overrides are specified
  serializer = "json"

  # Removed: used to be the Maximum size of a persistent message batch 
  # written to the journal.
  # Now this setting is without function, PersistentActor will write 
  # as many messages as it has accumulated since the last write.
  max-message-batch-size = 200

  # If there is more time in between individual events gotten from the Journal
  # recovery than this the recovery will fail.
  # Note that it also affect reading the snapshot before replaying events on
  # top of it, even though iti is configured for the journal.
  recovery-event-timeout = 30s

  circuit-breaker {
    max-failures = 10
    call-timeout = 10s
    reset-timeout = 30s
  }

  # The replay filter can detect a corrupt event stream by inspecting
  # sequence numbers and writerUuid when replaying events.
  replay-filter {
    # What the filter should do when detecting invalid events.
    # Supported values:
    # `repair-by-discard-old` : discard events from old writers,
    #                           warning is logged
    # `fail` : fail the replay, error is logged
    # `warn` : log warning but emit events untouche
    # `off` : disable this feature completely
    mode = repair-by-discard-old

    # It uses a look ahead buffer for analyzing the events.
    # This defines the size (in number of events) of the buffer.
    window-size = 100

    # How many old writerUuid to remember
    max-old-writers = 10

    # Set this to `on` to enable detailed debug logging of each
    # replayed event.
    debug = off
  }
}
```

#### Pre-Start Requirement

It is a good practice to allow user of your custom plugin to be able to auto initialize their back end event source from a blank slate. In order to do that, it is good practice to support `auto-initialize` setting in your journal HOCON settings.

In order to support this, we will have to be able to stash all incoming messages while we're creating the tables in the event source. Here's an implementation that we use in our example code:  

[!code-csharp[Startup](../../../src/examples/Akka.Persistence.Custom/Journal/SqliteJournal.cs?name=Startup "Journal startup sequence")]

## Implementing Akka.Persistence SnapshotStore

### Required Method Implementations

#### LoadAsync

```c#
Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
```

* **`persistenceId`**: Identification of the persistent actor
* **`criteria`**: Selection criteria for event loading

This call is protected with a circuit-breaker.

Asynchronously loads a snapshot.

##### Code Sample

`SelectSnapshotSql` in this example refers to this SQL query statement:

[!code-csharp[SelectSnapshotSql](../../../src/examples/Akka.Persistence.Custom/Snapshot/SqliteSnapshotStore.cs?name=SelectSnapshotSql "SelectSnapshotSql SQL Statement")]

SQLite code example:

[!code-csharp[LoadAsync](../../../src/examples/Akka.Persistence.Custom/Snapshot/SqliteSnapshotStore.cs?name=LoadAsync "LoadAsync method implementation")]

#### SaveAsync

```c#
Task SaveAsync(SnapshotMetadata metadata, object snapshot)
```

* **`metadata`**: Snapshot metadata
* **`snapshot`**: The snapshot object instance to be stored

This call is protected with a circuit-breaker

Asynchronously saves a snapshot.

##### Code Sample

`InsertSnapshotSql` in this example refers to this SQL query statement:

[!code-csharp[InsertSnapshotSql](../../../src/examples/Akka.Persistence.Custom/Snapshot/SqliteSnapshotStore.cs?name=InsertSnapshotSql "InsertSnapshotSql SQL Statement")]

SQLite code example:

[!code-csharp[SaveAsync](../../../src/examples/Akka.Persistence.Custom/Snapshot/SqliteSnapshotStore.cs?name=SaveAsync "SaveAsync method implementation")]

#### DeleteAsync

```c#
Task DeleteAsync(SnapshotMetadata metadata);
```

* **`metadata`**: Snapshot metadata

This call is protected with a circuit-breaker

Deletes the snapshot identified by by the provided `Metadata`

##### Code Sample

`DeleteSnapshotSql` in this example refers to this SQL query statement:

[!code-csharp[DeleteSnapshotSql](../../../src/examples/Akka.Persistence.Custom/Snapshot/SqliteSnapshotStore.cs?name=DeleteSnapshotSql "DeleteSnapshotSql SQL Statement")]

`DeleteSnapshotRangeSql` in this example refers to this SQL query statement:

[!code-csharp[DeleteSnapshotRangeSql](../../../src/examples/Akka.Persistence.Custom/Snapshot/SqliteSnapshotStore.cs?name=DeleteSnapshotRangeSql "DeleteSnapshotRangeSql SQL Statement")]

SQLite code example:

[!code-csharp[DeleteAsync](../../../src/examples/Akka.Persistence.Custom/Snapshot/SqliteSnapshotStore.cs?name=DeleteAsync "DeleteAsync method implementation")]

#### DeleteAsync

```c#
Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria);
```

* **`persistenceId`**: Identification of the persistent actor
* **`criteria`**: Selection criteria for event deletion

This call is protected with a circuit-breaker

Deletes all snapshots matching the provided `criteria`

##### Code Sample

`DeleteSnapshotRangeSql` in this example refers to this SQL query statement:

[!code-csharp[DeleteSnapshotRangeSql](../../../src/examples/Akka.Persistence.Custom/Snapshot/SqliteSnapshotStore.cs?name=DeleteSnapshotRangeSql "DeleteSnapshotRangeSql SQL Statement")]

SQLite code example:

[!code-csharp[DeleteAsync2](../../../src/examples/Akka.Persistence.Custom/Snapshot/SqliteSnapshotStore.cs?name=DeleteAsync2 "DeleteAsync method implementation")]

### Detail Implementation

#### Reading HOCON Settings

There are some HOCON settings that are by default loaded by the snapshot store base class and these can be overriden in your HOCON settings. The minimum HOCON settings that need to be defined for your custom plugin are:

```hocon
akka.persistence{
  snapshot-store {
    plugin = "akka.persistence.snapshot-store.custom-sqlite"
    custom-sqlite {
      # qualified type name of the SQLite persistence journal actor
      class = "Akka.Persistence.Custom.Snapshot.SqliteSnapshotStore, Akka.Persistence.Custom"
    }
  }
}
```

The default HOCON settings are:

```hocon
# Fallback settings for snapshot store plugin configurations
# These settings are used if they are not defined in plugin config section.
akka.persistence.snapshot-store-plugin-fallback {
  # Fully qualified class name providing snapshot store plugin api
  # implementation. It is mandatory to specify this property if
  # snapshot store is enabled.
  # The class must have a constructor without parameters or constructor with
  # one `Akka.Configuration.Config` parameter.
  class = ""

  # Dispatcher for the plugin actor.
  plugin-dispatcher = "akka.persistence.dispatchers.default-plugin-dispatcher"

  # Default serializer used as manifest serializer when applicable 
  # and payload serializer when no specific binding overrides are specified
  serializer = "json"

  circuit-breaker {
    max-failures = 5
    call-timeout = 20s
    reset-timeout = 60s
  }
}
```

#### Pre-Start Requirement

It is a good practice to allow user of your custom plugin to be able to auto initialize their back end event source from a blank slate. In order to do that, it is good practice to support `auto-initialize` setting in your journal HOCON settings.

In order to support this, we will have to be able to stash all incoming messages while we're creating the tables in the event source. Here's an implementation that we use in our example code:

[!code-csharp[Startup](../../../src/examples/Akka.Persistence.Custom/Snapshot/SqliteSnapshotStore.cs?name=Startup "SnapshotStore startup sequence")]

## Tying Everything Together Into An Akka.NET Extension

The last thing you need to do to make the new custom persistence provider to work is to create an Akka.NET extension that wraps the journal and snapshot storage so that it can be seamlessly used from inside Akka.NET.

In order to do this, you will need to extend two things, `IExtension` and `ExtensionIdProvider<T>`.

### Extending `IExtension`

`IExtension` is a marker interface that marks a class as an Akka.NET extension. It is the class instance that all user will use to interact with your custom extension.

There are two conventions that needs to be implemented when you extend `IExtension`:

* Any class extending this interface is **required** to provide a single constructor that takes a single `ExtendedActorSystem` as its argument.

[!code-csharp[Constructor](../../../src/examples/Akka.Persistence.Custom/SqlitePersistence.cs?name=Constructor "Extension constructor")]

* It is strongly recommended to create a static `Get` method that returns an instance of this class. This method is responsible for registering the extension with the Akka.NET extension manager and instantiates a new instance for users to use.

[!code-csharp[GetInstance](../../../src/examples/Akka.Persistence.Custom/SqlitePersistence.cs?name=GetInstance "Extension static Get method")]

### Extending `ExtensionIdProvider<T>`

`ExtensionIdProvider<T>` is a factory class that is responsible for creating new instances for your extension. This class is quite straight forward, all you need to do is to override a single method to make it work:

[!code-csharp[ExtensionIdProvider](../../../src/examples/Akka.Persistence.Custom/SqlitePersistence.cs?name=ExtensionIdProvider "ExtensionIdProvider implementation")]

## Unit Testing Journal and SnapshotStore

Akka.Persistence came with a standardized Technology Compatibility Kit (TCK) test kit that can be readily incorporated into your unit testing suite to test that a custom provider adheres to a basic compatibility requirement.

To do this, you will need to create an XUnit unit test project and reference your custom persistence project and the `Akka.Persistence.TCK` package. There are four abstract classes that can be implemented to test your persistence implementation:

* JournalSerializationSpec
* JournalSpec
* SnapshotStoreSerializationSpec
* SnapshotStoreSpec

For an implementation sample, please see the `/src/examples/Akka.Persistence.Custom.Tests` example project.
