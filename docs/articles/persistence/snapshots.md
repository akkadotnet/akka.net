---
uid: snapshots
title: Snapshots
---
# Snapshots

Snapshots can dramatically reduce recovery times of persistent actors and views. The following discusses snapshots in context of persistent actors but this is also applicable to persistent views.

Persistent actors can save snapshots of internal state by calling the `SaveSnapshot` method. If saving of a snapshot succeeds, the persistent actor receives a `SaveSnapshotSuccess` message, otherwise a `SaveSnapshotFailure` message.

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Persistence/PersistentActor/Snapshots.cs?name=Snapshots)]

During recovery, the persistent actor is offered a previously saved snapshot via a `SnapshotOffer` message from which it can initialize internal state.

```csharp
if (message is SnapshotOffer offeredSnapshot)
{
    state = offeredSnapshot.Snapshot;
}
else if (message is RecoveryCompleted)
{
    // ..
}
else
{
    // event
}
```
The replayed messages that follow the `SnapshotOffer` message, if any, are younger than the offered snapshot. They finally recover the persistent actor to its current (i.e. latest) state.

In general, a persistent actor is only offered a snapshot if that persistent actor has previously saved one or more snapshots and at least one of these snapshots matches the `SnapshotSelectionCriteria` that can be specified for recovery.

```csharp
public override Recovery Recovery => new Recovery(fromSnapshot: new SnapshotSelectionCriteria(maxSequenceNr: 457L, maxTimeStamp: DateTime.UtcNow));
```

If not specified, they default to `SnapshotSelectionCriteria.Latest` which selects the latest (= youngest) snapshot. To disable snapshot-based recovery, applications should use `SnapshotSelectionCriteria.None`. A recovery where no saved snapshot matches the specified `SnapshotSelectionCriteria` will replay all journaled messages.

> [!NOTE]
> In order to use snapshots, a default snapshot-store (`akka.persistence.snapshot-store.plugin`) must be configured, or the `UntypedPersistentActor` can pick a snapshot store explicitly by overriding `SnapshotPluginId`.
> Since it is acceptable for some applications to not use any snapshotting, it is legal to not configure a snapshot store. However, Akka will log a warning message when this situation is detected and then continue to operate until an actor tries to store a snapshot, at which point the operation will fail (by replying with an `SaveSnapshotFailure` for example).
> Note that `Cluster Sharding` is using snapshots, so if you use Cluster Sharding you need to define a snapshot store plugin.

## Snapshot deletion

A persistent actor can delete individual snapshots by calling the `DeleteSnapshot` method with the sequence number of when the snapshot was taken.

To bulk-delete a range of snapshots matching `SnapshotSelectionCriteria`,
persistent actors should use the `deleteSnapshots` method. Depending on the journal used this might be inefficient. It is
best practice to do specific deletes with `deleteSnapshot` or to include a `minSequenceNr` as well as a `maxSequenceNr`
for the `SnapshotSelectionCriteria`.

## Snapshot status handling
Saving or deleting snapshots can either succeed or fail – this information is reported back to the persistent actor via status messages as illustrated in the following table.

|Method	         | Success                |	Failure message
|------          |------                  |------
|SaveSnapshot    | SaveSnapshotSuccess    |	SaveSnapshotFailure
|DeleteSnapshot  | DeleteSnapshotSuccess  |	DeleteSnapshotFailure
|DeleteSnapshots | DeleteSnapshotsSuccess | DeleteSnapshotsFailure

If failure messages are left unhandled by the actor, a default warning log message will be logged for each incoming failure message. No default action is performed on the success messages, however you're free to handle them e.g. in order to delete an in memory representation of the snapshot, or in the case of failure to attempt save the snapshot again.
