---
layout: wiki
title: Actor lifecycle
---
## Actor Lifecycle

![Actor lifecycle](images/actor_lifecycle.png)

A path in an actor system represents a "place" which might be occupied by a living actor. Initially (apart from system initialized actors) a path is empty. When `ActorOf()` is called it assigns an incarnation of the actor described by the passed `Props` to the given path. An actor incarnation is identified by the path and a UID. A restart only swaps the Actor instance defined by the Props but the incarnation and hence the UID remains the same.

The lifecycle of an incarnation ends when the actor is stopped. At that point the appropriate lifecycle events are called and watching actors are notified of the termination. After the incarnation is stopped, the path can be reused again by creating an actor with `ActorOf()`. In this case the name of the new incarnation will be the same as the previous one but the UIDs will differ.

An `ActorRef` always represents an incarnation (path and UID) not just a given path. Therefore if an actor is stopped and a new one with the same name is created an ActorRef of the old incarnation will not point to the new one.

`ActorSelection` on the other hand points to the path (or multiple paths if wildcards are used) and is completely oblivious to which incarnation is currently occupying it. `ActorSelection` cannot be watched for this reason. It is possible to resolve the current incarnation's ActorRef living under the path by sending an `Identify` message to the `ActorSelection` which will be replied to with an `ActorIdentity` containing the correct reference (see Identifying Actors via Actor Selection). This can also be done with the resolveOne method of the `ActorSelection`, which returns a `Task` of the matching `ActorRef`.

### Lifecycle Monitoring aka DeathWatch
In order to be notified when another actor terminates (i.e. stops permanently, not temporary failure and restart), an actor may register itself for reception of the Terminated message dispatched by the other actor upon termination (see Stopping Actors). This service is provided by the DeathWatch component of the actor system.

Registering a monitor is easy (see fourth line, the rest is for demonstrating the whole functionality):

```csharp
public class WatchActor : UntypedActor
{
    public WatchActor()
    {
        Context.Watch(child);
    }

    ActorRef child = Context.ActorOf(Props.Empty, "child");
    ActorRef lastSender = Context.System.DeadLetters;

    protected override void OnReceive(object message)
    {
        if (message.Equals("kill"))
        {
            Context.Stop(child);
            lastSender = Sender;
        }
        else if (message is Terminated)
        {
            var t = (Terminated)message;

            if (t.ActorRef == child)
            {
                lastSender.Tell("finished");
            }
        }
        else
        {
            Unhandled(message);
        }
    }
}
```

It should be noted that the Terminated message is generated independent of the order in which registration and termination occur. In particular, the watching actor will receive a Terminated message even if the watched actor has already been terminated at the time of registration.

Registering multiple times does not necessarily lead to multiple messages being generated, but there is no guarantee that only exactly one such message is received: if termination of the watched actor has generated and queued the message, and another registration is done before this message has been processed, then a second message will be queued, because registering for monitoring of an already terminated actor leads to the immediate generation of the Terminated message.

It is also possible to deregister from watching another actor’s liveliness using Context.Unwatch(target). This works even if the Terminated message has already been enqueued in the mailbox; after calling unwatch no Terminated message for that actor will be processed anymore.

### Start Hook
Right after starting the actor, its PreStart method is invoked.

```csharp
protected override void PreStart()
{
    child = Context.ActorOf(Props.Empty);
}
```

This method is called when the actor is first created. During restarts it is called by the default implementation of postRestart, which means that by overriding that method you can choose whether the initialization code in this method is called only exactly once for this actor or for every restart. Initialization code which is part of the actor’s constructor will always be called when an instance of the actor class is created, which happens at every restart.

### Restart Hooks
All actors are supervised, i.e. linked to another actor with a fault handling strategy. Actors may be restarted in case an exception is thrown while processing a message (see Supervision and Monitoring). This restart involves the hooks mentioned above:

The old actor is informed by calling preRestart with the exception which caused the restart and the message which triggered that exception; the latter may be None if the restart was not caused by processing a message, e.g. when a supervisor does not trap the exception and is restarted in turn by its supervisor, or if an actor is restarted due to a sibling’s failure. If the message is available, then that message’s sender is also accessible in the usual way (i.e. by calling the Sender property).

This method is the best place for cleaning up, preparing hand-over to the fresh actor instance, etc. By default it stops all children and calls postStop.

The initial factory from the actorOf call is used to produce the fresh instance.

The new actor’s postRestart method is invoked with the exception which caused the restart. By default the preStart is called, just as in the normal start-up case.

An actor restart replaces only the actual actor object; the contents of the mailbox is unaffected by the restart, so processing of messages will resume after the postRestart hook returns. The message that triggered the exception will not be received again. Any message sent to an actor while it is being restarted will be queued to its mailbox as usual.

>**Warning**<br/>
Be aware that the ordering of failure notifications relative to user messages is not deterministic. In particular, a parent might restart its child before it has processed the last messages sent by the child before the failure. See Discussion: Message Ordering for details.

### Stop Hook
After stopping an actor, its postStop hook is called, which may be used e.g. for deregistering this actor from other services. This hook is guaranteed to run after message queuing has been disabled for this actor, i.e. messages sent to a stopped actor will be redirected to the deadLetters of the ActorSystem.