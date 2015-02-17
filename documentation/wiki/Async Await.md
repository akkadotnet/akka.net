---
layout: wiki
title: Async Await
---

# Akka.NET and Async Await

To enable Async Await support in an Akka.NET actor, you will need to configure your arctor to use the `task-dispatcher`.
This can be accomplished like this:

```csharp
var actor = Sys.ActorOf(Props.Create<AsyncAwaitActor>()
           .WithDispatcher("akka.actor.task-dispatcher"));
```

When the `task-dispatcher` is active, every incoming message to the actor will first be processed through the dispatcher itself.
The dispatcher will apply the "async state" that is needed for async await operations to work.
