---
layout: wiki
title: Persistence
---
## Persistence

Akka.Persistence plugin enables to create a statefull actors, which internal state may be stored inside persistent data storage and used for recovery in case of restart, migration or VM crash. Core concept behind Akka persistence lays in storing not only actor state directly (in form of the snapshots) but also history of all of the changes of that actor's state. This is quite useful solution common in patterns such as eventsourcing. Changes are immutable by nature, as they describe facts already reported in the history, and can be stored inside event journal in append-only mode. While recovering, actor restores it's state from the latests snapshot available - which can reduce recovery time - and then recreates it further by replaying events stored inside journal. Among other features provided by persistence plugin is support for command query segregation model and point-to-point communication with at-least-once delivery semantics.

### Architecture

Akka.Persistence features are available through new set of actor base classes:

- **PersistentActor** is a persistent, stateful equivalent of *ActorBase* class. It's able to persist event inside the journal, creating snapshots in snapshot stores and recover from them in thread-safe manner. It can be used for both changing and reading state of the actor.
- **PersistentView** is used to recreate internal state of other persistent actor based on journaled messages. It works in read-only manner - it cannot journal any event by itself.
- **GuaranteedDeliveryActor** may be used to ensure at-least-once delivery semantics between communicating actors, even in case when either sender or receiver VM crashes.
- **Journal** stores a sequence of events send by the persistent actor. The storage backend of the journal is plugable. By default it uses in-memory message stream and is NOT a persistent storage.
- **Snapshot store** is used to persist snapshots of either persistent actor's or view's internal state. They can be used to reduce recovery times in case when a lot of events needs to be replayed for specific persistent actor. Storage backend of the snapshot store is plugable. By default it uses local file system.

### Persistent actors

Unlike default `ActorBase` class `PersistentActor` and it's derivatives requires to setup a few more additional members:

- **PersistenceId** is a persistent actor's identifier that doesn't change across different actor incarnations. It's used to retrieve an event stream required by the persistent actor to recover it's internal state.
- **ReceiveRecover** is a method invoked during actor's recovery cycle. Incomming objects may be user-defined events as well as system messages, for example `SnapshotOffer` which is used to deliver latest actor state saved in the snapshot store.
- **ReceiveCommand** is an equivalent of basic `Receive` method of default Akka.NET actors.

Persistent actors also offer a set of specialized members:

- **Persist** and **PersistAsync** methods can be used to send events to event journal in order to store them inside. Second argument in a callback invoked when journal confirms, that events have been stored successfully.
- **Defer** and **DeferAsync** are used to perform various operations *after* events will be persisted and their callback handlers will be invoked. Unlike persist methods, defer won't store and event in a persistent storage. Defer methods may NOT be invoked in case when actor is restarted even thou journal will successfully persist events sent.
- **DeleteMessages** will order attached journal to remove part of it's events. It can be either logical deletion - messages are marked as deleted, but are not removed physically from the backend storage - or a physical one, when the messages are removed physically from the journal.
- **LoadSnapshot** will sent request to snapshot store to resent a current actor's snapshot.
- **SaveSnapshot** will sent current actor's internal state as snapshot to be saved by configured snapshot store.
- **DeleteSnapshot** and **DeleteSnapshots** methods may be used to specify snapshots to be removed from snapshot store in case, when they are no longer needed.
- **OnReplaySuccess** is a virtual method which will be called when recovery cycle will end successfully.
- **OnReplayFailure** is a virtual method which will be called when recovery cycle will fail unexpectedly from some reason.
- **IsRecovering** property determines if current actor is performing a recovery cycle at the moment.
- **SnapshotSequenceNr** property may be used to determine sequence number used for marking persisted events. This value is changing in monotically increasing manner.

In case when a manual recovery cycle initialization is necessary, it may be invoked by sending a `Recover` message to persistent actor.

### Persistent views

While persistent actor may be used to producing and persisting an events, views are used only to read internal state based on them. Like persistent actor, view has a **PersistenceId** to specify collection of events to be resent to current view. This value should however be correlated with PersistentId of actor - producer of the events. 

Other members:

- **ViewId** property is an view unique identifier that doesn't change across different actor incarnations. It's usefull in case when there should be a multiple different views associated with a single persistent actor, but showing it's state from a different perspectives.
- **IsAutoUpdate** property determines if view will try to automatically update it's state in specified time intervals. Without it, view won't update it's state until it receives an explicit `Update` message. This value can be set through configuration with *akka.persistence.view.auto-update* set to either *on* (by default) or *off*.
- **AutoUpdateInterval** specifies time interval in which view will be updating itself - only in case when *IsAutoUpdate* flag is on. This value can be set through configuration with *akka.persistence.view.auto-update-interval* key (5 seconds by default).
- **AutoUpdateReplayMax** property determines maximum number of events to be replayed during a single *Update* cycle. This value can be set through configuration with *akka.persistence.view.auto-update-replay-max* key (by default it's -1 - no limit).
- **LoadSnapshot** will sent request to snapshot store to resent a current view's snapshot.
- **SaveSnapshot** will sent current view's internal state as snapshot to be saved by configured snapshot store.
- **DeleteSnapshot** and **DeleteSnapshots** methods may be used to specify snapshots to be removed from snapshot store in case, when they are no longer needed.

### Guaranteed delivery

Guaranteed delivery actors are specializations of persistent actors and may be used to provide at-least-once delivery semantics, even in case when one of the communication endpoints will crash. Because it's possible that the same message will be send twice, actor's receive behavior must work in the idempotent manner.

Members:

- **Deliver** method is used to send message to another actor in at-least-once delivery semantics. Message sent this way must be confirmed by the other endpoint with **ConfirmDelivery** method. Otherwise it will be resend again and again until the redelivery limit will be reached.
- **GetDeliverySnapshot** and **SetDeliverySnapshot** methods are used as part of delivery snapshotting strategy. They return/reset state of the current guaranteed delivery actor unconfirmed messages. In order to save custom deliverer state inside snapshot, a returned delivery snapshot should be included into that snapshot and reset in *ReceiveRecovery* method, when `SnapshotOffer` arrives.
- **RedeliveryBurstLimit** is a virtual property which determines maximum number of unconfirmed messages to be send in each redelivery attempt. It may be usefull to prevent message overflow scenarios. It may be overriden or configured inside HOCON configuration under *akka.persistence.at-least-once-delivery.redelivery-burst-limit* path (10 000 by default).
- **UnconfirmedDeliveryAttemptsToWarn** is a virtual property which determines, how many unconfirmed deliveries may be sent before guranteed delivery actor will send an `UnconfirmedWarning` message to itself. Count is reset after actor's restart. It may be overriden or configured inside HOCON configuration under *akka.persistence.at-least-once-delivery.warn-after-number-of-unconfirmed-attempts* path (5 by default).
- **MaxUnconfirmedMessages** is a virtual property which determines maximum of unconfirmed deliveries hold in memory. After this threshold is exceeded any **Deliver** method will raise `MaxUnconfirmedMessagesExceededException`. It may be overriden or configured inside HOCON configuration under *akka.persistence.at-least-once-delivery.max-unconfirmed-messages* path (100 000 by default).
- **UnconfirmedCount** property shows a number of the unconfirmed messages.

### Journals

Journal is a specialized type of an actor, which exposes an API to handle incoming events and store them in backend storage. By default Akka.Persitence uses a `MemoryJournal` which stores all events in memory and therefore it's not a persistent storage. Custom journal configuration path may be specified inside *akka.persistence.journal.plugin* path and by default it requires two keys set: *class* and *plugin-dispatcher*. Example configuration:

```hocon
akka {
	persistence {
		journal {

			# Path to the journal plugin to be used
	    	plugin = "akka.persistence.journal.inmem"

	    	# In-memory journal plugin.
	    	inmem {

	        	# Class name of the plugin.
	        	class = "Akka.Persistence.Journal.MemoryJournal, Akka.Persistence"

	        	# Dispatcher for the plugin actor.
	        	plugin-dispatcher = "akka.actor.default-dispatcher"
	    	}
    	}
	}
}
```

### Snapshot store

Snapshot store is a specialized type of an actor, which exposes an API to handle incoming snapshot-related requests and is able to save snapshots in some backend storage. By default Akka.Persistence uses a `LocalSnapshotStore`, which uses a local file system as a storage. Custom snapshot store configuration path may be specified inside *akka.persistence.snapshot-store.plugin* path and by default it requires two keys set: *class* and *plugin-dispatcher*. Example configuration:

```hocon
akka {
	persistence {
		snapshot-store {

	    	# Path to the snapshot store plugin to be used
	    	plugin = "akka.persistence.snapshot-store.local"

	    	# Local filesystem snapshot store plugin.
	    	local {

	    		# Class name of the plugin.
	        	class = "Akka.Persistence.Snapshot.LocalSnapshotStore, Akka.Persistence"

	        	# Dispatcher for the plugin actor.
	        	plugin-dispatcher = "akka.persistence.dispatchers.default-plugin-dispatcher"

	        	# Dispatcher for streaming snapshot IO.
	        	stream-dispatcher = "akka.persistence.dispatchers.default-stream-dispatcher"

	        	# Storage location of snapshot files.
	        	dir = "snapshots"
	    	}
    	}
	}
}
```

### Contributing

Akka persistence plugin gives a custom journal and snapshot store creator a built-in set of test, which can be used to verify correctness of the implemented backend storage plugins. It's available through `Akka.Persistence.TestKit` package and uses xUnit as default test framework.
