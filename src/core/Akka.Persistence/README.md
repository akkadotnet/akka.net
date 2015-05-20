#   Akka.Persistence

## Architecture

-   **PersistentActor**: Is a persistent, stateful actor. It is able to persist events to a journal and can react to them in a thread-safe manner. It can be used to implement both command as well as event sourced actors. When a persistent actor is started or restarted, journaled messages are replayed to that actor, so that it can recover internal state from these messages.
-   **PersistentView**: A view is a persistent, stateful actor that receives journaled messages that have been written by another persistent actor. A view itself does not journal new messages, instead, it updates internal state only from a persistent actor's replicated message stream.
-   **AtLeastOnceDelivery**: To send messages with at-least-once delivery semantics to destinations, also in case of sender and receiver virtual machine crashes.
-   **Journal**: A journal stores the sequence of messages sent to a persistent actor. An application can control which messages are journaled and which are received by the persistent actor without being journaled. The storage backend of a journal is pluggable. The default journal storage plugin writes to the operating system's memory, replicated journals are available as Community plugins.
-   **SnapshotStore**: A snapshot store persists snapshots of a persistent actor's or a view's internal state. Snapshots are used for optimizing recovery times. The storage backend of a snapshot store is pluggable. The default snapshot storage plugin writes to the local filesystem.

## Technical Overview

### Eventsourced recovery cycle

Eventsourced recovery cycle starts in `PreStart` phase by actor sending a `Recover` message to itself. Eventsourced actor always starts in **Recovery pending** phase. During most of the recovery cycle it will only react on persistence system messages necessary to finish recovery cycle, stashing all other messages to be proceeded when actor state will recover.

1. **Recovery pending** - when actor receives a `Recover` message, it sends `LoadSnapshot` request to the snapshot store (by default it tries to recover from the latests snapshot found) and changes to **Recovery started** state.
2. **Recovery started** - actor waits for the `LoadSnapshotResult` response from snapshot store, as requested in previous state. If response contains a snapshot, it becomes wrapped in `SnapshotOffer` object and invoked by actor's `ReceiveRecover` method. Actor's last sequence number becomes updated from snapshot metadata. After that, a `ReplayMessages` request is sent to the journal and actor comes into `ReplayStarted` state.
3. **Replay started** - in this state actor responds to the following controll messages:
    -   `ReplayedMessage` - depending on the journal state, none or multiple messages may be send back to the actor. Each one updates actor's last sequence number. Message payload is passed to `ReceiveRecover` method of implementing actor. If any exception occur inside user-defined recovery logic, actor will move into **Replay failed** state and message will be pushed back on the beginning of the actor's mailbox.
    -   `ReplayMessagesSuccess` - it's returned by journal after all `ReplayedMessage`s has been sent back to actor. At this point actor's `OnReplaySuccess` method will be called and journal will be asked to return highest sequence number available. Actor will then move to the **Initializing** state.
    -   `ReplayMessagesFailed` - it may be returned when an error occurred during replay on the journal side. It contains an inner exception thrown by the journal. Actor's `OnReplayFailure` method is invoked and then exception itself (wrapped into `RecoveryFailure` object) is sent back to actor's `ReceiveRecover` method.
4. **Replay failed** - depending on message type actor last sequence number is updated and actor itself goes into **Prepare restart** phase.
5. **Prepare restart** - exception, which caused a failure and ultimatelly the restart is being rethrown. All previously stashed messages are being unstashed. All persisted messages are flushed to journal and `Recovery` messages is being resend to initialize recovery cycle again.
6. **Initializing state** - in this state actor state should be recovered, but actor is still waiting for the highest sequence number from the journal. After receiving it a `RecoveryCompleted` message is being passed to actor's `ReceiveRecover` method. If highest sequence number request was successfully responded by the journal, actor updates it's sequence number, unstashes all messages received during recovery cycle and moves to final **Processing commands** state.
7. **Processing commands** - at this state actor is ready to perform commands processing. All non-controll messages are pushed to actor's `ReceiveCommand` method. If `Persist`, `PersistAsync` or `Defer` methods will be called, actor may switch to **Persisting events** state in order to perform persisting user events in journal.
8. **Persisting events** - during this phase all incoming messages are stashed and actor waits for the write acknowledgment from the journal. When writes are confirmed, actor goes back to **Processing commands** and stashed messages becomes unstashed.

### Persistent view recovery cycle

Since persistent views are read-only variant of the persistence mechanism, their recovery states are slightly different from the other eventsourced actors - i.e. no journal writing steps are being performed. 

1. **Recovery pending** - same as Eventsourced.
2. **Recovery started** - same as Eventsourced.
3. **Replay started** - same as Eventsourced, except that `ReplayMessagesSuccess` and `ReplayMessagesFailure` both leads to the **Idle** state.
4. **Replay failed** - same as Eventsourced.
5. **Prepare restart** - same as Eventsourced, except no journal batch flushing is being performed.
6. **Idle** - final persistent state, in which all messages are passed to view's `Receive` method. The only exception is the `Update` controll message, which orders view to replay latests events found inside journal. By default this message is sent periodicaly to the view (details can be changed through HOCON config).