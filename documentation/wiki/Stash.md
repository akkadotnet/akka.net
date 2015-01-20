## Stash
The UntypedActorWithStash class enables an actor to temporarily stash away messages that can not or should not be handled using the actor's current behavior. Upon changing the actor's message handler, i.e., right before invoking getContext().become() or getContext().unbecome(), all stashed messages can be "unstashed", thereby prepending them to the actor's mailbox. This way, the stashed messages can be processed in the same order as they have been received originally. An actor that extends UntypedActorWithStash will automatically get a deque-based mailbox.

>**Note**<br/>
The abstract class UntypedActorWithStash implements the marker interface RequiresMessageQueue<DequeBasedMessageQueueSemantics> which requests the system to automatically choose a deque based mailbox implementation for the actor. If you want more control over the mailbox, see the documentation on mailboxes: Mailboxes.

Here is an example of the UntypedActorWithStash class in action:

```csharp
import akka.actor.UntypedActorWithStash;
public class ActorWithProtocol extends UntypedActorWithStash {
  public void onReceive(Object msg) {
    if (msg.equals("open")) {
      unstashAll();
      getContext().become(new Procedure<Object>() {
        public void apply(Object msg) throws Exception {
          if (msg.equals("write")) {
            // do writing...
          } else if (msg.equals("close")) {
            unstashAll();
            getContext().unbecome();
          } else {
            stash();
          }
        }
      }, false); // add behavior on top instead of replacing
    } else {
      stash();
    }
  }
}
```

Invoking `stash()` adds the current message (the message that the actor received last) to the actor's stash. It is typically invoked when handling the default case in the actor's message handler to stash messages that aren't handled by the other cases. It is illegal to stash the same message twice; to do so results in an `IllegalStateException` being thrown. The stash may also be bounded in which case invoking `stash()` may lead to a capacity violation, which results in a `StashOverflowException`. The capacity of the stash can be configured using the stash-capacity setting (an Int) of the mailbox's configuration.

Invoking `unstashAll()` enqueues messages from the stash to the actor's mailbox until the capacity of the mailbox (if any) has been reached (note that messages from the stash are prepended to the mailbox). In case a bounded mailbox overflows, a MessageQueueAppendFailedException is thrown. The stash is guaranteed to be empty after calling unstashAll().

The stash is backed by a `scala.collection.immutable.Vector`. As a result, even a very large number of messages may be stashed without a major impact on performance.

Note that the stash is part of the ephemeral actor state, unlike the mailbox. Therefore, it should be managed like other parts of the actor's state which have the same property. The `UntypedActorWithStash` implementation of `PreRestart` will call `UnstashAll()`, which is usually the desired behavior.

>**Note**<br/>
If you want to enforce that your actor can only work with an unbounded stash, then you should use the UntypedActorWithUnboundedStash class instead.
