---
uid: persistent-actors
title: Persistence Actors
---
# Persistent Actors

Unlike the default `UntypedActor` class, `PersistentActor` and its derivatives requires the setup of a few more additional members:

- `PersistenceId` is a persistent actor's identifier that doesn't change across different actor incarnations. It's used to retrieve an event stream required by the persistent actor to recover its internal state.
- `ReceiveRecover` is a method invoked during an actor's recovery cycle. Incoming objects may be user-defined events as well as system messages, for example `SnapshotOffer` which is used to deliver latest actor state saved in the snapshot store.
- `ReceiveCommand` is an equivalent of the basic `Receive` method of default Akka.NET actors.

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

A persistent actor receives a (non-persistent) command which is first validated if it can be applied to the current state. Here validation can mean anything from simple inspection of a command message's fields up to a conversation with several external services, for example. If validation succeeds, events are generated from the command, representing the effect of the command. These events are then persisted and, after successful persistence, used to change the actor's state. When the persistent actor needs to be recovered, only the persisted events are replayed of which we know that they can be successfully applied. In other words, events cannot fail when being replayed to a persistent actor, in contrast to commands. Event sourced actors may of course also process commands that do not change application state such as query commands for example.

Akka persistence supports event sourcing with the `ReceivePersistentActor` abstract class. An actor that extends this class uses the persist method to persist and handle events. The behavior of an `ReceivePersistentActor` is defined by implementing `Recover` and `Receive` methods. This is demonstrated in the following example.

```csharp
public class Cmd
{
    public Cmd(string data)
    {
        Data = data;
    }

    public string Data { get; }
}

public class Evt
{
    public Evt(string data)
    {
        Data = data;
    }

    public string Data { get; }
}

public class ExampleState
{
    private readonly List<string> _events;

    public ExampleState(List<string> events)
    {
        _events = events;
    }

    public ExampleState() : this(new List<string>())
    {
    }

    public void Update(Evt evt)
    {
        _events.Add(evt.Data);
    }

    public ExampleState Copy()
    {
        return new ExampleState(_events);
    }

    public int Size => _events.Count;
}


public class ExamplePersistentActor : ReceivePersistentActor
{
    private ExampleState _state = new ExampleState();

    public ExamplePersistentActor()
    {
        Recover<Evt>(evt =>
        {
            _state.Update(evt);
        });

        Recover<SnapshotOffer>(snapshot =>
        {
            _state = (ExampleState)snapshot.Snapshot;
        });

        Command<Cmd>(message =>
        {
            string data = message.Data;
            Evt evt1 = new Evt($"{data}-{_state.Size}");
            Evt evt2 = new Evt($"{data}-{_state.Size + 1}");

            var events = new List<Evt> { evt1, evt2 };

            PersistAll(events, evt =>
            {
                _state.Update(evt);
                if (evt == evt2)
                {
                    Context.System.EventStream.Publish(evt);
                }
            });
        });

        Command<string>(msg => msg == "snap", message =>
        {
            SaveSnapshot(_state.Copy());
        });

        Command<string>(msg => msg == "print", message =>
        {
            Console.WriteLine(_state);
        });
    }

    public override string PersistenceId { get; } = "sample-id-1";
}
```
The example defines two data types, `Cmd` and `Evt` to represent commands and events, respectively. The state of the `ExamplePersistentActor` is a list of persisted event data contained in `ExampleState`.

The persistent actor's `OnReceiveRecover` method defines how state is updated during recovery by handling `Evt` and `SnapshotOffer` messages. The persistent actor's `OnReceiveCommand` method is a command handler. In this example, a command is handled by generating two events which are then persisted and handled. Events are persisted by calling `Persist` with an event (or a sequence of events) as first argument and an event handler as second argument.

The persist method persists events asynchronously and the event handler is executed for successfully persisted events. Successfully persisted events are internally sent back to the persistent actor as individual messages that trigger event handler executions. An event handler may close over persistent actor state and mutate it. The sender of a persisted event is the sender of the corresponding command. This allows event handlers to reply to the sender of a command (not shown).

The main responsibility of an event handler is changing persistent actor state using event data and notifying others about successful state changes by publishing events.

When persisting events with persist it is guaranteed that the persistent actor will not receive further commands between the persist call and the execution(s) of the associated event handler. This also holds for multiple persist calls in context of a single command. Incoming messages are stashed until the persist is completed.

If persistence of an event fails, `OnPersistFailure` will be invoked (logging the error by default), and the actor will unconditionally be stopped. If persistence of an event is rejected before it is stored, e.g. due to serialization error, `OnPersistRejected` will be invoked (logging a warning by default), and the actor continues with the next message.

> [!NOTE]
> It's also possible to switch between different command handlers during normal processing and recovery with `Context.Become` and `Context.Unbecome`. To get the actor into the same state after recovery you need to take special care to perform the same state transitions with become and unbecome in the `ReceiveRecover` method as you would have done in the command handler. Note that when using become from `ReceiveRecover` it will still only use the `ReceiveRecover` behavior when replaying the events. When replay is completed it will use the new behavior.

## Identifiers
A persistent actor must have an identifier that doesn't change across different actor incarnations. The identifier must be defined with the `PersistenceId` method.

```csharp
public override string PersistenceId { get; } = "my-stable-persistence-id";
```

> [!NOTE]
> `PersistenceId` must be unique to a given entity in the journal (database table/keyspace). When replaying messages persisted to the journal, you query messages with a `PersistenceId`. So, if two different entities share the same `PersistenceId`, message-replaying behavior is corrupted.

## Recovery
By default, a persistent actor is automatically recovered on start and on restart by replaying journaled messages. New messages sent to a persistent actor during recovery do not interfere with replayed messages. They are cached and received by a persistent actor after recovery phase completes.

> [!NOTE]
> Accessing the `Sender` for replayed messages will always result in a `DeadLetters` reference, as the original sender is presumed to be long gone. If you indeed have to notify an actor during recovery in the future, store its `ActorPath` explicitly in your persisted events.

### Recovery customization
Applications may also customise how recovery is performed by returning a customised `Recovery` object in the recovery method of a `ReceivePersistentActor`.

To skip loading snapshots and replay all events you can use `SnapshotSelectionCriteria.None`. This can be useful if snapshot serialization format has changed in an incompatible way. It should typically not be used when events have been deleted.

```csharp
public override Recovery Recovery => new Recovery(fromSnapshot: SnapshotSelectionCriteria.None)
```

Another example, which can be fun for experiments but probably not in a real application, is setting an upper bound to the replay which allows the actor to be replayed to a certain point "in the past" instead to its most up to date state. Note that after that it is a bad idea to persist new events because a later recovery will probably be confused by the new events that follow the events that were previously skipped.

```csharp
public override Recovery Recovery => new Recovery(new SnapshotSelectionCriteria(457));
```

Recovery can be disabled by returning `Recovery.None` in the recovery property of a `ReceivePersistentActor`:

```csharp
public override Recovery Recovery => Recovery.None;
```

### Recovery status
A persistent actor can query its own recovery status via the methods

```csharp
public bool IsRecovering { get; }
public bool IsRecoveryFinished { get; }
```

Sometimes there is a need for performing additional initialization when the recovery has completed before processing any other message sent to the persistent actor. The persistent actor will receive a special `RecoveryCompleted` message right after recovery and before any other received messages.

```csharp
Recover<RecoveryCompleted>(message =>
{
    // perform init after recovery, before any other messages
});

Command<string>(message =>
{
    
});
```
The actor will always receive a `RecoveryCompleted` message, even if there are no events in the journal and the snapshot store is empty, or if it's a new persistent actor with a previously unused `PersistenceId`.

If there is a problem with recovering the state of the actor from the journal, `OnRecoveryFailure` is called (logging the error by default) and the actor will be stopped.

## Internal stash
The persistent actor has a private stash for internally caching incoming messages during `Recovery` or the `Persist` \ `PersistAll` method persisting events. However You can use inherited stash or create one or more stashes if needed. The internal stash doesn't interfere with these stashes apart from user inherited `UnstashAll` method, which prepends all messages in the inherited stash to the internal stash instead of mailbox. Hence, If the message in the inherited stash need to be handled after the messages in the internal stash, you should call inherited unstash method.

You should be careful to not send more messages to a persistent actor than it can keep up with, otherwise the number of stashed messages will grow. It can be wise to protect against `OutOfMemoryException` by defining a maximum stash capacity in the mailbox configuration:

```hocon
akka.actor.default-mailbox.stash-capacity = 10000
```

Note that the stash capacity is per actor. If you have many persistent actors, e.g. when using cluster sharding, you may need to define a small stash capacity to ensure that the total number of stashed messages in the system don't consume too much memory. Additionally, The persistent actor defines three strategies to handle failure when the internal stash capacity is exceeded. The default overflow strategy is the `ThrowOverflowExceptionStrategy`, which discards the current received message and throws a `StashOverflowException`, causing actor restart if default supervision strategy is used. you can override the `InternalStashOverflowStrategy` property to return `DiscardToDeadLetterStrategy` or `ReplyToStrategy` for any "individual" persistent actor, or define the "default" for all persistent actors by providing FQCN, which must be a subclass of `StashOverflowStrategyConfigurator`, in the persistence configuration:

```hocon
akka.persistence.internal-stash-overflow-strategy = "akka.persistence.ThrowExceptionConfigurator"
```

The `DiscardToDeadLetterStrategy` strategy also has a pre-packaged companion configurator `DiscardConfigurator`.

You can also query default strategy via the Akka persistence extension singleton:
```csharp
Context.System.DefaultInternalStashOverflowStrategy
```

> [!NOTE]
> The bounded mailbox should be avoid in the persistent actor, because it may be discarding the messages come from Storage backends. You can use bounded stash instead of bounded mailbox.

## Relaxed local consistency requirements and high throughput use-cases

If faced with relaxed local consistency requirements and high throughput demands sometimes `PersistentActor` and its persist may not be enough in terms of consuming incoming Commands at a high rate, because it has to wait until all Events related to a given Command are processed in order to start processing the next Command. While this abstraction is very useful for most cases, sometimes you may be faced with relaxed requirements about consistency – for example you may want to process commands as fast as you can, assuming that the Event will eventually be persisted and handled properly in the background, retroactively reacting to persistence failures if needed.

The `PersistAsync` method provides a tool for implementing high-throughput persistent actors. It will not stash incoming Commands while the Journal is still working on persisting and/or user code is executing event callbacks.

In the below example, the event callbacks may be called "at any time", even after the next Command has been processed. The ordering between events is still guaranteed ("evt-b-1" will be sent after "evt-a-2", which will be sent after "evt-a-1" etc.).

> [!NOTE]
> In order to implement the pattern known as "command sourcing" simply `PersistAsync` all incoming messages right away and handle them in the callback.

```csharp
public class MyPersistentActor : ReceivePersistentActor
{
    public override string PersistenceId => "HardCoded";

    public MyPersistentActor()
    {
        Action<string> replyToSender = message =>
        {
            Sender.Tell(message, Self);
        };

        Recover<string>(message =>
        {
            // handle recovery here
        });

        Command<string>(message =>
        {
            Sender.Tell(message, Self);

            PersistAsync($"evt-{message}-1", replyToSender);
            PersistAsync($"evt-{message}-2", replyToSender);
        });
    }
}
```
> [!WARNING]
> The callback will not be invoked if the actor is restarted (or stopped) in between the call to `PersistAsync` and the journal has confirmed the write.

## Deferring actions until preceding persist handlers have executed
Sometimes when working with `PersistAsync` you may find that it would be nice to define some actions in terms of happens-after the previous `PersistAsync` handlers have been invoked. `PersistentActor` provides an utility method called `DeferAsync`, which works similarly to `PersistAsync` yet does not persist the passed in event. It is recommended to use it for read operations, and actions which do not have corresponding events in your domain model.

Using this method is very similar to the persist family of methods, yet it does not persist the passed in event. It will be kept in memory and used when invoking the handler.

```csharp
public class MyPersistentActor : ReceivePersistentActor
{
    public override string PersistenceId => "HardCoded";

    public MyPersistentActor()
    {
        Action<string> replyToSender = message =>
        {
            Sender.Tell(message, Self);
        };

        Recover<string>(message =>
        {
            // handle recovery here
        });

        Command<string>(message =>
        {
            PersistAsync($"evt-{message}-1", replyToSender);
            PersistAsync($"evt-{message}-2", replyToSender);
            DeferAsync($"evt-{message}-3", replyToSender);
        });
    }
}
```

Notice that the `Sender` is safe to access in the handler callback, and will be pointing to the original sender of the command for which this `DeferAsync` handler was called.

The calling side will get the responses in this (guaranteed) order:

```csharp
persistentActor.tell("a");
persistentActor.tell("b");
 
// order of received messages:
// a
// b
// evt-a-1
// evt-a-2
// evt-a-3
// evt-b-1
// evt-b-2
// evt-b-3
```

> [!WARNING]
> The callback will not be invoked if the actor is restarted (or stopped) in between the call to `DeferAsync` and the journal has processed and confirmed all preceding writes..

## Nested persist calls

It is possible to call `Persist` and `PersistAsync` inside their respective callback blocks and they will properly retain both the thread safety (including the right value of `Sender`) as well as stashing guarantees.

In general it is encouraged to create command handlers which do not need to resort to nested event persisting, however there are situations where it may be useful. It is important to understand the ordering of callback execution in those situations, as well as their implication on the stashing behaviour (that persist enforces). In the following example two persist calls are issued, and each of them issues another persist inside its callback:
```csharp
public class MyPersistentActor : ReceivePersistentActor
{
    public override string PersistenceId => "HardCoded";

    public MyPersistentActor()
    {
        Action<string> replyToSender = (message) =>
        {
            Sender.Tell(message, Self);
        };

        Command<string>(message =>
        {
            Persist($"{message}-outer-1", innerMessage =>
            {
                Sender.Tell(innerMessage, Self);
                Persist($"{innerMessage}-inner-1", replyToSender);
            });

            Persist($"{message}-outer-2", innerMessage =>
            {
                Sender.Tell(innerMessage, Self);
                Persist($"{innerMessage}-inner-2", replyToSender);
            });
        });
    }
}
```
When sending two commands to this `PersistentActor`, the persist handlers will be executed in the following order:
```csharp
persistentActor.tell("a");
persistentActor.tell("b");
 
// order of received messages:
// a
// a-outer-1
// a-outer-2
// a-inner-1
// a-inner-2
// and only then process "b"
// b
// b-outer-1
// b-outer-2
// b-inner-1
// b-inner-2
```
First the "outer layer" of persist calls is issued and their callbacks are applied. After these have successfully completed, the inner callbacks will be invoked (once the events they are persisting have been confirmed to be persisted by the journal). Only after all these handlers have been successfully invoked will the next command be delivered to the persistent Actor. In other words, the stashing of incoming commands that is guaranteed by initially calling `Persist` on the outer layer is extended until all nested persist callbacks have been handled.

It is also possible to nest `PersistAsync` calls, using the same pattern:
```csharp
public class MyPersistentActor : ReceivePersistentActor
{
    public override string PersistenceId => "HardCoded";

    public MyPersistentActor()
    {
        Action<string> replyToSender = (message) =>
        {
            Sender.Tell(message, Self);
        };

        Command<string>(message =>
        {
            PersistAsync($"{message}-outer-1", innerMessage =>
            {
                Sender.Tell(innerMessage, Self);
                PersistAsync($"{innerMessage}-inner-1", replyToSender);
            });

            PersistAsync($"{message}-outer-2", innerMessage =>
            {
                Sender.Tell(innerMessage, Self);
                PersistAsync($"{innerMessage}-inner-2", replyToSender);
            });
        });
    }
}
```
In this case no stashing is happening, yet events are still persisted and callbacks are executed in the expected order:
```csharp
persistentActor.tell("a");
persistentActor.tell("b");
 
// order of received messages:
// a
// b
// a-outer-1
// a-outer-2
// b-outer-1
// b-outer-2
// a-inner-1
// a-inner-2
// b-inner-1
// b-inner-2
 
// which can be seen as the following causal relationship:
// a -> a-outer-1 -> a-outer-2 -> a-inner-1 -> a-inner-2
// b -> b-outer-1 -> b-outer-2 -> b-inner-1 -> b-inner-2
```
While it is possible to nest mixed `Persist` and `PersistAsync` with keeping their respective semantics it is not a recommended practice, as it may lead to overly complex nesting.

> [!WARNING]
> While it is possible to nest `Persist` calls within one another, it is not legal call persist from any other `Thread` than the Actors message processing `Thread`. For example, it is not legal to call `Persist` from tasks! Doing so will break the guarantees that the persist methods aim to provide. Always call `Persist` and `PersistAsync` from within the Actor's receive block (or methods synchronously invoked from there).

## Failures
If persistence of an event fails, `OnPersistFailure` will be invoked (logging the error by default), and the actor will unconditionally be stopped.

The reason that it cannot resume when persist fails is that it is unknown if the event was actually persisted or not, and therefore it is in an inconsistent state. Restarting on persistent failures will most likely fail anyway since the journal is probably unavailable. It is better to stop the actor and after a back-off timeout start it again. The `BackoffSupervisor` actor is provided to support such restarts.
```csharp
protected override void PreStart()
{
    var childProps = Props.Create<DocumentPersistentActor>();
    var actor = new BackoffSupervisor(
        childProps,
        "myActor",
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(30),
        0.2);
    base.PreStart();
}
```

If persistence of an event is rejected before it is stored, e.g. due to serialization error, `OnPersistRejected` will be invoked (logging a warning by default), and the actor continues with next message.

If there is a problem with recovering the state of the actor from the journal when the actor is started, `OnRecoveryFailure` is called (logging the error by default), and the actor will be stopped. Note that failure to load snapshot is also treated like this, but you can disable loading of snapshots if you for example know that serialization format has changed in an incompatible way, see [Recovery customization](#recovery customization).

## Atomic writes
Each event is of course stored atomically, but it is also possible to store several events atomically by using the `PersistAll` or `PersistAllAsync` method. That means that all events passed to that method are stored or none of them are stored if there is an error.

The recovery of a persistent actor will therefore never be done partially with only a subset of events persisted by `PersistAll`.

Some journals may not support atomic writes of several events and they will then reject the `PersistAll` command, i.e. `OnPersistRejected` is called with an exception (typically `NotSupportedException`).

## Batch writes
In order to optimize throughput when using `PersistAsync`, a persistent actor internally batches events to be stored under high load before writing them to the journal (as a single batch). The batch size is dynamically determined by how many events are emitted during the time of a journal round-trip: after sending a batch to the journal no further batch can be sent before confirmation has been received that the previous batch has been written. Batch writes are never timer-based which keeps latencies at a minimum.

## Message deletion

It is possible to delete all messages (journaled by a single persistent actor) up to a specified sequence number; Persistent actors may call the `DeleteMessages` method to this end.

Deleting messages in event sourcing based applications is typically either not used at all, or used in conjunction with snapshotting, i.e. after a snapshot has been successfully stored, a `DeleteMessages` (`ToSequenceNr`) up until the sequence number of the data held by that snapshot can be issued to safely delete the previous events while still having access to the accumulated state during replays - by loading the snapshot.

> [!WARNING]
> If you are using `Persistence Query`, query results may be missing deleted messages in a journal, depending on how deletions are implemented in the journal plugin. Unless you use a plugin which still shows deleted messages in persistence query results, you have to design your application so that it is not affected by missing messages.

The result of the `DeleteMessages` request is signaled to the persistent actor with a `DeleteMessagesSuccess` message if the delete was successful or a `DeleteMessagesFailure` message if it failed.

Message deletion doesn't affect the highest sequence number of the journal, even if all messages were deleted from it after `DeleteMessages` invocation.

## Persistence status handling

| Method   	             | Success      	        |  Failure / Rejection 	| After failure handler invoked
|------                  |------                    |------	                |------	  
| Persist / PersistAsync | persist handler invoked	| OnPersistFailure  	| Actor is stopped.
|                        |                          | OnPersistRejected     | No automatic actions.
| Recovery 	             | RecoverySuccess   	    | OnRecoveryFailure 	| Actor is stopped.
| DeleteMessages 	     | DeleteMessagesSuccess 	| DeleteMessagesFailure | No automatic actions.

The most important operations (Persist and Recovery) have failure handlers modelled as explicit callbacks which the user can override in the `PersistentActor`. The default implementations of these handlers emit a log message (error for persist/recovery failures, and warning for others), logging the failure cause and information about which message caused the failure.

For critical failures such as recovery or persisting events failing the persistent actor will be stopped after the failure handler is invoked. This is because if the underlying journal implementation is signalling persistence failures it is most likely either failing completely or overloaded and restarting right-away and trying to persist the event again will most likely not help the journal recover – as it would likely cause a Thundering herd problem, as many persistent actors would restart and try to persist their events again. Instead, using a `BackoffSupervisor` (as described in Failures) which implements an exponential-backoff strategy which allows for more breathing room for the journal to recover between restarts of the persistent actor.

> [!NOTE]
> Journal implementations may choose to implement a retry mechanism, e.g. such that only after a write fails N number of times a persistence failure is signalled back to the user. In other words, once a journal returns a failure, it is considered fatal by Akka Persistence, and the persistent actor which caused the failure will be stopped. Check the documentation of the journal implementation you are using for details if/how it is using this technique.

## Safely shutting down persistent actors

Special care should be given when shutting down persistent actors from the outside. With normal Actors it is often acceptable to use the special `PoisonPill` message to signal to an Actor that it should stop itself once it receives this message – in fact this message is handled automatically by Akka, leaving the target actor no way to refuse stopping itself when given a poison pill.

This can be dangerous when used with PersistentActor due to the fact that incoming commands are stashed while the persistent actor is awaiting confirmation from the Journal that events have been written when `Persist` was used. Since the incoming commands will be drained from the Actor's mailbox and put into its internal stash while awaiting the confirmation (thus, before calling the persist handlers) the Actor may receive and (auto)handle the `PoisonPill` before it processes the other messages which have been put into its stash, causing a pre-mature shutdown of the Actor.

> [!WARNING]
> Consider using explicit shut-down messages instead of `PoisonPill` when working with persistent actors.

The example below highlights how messages arrive in the Actor's mailbox and how they interact with its internal stashing mechanism when `Persist()` is used. Notice the early stop behaviour that occurs when `PoisonPill` is used:

```csharp
public class Shutdown
{
}

public class ShutdownPersistentActor : ReceivePersistentActor
{
    public ShutdownPersistentActor()
    {
        Recover<string>(rec =>
        {
            // handle recovery...
        });

        Command<string>(msg =>
        {
            Persist(msg, param =>
            {
                Console.WriteLine(param);
            });
        });

        Command<Shutdown>(msg =>
        {
            Context.Stop(Self);
        });
    }

    public override string PersistenceId
    {
        get
        {
            return "some-persistence-id";
        }
    }
}
```

```csharp
// UN-SAFE, due to PersistentActor's command stashing:
persistentActor.Tell("a");
persistentActor.Tell("b");
persistentActor.Tell(PoisonPill.Instance);
// order of received messages:
// a
//   # b arrives at mailbox, stashing;        internal-stash = [b]
//   # PoisonPill arrives at mailbox, stashing; internal-stash = [b, Shutdown]
// PoisonPill is an AutoReceivedMessage, is handled automatically
// !! stop !!
// Actor is stopped without handling `b` nor the `a` handler!
```

```csharp
// SAFE:
persistentActor.Tell("a");
persistentActor.Tell("b");
persistentActor.Tell(new Shutdown());
// order of received messages:
// a
//   # b arrives at mailbox, stashing;        internal-stash = [b]
//   # Shutdown arrives at mailbox, stashing; internal-stash = [b, Shutdown]
// handle-a
//   # unstashing;                            internal-stash = [Shutdown]
// b
// handle-b
//   # unstashing;                            internal-stash = []
// Shutdown
// -- stop --
```

## Replay filter
There could be cases where event streams are corrupted and multiple writers (i.e. multiple persistent actor instances) journaled different messages with the same sequence number. In such a case, you can configure how you filter replayed messages from multiple writers, upon recovery.

In your configuration, under the `akka.persistence.journal.xxx.replay-filter` section (where xxx is your journal plugin id), you can select the replay filter mode from one of the following values:

- repair-by-discard-old
- fail
- warn
- off

For example, if you configure the replay filter for `sqlite` plugin, it looks like this:
```hocon
# The replay filter can detect a corrupt event stream by inspecting
# sequence numbers and writerUuid when replaying events.
akka.persistence.journal.sqlite.replay-filter {
  # What the filter should do when detecting invalid events.
  # Supported values:
  # `repair-by-discard-old` : discard events from old writers,
  #                           warning is logged
  # `fail` : fail the replay, error is logged
  # `warn` : log warning but emit events untouched
  # `off` : disable this feature completely
  mode = repair-by-discard-old
}
```