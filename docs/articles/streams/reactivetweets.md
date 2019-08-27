---
uid: streams-tweets
title: Reactive Tweets
---

# Reactive Tweets

A typical use case for stream processing is consuming a live stream of data that we want to extract or aggregate some
other data from. In this example we'll consider consuming a stream of tweets and extracting information from them.

We will also consider the problem inherent to all non-blocking streaming
solutions: *"What if the subscriber is too slow to consume the live stream of
data?"*. Traditionally the solution is often to buffer the elements, but this
can—and usually will—cause eventual buffer overflows and instability of such
systems. Instead Akka Streams depend on internal backpressure signals that
allow to control what should happen in such scenarios.

> [!NOTE]
>  You can find a example implementation [here](https://github.com/Silv3rcircl3/Akka.Net-Streams-reactive-tweets), it's using 
  [Tweetinvi](https://github.com/linvi/tweetinvi) to call the Twitter STREAM API.
  Due to the fact that Tweetinvi doesn't implement the Reactive Streams specifications, we push the tweets into the stream
  via the `IActorRef` that is materialized from the following Source `Source.ActorRef<ITweet>(100, OverflowStrategy.DropHead);`.

## Transforming and consuming simple streams

The example application we will be looking at is a simple Twitter feed stream from which we'll want to extract certain information,
like for example the number of tweets a user has posted.

In order to prepare our environment by creating an `ActorSystem` and `ActorMaterializer`,
which will be responsible for materializing and running the streams we are about to create:

```csharp
using (var sys = ActorSystem.Create("Reactive-Tweets"))
{
    using (var mat = sys.Materializer())
    {
    }
}
```

The `ActorMaterializer` can optionally take `ActorMaterializerSettings` which can be used to define
materialization properties, such as default buffer sizes (see also [Buffers for asynchronous stages](xref:streams-buffers#buffers-for-asynchronous-stages)), the dispatcher to
be used by the pipeline etc. These can be overridden with ``WithAttributes`` on `Flow`, `Source`, `Sink` and `IGraph`.

Let's assume we have a stream of tweets readily available. In Akka this is expressed as a `Source[Out, M]`:

```csharp
Source<ITweet, NotUsed> tweetSource;
```

Streams always start flowing from a `Source[Out,M1]` then can continue through `Flow[In,Out,M2]` elements or
more advanced graph elements to finally be consumed by a `Sink[In,M3]` (ignore the type parameters ``M1``, ``M2``
and ``M3`` for now, they are not relevant to the types of the elements produced/consumed by these classes – they are
"materialized types", which we'll talk about [below](#materialized-value)).

The operations should look familiar to anyone who has used the .Net Collections library,
however they operate on streams and not collections of data (which is a very important distinction, as some operations
only make sense in streaming and vice versa):

```csharp
Source<string, NotUsed> formattedRetweets = tweetSource
  .Where(tweet => tweet.IsRetweet)
  .Select(FormatTweet);
```

Finally in order to [materialize](xref:streams-basics#stream-materialization) and run the stream computation we need to attach
the Flow to a `Sink` that will get the Flow running. The simplest way to do this is to call
``RunWith(sink, mat)`` on a ``Source``. For convenience a number of common Sinks are predefined and collected as methods on
the `Sink` [companion class](https://github.com/akkadotnet/akka.net/blob/dev/src/core/Akka.Streams/Dsl/Sink.cs).
For now let's simply print the formatted retweets:

```csharp
formattedRetweets.RunWith(Sink.ForEach<string>(Console.WriteLine), mat);
```

or by using the shorthand version (which are defined only for the most popular Sinks such as ``Sink.Aggregate`` and ``Sink.Foreach``):

```csharp
formattedRetweets.RunForeach(Console.WriteLine, mat);
```

Materializing and running a stream always requires a `Materializer` to be passed in like this: ``.Run(materializer)``

```csharp
using (var sys = ActorSystem.Create("Reactive-Tweets"))
{
    using (var mat = sys.Materializer())
    {
        Source<string, NotUsed> formattedRetweets = tweetSource
          .Where(tweet => tweet.IsRetweet)
          .Select(FormatTweet);
        
        formattedRetweets.RunForeach(Console.WriteLine, mat);
    }
}
```

## Flattening sequences in streams
In the previous section we were working on 1:1 relationships of elements which is the most common case, but sometimes
we might want to map from one element to a number of elements and receive a "flattened" stream, similarly like ``SelectMany``
works on .Net Collections. In order to get a flattened stream of hashtags from our stream of tweets we can use the ``SelectMany``
combinator:

```csharp
Source<IHashtagEntity, NotUsed> hashTags = tweetSource.SelectMany(tweet => tweet.Hashtags);
```

## Broadcasting a stream
Now let's say we want to persist all hashtags, as well as all author names from this one live stream.
For example we'd like to write all author handles into one file, and all hashtags into another file on disk.
This means we have to split the source stream into two streams which will handle the writing to these different files.

Elements that can be used to form such "fan-out" (or "fan-in") structures are referred to as "junctions" in Akka Streams.
One of these that we'll be using in this example is called `Broadcast`, and it simply emits elements from its
input port to all of its output ports.

Akka Streams intentionally separate the linear stream structures (Flows) from the non-linear, branching ones (Graphs)
in order to offer the most convenient API for both of these cases. Graphs can express arbitrarily complex stream setups
at the expense of not reading as familiarly as collection transformations.

Graphs are constructed using `GraphDSL` like this:

```csharp
Sink<IUser, NotUsed> writeAuthors = null;
Sink<IHashtagEntity, NotUsed> writeHashtags = null;

var g = RunnableGraph.FromGraph(GraphDsl.Create(b =>
{
    var broadcast = b.Add(new Broadcast<ITweet>(2));
    b.From(tweetSource).To(broadcast.In);
    b.From(broadcast.Out(0))
        .Via(Flow.Create<ITweet>().Select(tweet => tweet.CreatedBy))
        .To(writeAuthors);
    b.From(broadcast.Out(1))
        .Via(Flow.Create<ITweet>().SelectMany(tweet => tweet.Hashtags))
        .To(writeHashtags);

    return ClosedShape.Instance;
}));

g.Run(mat);
```

As you can see, inside the `GraphDSL` we use an implicit graph builder ``b`` to mutably construct the graph
using the ``from``, `via` or `to` "edge operator".

``GraphDSL.Create`` returns a `IGraph`, in this example a `IGraph<ClosedShape, NotUsed>` where
`ClosedShape` means that it is *a fully connected graph* or "closed" - there are no unconnected inputs or outputs.
Since it is closed it is possible to transform the graph into a `IRunnableGraph` using ``RunnableGraph.FromGraph``.
The runnable graph can then be ``Run()`` to materialize a stream out of it.

Both `IGraph` and `IRunnableGraph` are *immutable, thread-safe, and freely shareable*.

A graph can also have one of several other shapes, with one or more unconnected ports. Having unconnected ports
expresses a graph that is a *partial graph*. Concepts around composing and nesting graphs in large structures are
explained in detail in [Modularity, Composition and Hierarchy](xref:streams-modularity#basics-of-composition-and-modularity). It is also possible to wrap complex computation graphs as Flows, Sinks or Sources, which will be explained in detail in
[Constructing Sources, Sinks and Flows from Partial Graphs](xref:streams-working-with-graphs#constructing-sources-sinks-and-flows-from-partial-graphs).

## Back-pressure in action

One of the main advantages of Akka Streams is that they *always* propagate back-pressure information from stream Sinks
(Subscribers) to their Sources (Publishers). It is not an optional feature, and is enabled at all times. To learn more
about the back-pressure protocol used by Akka Streams and all other Reactive Streams compatible implementations read
[Back-pressure explained](xref:streams-basics#back-pressure-explained).

A typical problem applications (not using Akka Streams) like this often face is that they are unable to process the incoming data fast enough,
either temporarily or by design, and will start buffering incoming data until there's no more space to buffer, resulting
in either ``OutOfMemoryError`` s or other severe degradations of service responsiveness. With Akka Streams buffering can
and must be handled explicitly. For example, if we are only interested in the "*most recent tweets, with a buffer of 10
elements*" this can be expressed using the ``Buffer`` element:

```csharp
tweetSource
    .Buffer(10, OverflowStrategy.DropHead)
    .Select(SlowComputation)
    .RunWith(Sink.Ignore<ComputationResult>(), mat);
```

The ``Buffer`` element takes an explicit and required ``OverflowStrategy``, which defines how the buffer should react
when it receives another element while it is full. Strategies provided include dropping the oldest element (``DropHead``),
dropping the entire buffer, signalling errors etc. Be sure to pick and choose the strategy that fits your use case best.

## Materialized value

So far we've been only processing data using Flows and consuming it into some kind of external Sink - be it by printing
values or storing them in some external system. However sometimes we may be interested in some value that can be
obtained from the materialized processing pipeline. For example, we want to know how many tweets we have processed.
While this question is not as obvious to give an answer to in case of an infinite stream of tweets (one way to answer
this question in a streaming setting would be to create a stream of counts described as "*up until now*, we've processed N tweets"),
but in general it is possible to deal with finite streams and come up with a nice result such as a total count of elements.

```csharp
var count = Flow.Create<ITweet>().Select(_ => 1);

var sumSink = Sink.Aggregate<int, int>(0, (agg, i) => agg + i);

var counterGraph = tweetSource.Via(count).ToMaterialized(sumSink, Keep.Right);

var sum = counterGraph.Run(mat).Result;
```

First we prepare a reusable ``Flow`` that will change each incoming tweet into an integer of value ``1``. We'll use this in
order to combine those with a ``Sink.Aggregate`` that will sum all ``Int`` elements of the stream and make its result available as
a ``Task<Int>``. Next we connect the ``tweets`` stream to ``count`` with ``via``. Finally we connect the Flow to the previously
prepared Sink using ``ToMaterialized``.

Remember those mysterious ``Mat`` type parameters on ``Source<Out, Mat>``, ``Flow<In, Out, Mat>`` and ``Sink<In, Mat>``?
They represent the type of values these processing parts return when materialized. When you chain these together,
you can explicitly combine their materialized values. In our example we used the ``Keep.Right`` predefined function,
which tells the implementation to only care about the materialized type of the stage currently appended to the right.
The materialized type of ``sumSink`` is ``Task<Int>`` and because of using ``Keep.Right``, the resulting `IRunnableGraph`
has also a type parameter of ``Task<Int>``.

This step does *not* yet materialize the
processing pipeline, it merely prepares the description of the Flow, which is now connected to a Sink, and therefore can
be ``Run()``, as indicated by its type: ``IRunnableGraph<Task<Int>>``. Next we call ``Run(mat)``
to materialize and run the Flow. The value returned by calling ``Run()`` on a ``IRunnableGraph<T>`` is of type ``T``.
In our case this type is ``Task<Int>`` which, when completed, will contain the total length of our ``tweets`` stream.
In case of the stream failing, this future would complete with a Failure.

A `IRunnableGraph` may be reused
and materialized multiple times, because it is just the "blueprint" of the stream. This means that if we materialize a stream,
for example one that consumes a live stream of tweets within a minute, the materialized values for those two materializations
will be different, as illustrated by this example:

First, let's write such an element counter using ``Sink.Aggregate`` and see how the types look like:

```csharp
var sumSink = Sink.Aggregate<int, int>(0, (agg, i) => agg + i);

var counterRunnableGraph = tweetsInMinuteFromNow
    .Where(tweet => tweet.IsRetweet)
    .Select(_ => 1)
    .ToMaterialized(sumSink, Keep.Right);
    
// materialize the stream once in the morning
var morningTweetsCount = counterGraph.Run(mat);
// and once in the evening, reusing the flow
var eveningTweetsCount = counterGraph.Run(mat);
```

Many elements in Akka Streams provide materialized values which can be used for obtaining either results of computation or
steering these elements which will be discussed in detail in [Stream Materialization](xref:streams-basics#stream-materialization). Summing up this section, now we know
what happens behind the scenes when we run this one-liner, which is equivalent to the multi line version above:

```csharp
var sum = tweetSource.Select(_ => 1).RunWith(sumSink, mat);
```

> [!NOTE]
>  ``RunWith()`` is a convenience method that automatically ignores the materialized value of any other stages except
  those appended by the ``RunWith()`` itself. In the above example it translates to using ``Keep.Right`` as the combiner
  for materialized values.
