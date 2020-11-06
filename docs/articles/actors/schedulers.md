---
uid: schedulers
title: Scheduling Future and Recurring Actor Messages
---

# Scheduling Future and Recurring Actor Messages
A useful feature of Akka.NET is the ability to schedule messages to be delivered in the future or on a recurring basis. This functionality can be used for all sorts of use cases, such as:

1. Creating conditional timeouts;
2. Execute recurring tasks; or
3. Throttling or delaying work.

## Scheduling Actor Messages Using `IWithTimers`
As of Akka.NET v1.4.4 we introduced the [`IWithTimers` interface](https://getakka.net/api/Akka.Actor.IWithTimers.html), which gives Akka.NET actors a way of accessing the `ActorSystem`'s scheduler without having to remember to manually dispose of scheduled tasks afterwards. Any scheduled or recurring tasks created by the `IWithTimers` interface will be automatically cancelled once the [actor terminates](xref:supervision).

[!code-csharp[IWithTimersSample](../../../src/core/Akka.Docs.Tests/Actors/SchedulerSpecs.cs?name=TimerActor)]