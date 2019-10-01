---
uid: dispatchers
title: Dispatchers
---

# Dispatchers

## What Do Dispatchers Do?
Dispatchers are responsible for scheduling all code that run inside the `ActorSystem`. Dispatchers are one of the most important parts of Akka.NET, as they control the throughput and time share for each of the actors, giving each one a fair share of resources.

By default, all actors share a single **Global Dispatcher**. Unless you change the configuration, this dispatcher uses the *.NET Thread Pool* behind the scenes, which is optimized for most common scenarios. **That means the default configuration should be *good enough* for most cases.**

#### Why should I use different dispatchers?

When messages arrive in the [actor's mailbox](xref:mailboxes), the dispatcher schedules the delivery of messages in batches, and tries to deliver the entire batch before releasing the thread to another actor. While the default configuration is *good enough* for most scenarios, you may want to change ([through configuration](#configuring-dispatchers)) how much time the scheduler should spend running each actor.

There are some other common reasons to select a different dispatcher. These reasons include (but are not limited to):

* isolating one or more actors to specific threads in order to:
  * ensure high-load actors don't starve the system by consuming too much cpu-time;
  * ensure important actors always have a dedicated thread to do their job;
  * create [bulkheads](http://skife.org/architecture/fault-tolerance/2009/12/31/bulkheads.html), ensuring problems created in one part of the system do not leak to others;
* allow actors to execute in a specific SyncrhonizationContext;

> [!NOTE]
> Consider using custom dispatchers for special cases only. Correctly configuring dispatchers requires some understanding of how the framework works. Custom dispatchers *should not* be considered the default solution for performance problems. It's considered normal for complex applications to have one or a few custom dispatchers, it's not usual for most or all actors in a system to require a custom dispatcher configuration.

## Dispatchers vs. Dispatcher Configurations

Throughout this documentation and most Akka literature available, the term *dispatcher* is used to refer to *dispatcher configurations*, but they are in fact different things.

- **Dispatchers** are low level components that are responsible for scheduling code execution in the system. These components are built into Akka.NET, there is a fixed number of them and you don't need to create or change them.

- **Dispatcher Configurations** are custom settings you can create to *make use of dispatchers* in specific ways. There are some built-in dispatcher configurations, and you can create as many as you need for your applications.

Therefore, when you read about *"creating a custom dispatcher"* it usually means "*using a custom configuration for one of the built-in dispatchers*".

## Configuring Dispatchers

You can define a custom dispatcher configuration using a HOCON configuration section.

The example below creates a custom dispatcher called `my-dispatcher` that can be set in one or more actors during deployment:

```hocon
my-dispatcher {
    type = Dispatcher
    throughput = 100
    throughput-deadline-time = 0ms
}
```

You can then set actor's dispatcher using the deployment configuration:

```hocon
akka.actor.deployment {
    /my-actor {
        dispatcher = my-dispatcher
    }
}
```

Or you can also set it up in code:

```cs
system.ActorOf(Props.Create<MyActor>().WithDispatcher("my-dispatcher"), "my-actor");
```

#### Built-in Dispatcher Configurations

Some dispatcher configurations are available out-of-the-box for convenience. You can use them during actor deployment, [as described above](#configuring-dispatchers).

* **default-dispatcher** - A configuration that uses the [ThreadPoolDispatcher](#threadpooldispatcher). As the name says, this is the default dispatcher configuration used by the global dispatcher, and you don't need to define anything during deployment to use it.
* **task-dispatcher** - A configuration that uses the [TaskDispatcher](#taskdispatcher).
* **default-fork-join-dispatcher** - A configuration that uses the [ForkJoinDispatcher](#forkjoindispatcher).
* **synchronized-dispatcher** - A configuration that uses the [SynchronizedDispatcher](#synchronizeddispatcher).

## Built-in Dispatchers

These are the underlying dispatchers built-in to Akka.NET:

### ThreadPoolDispatcher

It schedules code to run in the [.NET Thread Pool](https://msdn.microsoft.com/en-us/library/System.Threading.ThreadPool.aspx), which is ***good enough* for most cases.**

The `type` used in the HOCON configuration for this dispatcher is just `Dispatcher`.

```hocon
custom-dispatcher {
type = Dispatcher
throughput = 100
}
```

> [!NOTE]
> While each configuration can have it's own throughput settings, all dispatchers using this type will run in the same default .NET Thread Pool.

### TaskDispatcher

The TaskDispatcher uses the [TPL](https://msdn.microsoft.com/en-us/library/dd460717.aspx) infrastructure to schedule code execution. This dispatcher is very similar to the Thread PoolDispatcher, but may be used in some rare scenarios where the thread pool isn't available.

```hocon
custom-task-dispatcher {
  type = TaskDispatcher
  throughput = 100
}
```

### PinnedDispatcher

The `PinnedDispatcher` uses a single dedicated thread to schedule code executions. Ideally, this dispatcher should be using sparingly.

```hocon
custom-dedicated-dispatcher {
  type = PinnedDispatcher
}
```

### ForkJoinDispatcher

The ForkJoinDispatcher uses a dedicated threadpool to schedule code execution. You can use this scheduler isolate some actors from the rest of the system. Each dispatcher configuration will have it's own thread pool.

This is the configuration for the [*default-fork-join-dispatcher*](#built-in-dispatcher-configurations).  You may use this as example for custom fork-join dispatchers.

```hocon
default-fork-join-dispatcher {
  type = ForkJoinDispatcher
  throughput = 100
  dedicated-thread-pool {
	  thread-count = 3
	  deadlock-timeout = 3s
	  threadtype = background
  }
}
```

* `thread-count` - The number of threads dedicated to this dispatcher.
* `deadlock-timeout` - The amount of time to wait before considering the thread as deadlocked. By default no timeout is set, meaning code can run in the threads for as long as they need. If you set a value, once the timeout is reached the thread will be aborted and a new threads will take it's place. Set this value carefully, as very low values may cause loss of work.
* `threadtype` - Must be `background` or `foreground`. This setting helps define [how .NET handles](https://msdn.microsoft.com/en-us/library/system.threading.thread.isbackground.aspx) the thread.

### SynchronizedDispatcher

The `SynchronizedDispatcher` uses the *current* [SynchronizationContext](https://msdn.microsoft.com/en-us/magazine/gg598924.aspx) to schedule executions.

You may use this dispatcher to create actors that update UIs in a reactive manner. An application that displays real-time updates of stock prices may have a dedicated actor to update the UI controls directly for example.

> [!NOTE]
> As a general rule, actors running in this dispatcher shouldn't do much work. Avoid doing any extra work that may be done by actors running in other pools.

This is the configuration for the [*synchronized-dispatcher*](#built-in-dispatcher-configurations).  You may use this as example for custom fork-join dispatchers.

```hocon
synchronized-dispatcher {
  type = "SynchronizedDispatcher"
  throughput = 10
}
```

In order to use this dispatcher, you must create the actor from the synchronization context you want to run-it. For example:

```csharp
private void Form1_Load(object sender, System.EventArgs e)
{
  system.ActorOf(Props.Create<UIWorker>().WithDispatcher("synchronized-dispatcher"), "ui-worker");
}
```

#### Common Dispatcher Configuration

The following configuration keys are available for any dispatcher configuration:

* `type` - (Required) The type of dispatcher to be used: `Dispatcher`, `TaskDispatcher`, `PinnedDispatcher`, `ForkJoinDispatcher` or `SynchronizedDispatcher`.
* `throughput` - (Required) The maximum # of messages processed each time the actor is activated. Most dispatchers default to `100`.
* `throughput-deadline-time` - The maximum amount of time to process messages when the actor is activated, or `0` for no limit. The default is `0`.

> [!NOTE]
> The throughput-deadline-time is used as a *best effort*, not as a *hard limit*. This means that if a message takes more time than the deadline allows, Akka.NET won't interrupt the process. Instead it will wait for it to finish before giving turn to the next actor.
