---
uid: testing-actor-systems
title: Testing Actor Systems
---

# Testing Actor Systems
As with any piece of software, automated tests are a very important part of the development cycle. The actor model presents a different view on how units of code are delimited and how they interact, which has an influence on how to perform tests.

Akka.Net comes with a dedicated module `Akka.TestKit` for supporting tests at different levels, which fall into two clearly distinct categories:

- Testing isolated pieces of code without involving the actor model, meaning without multiple threads; this implies completely deterministic behavior concerning the ordering of events and no concurrency concerns and will be called **Unit Testing** in the following.
- Testing (multiple) encapsulated actors including multi-threaded scheduling; this implies non-deterministic order of events but shielding from concurrency concerns by the actor model and will be called **Integration Testing** in the following.

There are of course variations on the granularity of tests in both categories, where unit testing reaches down to white-box tests and integration testing can encompass functional tests of complete actor networks. The important distinction lies in whether concurrency concerns are part of the test or not. The tools offered are described in detail in the following sections.

## Synchronous Unit Testing with TestActorRef

Testing the business logic inside `Actor` classes can be divided into two parts: first, each atomic operation must work in isolation, then sequences of incoming events must be processed correctly, even in the presence of some possible variability in the ordering of events. The former is the primary use case for single-threaded unit testing, while the latter can only be verified in integration tests.

Normally, the `IActorRef` shields the underlying `Actor` instance from the outside, the only communications channel is the actor's mailbox. This restriction is an impediment to unit testing, which led to the inception of the `TestActorRef`. This special type of reference is designed specifically for test purposes and allows access to the actor in two ways: either by obtaining a reference to the underlying actor instance, or by invoking or querying the actor's behaviour (receive). Each one warrants its own section below.

> [!NOTE]
> It is highly recommended to stick to traditional behavioural testing (using messaging to ask the `Actor` to reply with the state you want to run assertions against), instead of using `TestActorRef` whenever possible.

### Obtaining a Reference to an Actor
Having access to the actual `Actor` object allows application of all traditional unit testing techniques on the contained methods. Obtaining a reference is done like this:
```csharp
public class MyActor : UntypedActor
{
    protected override void OnReceive(object message)
    {
        if (message.Equals("say42"))
        {
            Sender.Tell(42, Self);
        }
        else if (message is Exception)
        {
            throw (Exception)message;
        }
    }

    public bool TestMe
    {
        get { return true; }
    }
}

[Fact]
public void DemonstrateTestActorRef()
{
    var props = Props.Create<MyActor>();
    var myTestActor = new TestActorRef<MyActor>(Sys, props, null, "testA");
    MyActor myActor = myTestActor.UnderlyingActor;
    Assert.True(myActor.TestMe);
}
```
Since `TestActorRef` is generic in the actor type it returns the underlying actor with its proper static type. From this point on you may bring any unit testing tool to bear on your actor as usual.

### Testing the Actor's Behavior
When the dispatcher invokes the processing behavior of an actor on a message, it actually calls apply on the current behavior registered for the actor. This starts out with the return value of the declared receive method, but it may also be changed using become and unbecome in response to external messages. All of this contributes to the overall actor behavior and it does not lend itself to easy testing on the `Actor` itself. Therefore the TestActorRef offers a different mode of operation to complement the `Actor` testing: it supports all operations also valid on normal `IActorRef`. Messages sent to the actor are processed synchronously on the current thread and answers may be sent back as usual. This trick is made possible by the `CallingThreadDispatcher` described below; this dispatcher is set implicitly for any actor instantiated into a `TestActorRef`.
```csharp
var props = Props.Create<MyActor>();
var myTestActor = new TestActorRef<MyActor>(Sys, props, null, "testB");
Task<int> future = myTestActor.Ask<int>("say42", TimeSpan.FromMilliseconds(3000));
Assert.True(future.IsCompleted);
Assert.Equal(42, await future);
```
As the `TestActorRef` is a subclass of `LocalActorRef` with a few special extras, also aspects like supervision and restarting work properly, but beware that execution is only strictly synchronous as long as all actors involved use the `CallingThreadDispatcher`. As soon as you add elements which include more sophisticated scheduling you leave the realm of unit testing as you then need to think about asynchronicity again (in most cases the problem will be to wait until the desired effect had a chance to happen).

One more special aspect which is overridden for single-threaded tests is the `ReceiveTimeout`, as including that would entail asynchronous queuing of `ReceiveTimeout` messages, violating the synchronous contract.

### The Way In-Between: Expecting Exceptions
If you want to test the actor behavior, including hotswapping, but without involving a dispatcher and without having the `TestActorRef` swallow any thrown exceptions, then there is another mode available for you: just use the receive method on `TestActorRef`, which will be forwarded to the underlying actor:
```csharp
var props = Props.Create<MyActor>();
var myTestActor = new TestActorRef<MyActor>(Sys, props, null, "testB");
try
{
    myTestActor.Receive(new Exception("expected"));
}
catch (Exception e)
{
    Assert.Equal("expected", e.Message);
}
```

### Use Cases
You may of course mix and match both modi operandi of `TestActorRef` as suits your test needs:

- one common use case is setting up the actor into a specific internal state before sending the test message
- another is to verify correct internal state transitions after having sent the test message

## Asynchronous Integration Testing with TestKit
When you are reasonably sure that your actor's business logic is correct, the next step is verifying that it works correctly within its intended environment (if the individual actors are simple enough, possibly because they use the `FSM` module, this might also be the first step). The definition of the environment depends of course very much on the problem at hand and the level at which you intend to test, ranging for functional/integration tests to full system tests. The minimal setup consists of the test procedure, which provides the desired stimuli, the actor under test, and an actor receiving replies. Bigger systems replace the actor under test with a network of actors, apply stimuli at varying injection points and arrange results to be sent from different emission points, but the basic principle stays the same in that a single procedure drives the test.

The `TestKit` class contains a collection of tools which makes this common task easy.

```csharp
public class MyActor : UntypedActor
{
    protected override void OnReceive(object message)
    {
        if (message.Equals("hello world"))
        {
            Sender.Tell(message, Self);
        }
        else
        {
            Unhandled(message);
        }
    }

    public bool TestMe
    {
        get { return true; }
    }
}

[Fact]
public void EndBackMessagesUnchanged()
{
    var echo = Sys.ActorOf(Props.Create<MyActor>());
    echo.Tell("hello world");
    ExpectMsg("hello world");
}
```
The `TestKit` contains an actor named `MyActor` which is the entry point for messages to be examined with the various `ExpectMsg...` assertions detailed below. When mixing in the class `ImplicitSender` this test actor is implicitly used as sender reference when dispatching messages from the test procedure. The `MyActor` may also be passed to other actors as usual, usually subscribing it as notification listener. There is a whole set of examination methods, e.g. receiving all consecutive messages matching certain criteria, receiving a whole sequence of fixed messages or classes, receiving nothing for some time, etc.

The `ActorSystem` passed in to the constructor of `TestKit` is accessible via the system member. Remember to shut down the actor system after the test is finished (also in case of failure) so that all actors—including the test actor—are stopped.

### Built-In Assertions

The above mentioned `ExpectMsg` is not the only method for formulating assertions concerning received messages. Here is the full list:

- `T ExpectMsg<T>(TimeSpan? duration = null, string hint)`
The given message object must be received within the specified time; the object will be returned.

- `T ExpectMsgAnyOf<T>(params T[] messages)`
An object must be received, and it must be equal to at least one of the passed reference objects; the received object will be returned.

- `IReadOnlyCollection<T> ExpectMsgAllOf<T>(TimeSpan max, params T[] messages)`
A number of objects matching the size of the supplied object array must be received within the given time, and for each of the given objects there must exist at least one among the received ones which equals it. The full sequence of received objects is returned.

- `void ExpectNoMsg(TimeSpan duration)`
No message must be received within the given time. This also fails if a message has been received before calling this method which has not been removed from the queue using one of the other methods.

- `T ExpectMsgFrom<T>(IActorRef sender, TimeSpan? duration = null, string hint = null)`
Receive one message of the specified type from the test actor and assert that it equals the message and was sent by the specified sender

- `IReadOnlyCollection<object> ReceiveN(int numberOfMessages, TimeSpan max)`
`n` messages must be received within the given time; the received messages are returned.

- `object FishForMessage(Predicate<object> isMessage, TimeSpan? max, string)`
Keep receiving messages as long as the time is not used up and the partial function matches and returns `false`. Returns the message received for which it returned `true` or throws an exception, which will include the provided hint for easier debugging.

### Expecting Log Messages

### Timing Assertions

### Resolving Conflicts with Implicit IActorRef

### Using Multiple Probe Actors

### Testing parent-child relationships


## CallingThreadDispatcher

The `CallingThreadDispatcher` serves good purposes in unit testing, as described above, but originally it was conceived in order to allow contiguous stack traces to be generated in case of an error. As this special dispatcher runs everything which would normally be queued directly on the current thread, the full history of a message's processing chain is recorded on the call stack, so long as all intervening actors run on this dispatcher.

### How to use it
Just set the dispatcher as you normally would
```csharp
Sys.ActorOf(Props.Create<MyActor>().WithDispatcher(CallingThreadDispatcher.Id));
```

### How it works
When receiving an invocation, the `CallingThreadDispatcher` checks whether the receiving actor is already active on the current thread. The simplest example for this situation is an actor which sends a message to itself. In this case, processing cannot continue immediately as that would violate the actor model, so the invocation is queued and will be processed when the active invocation on that actor finishes its processing; thus, it will be processed on the calling thread, but simply after the actor finishes its previous work. In the other case, the invocation is simply processed immediately on the current thread. Tasks scheduled via this dispatcher are also executed immediately.

This scheme makes the `CallingThreadDispatcher` work like a general purpose dispatcher for any actors which never block on external events.

In the presence of multiple threads it may happen that two invocations of an actor running on this dispatcher happen on two different threads at the same time. In this case, both will be processed directly on their respective threads, where both compete for the actor's lock and the loser has to wait. Thus, the actor model is left intact, but the price is loss of concurrency due to limited scheduling. In a sense this is equivalent to traditional mutex style concurrency.

The other remaining difficulty is correct handling of suspend and resume: when an actor is suspended, subsequent invocations will be queued in thread-local queues (the same ones used for queuing in the normal case). The call to resume, however, is done by one specific thread, and all other threads in the system will probably not be executing this specific actor, which leads to the problem that the thread-local queues cannot be emptied by their native threads. Hence, the thread calling resume will collect all currently queued invocations from all threads into its own queue and process them.

### Benefits
To summarize, these are the features with the `CallingThreadDispatcher` has to offer:

- Deterministic execution of single-threaded tests while retaining nearly full actor semantics
- Full message processing history leading up to the point of failure in exception stack traces
- Exclusion of certain classes of dead-lock scenarios

## Tracing Actor Invocations
The testing facilities described up to this point were aiming at formulating assertions about a system’s behavior. If a test fails, it is usually your job to find the cause, fix it and verify the test again. This process is supported by debuggers as well as logging, where the Akka.NET offers the following options:

- Logging of exceptions thrown within Actor instances. This is always on; in contrast to the other logging mechanisms, this logs at *ERROR* level.
- Logging of special messages. Actors handle certain special messages automatically, e.g. `Kill`, `PoisonPill`, etc. Tracing of these message invocations is enabled by the setting *akka.actor.debug.autoreceive*, which enables this on all actors.
- Logging of the actor lifecycle. Actor creation, start, restart, monitor start, monitor stop and stop may be traced by enabling the setting *akka.actor.debug.lifecycle*; this, too, is enabled uniformly on all actors.

All these messages are logged at `DEBUG` level. To summarize, you can enable full logging of actor activities using this configuration fragment:
```hocon
akka {
  loglevel = "DEBUG"
  actor {
    debug {
      autoreceive = on
      lifecycle = on
    }
  }
}
```