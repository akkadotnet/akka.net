---
uid: persistent-views
title: Persistence Views
---
# Persistent Views

> [!WARNING]
> `PersistentView` is deprecated. Use [PersistenceQuery](xref:persistence-query) when it will be ported. The corresponding query type is `EventsByPersistenceId`. There are several alternatives for connecting the `Source` to an actor corresponding to a previous `PersistentView` actor:
> - `Sink.ActorRef` is simple, but has the disadvantage that there is no back-pressure signal from the destination actor, i.e. if the actor is not consuming the messages fast enough the mailbox of the actor will grow
> - `MapAsync` combined with [Ask: Send-And-Receive-Future](xref:receive-actor-api#ask-send-and-receive-future) is almost as simple with the advantage of back-pressure being propagated all the way
> - `ActorSubscriber` in case you need more fine grained control
>
> The consuming actor may be a plain `UntypedActor` or a `UntypedPersistentActor` if it needs to store its own state (e.g. `FromSequenceNr` offset).

While a persistent actor may be used to produce and persist events, views are used only to read internal state based on them. Like the persistent actor, a view has a `PersistenceId` to specify a  collection of events to be resent to current view. This value should however be correlated with the  `PersistentId` of an actor who is the producer of the events.

Other members:

- `ViewId` property is a view unique identifier that doesn't change across different actor incarnations. It's useful in cases where there are multiple different views associated with a single persistent actor, but showing its state from a different perspectives.
- `IsAutoUpdate` property determines if the view will try to automatically update its state in specified time intervals. Without it, the view won't update its state until it receives an explicit `Update` message. This value can be set through configuration with *akka.persistence.view.auto-update* set to either *on* (by default) or *off*.
- `AutoUpdateInterval` specifies a time interval in which the view will be updating itself - only in cases where the *IsAutoUpdate* flag is on. This value can be set through configuration with *akka.persistence.view.auto-update-interval* key (5 seconds by default).
- `AutoUpdateReplayMax` property determines the maximum number of events to be replayed during a single *Update* cycle. This value can be set through configuration with *akka.persistence.view.auto-update-replay-max* key (by default it's -1 - no limit).
- `LoadSnapshot` will send a request to the snapshot store to resend a current view's snapshot.
- `SaveSnapshot` will send the current view's internal state as a snapshot to be saved by the  configured snapshot store.
- `DeleteSnapshot` and `DeleteSnapshots` methods may be used to specify snapshots to be removed from the snapshot store in cases where they are no longer needed.

The `PersistenceId` identifies the persistent actor from which the view receives journaled messages. It is not necessary that the referenced persistent actor is actually running. Views read messages from a persistent actor's journal directly. When a persistent actor is started later and begins to write new messages, by default the corresponding view is updated automatically.

It is possible to determine if a message was sent from the Journal or from another actor in user-land by calling the `IsPersistent` property. Having that said, very often you don't need this information at all and can simply apply the same logic to both cases (skip the if `IsPersistent` check).

## Updates
The default update interval of all persistent views of an actor system is configurable:

```hocon
akka.persistence.view.auto-update-interval = 5s
```

`PersistentView` implementation classes may also override the `AutoUpdateInterval` method to return a custom update interval for a specific view class or view instance. Applications may also trigger additional updates at any time by sending a view an Update message.

```csharp
IActorRef view = system.ActorOf<ViewActor>();
view.Tell(new Update(true));
```

If the await parameter is set to true, messages that follow the `Update` request are processed when the incremental message replay, triggered by that update request, completed. If set to false (default), messages following the update request may interleave with the replayed message stream. 

Automated updates of all persistent views of an actor system can be turned off by configuration:
```hocon
akka.persistence.view.auto-update = off
```
Implementation classes may override the configured default value by overriding the autoUpdate method. To limit the number of replayed messages per update request, applications can configure a custom *akka.persistence.view.auto-update-replay-max* value or override the `AutoUpdateReplayMax` property. The number of replayed messages for manual updates can be limited with the replayMax parameter of the Update message.

## Recovery
Initial recovery of persistent views works the very same way as for persistent actors (i.e. by sending a `Recover` message to self). The maximum number of replayed messages during initial recovery is determined by `AutoUpdateReplayMax`. Further possibilities to customize initial recovery are explained in section Recovery.

## Identifiers
A persistent view must have an identifier that doesn't change across different actor incarnations. The identifier must be defined with the `ViewId` method.

The `ViewId` must differ from the referenced `PersistenceId`, unless Snapshots of a view and its persistent actor should be shared (which is what applications usually do not want).
