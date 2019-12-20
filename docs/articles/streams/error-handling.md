---
uid: error-handling
title: Error Handling in Streams
---
# Error Handling in Streams

When a stage in a stream fails this will normally lead to the entire stream being torn down. Each of the stages downstream gets informed about the failure and each upstream stage sees a cancellation.

In many cases you may want to avoid complete stream failure, this can be done in a few different ways:

- `Recover` to emit a final element then complete the stream normally on upstream failure
- `RecoverWithRetries` to create a new upstream and start consuming from that on failure
- Restarting sections of the stream after a backoff
- Using a supervision strategy for stages that support it

In addition to these built in tools for error handling, a common pattern is to wrap the stream inside an actor, and have the actor restart the entire stream on failure.

## Recover
`Recover` allows you to emit a final element and then complete the stream on an upstream failure. Deciding which exceptions should be recovered is done through a `delegate`. If an exception does not have a matching case the stream is failed.

Recovering can be useful if you want to gracefully complete a stream on failure while letting downstream know that there was a failure.

```C#
Source.From(Enumerable.Range(0, 6)).Select(n =>
    {
        if (n < 5)
            return n.ToString();

        throw new ArithmeticException("Boom!");
    })
    .Recover(exception =>
    {
        if (exception is ArithmeticException)
            return new Option<string>("stream truncated");
        return Option<string>.None;
    })
    .RunForeach(Console.WriteLine, materializer);
```
This will output:
```
0
1
2
3
4
stream truncated
```

## Recover with retries
`RecoverWithRetries` allows you to put a new upstream in place of the failed one, recovering stream failures up to a specified maximum number of times.

Deciding which exceptions should be recovered is done through a `delegate`. If an exception does not have a matching case the stream is failed.

```scala
var planB = Source.From(new List<string> {"five", "six", "seven", "eight"});

Source.From(Enumerable.Range(0, 10)).Select(n =>
    {
        if (n < 5)
            return n.ToString();

        throw new ArithmeticException("Boom!");
    })
    .RecoverWithRetries(attempts: 1, partialFunc: exception =>
    {
        if (exception is ArithmeticException)
            return planB;
        return null;
    })
    .RunForeach(Console.WriteLine, materializer);
```

This will output:

```
0
1
2
3
4
five
six
seven
eight
```

## Delayed restarts with a backoff stage

Just as Akka provides the [backoff supervision pattern for actors](xref:supervision#delayed-restarts-with-the-backoffsupervisor-pattern), Akka streams
also provides a `RestartSource`, `RestartSink` and `RestartFlow` for implementing the so-called *exponential backoff
supervision strategy*, starting a stage again when it fails or completes, each time with a growing time delay between restarts.

This pattern is useful when the stage fails or completes because some external resource is not available
and we need to give it some time to start-up again. One of the prime examples when this is useful is
when a WebSocket connection fails due to the HTTP server it's running on going down, perhaps because it is overloaded. 
By using an exponential backoff, we avoid going into a tight reconnect look, which both gives the HTTP server some time
to recover, and it avoids using needless resources on the client side.

The following snippet shows how to create a backoff supervisor using `Akka.Streams.Dsl.RestartSource` 
which will supervise the given `Source`. The `Source` in this case is a 
`HttpResponseMessage`, produced by `HttpCLient`. If the stream fails or completes at any point, the request will
be made again, in increasing intervals of 3, 6, 12, 24 and finally 30 seconds (at which point it will remain capped due
to the `maxBackoff` parameter):

[!code-csharp[RestartDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/RestartDocTests.cs?name=restart-with-backoff-source)]

Using a `randomFactor` to add a little bit of additional variance to the backoff intervals
is highly recommended, in order to avoid multiple streams re-start at the exact same point in time,
for example because they were stopped due to a shared resource such as the same server going down
and re-starting after the same configured interval. By adding additional randomness to the
re-start intervals the streams will start in slightly different points in time, thus avoiding
large spikes of traffic hitting the recovering server or other resource that they all need to contact.

The above `RestartSource` will never terminate unless the `Sink` it's fed into cancels. It will often be handy to use
it in combination with a `KillSwitch`, so that you can terminate it when needed:

[!code-csharp[RestartDocTests.cs](../../../src/core/Akka.Docs.Tests/Streams/RestartDocTests.cs?name=with-kill-switch)]

Sinks and flows can also be supervised, using `Akka.Streams.Dsl.RestartSink` and `Akka.Streams.Dsl.RestartFlow`.
The `RestartSink` is restarted when it cancels, while the `RestartFlow` is restarted when either the in port cancels,
the out port completes, or the out port sends an error.

## Supervision Strategies

> [!NOTE]
> The stages that support supervision strategies are explicitly documented to do so, if there is nothing in the documentation of a stage saying that it adheres to the supervision strategy it means it fails rather than applies supervision..

The error handling strategies are inspired by actor supervision strategies, but the semantics have been adapted to the domain of stream processing. The most important difference is that supervision is not automatically applied to stream stages but instead something that each stage has to implement explicitly.

For many stages it may not even make sense to implement support for supervision strategies, this is especially true for stages connecting to external technologies where for example a failed connection will likely still fail if a new connection is tried immediately.

For stages that do implement supervision, the strategies for how to handle exceptions from processing stream elements can be selected when materializing the stream through use of an attribute.

There are three ways to handle exceptions from application code:
- `Stop` - The stream is completed with failure.
- `Resume` - The element is dropped and the stream continues.
- `Restart` - The element is dropped and the stream continues after restarting the stage. Restarting a stage means that any accumulated state is cleared. This is typically performed by creating a new instance of the stage.

By default the stopping strategy is used for all exceptions, i.e. the stream will be completed with failure when an exception is thrown.

```C#
var source = Source.From(Enumerable.Range(0, 6)).Select(x => 100/x);
var result = source.RunWith(Sink.Aggregate<int, int>(0, (sum, i) => sum + i), materializer);
// division by zero will fail the stream and the
// result here will be a Task completed with Failure(DivideByZeroException)
```

The default supervision strategy for a stream can be defined on the settings of the materializer.

```C#
Decider decider = cause => cause is DivideByZeroException
    ? Directive.Resume
    : Directive.Stop;
var settings = ActorMaterializerSettings.Create(system).WithSupervisionStrategy(decider);
var materializer = system.Materializer(settings);

var source = Source.From(Enumerable.Range(0, 6)).Select(x => 100/x);
var result = source.RunWith(Sink.Aggregate<int, int>(0, (sum, i) => sum + i), materializer);
// the element causing division by zero will be dropped
// result here will be a Task completed with Success(228)
```

Here you can see that all `DivideByZeroException` will resume the processing, i.e. the
elements that cause the division by zero are effectively dropped.

> [!NOTE]
> Be aware that dropping elements may result in deadlocks in graphs with cycles, as explained in [Graph cycles, liveness and deadlocks](xref:streams-working-with-graphs#graph-cycles-liveness-and-deadlocks).

The supervision strategy can also be defined for all operators of a flow.

```C#
Decider decider = cause => cause is DivideByZeroException
    ? Directive.Resume
    : Directive.Stop;

var flow = Flow.Create<int>()
    .Where(x => 100 / x < 50)
    .Select(x => 100 / (5 - x))
    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(decider));
var source = Source.From(Enumerable.Range(0, 6)).Via(flow);
var result = source.RunWith(Sink.Aggregate<int, int>(0, (sum, i) => sum + i), materializer);
// the elements causing division by zero will be dropped
// result here will be a Future completed with Success(150)
```

`Restart` works in a similar way as `Resume` with the addition that accumulated state,
if any, of the failing processing stage will be reset.

```C#
Decider decider = cause => cause is ArgumentException
    ? Directive.Restart
    : Directive.Stop;

var flow = Flow.Create<int>()
    .Scan(0, (acc, x) =>
    {
      if(x < 0)
            throw new ArgumentException("negative not allowed");
        return acc + x;
    })
    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(decider));
var source = Source.From(new [] {1,3,-1,5,7}).Via(flow);
var result = source.Limit(1000).RunWith(Sink.Seq<int>(), materializer);
// the negative element cause the scan stage to be restarted,
// i.e. start from 0 again
// result here will be a Task completed with Success(List(0, 1, 4, 0, 5, 12))
```

## Errors from SelectAsync
Stream supervision can also be applied to the tasks of `SelectAsync` and `SelectAsyncUnordered` even if such failures happen in the task rather than inside the stage itself. .

Let's say that we use an external service to lookup email addresses and we would like to
discard those that cannot be found.

We start with the tweet stream of authors:

```C#
var authors = tweets
    .Where(t => t.HashTags.Contains("Akka.Net"))
    .Select(t => t.Author);
```

Assume that we can lookup their email address using:

```C#
Task<string> LookupEmail(string handle)
```

The `Task` is completed with `Failure` if the email is not found.

Transforming the stream of authors to a stream of email addresses by using the `LookupEmail`
service can be done with `SelectAsync` and we use `Deciders.ResumingDecider` to drop
unknown email addresses:

```c#
var emailAddresses = authors.Via(
    Flow.Create<Author>()
        .SelectAsync(4, author => AddressSystem.LookupEmail(author.Handle))
        .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider)));
```

If we would not use `Resume` the default stopping strategy would complete the stream
with failure on the first `Task` that was completed with `Failure`.
