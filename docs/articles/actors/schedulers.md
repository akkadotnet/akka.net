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
As of Akka.NET v1.4.4 we introduced the [`IWithTimers` interface](https://getakka.net/api/Akka.Actor.IWithTimers.html), which gives Akka.NET actors a way of accessing the `ActorSystem`'s scheduler without having to remember to manually dispose of scheduled tasks afterwards. Any scheduled or recurring tasks created by the `IWithTimers` interface will be automatically cancelled once the [actor terminates](xref:supervision).

[!code-csharp[IWithTimersSample](../../../src/core/Akka.Docs.Tests/Actors/SchedulerSpecs.cs?name=TimerActor)]

In this approach, all timers are created using a specific key that can also be used to stop a timer again in the future:

[!code-csharp[StartTimers](../../../src/core/Akka.Docs.Tests/Actors/SchedulerSpecs.cs?name=StartTimers)]

The key that was used to create a timer can also be used to query whether that timer is still running or not:

[!code-csharp[CheckTimer](../../../src/core/Akka.Docs.Tests/Actors/SchedulerSpecs.cs?name=CheckTimer)]

And that key can be used to stop those timers as well:

[!code-csharp[StopTimers](../../../src/core/Akka.Docs.Tests/Actors/SchedulerSpecs.cs?name=StartTimers)]

To use the `IWithTimer` interface, simply decorate your actor class with it and call the [`Timer.StartPeriodicTimer`](https://getakka.net/api/Akka.Actor.ITimerScheduler.html#Akka_Actor_ITimerScheduler_StartPeriodicTimer_System_Object_System_Object_System_TimeSpan_) and [`Timer.StartSingleTimer`](https://getakka.net/api/Akka.Actor.ITimerScheduler.html#Akka_Actor_ITimerScheduler_StartSingleTimer_System_Object_System_Object_System_TimeSpan_) methods. All of those timers will automatically be cancelled when the actor terminates.