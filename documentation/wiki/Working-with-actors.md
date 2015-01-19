# Working with actors

The Actor Model provides a higher level of abstraction for writing concurrent and distributed systems. It alleviates the developer from having to deal with explicit locking and thread management, making it easier to write correct concurrent and parallel systems. Actors were defined in the 1973 paper by Carl Hewitt but have been popularized by the Erlang language, and used for example at Ericsson with great success to build highly concurrent and reliable telecom systems.

## Creating Actors
>**Note**<br/>
Since Akka.NET enforces parental supervision every actor is supervised and (potentially) the supervisor of its children, it is advisable that you familiarize yourself with [Actor Systems](Actor Systems) and [Supervision and Monitoring](Supervision) and it may also help to read [Actor References, Paths and Addresses](Addressing).

### Defining an Actor class
Actors in C# are implemented by extending the `ReceiveActor` class and configuring what messages to receive using the `Receive<TMessage>` method.

Here is an example:

```csharp
using Akka;
using Akka.Actor;
using Akka.Event;
 
public class MyActor: ReceiveActor
{
  LoggingAdapter log = Logging.GetLogger(Context);
 
  public MyActor()
  {
    Receive<string>(message => {
      log.Info("Received String message: {0}", message);
      Sender.Tell(message);
    });  
    Receive<SomeMessage(message => {...});
  }
}
```

### The Inbox
When writing code outside of actors which shall communicate with actors, the ask pattern can be a solution (see below), but there are two thing it cannot do: receiving multiple replies (e.g. by subscribing an `ActorRef` to a notification service) and watching other actors’ lifecycle. For these purposes there is the Inbox class:

```csharp
final Inbox inbox = Inbox.create(system);
inbox.send(target, "hello");
try {
  assert inbox.receive(Duration.create(1, TimeUnit.SECONDS)).equals("world");
} catch (java.util.concurrent.TimeoutException e) {
  // timeout
}
```
The send method wraps a normal tell and supplies the internal actor’s reference as the sender. This allows the reply to be received on the last line. Watching an actor is quite simple as well:

```csharp
final Inbox inbox = Inbox.create(system);
inbox.watch(target);
target.tell(PoisonPill.getInstance(), ActorRef.noSender());
try {
  assert inbox.receive(Duration.create(1, TimeUnit.SECONDS)) instanceof Terminated;
} catch (java.util.concurrent.TimeoutException e) {
  // timeout
}
```
## UntypedActor API
The `UntypedActor` class defines only one abstract method, the above mentioned `OnReceive(object message)`, which implements the behavior of the actor.

If the current actor behavior does not match a received message, it's recommended that you call the unhandled method, which by default publishes a new `Akka.Actor.UnhandledMessage(message, sender, recipient)` on the actor system’s event stream (set configuration item `akka.actor.debug.unhandled` to on to have them converted into actual `Debug` messages).

In addition, it offers:

* `Self` reference to the `ActorRef` of the actor

* `Sender` reference sender Actor of the last received message, typically used as described in Reply to messages

* `SupervisorStrategy` user overridable definition the strategy to use for supervising child actors

This strategy is typically declared inside the actor in order to have access to the actor’s internal state within the decider function: since failure is communicated as a message sent to the supervisor and processed like other messages (albeit outside of the normal behavior), all values and variables within the actor are available, as is the getSender() reference (which will be the immediate child reporting the failure; if the original failure occurred within a distant descendant it is still reported one level up at a time).

* `Context` exposes contextual information for the actor and the current message, such as:

  * factory methods to create child actors (actorOf)
  * system that the actor belongs to
  * parent supervisor
  * supervised children
  * lifecycle monitoring
  * hotswap behavior stack as described in HotSwap

The remaining visible methods are user-overridable life-cycle hooks which are described in the following:

```csharp
public override void PreStart() {
}
 
protected override void PreRestart(Exceptionreason, object message) {
  foreach (ActorRef each in Context.GetChildren()) {
    Context.Unwatch(each);
    Context.Stop(each);
  }
  PostStop();
}
 
protected override void PostRestart(Exception reason) {
  PreStart();
}
 
protected override void PostStop() {
}
```
The implementations shown above are the defaults provided by the UntypedActor class.


## Identifying Actors via Actor Selection
As described in Actor References, Paths and Addresses, each actor has a unique logical path, which is obtained by following the chain of actors from child to parent until reaching the root of the actor system, and it has a physical path, which may differ if the supervision chain includes any remote supervisors. These paths are used by the system to look up actors, e.g. when a remote message is received and the recipient is searched, but they are also useful more directly: actors may look up other actors by specifying absolute or relative paths—logical or physical—and receive back an ActorSelection with the result:

```csharp
// will look up this absolute path
Context.ActorSelection("/user/serviceA/actor");
// will look up sibling beneath same supervisor
Context.ActorSelection("../joe");
```
The supplied path is parsed as a `System.URI`, which basically means that it is split on / into path elements. If the path starts with /, it is absolute and the look-up starts at the root guardian (which is the parent of "/user"); otherwise it starts at the current actor. If a path element equals .., the look-up will take a step “up” towards the supervisor of the currently traversed actor, otherwise it will step “down” to the named child. It should be noted that the .. in actor paths here always means the logical structure, i.e. the supervisor.

The path elements of an actor selection may contain wildcard patterns allowing for broadcasting of messages to that section:

```csharp
// will look all children to serviceB with names starting with worker
Context.ActorSelection("/user/serviceB/worker*");
// will look up all siblings beneath same supervisor
Context.ActorSelection("../*");
```
Messages can be sent via the `ActorSelection` and the path of the `ActorSelection`is looked up when delivering each message. If the selection does not match any actors the message will be dropped.

To acquire an `AtorRef` for an `ActorSelection` you need to send a message to the selection and use the `Sender` reference of the reply from the actor. There is a built-in Identify message that all Actors will understand and automatically reply to with a `ActorIdentity` message containing the ActorRef. This message is handled specially by the actors which are traversed in the sense that if a concrete name lookup fails (i.e. a non-wildcard path element does not correspond to a live actor) then a negative result is generated. Please note that this does not mean that delivery of that reply is guaranteed, it still is a normal message.

```csharp
using Akka;
using Akka.Actor;

public class Follower : UntypedActor {
  final String identifyId = "1";
  {
    ActorSelection selection =
      getContext().actorSelection("/user/another");
    selection.tell(new Identify(identifyId), getSelf());
  }
  ActorRef another;
  
  final ActorRef probe;
  public Follower(ActorRef probe) {
    this.probe = probe;
  }
 
  @Override
  public void onReceive(Object message) {
    if (message instanceof ActorIdentity) {
      ActorIdentity identity = (ActorIdentity) message;
      if (identity.correlationId().equals(identifyId)) {
        ActorRef ref = identity.getRef();
        if (ref == null)
          getContext().stop(getSelf());
        else {
          another = ref;
          getContext().watch(another);
          probe.tell(ref, getSelf());
        }
      }
    } else if (message instanceof Terminated) {
      final Terminated t = (Terminated) message;
      if (t.getActor().equals(another)) {
        getContext().stop(getSelf());
      }
    } else {
      unhandled(message);
    }
  }
}
```
You can also acquire an ActorRef for an ActorSelection with the resolveOne method of the ActorSelection. It returns a Future of the matching ActorRef if such an actor exists. It is completed with failure `akka.actor.ActorNotFound` if no such actor exists or the identification didn't complete within the supplied timeout.

Remote actor addresses may also be looked up, if remoting is enabled:

```csharp
Context().ActorSelection("akka.tcp://app@otherhost:1234/user/serviceB");
```
An example demonstrating remote actor look-up is given in Remoting Sample.

>**Note**<br/>
actorFor is deprecated in favor of actorSelection because actor references acquired with actorFor behave differently for local and remote actors. In the case of a local actor reference, the named actor needs to exist before the lookup, or else the acquired reference will be an EmptyLocalActorRef. This will be true even if an actor with that exact path is created after acquiring the actor reference. For remote actor references acquired with actorFor the behaviour is different and sending messages to such a reference will under the hood look up the actor by path on the remote system for every message send.

## Messages and immutability
**IMPORTANT:** Messages can be any kind of object but have to be immutable. Akka can’t enforce immutability (yet) so this has to be by convention.

Here is an example of an immutable message:

```csharp
public class ImmutableMessage {
  private final int sequenceNumber;
  private final List<String> values;
 
  public ImmutableMessage(int sequenceNumber, List<String> values) {
    this.sequenceNumber = sequenceNumber;
    this.values = Collections.unmodifiableList(new ArrayList<String>(values));
  }
 
  public int getSequenceNumber() {
    return sequenceNumber;
  }
 
  public List<String> getValues() {
    return values;
  }
}
```
## Send messages
Messages are sent to an Actor through one of the following methods.

tell means “fire-and-forget”, e.g. send a message asynchronously and return immediately.
ask sends a message asynchronously and returns a Future representing a possible reply.
Message ordering is guaranteed on a per-sender basis.

>**Note**<br/>
There are performance implications of using ask since something needs to keep track of when it times out, there needs to be something that bridges a Promise into an ActorRef and it also needs to be reachable through remoting. So always prefer tell for performance, and only ask if you must.

In all these methods you have the option of passing along your own ActorRef. Make it a practice of doing so because it will allow the receiver actors to be able to respond to your message, since the sender reference is sent along with the message.

### Tell: Fire-forget
This is the preferred way of sending messages. No blocking waiting for a message. This gives the best concurrency and scalability characteristics.

```csharp
// don’t forget to think about who is the sender (2nd argument)
target.tell(message, getSelf());
```
The sender reference is passed along with the message and available within the receiving actor via its getSender method while processing this message. Inside of an actor it is usually getSelf who shall be the sender, but there can be cases where replies shall be routed to some other actor—e.g. the parent—in which the second argument to tell would be a different one. Outside of an actor and if no reply is needed the second argument can be null; if a reply is needed outside of an actor you can use the ask-pattern described next..

### Ask: Send-And-Receive-Future
The ask pattern involves actors as well as futures, hence it is offered as a use pattern rather than a method on ActorRef:

```csharp
import static akka.pattern.Patterns.ask;
import static akka.pattern.Patterns.pipe;
import scala.concurrent.Future;
import scala.concurrent.duration.Duration;
import akka.dispatch.Futures;
import akka.dispatch.Mapper;
import akka.util.Timeout;
final Timeout t = new Timeout(Duration.create(5, TimeUnit.SECONDS));
 
final ArrayList<Future<Object>> futures = new ArrayList<Future<Object>>();
futures.add(ask(actorA, "request", 1000)); // using 1000ms timeout
futures.add(ask(actorB, "another request", t)); // using timeout from
                                                // above
 
final Future<Iterable<Object>> aggregate = Futures.sequence(futures,
    system.dispatcher());
 
final Future<Result> transformed = aggregate.map(
    new Mapper<Iterable<Object>, Result>() {
      public Result apply(Iterable<Object> coll) {
        final Iterator<Object> it = coll.iterator();
        final String x = (String) it.next();
        final String s = (String) it.next();
        return new Result(x, s);
      }
    }, system.dispatcher());
 
pipe(transformed, system.dispatcher()).to(actorC);
```
This example demonstrates ask together with the pipe pattern on futures, because this is likely to be a common combination. Please note that all of the above is completely non-blocking and asynchronous: ask produces a Future, two of which are composed into a new future using the Futures.sequence and map methods and then pipe installs an onComplete-handler on the future to effect the submission of the aggregated Result to another actor.

Using ask will send a message to the receiving Actor as with tell, and the receiving actor must reply with getSender().tell(reply, getSelf()) in order to complete the returned Future with a value. The ask operation involves creating an internal actor for handling this reply, which needs to have a timeout after which it is destroyed in order not to leak resources; see more below.

>**Warning**<br/>
To complete the future with an exception you need send a Failure message to the sender. This is not done automatically when an actor throws an exception while processing a message.

```csharp
try {
  String result = operation();
  getSender().tell(result, getSelf());
} catch (Exception e) {
  getSender().tell(new akka.actor.Status.Failure(e), getSelf());
  throw e;
}
```
If the actor does not complete the future, it will expire after the timeout period, specified as parameter to the ask method; this will complete the Future with an AskTimeoutException.

See Futures for more information on how to await or query a future.

The onComplete, onSuccess, or onFailure methods of the Future can be used to register a callback to get a notification when the Future completes. Gives you a way to avoid blocking.

>**Warning**<br/>
When using future callbacks, inside actors you need to carefully avoid closing over the containing actor’s reference, i.e. do not call methods or access mutable state on the enclosing actor from within the callback. This would break the actor encapsulation and may introduce synchronization bugs and race conditions because the callback will be scheduled concurrently to the enclosing actor. Unfortunately there is not yet a way to detect these illegal accesses at compile time. See also: Actors and shared mutable state

### Forward message
You can forward a message from one actor to another. This means that the original sender address/reference is maintained even though the message is going through a 'mediator'. This can be useful when writing actors that work as routers, load-balancers, replicators etc. You need to pass along your context variable as well.

```csharp
target.Forward(result, Context());
```
## Receive messages
When an actor receives a message it is passed into the onReceive method, this is an abstract method on the UntypedActor base class that needs to be defined.

Here is an example:
```csharp
import akka.actor.UntypedActor;
import akka.event.Logging;
import akka.event.LoggingAdapter;
 
public class MyUntypedActor extends UntypedActor {
  LoggingAdapter log = Logging.getLogger(getContext().system(), this);
 
  public void onReceive(Object message) throws Exception {
    if (message instanceof String) {
      log.info("Received String message: {}", message);
      getSender().tell(message, getSelf());
    } else
      unhandled(message);
  }
}
```
An alternative to using if-instanceof checks is to use Apache Commons MethodUtils to invoke a named method whose parameter type matches the message type.

## Reply to messages
If you want to have a handle for replying to a message, you can use getSender(), which gives you an ActorRef. You can reply by sending to that ActorRef with getSender().tell(replyMsg, getSelf()). You can also store the ActorRef for replying later, or passing on to other actors. If there is no sender (a message was sent without an actor or future context) then the sender defaults to a 'dead-letter' actor ref.
```csharp
@Override
public void onReceive(Object msg) {
  Object result =
      // calculate result ...
  
  // do not forget the second argument!
  getSender().tell(result, getSelf());
}
```
## Stopping actors
Actors are stopped by invoking the stop method of a ActorRefFactory, i.e. ActorContext or ActorSystem. Typically the context is used for stopping child actors and the system for stopping top level actors. The actual termination of the actor is performed asynchronously, i.e. stop may return before the actor is stopped.

Processing of the current message, if any, will continue before the actor is stopped, but additional messages in the mailbox will not be processed. By default these messages are sent to the deadLetters of the ActorSystem, but that depends on the mailbox implementation.

Termination of an actor proceeds in two steps: first the actor suspends its mailbox processing and sends a stop command to all its children, then it keeps processing the internal termination notifications from its children until the last one is gone, finally terminating itself (invoking postStop, dumping mailbox, publishing Terminated on the DeathWatch, telling its supervisor). This procedure ensures that actor system sub-trees terminate in an orderly fashion, propagating the stop command to the leaves and collecting their confirmation back to the stopped supervisor. If one of the actors does not respond (i.e. processing a message for extended periods of time and therefore not receiving the stop command), this whole process will be stuck.

Upon ActorSystem.shutdown, the system guardian actors will be stopped, and the aforementioned process will ensure proper termination of the whole system.

The postStop hook is invoked after an actor is fully stopped. This enables cleaning up of resources:
```csharp
@Override
public void postStop() {
  // clean up resources here ...
}
```
>**Note**<br/>
Since stopping an actor is asynchronous, you cannot immediately reuse the name of the child you just stopped; this will result in an InvalidActorNameException. Instead, watch the terminating actor and create its replacement in response to the Terminated message which will eventually arrive.

### PoisonPill
You can also send an actor the akka.actor.PoisonPill message, which will stop the actor when the message is processed. PoisonPill is enqueued as ordinary messages and will be handled after messages that were already queued in the mailbox.

Use it like this:

```csharp
myActor.Tell(Akka.Actor.PoisonPill.Instance, Sender);
```
### Graceful Stop
gracefulStop is useful if you need to wait for termination or compose ordered termination of several actors:

```csharp
import static akka.pattern.Patterns.gracefulStop;
import scala.concurrent.Await;
import scala.concurrent.Future;
import scala.concurrent.duration.Duration;
import akka.pattern.AskTimeoutException;
try {
  Future<Boolean> stopped =
    gracefulStop(actorRef, Duration.create(5, TimeUnit.SECONDS), Manager.SHUTDOWN);
  Await.result(stopped, Duration.create(6, TimeUnit.SECONDS));
  // the actor has been stopped
} catch (AskTimeoutException e) {
  // the actor wasn't stopped within 5 seconds
}
public class Manager extends UntypedActor {
  
  public static final String SHUTDOWN = "shutdown";
  
  ActorRef worker = getContext().watch(getContext().actorOf(
      Props.create(Cruncher.class), "worker"));
  
  public void onReceive(Object message) {
    if (message.equals("job")) {
      worker.tell("crunch", getSelf());
    } else if (message.equals(SHUTDOWN)) {
      worker.tell(PoisonPill.getInstance(), getSelf());
      getContext().become(shuttingDown);
    }
  }
  
  Procedure<Object> shuttingDown = new Procedure<Object>() {
    @Override
    public void apply(Object message) {
      if (message.equals("job")) {
        getSender().tell("service unavailable, shutting down", getSelf());
      } else if (message instanceof Terminated) {
        getContext().stop(getSelf());
      }
    }
  };
}
```
When `GracefulStop()` returns successfully, the actor’s `PostStop()` hook will have been executed: there exists a happens-before edge between the end of postStop() and the return of gracefulStop().

In the above example a custom Manager.SHUTDOWN message is sent to the target actor to initiate the process of stopping the actor. You can use PoisonPill for this, but then you have limited possibilities to perform interactions with other actors before stopping the target actor. Simple cleanup tasks can be handled in postStop.

>**Warning**<br/>
Keep in mind that an actor stopping and its name being deregistered are separate events which happen asynchronously from each other. Therefore it may be that you will find the name still in use after gracefulStop() returned. In order to guarantee proper deregistration, only reuse names from within a supervisor you control and only in response to a Terminated message, i.e. not for top-level actors.

## HotSwap
### Upgrade
Akka supports hotswapping the Actor’s message loop (e.g. its implementation) at runtime. Use the getContext().become method from within the Actor. The hotswapped code is kept in a Stack which can be pushed (replacing or adding at the top) and popped.

>**Warning**<br/>
Please note that the actor will revert to its original behavior when restarted by its Supervisor.

To hotswap the Actor using `getContext().become`:

```csharp
import akka.japi.Procedure;
public class HotSwapActor extends UntypedActor {
 
  Procedure<Object> angry = new Procedure<Object>() {
    @Override
    public void apply(Object message) {
      if (message.equals("bar")) {
        getSender().tell("I am already angry?", getSelf());
      } else if (message.equals("foo")) {
        getContext().become(happy);
      }
    }
  };
 
  Procedure<Object> happy = new Procedure<Object>() {
    @Override
    public void apply(Object message) {
      if (message.equals("bar")) {
        getSender().tell("I am already happy :-)", getSelf());
      } else if (message.equals("foo")) {
        getContext().become(angry);
      }
    }
  };
 
  public void onReceive(Object message) {
    if (message.equals("bar")) {
      getContext().become(angry);
    } else if (message.equals("foo")) {
      getContext().become(happy);
    } else {
      unhandled(message);
    }
  }
}
```
This variant of the become method is useful for many different things, such as to implement a Finite State Machine (FSM). It will replace the current behavior (i.e. the top of the behavior stack), which means that you do not use unbecome, instead always the next behavior is explicitly installed.

The other way of using become does not replace but add to the top of the behavior stack. In this case care must be taken to ensure that the number of “pop” operations (i.e. unbecome) matches the number of “push” ones in the long run, otherwise this amounts to a memory leak (which is why this behavior is not the default).

```csharp
public class UntypedActorSwapper {
 
  public static class Swap {
    public static Swap SWAP = new Swap();
 
    private Swap() {
    }
  }
 
  public static class Swapper extends UntypedActor {
    LoggingAdapter log = Logging.getLogger(getContext().system(), this);
 
    public void onReceive(Object message) {
      if (message == SWAP) {
        log.info("Hi");
        getContext().become(new Procedure<Object>() {
          @Override
          public void apply(Object message) {
            log.info("Ho");
            getContext().unbecome(); // resets the latest 'become'
          }
        }, false); // this signals stacking of the new behavior
      } else {
        unhandled(message);
      }
    }
  }
 
  public static void main(String... args) {
    ActorSystem system = ActorSystem.create("MySystem");
    ActorRef swap = system.actorOf(Props.create(Swapper.class));
    swap.tell(SWAP, ActorRef.noSender()); // logs Hi
    swap.tell(SWAP, ActorRef.noSender()); // logs Ho
    swap.tell(SWAP, ActorRef.noSender()); // logs Hi
    swap.tell(SWAP, ActorRef.noSender()); // logs Ho
    swap.tell(SWAP, ActorRef.noSender()); // logs Hi
    swap.tell(SWAP, ActorRef.noSender()); // logs Ho
  }
 
}
```

## Killing an Actor
You can kill an actor by sending a Kill message. This will cause the actor to throw a ActorKilledException, triggering a failure. The actor will suspend operation and its supervisor will be asked how to handle the failure, which may mean resuming the actor, restarting it or terminating it completely. See What Supervision Means for more information.

Use `Kill` like this:

```csharp
victim.Tell(Akka.Actor.Kill.Instance, ActorRef.NoSender);
```
## Actors and exceptions
It can happen that while a message is being processed by an actor, that some kind of exception is thrown, e.g. a database exception.

### What happens to the Message
If an exception is thrown while a message is being processed (i.e. taken out of its mailbox and handed over to the current behavior), then this message will be lost. It is important to understand that it is not put back on the mailbox. So if you want to retry processing of a message, you need to deal with it yourself by catching the exception and retry your flow. Make sure that you put a bound on the number of retries since you don't want a system to livelock (so consuming a lot of cpu cycles without making progress). Another possibility would be to have a look at the PeekMailbox pattern.

### What happens to the mailbox
If an exception is thrown while a message is being processed, nothing happens to the mailbox. If the actor is restarted, the same mailbox will be there. So all messages on that mailbox will be there as well.

### What happens to the actor
If code within an actor throws an exception, that actor is suspended and the supervision process is started (see Supervision and Monitoring). Depending on the supervisor’s decision the actor is resumed (as if nothing happened), restarted (wiping out its internal state and starting from scratch) or terminated.

## Initialization patterns
The rich lifecycle hooks of Actors provide a useful toolkit to implement various initialization patterns. During the lifetime of an ActorRef, an actor can potentially go through several restarts, where the old instance is replaced by a fresh one, invisibly to the outside observer who only sees the ActorRef.

One may think about the new instances as "incarnations". Initialization might be necessary for every incarnation of an actor, but sometimes one needs initialization to happen only at the birth of the first instance when the ActorRef is created. The following sections provide patterns for different initialization needs.

### Initialization via constructor
Using the constructor for initialization has various benefits. First of all, it makes it possible to use val fields to store any state that does not change during the life of the actor instance, making the implementation of the actor more robust. The constructor is invoked for every incarnation of the actor, therefore the internals of the actor can always assume that proper initialization happened. This is also the drawback of this approach, as there are cases when one would like to avoid reinitializing internals on restart. For example, it is often useful to preserve child actors across restarts. The following section provides a pattern for this case.

### Initialization via preStart
The method preStart() of an actor is only called once directly during the initialization of the first instance, that is, at creation of its ActorRef. In the case of restarts, preStart() is called from postRestart(), therefore if not overridden, preStart() is called on every incarnation. However, overriding postRestart() one can disable this behavior, and ensure that there is only one call to preStart().

One useful usage of this pattern is to disable creation of new ActorRefs for children during restarts. This can be achieved by overriding preRestart():

```csharp
@Override
public void preStart() {
  // Initialize children here
}
 
// Overriding postRestart to disable the call to preStart()
// after restarts
@Override
public void postRestart(Throwable reason) {
}
 
// The default implementation of preRestart() stops all the children
// of the actor. To opt-out from stopping the children, we
// have to override preRestart()
@Override
public void preRestart(Throwable reason, Option<Object> message)
  throws Exception {
  // Keep the call to postStop(), but no stopping of children
  postStop();
}
```
Please note, that the child actors are still restarted, but no new ActorRef is created. One can recursively apply the same principles for the children, ensuring that their preStart() method is called only at the creation of their refs.

For more information see What Restarting Means.

### Initialization via message passing
There are cases when it is impossible to pass all the information needed for actor initialization in the constructor, for example in the presence of circular dependencies. In this case the actor should listen for an initialization message, and use become() or a finite state-machine state transition to encode the initialized and uninitialized states of the actor.

```csharp
private String initializeMe = null;
 
@Override
public void onReceive(Object message) throws Exception {
  if (message.equals("init")) {
    initializeMe = "Up and running";
    getContext().become(new Procedure<Object>() {
      @Override
      public void apply(Object message) throws Exception {
        if (message.equals("U OK?"))
          getSender().tell(initializeMe, getSelf());
      }
    });
  }
}
```
If the actor may receive messages before it has been initialized, a useful tool can be the Stash to save messages until the initialization finishes, and replaying them after the actor became initialized.

>**Warning**<br/>
This pattern should be used with care, and applied only when none of the patterns above are applicable. One of the potential issues is that messages might be lost when sent to remote actors. Also, publishing an ActorRef in an uninitialized state might lead to the condition that it receives a user message before the initialization has been done.