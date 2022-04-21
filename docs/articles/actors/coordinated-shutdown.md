---
uid: coordinated-shutdown
title: Coordinated Shutdown
---
# Coordinated Shutdown

There's an `ActorSystem` extension called `CoordinatedShutdown` that will stop certain Akka.NET actors / services and execute tasks in a programmable order during shutdown.

The default phases and their orderings are defined in the default HOCON configuration as `akka.coordinated-shutdown.phases`, and they are defined below:

[!code-hocon[Pigeon.conf](../../../src/core/Akka/Configuration/Pigeon.conf#L1019-L1091)]

## Custom Phases

As an end-user, you can register tasks to execute during any of these shutdown phases and add additional phases if you wish.

More phases can be added to an application by overriding the HOCON of an existing phase to include additional members in its `phase.depends-on` property. Here's an example where an additional phase might be executing before shutting down the cluster, for instance:

```hocon
akka.coordinated-shutdown.phases.before-cluster-shutdown.depends-on = [service-stop, my-phase]
my-phase{
    timeout = 10s
    recover = on
}
```

For each phase, the following properties can be set:

1. `depends-on` - specifies which other phases have to run successfully first before this phase can begin;
2. `timeout` - specifies how long the tasks in this phase are allowed to run before timing out;
3. `recover` - if set to `off`, if any of the tasks in this phase throw an exception then the `CoordinatedShutdown` will be aborted and no further phases will be run. If set to `on` then any thrown errors are suppressed and the system will continue to execute the `CoordinatedShutdown`.

The default phases are defined in a linear order, but in practice the phases are ordered into a directed acyclic graph (DAG) using [Topological sort](https://en.wikipedia.org/wiki/Topological_sorting).

## Registering Tasks to a Phase

For instance, if you're using [Akka.Cluster](xref:cluster-overview) it's commonplace to register application-specific cleanup tasks during the `cluster-leave` and `cluster-exiting` phases. Here's an example:

```csharp
var coordShutdown = CoordinatedShutdown.Get(myActorSystem);
coordShutdown.AddTask(CoordinatedShutdown.PhaseClusterLeave, "cleanup-my-api", () =>
{
    return _myCustomSocketApi.CloseAsync().ContinueWith(tr => Done.Instance);
});
```

Each shutdown task added to a phase must specify a function that returns a value of type `Task<Done>`. This function will be invoked once the `CoordinatedShutdown.Run()` command is executed and the `Task<Done>` returned by the function will be completed in parallel with all other tasks executing in the given phase. Those tasks may complete in any possible order; `CoordinatedShutdown` doesn't attempt to order the execution of tasks within a single phase. Once all of those tasks have completed OR if the phases timeout has expired, the next phase will begin.

Tasks should be registered as early as possible, preferably at system startup, in order to ensure that all registered tasks are run. If tasks are added after the `CoordinatedShutdown` have begun its run, it's possible that the newly registered tasks will not be executed.

## Running `CoordinatedShutdown`

There are a few different ways to start the `CoordinatedShutdown` process.

If you wish to execute the `CoordinatedShutdown` yourself, you can simply call `CoordinatedShutdown.Run(CoordinatedShutdown.Reason)`, which takes a [`CoordinatedShutdown.Reason`](/api/Akka.Actor.CoordinatedShutdown.Reason.html) argument will return a `Task<Done>`.

[!code-csharp[CoordinatedShutdownSpecs.cs](../../../src/core/Akka.Docs.Tests/Actors/CoordinatedShutdownSpecs.cs?name=coordinated-shutdown-builtin)]

It's safe to call this method multiple times as the shutdown process will only be run once and will return the same completion task each time. The `Task<Done>` will complete once all phases have run successfully, or a phase with `recover = off` failed.

> [!NOTE]
> It's possible to subclass the `CoordinatedShutdown.Reason` type and pass in a custom implementation which includes custom properties and data. This data is accessible inside the shutdown phases themselves via the [`CoordinatedShutdown.ShutdownReason` property](/api/Akka.Actor.CoordinatedShutdown.html#Akka_Actor_CoordinatedShutdown_ShutdownReason).

### Automatic `ActorSystem` and Process Termination

By default, when the final phase of the `CoordinatedShutdown` executes the calling `ActorSystem` will be terminated. This behavior can be changed by setting the following HOCON value in your configuration:

```hocon
akka.coordinated-shutdown.terminate-actor-system = off
```

If this setting is disabled (it is enabled b default), the `ActorSystem` will not be terminated as the final phase of the `CoordinatedShutdown` phases.

`CoordinatedShutdown` phases, by default, are also executed when the `ActorSystem` is terminated. You can change this behavior by disabling this HOCON value in your configuration:

```hocon
akka.coordinated-shutdown.run-by-actor-system-terminate = off
```

> [!NOTE]
> It is illegal to have _run-by-actor-system-terminate_ enabled and have _terminate-actor-system_ disabled. Having these configuration combination will raise a `ConfigurationException` during `ActorSystem` startup.

The CLR process will still be running, even when the `ActorSystem` is terminated by the `CoordinatedShutdown`. If you'd like to automatically terminate the process running your `ActorSystem`, you can set the following HOCON value in your configuration:

```hocon
akka.coordinated-shutdown.exit-clr = on
```

If this setting is enabled (it's disabled by default), you'll be able to shutdown the current running process automatically via an `Environment.Exit(0)` call made during the final phase of the `CoordinatedShutdown`.

### `CoordinatedShutdown` and Akka.Cluster

If you're using Akka.Cluster, the `CoordinatedShutdown` will automatically register tasks for completing the following:

1. Gracefully leaving the cluster;
2. Gracefully handing over / terminating ClusterSingleton and Cluster.Sharding instances; and
3. Terminating the `Cluster` system itself.

By default, this graceful leave action will by triggered whenever the `CoordinatedShutdown.Run(Reason)` method is called. Conversely, calling `Cluster.Leave` on a cluster member will also cause the `CoordinatedShutdown` to run and will terminate the `ActorSystem` once the node has left the cluster.

`CoordinatedShutdown.Run()` will also be executed if a node is removed via `Cluster.Down` (non-graceful exit), but this can be disabled by changing the following Akka.Cluster HOCON setting:

```hocon
akka.cluster.run-coordinated-shutdown-when-down = off
```

### Invoking `CoordinatedShutdown.Run()` on Process Exit

By default `CoordinatedShutdown.Run()` will be called whenever the current process attempts to exit (using the `AppDomain.ProcessExit` event hook) and this will give the `ActorSystem` and the underlying clustering tools an opportunity to cleanup gracefully before the process finishes exiting.

If you wish to disable this behavior, you can pass in the following HOCON configuration value:

```hocon
akka.coordinated-shutdown.run-by-clr-shutdown-hook = off
```
