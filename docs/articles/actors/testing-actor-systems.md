# Testing Actor Systems


As with any piece of software, automated tests are a very important part of the development cycle. The actor model presents a different view on how units of code are delimited and how they interact, which has an influence on how to perform tests.

Akka.Net comes with a dedicated module `Akka.TestKit` for supporting tests at different levels.

## Asynchronous Testing: TestKit

Testkit allows you to test your actors in a controlled but realistic environment. The definition of the environment depends of course very much on the problem at hand and the level at which you intend to test, ranging from simple checks to full system tests.

The minimal setup consists of the test procedure, which provides the desired stimuli, the actor under test, and an actor receiving replies. Bigger systems replace the actor under test with a network of actors, apply stimuli at varying injection points and arrange results to be sent from different emission points, but the basic principle stays the same in that a single procedure drives the test.

The `TestKit` class contains a collection of tools which makes this common task easy.

[!code-csharp[IntroSample](../../examples/DocsExamples/Testkit/TestKitSampleTest.cs?range=9-64)]

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
 
