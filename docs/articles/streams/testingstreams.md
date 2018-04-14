---
uid: streams-testing
title: Testing streams
---

# Testing streams

Verifying behaviour of Akka Stream sources, flows and sinks can be done using
various code patterns and libraries. Here we will discuss testing these
elements using:

- simple sources, sinks and flows;
- sources and sinks in combination with `TestProbe` from the `Akka.Testkit` module;
- sources and sinks specifically crafted for writing tests from the `Akka.Streams.Testkit` module.

It is important to keep your data processing pipeline as separate sources,
flows and sinks. This makes them easily testable by wiring them up to other
sources or sinks, or some test harnesses that `Akka.Testkit` or
`Akka.Streams.Testkit` provide.

## Built in sources, sinks and combinators

Testing a custom sink can be as simple as attaching a source that emits
elements from a predefined collection, running a constructed test flow and
asserting on the results that sink produced. Here is an example of a test for a
sink:

```csharp
var sinkUnderTest = Flow.Create<int>()
    .Select(x => x*2)
    .ToMaterialized(Sink.Aggregate<int, int>(0, (sum, i) => sum + i), Keep.Right);

var task = Source.From(Enumerable.Range(1, 4)).RunWith(sinkUnderTest, materializer);
task.Wait(TimeSpan.FromMilliseconds(500)).Should().BeTrue();
task.Result.Should().Be(20);
```

The same strategy can be applied for sources as well. In the next example we
have a source that produces an infinite stream of elements. Such source can be
tested by asserting that first arbitrary number of elements hold some
condition. Here the ``Grouped`` combinator and ``Sink.First`` are very useful.

```csharp
var sourceUnderTest = Source.Repeat(1).Select(x => x*2);

var task = sourceUnderTest.Grouped(10).RunWith(Sink.First<IEnumerable<int>>(), materializer);
task.Wait(TimeSpan.FromMilliseconds(500)).Should().BeTrue();
task.Result.ShouldAllBeEquivalentTo(Enumerable.Repeat(2, 10));
```

When testing a flow we need to attach a source and a sink. As both stream ends
are under our control, we can choose sources that tests various edge cases of
the flow and sinks that ease assertions.

```csharp
var flowUnderTest = Flow.Create<int>().TakeWhile(x => x < 5);

var task = Source.From(Enumerable.Range(1, 10))
    .Via(flowUnderTest)
    .RunWith(Sink.Aggregate<int, List<int>>(new List<int>(), (list, i) =>
    {
        list.Add(i);
        return list;
    }), materializer);

task.Wait(TimeSpan.FromMilliseconds(500)).Should().BeTrue();
task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 4));
```

## TestKit

Akka Stream offers integration with Actors out of the box. This support can be
used for writing stream tests that use familiar `TestProbe` from the
`Akka.testkit` API.

One of the more straightforward tests would be to materialize stream to a
`Task` and then use ``pipe`` pattern to pipe the result of that future
to the probe.

```csharp
var sourceUnderTest = Source.From(Enumerable.Range(1, 4)).Grouped(2);

var expected = new[] {Enumerable.Range(1, 2), Enumerable.Range(3, 2)}.AsEnumerable();
var probe = CreateTestProbe();

sourceUnderTest.Grouped(2)
    .RunWith(Sink.First<IEnumerable<IEnumerable<int>>>(), materializer)
    .PipeTo(probe.Ref);
    
probe.ExpectMsg(expected);
```

Instead of materializing to a task, we can use a `Sink.ActorRef` that
sends all incoming elements to the given `IActorRef`. Now we can use
assertion methods on `TestProbe` and expect elements one by one as they
arrive. We can also assert stream completion by expecting for
``OnCompleteMessage`` which was given to `Sink.ActorRef`.

```csharp
var sourceUnderTest = Source.Tick(TimeSpan.FromSeconds(0), TimeSpan.FromMilliseconds(200), "Tick");

var probe = CreateTestProbe();
var cancellable = sourceUnderTest.To(Sink.ActorRef<string>(probe.Ref, "completed")).Run(materializer);

probe.ExpectMsg("Tick");
probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
probe.ExpectMsg("Tick", TimeSpan.FromMilliseconds(200));
cancellable.Cancel();
probe.ExpectMsg("completed");
```

Similarly to `Sink.ActorRef` that provides control over received
elements, we can use `Source.ActorRef` and have full control over
elements to be sent.

```csharp
var sinkUnderTest = Flow.Create<int>()
    .Select(x => x.ToString())
    .ToMaterialized(Sink.Aggregate<string, string>("", (s, s1) => s + s1), Keep.Right);

var t = Source.ActorRef<int>(8, OverflowStrategy.Fail)
    .ToMaterialized(sinkUnderTest, Keep.Both)
    .Run(materializer);

var actorRef = t.Item1;
var task = t.Item2;

actorRef.Tell(1);
actorRef.Tell(2);
actorRef.Tell(3);
actorRef.Tell(new Status.Success("done"));

task.Wait(TimeSpan.FromMilliseconds(500)).Should().BeTrue();
task.Result.Should().Be("123");
```

## Streams TestKit

You may have noticed various code patterns that emerge when testing stream
pipelines. Akka Stream has a separate `Akka.Streams.Testkit` module that
provides tools specifically for writing stream tests. This module comes with
two main components that are `TestSource` and `TestSink` which
provide sources and sinks that materialize to probes that allow fluent API.

> [!NOTE]
> Be sure to add the module `Akka.Streams.Testkit` to your dependencies.

A sink returned by ``TestSink.Probe`` allows manual control over demand and
assertions over elements coming downstream.

```csharp
var sourceUnderTest = Source.From(Enumerable.Range(1, 4)).Where(x => x%2 == 0).Select(x => x*2);

sourceUnderTest.RunWith(this.SinkProbe<int>(), materializer)
    .Request(2)
    .ExpectNext(4, 8)
    .ExpectComplete();
```

A source returned by ``TestSource.Probe`` can be used for asserting demand or
controlling when stream is completed or ended with an error.

```csharp
var sinkUnderTest = Sink.Cancelled<int>();

this.SourceProbe<int>()
    .ToMaterialized(sinkUnderTest, Keep.Left)
    .Run(materializer)
    .ExpectCancellation();
```

You can also inject exceptions and test sink behaviour on error conditions.

```csharp
var sinkUnderTest = Sink.First<int>();

var t = this.SourceProbe<int>()
    .ToMaterialized(sinkUnderTest, Keep.Both)
    .Run(materializer);
var probe = t.Item1;
var task = t.Item2;

probe.SendError(new Exception("boom"));

task.Wait(TimeSpan.FromMilliseconds(500)).Should().BeTrue();
task.Exception.Message.Should().Be("boom");
```

Test source and sink can be used together in combination when testing flows.

```csharp
var flowUnderTest = Flow.Create<int>().SelectAsyncUnordered(2, sleep => Task.Run(() =>
{
    Thread.Sleep(10*sleep);
    return sleep;
}));

var t = this.SourceProbe<int>()
    .Via(flowUnderTest)
    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
    .Run(materializer);

var pub = t.Item1;
var sub = t.Item2;

sub.Request(3);
pub.SendNext(3);
pub.SendNext(2);
pub.SendNext(1);

sub.ExpectNextUnordered(1, 2, 3);

pub.SendError(new Exception("Power surge in the linear subroutine C-47!"));
var ex = sub.ExpectError();
ex.Message.Should().Contain("C-47");
```

## Fuzzing Mode

For testing, it is possible to enable a special stream execution mode that exercises concurrent execution paths
more aggressively (at the cost of reduced performance) and therefore helps exposing race conditions in tests. To
enable this setting add the following line to your configuration:

```
akka.stream.materializer.debug.fuzzing-mode = on
```

> [!WARNING]
> Never use this setting in production or benchmarks. This is a testing tool to provide more coverage of your code
during tests, but it reduces the throughput of streams. A warning message will be logged if you have this setting
enabled.
