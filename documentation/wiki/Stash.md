---
layout: wiki
title: Stash
---
## Stash

An actor can temporarily stash away messages that cannot or should not be handled using the actor's current behavior. Upon changing the actor's message handler, i.e., right before invoking `Become` or `Unbecome`, all stashed messages can be "unstashed", thereby prepending them to the actor's mailbox. This way, the stashed messages can be processed in the same order as they have been received originally.

In Akka.NET, this functionality is provided by two interfaces:

* *WithUnboundedStash* - uses a stash with unlimited storage
* *WithBoundedStash* - uses a stash with limited storage

When an actor implements one of these interfaces, it will automatically get a deque-based mailbox.

These interfaces require you to provide a Stash property on your actor. The property will be automatically populated with an appropriated implementation during the actor's creation:

```csharp
public class MyActorWithStash : UntypedActor, WithUnboundedStash
{
    public IStash Stash { get; set; }
    
    ...
}
```

Here is an example of a stash in action:

```csharp
public class ActorWithProtocol : UntypedActor, WithUnboundedStash
{
    public IStash Stash { get; set; }

    protected override void OnReceive(object message)
    {
        if (message.Equals("open"))
        {
            Stash.UnstashAll();

            Become(m =>
            {
                if (m.Equals("write"))
                {
                    // do writing
                }
                else if (m.Equals("close"))
                {
                    Unbecome();
                }
            }, false); // add behavior on top instead of replacing
        }
        else
        {
            Stash.Stash();
        }
    }
}
```

Invoking `Stash()` adds the current message (the message that the actor received last) to the actor's stash. It is typically invoked when handling the default case in the actor's message handler to stash messages that aren't handled by the other cases. It is illegal to stash the same message twice; to do so results in an `IllegalActorStateException` being thrown. The stash may also be bounded in which case invoking `Stash()` may lead to a capacity violation, which results in a `StashOverflowException`. The capacity of the stash can be configured using the stash-capacity setting (an int) of the mailbox's configuration.

Invoking `UnstashAll()` enqueues messages from the stash to the actor's mailbox until the capacity of the mailbox (if any) has been reached (note that messages from the stash are prepended to the mailbox). In case a bounded mailbox overflows, a MessageQueueAppendFailedException is thrown. The stash is guaranteed to be empty after calling UnstashAll().

>**Note**<br/> The stash is part of the ephemeral actor state, unlike the mailbox. Therefore, it should be managed like other parts of the actor's state which have the same property.

It is common to call `UnstashAll()` in the `PreRestart` event, so when the actor restarts, it will process any pending message.
