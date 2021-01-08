---
uid: streams-pipelining
title: Pipelining and Parallelism
---

# Pipelining and Parallelism

Akka Streams processing stages (be it simple operators on Flows and Sources or graph junctions) are "fused" together
and executed sequentially by default. This avoids the overhead of events crossing asynchronous boundaries but
limits the flow to execute at most one stage at any given time.

In many cases it is useful to be able to concurrently execute the stages of a flow, this is done by explicitly marking
them as asynchronous using the ``Async`` method. Each processing stage marked as asynchronous will run in a
dedicated actor internally, while all stages not marked asynchronous will run in one single actor.

We will illustrate through the example of pancake cooking how streams can be used for various processing patterns,
exploiting the available parallelism on modern computers. The setting is the following: both Chris and Bartosz
like to make pancakes, but they need to produce sufficient amount in a cooking session to make all of the children
happy. To increase their pancake production throughput they use two frying pans. How they organize their pancake
processing is markedly different.

## Pipelining

Bartosz uses the two frying pans in an asymmetric fashion. The first pan is only used to fry one side of the
pancake then the half-finished pancake is flipped into the second pan for the finishing fry on the other side.
Once the first frying pan becomes available it gets a new scoop of batter. As an effect, most of the time there
are two pancakes being cooked at the same time, one being cooked on its first side and the second being cooked to
completion.
This is how this setup would look like implemented as a stream:

```csharp
// Takes a scoop of batter and creates a pancake with one side cooked
var fryingPan1 = Flow.Create<ScoopOfBatter>().Select(batter => HalfCookedPancake());

// Finishes a half-cooked pancake
var fryingPan2 = Flow.Create<HalfCookedPancake>().Select(halfCooked => Pancake());

// With the two frying pans we can fully cook pancakes
var pancakeChef = Flow.Create<ScoopOfBatter>().Via(fryingPan1.Async()).Via(fryingPan2.Async());
```

The two ``Select`` stages in sequence (encapsulated in the "frying pan" flows) will be executed in a pipelined way,
basically doing the same as Bartosz with his frying pans:

 1. A ``ScoopOfBatter`` enters ``fryingPan1``
 2. ``fryingPan1`` emits a HalfCookedPancake once ``fryingPan2`` becomes available
 3. ``fryingPan2`` takes the HalfCookedPancake
 4. at this point fryingPan1 already takes the next scoop, without waiting for fryingPan2 to finish

The benefit of pipelining is that it can be applied to any sequence of processing steps that are otherwise not
parallelisable (for example because the result of a processing step depends on all the information from the previous
step). One drawback is that if the processing times of the stages are very different then some of the stages will not
be able to operate at full throughput because they will wait on a previous or subsequent stage most of the time. In the
pancake example frying the second half of the pancake is usually faster than frying the first half, ``fryingPan2`` will
not be able to operate at full capacity <a href="#foot-note-1">[1]</a>.

> [!NOTE]
> Asynchronous stream processing stages have internal buffers to make communication between them more efficient.
For more details about the behavior of these and how to add additional buffers refer to 
[Buffers and working with rate](xref:streams-buffers).

## Parallel processing
Chris uses the two frying pans symmetrically. He uses both pans to fully fry a pancake on both sides, then puts
the results on a shared plate. Whenever a pan becomes empty, he takes the next scoop from the shared bowl of batter.
In essence he parallelizes the same process over multiple pans. This is how this setup will look like if implemented
using streams:

```csharp
var fryingPan = Flow.Create<ScoopOfBatter>().Select(batter => Pancake());

var pancakeChef = Flow.FromGraph(GraphDsl.Create(b =>
{
    var dispatchBatter = b.Add(new Balance<ScoopOfBatter>(2));
    var mergePancakes = b.Add(new Merge<Pancake>(2));

    // Using two frying pans in parallel, both fully cooking a pancake from the batter.
    // We always put the next scoop of batter to the first frying pan that becomes available.
    b.From(dispatchBatter.Out(0)).Via(fryingPan.Async()).To(mergePancakes.In(0));
    // Notice that we used the "fryingPan" flow without importing it via builder.Add().
    // Flows used this way are auto-imported, which in this case means that the two
    // uses of "fryingPan" mean actually different stages in the graph.
    b.From(dispatchBatter.Out(1)).Via(fryingPan.Async()).To(mergePancakes.In(1));

    return new FlowShape<ScoopOfBatter, Pancake>(dispatchBatter.In, mergePancakes.Out);
}));
```
The benefit of parallelizing is that it is easy to scale. In the pancake example
it is easy to add a third frying pan with Chris' method, but Bartosz cannot add a third frying pan,
since that would require a third processing step, which is not practically possible in the case of frying pancakes.

One drawback of the example code above that it does not preserve the ordering of pancakes. This might be a problem
if children like to track their "own" pancakes. In those cases the ``Balance`` and ``Merge`` stages should be replaced
by strict round-robin balancing and merging stages that put in and take out pancakes in a strict order.

A more detailed example of creating a worker pool can be found in the cookbook: 
[Balancing jobs to a fixed pool of workers](xref:streams-cookbook#balancing-jobs-to-a-fixed-pool-of-workers)

## Combining pipelining and parallel processing

The two concurrency patterns that we demonstrated as means to increase throughput are not exclusive.
In fact, it is rather simple to combine the two approaches and streams provide
a nice unifying language to express and compose them.

First, let's look at how we can parallelize pipelined processing stages. In the case of pancakes this means that we
will employ two chefs, each working using Bartosz's pipelining method, but we use the two chefs in parallel, just like
Chris used the two frying pans. This is how it looks like if expressed as streams:

```csharp
var pancakeChef = Flow.FromGraph(GraphDsl.Create(b =>
{
    var dispatchBatter = b.Add(new Balance<ScoopOfBatter>(2));
    var mergePancakes = b.Add(new Merge<Pancake>(2));

    // Using two pipelines, having two frying pans each, in total using
    // four frying pans
    b.From(dispatchBatter.Out(0)).Via(fryingPan1.Async()).Via(fryingPan2.Async()).To(mergePancakes.In(0));
    b.From(dispatchBatter.Out(1)).Via(fryingPan1.Async()).Via(fryingPan2.Async()).To(mergePancakes.In(1));

    return new FlowShape<ScoopOfBatter, Pancake>(dispatchBatter.In, mergePancakes.Out);
}));
```

The above pattern works well if there are many independent jobs that do not depend on the results of each other, but
the jobs themselves need multiple processing steps where each step builds on the result of
the previous one. In our case individual pancakes do not depend on each other, they can be cooked in parallel, on the
other hand it is not possible to fry both sides of the same pancake at the same time, so the two sides have to be fried
in sequence.

It is also possible to organize parallelized stages into pipelines. This would mean employing four chefs:

 - the first two chefs prepare half-cooked pancakes from batter, in parallel, then putting those on a large enough
   flat surface.
 - the second two chefs take these and fry their other side in their own pans, then they put the pancakes on a shared
   plate.

This is again straightforward to implement with the streams API:

```csharp
var pancakeChefs1 = Flow.FromGraph(GraphDsl.Create(b =>
{
    var dispatchBatter = b.Add(new Balance<ScoopOfBatter>(2));
    var mergeHalfPancakes = b.Add(new Merge<HalfCookedPancake>(2));

    // Two chefs work with one frying pan for each, half-frying the pancakes then putting
    // them into a common pool
    b.From(dispatchBatter.Out(0)).Via(fryingPan1.Async()).To(mergeHalfPancakes.In(0));
    b.From(dispatchBatter.Out(1)).Via(fryingPan1.Async()).To(mergeHalfPancakes.In(1));

    return new FlowShape<ScoopOfBatter, HalfCookedPancake>(dispatchBatter.In, mergeHalfPancakes.Out);
}));

var pancakeChefs2 = Flow.FromGraph(GraphDsl.Create(b =>
{
    var dispatchHalfPancakes = b.Add(new Balance<HalfCookedPancake>(2));
    var mergePancakes = b.Add(new Merge<Pancake>(2));

    // Two chefs work with one frying pan for each, finishing the pancakes then putting
    // them into a common pool
    b.From(dispatchHalfPancakes.Out(0)).Via(fryingPan2.Async()).To(mergePancakes.In(0));
    b.From(dispatchHalfPancakes.Out(1)).Via(fryingPan2.Async()).To(mergePancakes.In(1));

    return new FlowShape<HalfCookedPancake, Pancake>(dispatchHalfPancakes.In, mergePancakes.Out);
}));

var kitchen = pancakeChefs1.Via(pancakeChefs2);
```

This usage pattern is less common but might be usable if a certain step in the pipeline might take wildly different
times to finish different jobs. The reason is that there are more balance-merge steps in this pattern
compared to the parallel pipelines. This pattern rebalances after each step, while the previous pattern only balances
at the entry point of the pipeline. This only matters however if the processing time distribution has a large
deviation.

<a name="foot-note-1">[1]</a> Bartosz's reason for this seemingly suboptimal procedure is that he prefers the temperature of the second pan
       to be slightly lower than the first in order to achieve a more homogeneous result.
