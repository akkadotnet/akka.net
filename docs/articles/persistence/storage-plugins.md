---
uid: storage-plugins
title: Storage plugins
---
# Storage Plugins

## Journals

Journal is a specialized type of actor which exposes an API to handle incoming events and store them in backend storage. By default Akka.Persistence uses a `MemoryJournal` which stores all events in memory and therefore it's not persistent storage. A custom journal configuration path may be specified inside *akka.persistence.journal.plugin* path and by default it requires two keys set: *class* and *plugin-dispatcher*. Example configuration:

[!code-json[Main](../../../src/core/Akka.Persistence/persistence.conf#L201-L207)]

## Snapshot Store

Snapshot store is a specialized type of actor which exposes an API to handle incoming snapshot-related requests and is able to save snapshots in some backend storage. By default Akka.Persistence uses a `LocalSnapshotStore`, which uses a local file system as storage. A custom snapshot store configuration path may be specified inside *akka.persistence.snapshot-store.plugin* path and by default it requires two keys set: *class* and *plugin-dispatcher*. Example configuration:

[!code-json[Main](../../../src/core/Akka.Persistence/persistence.conf#L209-L215)]

### Eager Initialization of Persistence Plugin

By default, persistence plugins are started on-demand, as they are used. In some case, however, it might be beneficial to start a certain plugin eagerly. In order to do that, specify the IDs of plugins you wish to start automatically under `akka.persistence.journal.auto-start-journals` and `akka.persistence.snapshot-store.auto-start-snapshot-stores`.

For example, if you want eager initialization for the sqlite journal and snapshot store plugin, your configuration should look like this:  

```hocon
akka {
  persistence {
    journal {
      plugin = "akka.persistence.journal.sqlite"
      auto-start-journals = ["akka.persistence.journal.sqlite"]
    }
    snapshot-store {
      plugin = "akka.persistence.snapshot-store.sqlite"
      auto-start-snapshot-stores = ["akka.persistence.snapshot-store.sqlite"]
    }
  }
}
```

### Controlling Journal or Snapshot Crash Behavior

By default the base implementations upon which all journal or snapshot-store implementations are build upon provides out of the box behavior for dealing with errors that occur during the writing or reading of data from the underlying store. Errors that occur will be communicated with the persistent actor that is using them at that time.
So in general once started successfully the journal or snapshot-store will be ready and available for the duration of your application, and won't crash. However in the case they do crash, due to unforeseen circumstances the default behavior is to immediately restart them. This is generally the behavior you want.
But in case you do want to customize how the system handles the crashing of the journal or snapshot-store. You can specify your own supervision strategy using the `supervisor-strategy` property.
This class needs to inherit from `Akka.Actor.SupervisorStrategyConfigurator` and have a parameter-less constructor.
Configuration example:

```hocon
akka {
  persistence {
    journal {
      plugin = "akka.persistence.journal.sqlite"
      auto-start-journals = ["akka.persistence.journal.sqlite"]
      supervisor-strategy = "My.full.namespace.CustomSupervisorStrategyConfigurator"
    }
    snapshot-store {
      plugin = "akka.persistence.snapshot-store.sqlite"
      auto-start-snapshot-stores = ["akka.persistence.snapshot-store.sqlite"]
      supervisor-strategy = "My.full.namespace.CustomSupervisorStrategyConfigurator"
    }
  }
}
```

One such case could be to detect and handle misconfigured application settings during startup. For example if your using a SQL based journal and you misconfigured the connection string you might opt to return a supervision strategy that detects certain network connection errors, and after a few retries signals your application to shutdown instead of continue running with a journal or snapshot-store that in all likelihood will never be able to recover, forever stuck in a restart loop while your application is running.

An example of what this could look like is this:

```csharp

  public class MyCustomSupervisorConfigurator : SupervisorStrategyConfigurator
        {
            public override SupervisorStrategy Create()
            {
                //optionally only stop if the error occurs more then x times in y period
                //this will be highly likely if its an unrecoverable error during start/init of the journal/snapshot store
                return new OneForOneStrategy(10,TimeSpan.FromSeconds(5),ex =>
                {
                    //detect unrecoverable exception here
                    return Directive.Stop;
                });
            }
        }
```
