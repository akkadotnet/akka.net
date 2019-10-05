---
uid: scheduler
title: Scheduler
---
# Scheduler

Sometimes the need for making things happen in the future arises, and where do
you go look then?  Look no further than `ActorSystem`! There you find the
`Scheduler` property that returns an instance of `Akka.Actor.Scheduler`,
this instance is unique per `ActorSystem` and is used internally for scheduling
things to happen at specific points in time.

You can schedule sending of messages to actors and execution of tasks
(`Action` delegate). You will get a `Task` back. By providing a
`CancellationTokenSource` you can cancel the scheduled task.

The scheduler in Akka is designed for high-throughput of thousands up to millions of triggers. The prime use-case being triggering Actor receive timeouts, Future timeouts, circuit breakers and other time dependent events which happen all-the-time and in many instances at the same time. The implementation is based on a Hashed Wheel Timer, which is a known data structure and algorithm for handling such use cases, refer to the [Hashed and Hierarchical Timing Wheels](http://www.cs.columbia.edu/~nahum/w6998/papers/sosp87-timing-wheels.pdf) whitepaper by Varghese and Lauck if you'd like to understand its inner workings.

The Akka scheduler is **not** designed for long-term scheduling (see [Akka.Quartz.Actor](https://github.com/akkadotnet/Akka.Quartz.Actor) instead for this use-case) nor is it to be used for highly precise firing of the events. The maximum amount of time into the future you can schedule an event to trigger is around 8 months, which in practice is too much to be useful since this would assume the system never went down during that period. If you need long-term scheduling we highly recommend looking into alternative schedulers, as this is not the use-case the Akka scheduler is implemented for.

> [!WARNING]
> The default implementation of Scheduler used by Akka is based on job buckets which are emptied according to a fixed schedule. It does not execute tasks at the exact time, but on every tick, it will run everything that is (over)due. The accuracy of the default Scheduler can be modified by the `akka.scheduler.tick-duration` configuration property.

## Some examples

```csharp
var system = ActorSystem.Create("MySystem");
var someActor = system.ActorOf<SomeActor>("someActor");
var someMessage = new SomeMessage() {...};
system
   .Scheduler
   .ScheduleTellRepeatedly(TimeSpan.FromSeconds(0),
             TimeSpan.FromSeconds(5),
             someActor, someMessage, ActorRefs.NoSender); //or ActorRefs.Nobody or something else
```

The above example will schedule "someMessage" to be sent to "someActor"
every 5 seconds.

> [!WARNING]
> If you schedule a closure you should be extra careful to not pass or close over
unstable references. In practice this means that you should not call methods on
the enclosing actor from within the closure. If you need to schedule an
invocation it is better to use the `Schedule()` variant accepting a message
and an `IActorRef` to schedule a message to self (containing the necessary
parameters) and then call the method when the message is received.

## From inside the actor

```
Context.System.Scheduler.ScheduleTellRepeatedly(....);
```
> [!WARNING]
> All scheduled task will be executed when the `ActorSystem` is terminated. i.e. the task may execute before its timeout.

## The scheduler interface
The actual scheduler implementation is defined by config and loaded upon ActorSystem start-up, which means that it is possible to provide a different one using the `akka.scheduler.implementation` configuration property. The referenced class must implement the `Akka.Actor.IScheduler` and `Akka.Actor.IAdvancedScheduler` interfaces

## The cancellable interface

Scheduling a task will result in a `ICancellable` or (or throw an `Exception` if attempted after the scheduler's shutdown). This allows you to cancel something that has been scheduled for execution.

> [!WARNING]
> If the task has already been started, then this does not abort the task's execution. 
