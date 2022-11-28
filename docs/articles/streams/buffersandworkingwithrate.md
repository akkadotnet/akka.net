---
uid: streams-buffers
title: Buffers and working with rate
---

# Buffers and Working With Rate

When upstream and downstream rates differ, especially when the throughput has spikes, it can be useful to introduce buffers in a stream. In this chapter we cover how buffers are used in Akka Streams.

## Buffers for Asynchronous Stages

In this section we will discuss internal buffers that are introduced as an optimization when using asynchronous stages.
To run a stage asynchronously it has to be marked explicitly as such using the .async method. Being run asynchronously means that a stage, after handing out an element to its downstream consumer is able to immediately process the next message. To demonstrate what we mean by this, let's take a look at the following example:

```csharp
     var source = Source.From(Enumerable.Range(1, 100))
                   .Select(i => { Console.WriteLine($"A: {i}"); return i; }).Async()
                   .Select(i => { Console.WriteLine($"B: {i}"); return i; }).Async()
                   .Select(i => { Console.WriteLine($"C: {i}"); return i; }).Async()
                    .RunWith(Sink.Ignore<int>(), materializer);
```

Running the above example, one of the possible outputs looks like this:

```text
A: 2
B: 1
A: 3
B: 2
C: 1
B: 3
C: 2
C: 3
```

Note that the order is *not* `A:1, B:1, C:1, A:2, B:2, C:2,` which would correspond to the normal fused synchronous execution model of flows where an element completely passes through the processing pipeline before the next element enters the flow. The next element is processed by an asynchronous stage as soon as it is emitted the previous one.

While pipelining in general increases throughput, in practice there is a cost of passing an element through the asynchronous (and therefore thread crossing) boundary which is significant. To amortize this cost Akka Streams uses a windowed, batching backpressure strategy internally. It is windowed because as opposed to a Stop-And-Wait protocol multiple elements might be "in-flight" concurrently with requests for elements. It is also batching because a new element is not immediately requested once an element has been drained from the window-buffer but multiple elements are requested after multiple elements have been drained. This batching strategy reduces the communication cost of propagating the backpressure signal through the asynchronous boundary.

While this internal protocol is mostly invisible to the user (apart form its throughput increasing effects) there are situations when these details get exposed. In all of our previous examples we always assumed that the rate of the processing chain is strictly coordinated through the backpressure signal causing all stages to process no faster than the throughput of the connected chain. There are tools in Akka Streams however that enable the rates of different segments of a processing chain to be "detached" or to define the maximum throughput of the stream through external timing sources. These situations are exactly those where the internal batching buffering strategy suddenly becomes non-transparent.

## Internal Buffers and Their Effect

As we have explained, for performance reasons Akka Streams introduces a buffer for every asynchronous processing stage. The purpose of these buffers is solely optimization, in fact the size of 1 would be the most natural choice if there would be no need for throughput improvements. Therefore it is recommended to keep these buffer sizes small, and increase them only to a level suitable for the throughput requirements of the application. Default buffer sizes can be set through configuration:

```hocon
akka.stream.materializer.max-input-buffer-size = 16
```

Alternatively they can be set by passing a `ActorMaterializerSettings` to the materializer:

```csharp
var materializer = ActorMaterializer.Create(system, 
ActorMaterializerSettings.Create(system).WithInputBuffer(64, 64));
```

If the buffer size needs to be set for segments of a Flow only, it is possible by defining a separate Flow with these attributes:

```csharp
var section = Flow.Create<int>()
.Select(_ => _*2)
.Async()
.WithAttributes(Attributes.CreateInputBuffer(1,1));// the buffer size of this map is 1

var flow = Flow.FromGraph(section)
.Via(Flow.Create<int>().Select(_ => _/2))
.Async(); // the buffer size of this map is the default
```

Here is an example of a code that demonstrate some of the issues caused by internal buffers:

```csharp
RunnableGraph.FromGraph(GraphDsl.Create(b => {
    // this is the asynchronous stage in this graph
    var zipper = b.Add(ZipWith.Apply<Tick,int,int>((tick, count) => count).Async());

    var s = b.Add(Source.Tick(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), new Tick()));
    b.From(s).To(zipper.In0);

    var s2 = b.Add(Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "message!")
        .ConflateWithSeed(seed => 1, (count, _) => count + 1));
    
    b.From(s2).To(zipper.In1);

    b.From(zipper.Out).To(Sink.ForEach<int>(i => Console.WriteLine($"test: {i}"))
        .MapMaterializedValue(_ => NotUsed.Instance));
   
    return ClosedShape.Instance;
                }));
```

Running the above example one would expect the number 3 to be printed in every 3 seconds (the `conflateWithSeed` step here is configured so that it counts the number of elements received before the downstream `ZipWith` consumes them). What is being printed is different though, we will see the number 1. The reason for this is the internal buffer which is by default 16 elements large, and pre-fetches elements before the `ZipWith` starts consuming them. It is possible to fix this issue by changing the buffer size of `ZipWith` (or the whole graph) to 1. We will still see a leading 1 though which is caused by an initial pre-fetch of the `ZipWith` element.

> [!NOTE]
> In general, when time or rate driven processing stages exhibit strange behavior, one of the first solutions to try should be to decrease the input buffer of the affected elements to 1.

## Buffers in Akka Streams

In this section we will discuss explicit user defined buffers that are part of the domain logic of the stream processing pipeline of an application.

The example below will ensure that 1000 jobs (but not more) are dequeued from an external (imaginary) system and stored locally in memory -- relieving the external system:

```csharp
// Getting a stream of jobs from an imaginary external system as a Source
Source<Job,NotUsed> jobs = inboundJobsConnector()
jobs.buffer(1000, OverflowStrategy.backpressure);
```

The next example will also queue up 1000 jobs locally, but if there are more jobs waiting in the imaginary external systems, it makes space for the new element by dropping one element from the tail of the buffer. Dropping from the tail is a very common strategy but it must be noted that this will drop the youngest waiting job. If some "fairness" is desired in the sense that we want to be nice to jobs that has been waiting for long, then this option can be useful.

```csharp
  jobs.buffer(1000, OverflowStrategy.dropTail)
```

Instead of dropping the youngest element from the tail of the buffer a new element can be dropped without en-queueing it to the buffer at all.

```csharp
    jobs.buffer(1000, OverflowStrategy.dropNew)
```

Here is another example with a queue of 1000 jobs, but it makes space for the new element by dropping one element from the head of the buffer. This is the oldest waiting job. This is the preferred strategy if jobs are expected to be resent if not processed in a certain period. The oldest element will be retransmitted soon, (in fact a retransmitted duplicate might be already in the queue!) so it makes sense to drop it first.

```csharp
jobs.buffer(1000, OverflowStrategy.dropHead)
```

Compared to the dropping strategies above, dropBuffer drops all the 1000 jobs it has enqueued once the buffer gets full. This aggressive strategy is useful when dropping jobs is preferred to delaying jobs.

```csharp
jobs.buffer(1000, OverflowStrategy.dropBuffer)
```

If our imaginary external job provider is a client using our API, we might want to enforce that the client cannot have more than 1000 queued jobs otherwise we consider it flooding and terminate the connection. This is easily achievable by the error strategy which simply fails the stream once the buffer gets full.

```csharp
jobs.buffer(1000, OverflowStrategy.fail)
```

## Rate Transformation

### Understanding Conflate

When a fast producer can not be informed to slow down by backpressure or some other signal, conflate might be useful to combine elements from a producer until a demand signal comes from a consumer.

Below is an example snippet that summarizes fast stream of elements to a standard deviation, mean and count of elements that have arrived while the stats have been calculated.

```csharp
  var statsFlow = Flow.Create<double>()
                    .ConflateWithSeed(_ => ImmutableList.Create(_), (agg, acc) => agg.Add(acc))
                    .Select(s => {
                        var u = s.Sum()/s.Count();

                        var se = s.Select(x => Math.Pow(x - u, 2));
                        var s = Math.Sqrt(se.Sum()/ se.Count());
                        return new { s , u , size=s.Count()};
                    });
```

This example demonstrates that such flow's rate is decoupled. The element rate at the start of the flow can be much higher that the element rate at the end of the flow.

Another possible use of `conflate` is to not consider all elements for summary when producer starts getting too fast. Example below demonstrates how conflate can be used to implement random drop of elements when consumer is not able to keep up with the producer.

```csharp
  var p = 0.01;
  var sampleFlow = Flow.Create<double>()
       .ConflateWithSeed(x => ImmutableList.Create(x), (agg, d) => {
           if (ThreadLocalRandom.Current.NextDouble() < p)
               agg.Add(d);
           return agg;
       }).Concat(identity);
```

### Understanding Expand

Expand helps to deal with slow producers which are unable to keep up with the demand coming from consumers. Expand allows to extrapolate a value to be sent as an element to a consumer.

As a simple use of expand here is a flow that sends the same element to consumer when producer does not send any new elements.

```csharp
 var lastFlow = Flow.Create<int>()
        .Expand(_ => Enumerable.Repeat(_, int.MaxValue).GetEnumerator()); 
```

Expand also allows to keep some state between demand requests from the downstream. Leveraging this, here is a flow that tracks and reports a drift between fast consumer and slow producer.

```csharp
var driftFlow = Flow.Create<int>()
    .Expand(i => Enumerable.Repeat(0, int.MaxValue).Select(n => new {i, n}).GetEnumerator());
```

Note that all of the elements coming from upstream will go through expand at least once. This means that the output of this flow is going to report a drift of zero if producer is fast enough, or a larger drift otherwise.

### Reusing Values Downstream

When working with fan-in stages such as `Zip` where one `Source<T>` might produce messages infrequently, it can be helpful to cache the previous value and re-use it in combination with a faster stream.

For instance, consider the following scenario:

* A `Source<HttpClient, NotUsed>` that emits an updated `HttpClient` with new bearer-token credentials every 30 minutes and
* A `Source<HttpRequestMessage, NotUsed>` that emits outbound `HttpRequestMessage`s as they come - at any given moment it can produce zero requests per second or thousands of requests per second.

In this scenario we're going to want to combine the `Source<HttpClient, NotUsed>` and `Source<HttpRequestMessage, NotUsed>` together so the `HttpClient` can execute all of the `HttpRequestMessage`s - however, given that `HttpClient`s are only emitted once every 30 minutes - how can we use a stage like `Zip` to make sure that every `HttpRequestMessage` gets serviced in a timely, low-latency fashion?

Enter the `ReuseLatest` stage - which will allow us to reuse the most recent `HttpClient` each time a new `HttpRequestMessage` arrives:

```csharp
public static Source<HttpClient, ICancelable> CreateSourceInternal(string clientId,
        Func<Task<string>> tokenProvider, TimeSpan tokenRefreshTimeout)
{
        var source = Source.Tick(TimeSpan.Zero, TimeSpan.FromSeconds(30), clientId)
            .SelectAsync(1, async c => 
                // refresh bearer token, create new HttpClient
                CreateClient(c, (await tokenProvider().WaitAsync(tokenRefreshTimeout))))
             // reuse the previous value whenever there's downstream demand
            .ReuseLatest();
        return source;
}
```

This type of design allows us to decouple the rate at which `HttpClient`s are produced from the rate at which `HttpRequestMessage`s are.
