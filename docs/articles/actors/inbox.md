---
uid: inbox
title: Inbox
---

# The Inbox
When writing code outside of actors which shall communicate with actors, the ask pattern can be a solution (see below), but there are two things it cannot do: receiving multiple replies (e.g. by subscribing an `IActorRef` to a notification service) and watching other actors' lifecycle. For these purposes there is the `Inbox` class:
```csharp
var target = system.ActorOf(Props.Empty);
var inbox = Inbox.Create(system);

inbox.Send(target, "hello");

try
{
    inbox.Receive(TimeSpan.FromSeconds(1)).Equals("world");
}
catch (TimeoutException)
{
    // timeout
}
```

The send method wraps a normal `Tell` and supplies the internal actor's reference as the sender. This allows the reply to be received on the last line. Watching an actor is quite simple as well
```csharp
using System.Diagnostics;
...
var inbox = Inbox.Create(system);
inbox.Watch(target);
target.Tell(PoisonPill.Instance, ActorRefs.NoSender);

try
{
    Debug.Assert(inbox.Receive(TimeSpan.FromSeconds(1)) is Terminated);
}
catch (TimeoutException)
{
    // timeout
}
```