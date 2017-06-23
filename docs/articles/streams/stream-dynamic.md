---
layout: docs.hbs
title: Dynamic stream handling
---

#Controlling graph completion with KillSwitch

A ``KillSwitch`` allows the completion of graphs of ``FlowShape`` from the outside. It consists of a flow element that
can be linked to a graph of ``FlowShape`` needing completion control.
The ``IKillSwitch`` interface allows to complete or fail the graph(s).

```csharp
public interface IKillSwitch
{
    /// <summary>
    /// After calling <see cref="Shutdown"/> the linked <see cref="IGraph{TShape}"/>s of <see cref="FlowShape{TIn,TOut}"/> are completed normally.
    /// </summary>
    void Shutdown();

    /// <summary>
    /// After calling <see cref="Abort"/> the linked <see cref="IGraph{TShape}"/>s of <see cref="FlowShape{TIn,TOut}"/> are failed.
    /// </summary>
    void Abort(Exception cause);
}
```

After the first call to either ``Shutdown`` and ``Abort``, all subsequent calls to any of these methods will be ignored.
Graph completion is performed by both

* completing its downstream
* cancelling (in case of ``Shutdown``) or failing (in case of ``Abort``) its upstream.

A ``KillSwitch`` can control the completion of one or multiple streams, and therefore comes in two different flavours.

####UniqueKillSwitch

``UniqueKillSwitch`` allows to control the completion of **one** materialized ``Graph`` of ``FlowShape``. Refer to the
below for usage examples.

* **Shutdown**

```csharp
var countingSource = Source.From(Enumerable.Range(1, int.MaxValue))
    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure);
var lastSink = Sink.Last<int>();

var t =
    countingSource.ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
        .ToMaterialized(lastSink, Keep.Both)
        .Run(materializer);

var killSwitch = t.Item1;
var last = t.Item2;

DoSomethingElse();

killSwitch.Shutdown();

last.Result.Should().Be(2);
```

* **Abort**

```csharp
var countingSource = Source.From(Enumerable.Range(1, int.MaxValue))
    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure);
var lastSink = Sink.Last<int>();

var t =
    countingSource.ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
        .ToMaterialized(lastSink, Keep.Both)
        .Run(materializer);

var killSwitch = t.Item1;
var last = t.Item2;

var error = new Exception("boom!");
killSwitch.Abort(error);

last.Wait();
last.Exception.Should().Be(error);
```

####SharedKillSwitch

A ``SharedKillSwitch`` allows to control the completion of an arbitrary number graphs of ``FlowShape``. It can be
materialized multiple times via its ``Flow`` method, and all materialized graphs linked to it are controlled by the switch.
Refer to the below for usage examples.

* **Shutdown**

```csharp
var countingSource = Source.From(Enumerable.Range(1, int.MaxValue))
    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure);
var lastSink = Sink.Last<int>();
var sharedKillSwitch = KillSwitches.Shared("my-kill-switch");

var last = countingSource.Via(sharedKillSwitch.Flow<int>()).RunWith(lastSink, materializer);

var delayedLast =
    countingSource.Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure)
        .Via(sharedKillSwitch.Flow<int>())
        .RunWith(lastSink, materializer);

DoSomethingElse();

sharedKillSwitch.Shutdown();

last.Result.Should().Be(2);
delayedLast.Result.Should().Be(1);
```

* **Abort**

```csharp
var countingSource = Source.From(Enumerable.Range(1, int.MaxValue))
    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure);
var lastSink = Sink.Last<int>();
var sharedKillSwitch = KillSwitches.Shared("my-kill-switch");

var last1 = countingSource.Via(sharedKillSwitch.Flow<int>()).RunWith(lastSink, materializer);
var last2 = countingSource.Via(sharedKillSwitch.Flow<int>()).RunWith(lastSink, materializer);


var error = new Exception("boom!");
sharedKillSwitch.Abort(error);

last1.Wait();
last2.Wait();
last1.Exception.Should().Be(error);
last2.Exception.Should().Be(error);
```

> [!NOTE]
> A ``UniqueKillSwitch`` is always a result of a materialization, whilst ``SharedKillSwitch`` needs to be constructed before any materialization takes place.
