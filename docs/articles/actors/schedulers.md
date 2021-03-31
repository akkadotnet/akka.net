---
uid: schedulers
title: Scheduling Future and Recurring Actor Messages
---

# Scheduling Future and Recurring Actor Messages
A useful feature of Akka.NET is the ability to schedule messages to be delivered in the future or on a recurring basis. This functionality can be used for all sorts of use cases, such as:

1. Creating conditional timeouts;
2. Execute recurring tasks; or
3. Throttling or delaying work.

## Scheduling Actor Messages Using `IWithTimers` (Recommended Approach)
As of Akka.NET v1.4.4 we introduced the [`IWithTimers` interface](xref:Akka.Actor.IWithTimers), which gives Akka.NET actors a way of accessing the `ActorSystem`'s scheduler without having to remember to manually dispose of scheduled tasks afterwards. Any scheduled or recurring tasks created by the `IWithTimers` interface will be automatically cancelled once the [actor terminates](xref:supervision).

[!code-csharp[IWithTimersSample](../../../src/core/Akka.Docs.Tests/Actors/SchedulerSpecs.cs?name=TimerActor)]

In this approach, all timers are created using a specific key that can also be used to stop a timer again in the future:

[!code-csharp[StartTimers](../../../src/core/Akka.Docs.Tests/Actors/SchedulerSpecs.cs?name=StartTimers)]

The key that was used to create a timer can also be used to query whether that timer is still running or not:

[!code-csharp[CheckTimer](../../../src/core/Akka.Docs.Tests/Actors/SchedulerSpecs.cs?name=CheckTimer)]

And that key can be used to stop those timers as well:

[!code-csharp[StopTimers](../../../src/core/Akka.Docs.Tests/Actors/SchedulerSpecs.cs?name=StartTimers)]

To use the `IWithTimer` interface, simply decorate your actor class with it and call the [`Timer.StartPeriodicTimer`](xref:Akka.Actor.ITimerScheduler#Akka_Actor_ITimerScheduler_StartPeriodicTimer_System_Object_System_Object_System_TimeSpan_) and [`Timer.StartSingleTimer`](xref:Akka.Actor.ITimerScheduler#Akka_Actor_ITimerScheduler_StartSingleTimer_System_Object_System_Object_System_TimeSpan_) methods. All of those timers will automatically be cancelled when the actor terminates.

### Testing for Idle Timeouts with `ReceiveTimeout`
One specific case with actors, and this is particularly useful for areas like [Akka.Cluster.Sharding](xref:cluster-sharding), is the ability to time out "idle" actors after a specified period of inactivity.

This can be accomplished using the `ReceiveTimeout` capability.

[!code-csharp[ReceiveTimeout](../../../src/core/Akka.Docs.Tests/Actors/ReceiveTimeoutSpecs.cs?name=ReceiveTimeoutActor)]

* `ReceiveTimeout` is a sliding window timeout - the timeout gets reset every time an actor receives a message that does not implement the [`INotInfluenceReceiveTimeout` interface](xref:Akka.Actor.INotInfluenceReceiveTimeout) the timer is reset back to its original duration.
* If the timeout expires, the actor will be notified by receiving a copy of the `ReceiveTimeout` message - at this stage the actor can do things like shut itself down, flush its state to a database, or whatever else you might need the actor to do once it becomes idle.
* The `SetReceiveTimeout(TimeSpan? time = null)` value can be changed at runtime or it can be cancelled altogether by calling `Context.SetReceiveTimeout(null)`; and
* The `ReceiveTimeout` will automatically be cancelled when the actor terminates.

## Scheduling Recurring Tasks with `IScheduler`
While the `IWithTimers` interface is the recommended approach for working with actors, the `ActorSystem` itself includes the underlying [`IScheduler` interface](xref:Akka.Actor.IScheduler), which exposes timing primitives that can be used inside or outside of individual actors.

[!code-csharp[Scheduler](../../../src/core/Akka.Docs.Tests/Actors/SchedulerSpecs.cs?name=Scheduler)]

The `ActorSystem.Scheduler` can be used for any number of different types of tasks, but those tasks will not be cancelled automatically. You have to call the `IScheduler.Schedule_{method}_RepeatedlyCancelable` method, store the `ICancelable` returned by that method, and then call `ICancelable.Cancel()` once you're finished with it to dispose the method.

To learn more about working with the `IScheduler`, please see [Akka.NET Bootcamp Unit 2 Lesson 3 - Using the Scheduler to Send Messages Later](https://github.com/petabridge/akka-bootcamp/blob/master/src/Unit-2/lesson3/README.md).