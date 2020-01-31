---
uid: testing-actor-systems
title: Testing Actor Systems
---


# Testing Actor Systems


As with any piece of software, automated tests are a very important part of the development cycle. The actor model presents a different view on how units of code are delimited and how they interact, which has an influence on how to perform tests.

Akka.Net comes with a dedicated module `Akka.TestKit` for supporting tests at different levels.

## Asynchronous Testing: TestKit

Testkit allows you to test your actors in a controlled but realistic environment. The definition of the environment depends of course very much on the problem at hand and the level at which you intend to test, ranging from simple checks to full system tests.

The minimal setup consists of the test procedure, which provides the desired stimuli, the actor under test, and an actor receiving replies. Bigger systems replace the actor under test with a network of actors, apply stimuli at varying injection points and arrange results to be sent from different emission points, but the basic principle stays the same in that a single procedure drives the test.

The `TestKit` class contains a collection of tools which makes this common task easy.

[!code-csharp[IntroSample](../../../src/core/Akka.Docs.Tests/Testkit/TestKitSampleTest.cs?name=IntroSample_0)]

The `TestKit` contains an actor named `TestActor` which is the entry point for messages to be examined with the various `ExpectMsg..` assertions detailed below. The `TestActor` may also be passed to other actors as usual, usually subscribing it as notification listener. There is a while set of examination methods, e.g. receiving all consecutive messages matching certain criteria, receiving a while sequence of fixed messages or classes, receiving nothing for some time, etc.

You can provide your own ActorSystem instance, or Config by overriding the TestKit constructor. The ActorSystem used by the TestKit is accessible via the `Sys` member.

## Built-In Assertions

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

In addition to message reception assertions there are also methods which help with messages flows:

- `object ReceiveOne(TimeSpan? max = null)` 
Receive one message from the internal queue of the TestActor. This method blocks the specified duration or until a message is received. If no message was received, null is returned.

- `IReadOnlyList<T> ReceiveWhile<T>(TimeSpan? max, TimeSpan? idle, Func<object, T> filter, int msgs = int.MaxValue)` Collect messages as long as
   - They are matching the provided filter
   - The given time interval is not used up
   - The next message is received within the idle timeout
   - The number of messages has not yet reached the maximum All collected messages are returned. The maximum duration defaults to the time remaining in the innermost enclosing `Within` block and the idle duration defaults to infinity (thereby disabling the idle timeout feature). The number of expected messages defaults to `Int.MaxValue`, which effectively disables this limit.

- `void AwaitCondition(Func<bool> conditionIsFulfilled, TimeSpan? max, TimeSpan? interval, string message = null)` Poll the given condition every `interval` until it returns `true` or the `max` duration is used up. The interval defaults to 100ms and the maximum defaults to the time remaining in the innermost enclosing `within` block.

- `void AwaitAssert(Action assertion, TimeSpan? duration = default(TimeSpan?), TimeSpan? interval = default(TimeSpan?))`Poll the given assert function every `interval` until it does not throw an exception or the `max` duration is used up. If the timeout expires the last exception is thrown. The interval defaults to 100ms and the maximum defautls to the time remaining in the innermost enclosing `within` block. The interval defaults to 100ms and the maximum defaults to the time remaining in the innermost enclosing `within` block.

- `void IgnoreMessages(Func<object, bool> shouldIgnoreMessage)` The internal `testActor` contains a partial function for ignoring messages: it will only enqueue messages which do not match the function or for which the function returns `false`. This feature is useful e.g. when testing a logging system, where you want to ignore regular messages and are only interesting in your specific ones.

## Expecting Log Messages
Since an integration test does not allow to the internal processing of the participating actors, verifying expected exceptions cannot be done directly. Instead, use the logging system for this purpose: replacing the normal event handler with the `TestEventListener` and using an `EventFilter` allows assertions on log messages, including those which are generated by exceptions:

//TODO EVENTFILTER SAMPLE

If a number of occurrences is specific --as demonstrated above-- then `intercept` will block until that number of matching messages have been received or the timeout configured in `akka.test.filter-leeway` is used up (time starts counting after the passed-in block of code returns). In case of a timeout the test fails.

> [!NOTE]
> By default the TestKit already loads the TestEventListener as a logger. Be aware that if you want to specify your own config. Use the `DefaultConfig` property to apply overrides.

## Timing Assertions
Another important part of functional testing concerns timing: certain events must not happen immediately (like a timer), others need to happen before a deadline. Therefore, all examination methods accept an upper time limit within the positive or negative result must be obtained. Lower time limits need to be checked external to the examination, which is facilitated by a new construct for managing time constraints:

[!code-csharp[WithinSample](../../../src/core/Akka.Docs.Tests/Testkit/WithinSampleTest.cs?name=WithinSample_0)]

The block in `within` must complete after a `Duration` which is between `min` and `max`, where the former defaults to zero. The deadline calculated by adding the `max` parameter to the block's start time is implicitly available within the block to all examination methods, if you do not specify it, it is inherited from the innermost enclosing `within` block.

It should be noted that if the last message-receiving assertion of the block is `ExpectNoMsg` or `ReceiveWhile`, the final check of the within is skipped in order to avoid false positives due to wake-up latencies. This means that while individual contained assertions still use the maximum time bound, the overall block may take arbitrarily longer in this case.

```csharp
var worker = ActorOf<Worker>();
Within(200.Milliseconds()) {
  worker.Tell("some work");
  ExpectMsg("Some Result");
  ExpectNoMsg(); //will block for the rest of the 200ms
  Thead.Sleep(300); //will NOT make this block fail
}
```

## Accounting for Slow Test System
The tight timeouts you use during testing on your lightning-fast notebook will invariably lead to spurious test failures on your heavily loaded build server. To account for this situation, all maximum durations are internally scaled by a factor taken from the **Configuration**, `akka.test.timefactor`, which defaults to 1.

You can scale other durations with the same factor by using the `Dilated` method in `TestKit`.

//TODO DILATED EXAMPLE

## Using Multiple Probe Actors

When the actors under test are supposed to send various messages to different destinations, it may be difficult distinguishing the message streams arriving at the `TestActor` when using the `TestKit` as shown until now. Another approach is to use it for creation of simple probe actors to be inserted in the message flows. The functionality is best explained using a small example:

[!code-csharp[ProbeSample](../../../src/core/Akka.Docs.Tests/Testkit/ProbeSampleTest.cs?name=ProbeSample_0)]

This simple test verifies an equally simple Forwarder actor by injecting a probe as the forwarder’s target. Another example would be two actors A and B which collaborate by A sending messages to B. In order to verify this message flow, a `TestProbe` could be inserted as target of A, using the forwarding capabilities or auto-pilot described below to include a real B in the test setup.

If you have many test probes, you can name them to get meaningful actor names in test logs and assertions:

[!code-csharp[MultipleProbeSample](../../../src/core/Akka.Docs.Tests/Testkit/ProbeSampleTest.cs?name=MultipleProbeSample_0)]

Probes may also be equipped with custom assertions to make your test code even more concise and clear:

//TODO CUSTOM PROBE IMPL SAMPLE

You have complete flexibility here in mixing and matching the `TestKit` facilities with your own checks and choosing an intuitive name for it. In real life your code will probably be a bit more complicated than the example given above; just use the power!

> [!WARNING]
> Any message send from a `TestProbe` to another actor which runs on the `CallingThreadDispatcher` runs the risk of dead-lock, if that other actor might also send to this probe. The implementation of `TestProbe.Watch` and `TestProbe.Unwatch` will also send a message to the watchee, which means that it is dangerous to try watching e.g. `TestActorRef` from a `TestProbe`.

###Watching Other Actors from probes
A `TestProbe` can register itself for DeathWatch of any other actor:

```csharp
  var probe = CreateTestProbe();
  probe.Watch(target);

  target.Tell(PoisonPill.Instance);

  var msg = probe.ExpectMsg<Terminated>();
  Assert.Equal(msg.ActorRef, target);
```

###Replying to Messages Received by Probes
The probes stores the sender of the last dequeued message (i.e. after its `ExpectMsg*` reception), which may be retrieved using the `GetLastSender()` method. This information can also implicitly be used for having the probe reply to the last received message:

[!code-csharp[ReplyingToProbeMessages](../../../src/core/Akka.Docs.Tests/Testkit/ProbeSampleTest.cs?name=ReplyingToProbeMessages_0)]

###Forwarding Messages Received by Probes
The probe can also forward a received message (i.e. after its `ExpectMsg*` reception), retaining the original sender:

[!code-csharp[ForwardingProbeMessages](../../../src/core/Akka.Docs.Tests/Testkit/ProbeSampleTest.cs?name=ForwardingProbeMessages_0)]

###Auto-Pilot
Receiving messages in a queue for later inspection is nice, but in order to keep a test running and verify traces later you can also install an `AutoPilot` in the participating test probes (actually in any `TestKit`) which is invoked before enqueueing to the inspection queue. This code can be used to forward messages, e.g. in a chain `A --> Probe --> B`, as long as a certain protocol is obeyed.

[!code-csharp[ProbeAutopilot](../../../src/core/Akka.Docs.Tests/Testkit/ProbeSampleTest.cs?name=ProbeAutopilot_0)]

The `run` method must return the auto-pilot for the next message. There are multiple options here:
You can return the `AutoPilot.NoAutoPilot` to stop the autopilot, or `AutoPilot.KeepRunning` to keep using the current `AutoPilot`. Obviously you can also chain a new `AutoPilot` instance to switch behaviors.

###Caution about Timing Assertions
The behavior of `Within` blocks when using test probes might be perceived as counter-intuitive: you need to remember that the nicely scoped deadline as described **above** is local to each probe. Hence, probes do not react to each other's deadlines or to the deadline set in an enclosing `TestKit` instance.

##Testing parent-child relationships

The parent of an actor is always the actor that created it. At times this leads to a coupling between the two that may not be straightforward to test. There are several approaches to improve testability of a child actor that needs to refer to its parent:

1. When creating a child, pass an explicit reference to its parent
2. Create the child with a `TestProbe` as parent
3. Create a fabricated parent when testing

Conversely, a parent's binding to its child can be lessened as follows:

1. When creating a parent, tell the parent how to create its child.

For example, the structure of the code you want to test may follow this pattern:

[!code-csharp[ParentStructure](../../../src/core/Akka.Docs.Tests/Testkit/ParentSampleTest.cs?name=ParentStructure_0)]

###Introduce child to its parent
The first option is to avoid use of the `context.parent` function and create a child with a custom parent by passing an explicit reference to its parent instead.

[!code-csharp[DependentChild](../../../src/core/Akka.Docs.Tests/Testkit/ParentSampleTest.cs?name=DependentChild_0)]

###Create the child using the TestProbe
The `TestProbe` class can directly create child actors using the `ChildActorOf` methods.  

[!code-csharp[TestProbeChild](../../../src/core/Akka.Docs.Tests/Testkit/ParentSampleTest.cs?name=TestProbeChild_0)]

###Using a fabricated parent
If you prefer to avoid modifying the parent or child constructor you can create a fabricated parent in your test. This, however, does not enable you to test the parent actor in isolation.

[!code-csharp[FabrikatedParent](../../../src/core/Akka.Docs.Tests/Testkit/ParentSampleTest.cs?name=FabrikatedParent_0)]

###Externalize child making from the parent
Alternatively, you can tell the parent how to create its child. There are two ways to do this: by giving it a `Props` object or by giving it a function which takes care of creating the child actor:

[!code-csharp[FabrikatedParent](../../../src/core/Akka.Docs.Tests/Testkit/ParentSampleTest.cs?name=FabrikatedParent_1)]

Creating the Props is straightforward and the function may look like this in your test code:

```csharp
    Func<IUntypedActorContext, IActorRef> maker = (ctx) => probe.Ref;
    var parent = Sys.ActorOf(Props.Create<GenericDependentParent>(maker));
```

And like this in your application code:

```csharp
    Func<IUntypedActorContext, IActorRef> maker = (ctx) => ctx.ActorOf(Props.Create<Child>())
    var parent = Sys.ActorOf(Props.Create<GenericDependentParent>(maker));
```

Which of these methods is the best depends on what is most important to test. The most generic option is to create the parent actor by passing it a function that is responsible for the Actor creation, but using TestProbe or having a fabricated parent is often sufficient.

##CallingThreadDispatcher

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

##Configuration
There are several configuration properties for the TestKit module, please refer to the [reference configuration](https://github.com/akkadotnet/akka.net/blob/master/src/core/Akka.TestKit/Configs/TestScheduler.conf)

TODO describe how to pass custom config

##Synchronous Testing: TestActorRef

Testing the business logic inside `Actor` classes can be divided into two parts: first, each atomic operation must work in isolation, then sequences of incoming events must be processed correctly, even in the presence of some possible variability in the ordering of events. The former is the primary use case for single-threaded unit testing, while the latter can only be verified in integration tests.

Normally, the `IActorRef` shields the underlying `Actor` instance from the outside, the only communications channel is the actor's mailbox. This restriction is an impediment to unit testing, which led to the inception of the `TestActorRef`. This special type of reference is designed specifically for test purposes and allows access to the actor in two ways: either by obtaining a reference to the underlying actor instance, or by invoking or querying the actor's behaviour (receive). Each one warrants its own section below.

> [!NOTE]
> It is highly recommended to stick to traditional behavioral testing (using messaging to ask the `Actor` to reply with the state you want to run assertions against), instead of using `TestActorRef` whenever possible.

## Obtaining a Reference to an Actor
Having access to the actual `Actor` object allows application of all traditional unit testing techniques on the contained methods. Obtaining a reference is done like this:

```csharp
    var props = Props.Create<MyActor>();
    var myTestActor = new TestActorRef<MyActor>(Sys, props, null, "testA");
    MyActor myActor = myTestActor.UnderlyingActor;
```

Since `TestActorRef` is generic in the actor type it returns the underlying actor with its proper static type. From this point on you may bring any unit testing tool to bear on your actor as usual.

##Testing Finite State Machines
If your actor under test is a `FSM`, you may use the special `TestFSMRef` which offers all features of a normal `TestActorRef` and in addition allows access to the internal state:

```csharp
var fsm = new TestFSMRef<TestFsmActor, int, string>();
            
Assert.True(fsm.StateName == 1);
Assert.True(fsm.StateData == "");

fsm.Tell("go"); //being a TestActorRef, this runs on the CallingThreadDispatcher

Assert.True(fsm.StateName == 2);
Assert.True(fsm.StateData == "go");

fsm.SetState(1);
Assert.True(fsm.StateName == 1);

Assert.False(fsm.IsTimerActive("test"));
fsm.SetTimer("test",12, 10.Milliseconds(), true);
Assert.True(fsm.IsTimerActive("test"));
fsm.CancelTimer("test");
Assert.False(fsm.IsTimerActive("test"));
```

All methods shown above directly access the FSM state without any synchronization; this is perfectly alright if the `CallingThreadDispatcher` is used and no other threads are involved, but it may lead to surprises if you were to actually exercise timer events, because those are executed on the `Scheduler` thread.

##Testing the Actor's behavior
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

##EventFilters

EventFilters are a tool use can use to scan and expect for LogEvents generated by your actors. Typically these are generated by custom calls on the `Context.GetLogger()` object, when you log something. 
However DeadLetter messages and Exceptions ultimately also result in a `LogEvent` message being generated.

These are all things that can be intercepted, and asserted upon using the `EventFilter`.
An example of how you can get a reference to the `EventFilter`
```csharp
    var filter = CreateEventFilter(Sys);
            
    filter.DeadLetter<string>().ExpectOne(() =>
    {
        //cause a message to be deadlettered here
        
    });

    filter.Custom(logEvent => logEvent is Error && (string)logEvent.Message == "whatever").ExpectOne(() =>
    {
        Log.Error("whatever");
    });

    filter.Exception<MyException>().ExpectOne(() => Log.Error(new MyException(), "the message"));
```
