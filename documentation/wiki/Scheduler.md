---
layout: wiki
title: Scheduler
---
# Scheduler

Sometimes the need for making things happen in the future arises, and where do
you go look then?  Look no further than ``ActorSystem``! There you find the
`Scheduler` property that returns an instance of
`Akka.Actor.Scheduler`, this instance is unique per ActorSystem and is
used internally for scheduling things to happen at specific points in time.

You can schedule sending of messages to actors and execution of tasks
(Action delegate).  You will get a ``Task`` back.
By providing a `CancellationTokenSource` you can cancel the scheduled task.

> **warning**<br/>
Scheduling timing is inexact.

##Some examples

```csharp
var system = ActorSystem.Create("MySystem");
var someActor = system.ActorOf<SomeActor>("someActor");
var someMessage = new SomeMessage() {...};
system
   .Scheduler
   .Schedule(TimeSpan.FromSeconds(0),
             TimeSpan.FromSeconds(5),
             someActor, someMessage);
```

The above example will schedule "someMessage" to be sent to "someActor" every 5 seconds.

> **warning**<br/>
If you schedule a closure you should be extra careful
to not pass or close over unstable references. In practice this means that you should
not call methods on the enclosing Actor from within the closure.
If you need to schedule an invocation it is better to use the ``Schedule()``
variant accepting a message and an ``ActorRef`` to schedule a message to self
(containing the necessary parameters) and then call the method when the message is received.