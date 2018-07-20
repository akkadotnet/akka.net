---
uid: streams-cookbook
title: Streams Cookbook
---

# Introduction

This is a collection of patterns to demonstrate various usage of the Akka Streams API by solving small targeted
problems in the format of "recipes". The purpose of this page is to give inspiration and ideas how to approach
various small tasks involving streams. The recipes in this page can be used directly as-is, but they are most powerful as
starting points: customization of the code snippets is warmly encouraged.

This part also serves as supplementary material for the main body of documentation. It is a good idea to have this page
open while reading the manual and look for examples demonstrating various streaming concepts
as they appear in the main body of documentation.

If you need a quick reference of the available processing stages used in the recipes see 
[Overview of built-in stages and their semantics](xref:streams-builtin-stages)

# Working with Flows

In this collection we show simple recipes that involve linear flows. The recipes in this section are rather
general, more targeted recipes are available as separate sections ( [Buffers and working with rate](xref:streams-buffers), [Working with streaming IO](xref:streams-io)).

#### Logging elements of a stream

**Situation:** During development it is sometimes helpful to see what happens in a particular section of a stream.

The simplest solution is to simply use a ``Select`` operation and use ``WriteLine`` to print the elements received to the console.
While this recipe is rather simplistic, it is often suitable for a quick debug session.

```csharp
var mySource = Source.Empty<string>();

var loggedSource = mySource.Select(element =>
{
  Console.WriteLine(element);
  return element;
});
```

Another approach to logging is to use ``Log()`` operation which allows configuring logging for elements flowing through
the stream as well as completion and erroring.

```csharp
// customise log levels
mySource.Log("before-select")
    .WithAttributes(Attributes.CreateLogLevels(onElement: LogLevel.WarningLevel))
    .Select(Analyse);

// or provide custom logging adapter
mySource.Log("custom", null, Logging.GetLogger(sys, "customLogger"));
```

#### Flattening a stream of sequences

**Situation:** A stream is given as a stream of sequence of elements, but a stream of elements needed instead, streaming
all the nested elements inside the sequences separately.

The ``SelectMany`` operation can be used to implement a one-to-many transformation of elements using a mapper function
in the form of ``In => IEnumerable<Out>``. In this case we want to map a ``Enumerable`` of elements to the elements in the
collection itself, so we can just call ``SelectMany(x => x)``.

```csharp
Source<List<Message>,NotUsed > myData = someDataSource;
Source<Message, NotUsed> flattened = myData.SelectMany(x => x);
```

#### Draining a stream to a strict collection

**Situation:** A possibly unbounded sequence of elements is given as a stream, which needs to be collected into a collection while ensuring boundedness

A common situation when working with streams is one where we need to collect incoming elements into a collection.
This operation is supported via ``Sink.Seq`` which materializes into a ``Task<IEnumerable<T>>``.

The function ``Limit`` or ``Take`` should always be used in conjunction in order to guarantee stream boundedness, thus preventing the program from running out of memory.

For example, this is best avoided:

```csharp
// Dangerous: might produce a collection with 2 billion elements!
var f = mySource.RunWith(Sink.Seq<string>(), materializer);
```

Rather, use ``Limit`` or ``Take`` to ensure that the resulting ``Enumerable`` will contain only up to ``max`` elements:

```csharp
var MAX_ALLOWED_SIZE = 100;

// OK. Task will fail with a `StreamLimitReachedException`
// if the number of incoming elements is larger than max
var limited = mySource.Limit(MAX_ALLOWED_SIZE).RunWith(Sink.Seq<string>(), materializer);

// OK. Collect up until max-th elements only, then cancel upstream
var ignoreOverflow = mySource.Take(MAX_ALLOWED_SIZE).RunWith(Sink.Seq<string>(), materializer);
```

#### Calculating the digest of a ByteString stream

**Situation:** A stream of bytes is given as a stream of ``ByteStrings`` and we want to calculate the cryptographic digest
of the stream.

This recipe uses a `GraphStage` to host a `HashAlgorithm` class (part of the .Net Cryptography
API). When the stream starts, the ``onPull`` handler of the stage is called, which just bubbles up the ``pull`` event to its upstream. 
As a response to this pull, a ByteString chunk will arrive (``onPush``) which we store, then it will pull for the next chunk.

Eventually the stream of ``ByteStrings`` depletes and we get a notification about this event via ``onUpstreamFinish``.
At this point we want to emit the digest value, but we cannot do it with ``push`` in this handler directly since there may
be no downstream demand. Instead we call ``emit`` which will temporarily replace the handlers, emit the provided value when
demand comes in and then reset the stage state. It will then complete the stage.

```csharp
public class DigestCalculator : GraphStage<FlowShape<ByteString, ByteString>>
{
    private readonly string _algorithm;

    private sealed class Logic : GraphStageLogic
    {
        private readonly HashAlgorithm _digest;
        private ByteString _bytes;

        public Logic(DigestCalculator calculator) : base(calculator.Shape)
        {
            _digest = HashAlgorithm.Create(calculator._algorithm);
            _bytes = ByteString.Empty;

            SetHandler(calculator.Out, onPull: () => { Pull(calculator.In); });

            SetHandler(calculator.In, onPush: () =>
            {
                _bytes += Grab(calculator.In);
                Pull(calculator.In);
            }, onUpstreamFinish: () =>
            {
                Emit(calculator.Out, ByteString.Create(_digest.ComputeHash(_bytes.ToArray())));
                CompleteStage();
            });
        }
    }

    public DigestCalculator(string algorithm)
    {
        _algorithm = algorithm;
        Shape = new FlowShape<ByteString, ByteString>(In, Out);
    }

    public Inlet<ByteString> In { get; } = new Inlet<ByteString>("DigestCalculator.in");

    public Outlet<ByteString> Out { get; } = new Outlet<ByteString>("DigestCalculator.out");

    public override FlowShape<ByteString, ByteString> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}
  
var data = Source.Empty<ByteString>();
var digest = data.Via(new DigestCalculator("SHA-256"));
```

#### Parsing lines from a stream of ByteStrings

**Situation:** A stream of bytes is given as a stream of ``ByteStrings`` containing lines terminated by line ending
characters (or, alternatively, containing binary frames delimited by a special delimiter byte sequence) which
needs to be parsed.

The `Framing` helper object contains a convenience method to parse messages from a stream of ``ByteStrings``:

```csharp
var rawData = Source.Empty<ByteString>();
var linesStream = rawData
    .Via(Framing.Delimiter(delimiter: ByteString.FromString("\r\n"), maximumFrameLength: 10, allowTruncation: true))
    .Select(b => b.DecodeString());
```

#### Implementing reduce-by-key


**Situation:** Given a stream of elements, we want to calculate some aggregated value on different subgroups of the
elements.

The "hello world" of reduce-by-key style operations is *wordcount* which we demonstrate below. Given a stream of words
we first create a new stream that groups the words according to the ``identity`` function, i.e. now
we have a stream of streams, where every substream will serve identical words.

To count the words, we need to process the stream of streams (the actual groups
containing identical words). ``GroupBy`` returns a `SubFlow`, which
means that we transform the resulting substreams directly. In this case we use
the ``Reduce`` combinator to aggregate the word itself and the number of its
occurrences within a tuple `(String, Integer)`. Each substream will then
emit one final value—precisely such a pair—when the overall input completes. As
a last step we merge back these values from the substreams into one single
output stream.

One noteworthy detail pertains to the ``MaximumDistinctWords`` parameter: this
defines the breadth of the groupBy and merge operations. Akka Streams is
focused on bounded resource consumption and the number of concurrently open
inputs to the merge operator describes the amount of resources needed by the
merge itself.  Therefore only a finite number of substreams can be active at
any given time. If the ``GroupBy`` operator encounters more keys than this
number then the stream cannot continue without violating its resource bound, in
this case ``GroupBy`` will terminate with a failure.

```csharp
var words = Source.Empty<string>();
var counts = words
    // split the words into separate streams first
    .GroupBy(MaximumDistinctWords, x => x)
    //transform each element to pair with number of words in it
    .Select(x => Tuple.Create(x, 1))
    // add counting logic to the streams
    .Sum((l, r) => Tuple.Create(l.Item1, l.Item2 + r.Item2))
    // get a stream of word counts
    .MergeSubstreams();
```

By extracting the parts specific to *wordcount* into

* a ``GroupKey`` function that defines the groups
* a ``Select`` map each element to value that is used by the reduce on the substream
* a ``Reduce`` function that does the actual reduction

we get a generalized version below:

```csharp
public Flow<TIn, Tuple<TKey, TOut>, NotUsed> ReduceByKey<TIn, TKey, TOut>(int maximumGroupSize, 
    Func<TIn, TKey> groupKey, 
    Func<TIn, TOut> map,
    Func<TOut, TOut, TOut> reduce)
{
    return (Flow<TIn, Tuple<TKey, TOut>, NotUsed>)
        Flow.Create<TIn>()
            .GroupBy(maximumGroupSize, groupKey)
            .Select(e => Tuple.Create(groupKey(e), map(e)))
            .Sum((l, r) => Tuple.Create(l.Item1, reduce(l.Item2, r.Item2)))
            .MergeSubstreams();
}

var counts = words.Via(ReduceByKey(MaximumDistinctWords,
    groupKey: (string word) => word,
    map: word => 1,
    reduce: (l, r) => l + r));
```

> [!NOTE]
> Please note that the reduce-by-key version we discussed above is sequential in reading the overall input stream, 
in other words it is **NOT** a parallelization pattern like MapReduce and similar frameworks.

#### Sorting elements to multiple groups with groupBy

**Situation:** The ``GroupBy`` operation strictly partitions incoming elements, each element belongs to exactly one group.
Sometimes we want to map elements into multiple groups simultaneously.

To achieve the desired result, we attack the problem in two steps:

* first, using a function ``TopicMapper`` that gives a list of topics (groups) a message belongs to, we transform our
  stream of ``Message`` to a stream of ``(Message, Topic)`` where for each topic the message belongs to a separate pair
  will be emitted. This is achieved by using ``SelectMany``
* Then we take this new stream of message topic pairs (containing a separate pair for each topic a given message
  belongs to) and feed it into GroupBy, using the topic as the group key.
  
```csharp
Func<Message, ImmutableHashSet<Topic>> topicMapper = ExtractTopics;
var elements = Source.Empty<Message>();
var messageAndTopic = elements.SelectMany(msg =>
{
    var topicsForMessage = topicMapper(msg);
    // Create a (Msg, Topic) pair for each of the topics
    // the message belongs to
    return topicsForMessage.Select(t => Tuple.Create(msg, t));
});

var multiGroups = messageAndTopic.GroupBy(2, tuple => tuple.Item2).Select(tuple =>
{
    var msg = tuple.Item1;
    var topic = tuple.Item2;

    // do what needs to be done
});
```

# Working with Graphs

In this collection we show recipes that use stream graph elements to achieve various goals.

#### Triggering the flow of elements programmatically

**Situation:** Given a stream of elements we want to control the emission of those elements according to a trigger signal.
In other words, even if the stream would be able to flow (not being backpressured) we want to hold back elements until a
trigger signal arrives.

This recipe solves the problem by simply zipping the stream of ``Message`` elements with the stream of ``Trigger``
signals. Since ``Zip`` produces pairs, we simply map the output stream selecting the first element of the pair.

```csharp
var elements = Source.Empty<Message>();
var triggerSource = Source.Empty<Trigger>();
var sink = Sink.Ignore<Message>().MapMaterializedValue(_ => NotUsed.Instance);

var graph = RunnableGraph.FromGraph(GraphDsl.Create(b =>
{
    var zip = b.Add(new Zip<Message, Trigger>());

    b.From(elements).To(zip.In0);
    b.From(triggerSource).To(zip.In1);
    b.From(zip.Out).Via(Flow.Create<Tuple<Message, Trigger>>().Select(t => t.Item1)).To(sink);

    return ClosedShape.Instance;
}));
```

Alternatively, instead of using a ``Zip``, and then using ``Select`` to get the first element of the pairs, we can avoid
creating the pairs in the first place by using ``ZipWith`` which takes a two argument function to produce the output
element. If this function would return a pair of the two argument it would be exactly the behavior of ``Zip`` so
``ZipWith`` is a generalization of zipping.

```csharp
var graph = RunnableGraph.FromGraph(GraphDsl.Create(b =>
{
    var zip = b.Add(ZipWith.Apply((Message msg, Trigger trigger) => msg));

    b.From(elements).To(zip.In0);
    b.From(triggerSource).To(zip.In1);
    b.From(zip.Out).To(sink);

    return ClosedShape.Instance;
}));
```

#### Balancing jobs to a fixed pool of workers

**Situation:** Given a stream of jobs and a worker process expressed as a `Flow` create a pool of workers
that automatically balances incoming jobs to available workers, then merges the results.

We will express our solution as a function that takes a worker flow and the number of workers to be allocated and gives
a flow that internally contains a pool of these workers. To achieve the desired result we will create a `Flow`
from a graph.

The graph consists of a ``Balance`` node which is a special fan-out operation that tries to route elements to available
downstream consumers. In a ``for`` loop we wire all of our desired workers as outputs of this balancer element, then
we wire the outputs of these workers to a ``Merge`` element that will collect the results from the workers.

To make the worker stages run in parallel we mark them as asynchronous with `Async`.

```csharp
public Flow<TIn, TOut, NotUsed> Balancer<TIn, TOut>(Flow<TIn, TOut, NotUsed> worker, int workerCount)
{
    return Flow.FromGraph(GraphDsl.Create(b =>
    {
        var balancer = b.Add(new Balance<TIn>(workerCount, waitForAllDownstreams: true));
        var merge = b.Add(new Merge<TOut>(workerCount));

        for (var i = 0; i < workerCount; i++)
            b.From(balancer).Via(worker.Async()).To(merge);

        return new FlowShape<TIn, TOut>(balancer.In, merge.Out);
    }));
}

var myJobs = Source.Empty<Job>();
var worker = Flow.Create<Job>().Select(j => new Done(j));
var processedJobs = myJobs.Via(Balancer(worker, 3));
```

# Working with rate

This collection of recipes demonstrate various patterns where rate differences between upstream and downstream
needs to be handled by other strategies than simple backpressure.

#### Dropping elements

**Situation:** Given a fast producer and a slow consumer, we want to drop elements if necessary to not slow down
the producer too much.

This can be solved by using a versatile rate-transforming operation, ``Conflate``. Conflate can be thought as
a special ``Sum`` operation that collapses multiple upstream elements into one aggregate element if needed to keep
the speed of the upstream unaffected by the downstream.

When the upstream is faster, the sum process of the ``Conflate`` starts. Our reducer function simply takes
the freshest element. This is shown as a simple dropping operation.

```csharp
var droppyStream = Flow.Create<Message>().Conflate((lastMessage, newMessage) => newMessage);
```

There is a more general version of ``Conflate`` named ``ConflateWithSeed`` that allows to express more complex aggregations, more
similar to a ``Aggregate``.

#### Dropping broadcast

**Situation:** The default ``Broadcast`` graph element is properly backpressured, but that means that a slow downstream
consumer can hold back the other downstream consumers resulting in lowered throughput. In other words the rate of
``Broadcast`` is the rate of its slowest downstream consumer. In certain cases it is desirable to allow faster consumers
to progress independently of their slower siblings by dropping elements if necessary.

One solution to this problem is to append a ``Buffer`` element in front of all of the downstream consumers
defining a dropping strategy instead of the default ``Backpressure``. This allows small temporary rate differences
between the different consumers (the buffer smooths out small rate variances), but also allows faster consumers to
progress by dropping from the buffer of the slow consumers if necessary.

```csharp
var mysink1 = Sink.Ignore<int>();
var mysink2 = Sink.Ignore<int>();
var mysink3 = Sink.Ignore<int>();

var graph = RunnableGraph.FromGraph(GraphDsl.Create(mysink1, mysink2, mysink3, Tuple.Create,
    (builder, sink1, sink2, sink3) =>
    {
        var broadcast = builder.Add(new Broadcast<int>(3));

        builder.From(broadcast).Via(Flow.Create<int>().Buffer(10, OverflowStrategy.DropHead)).To(sink1);
        builder.From(broadcast).Via(Flow.Create<int>().Buffer(10, OverflowStrategy.DropHead)).To(sink2);
        builder.From(broadcast).Via(Flow.Create<int>().Buffer(10, OverflowStrategy.DropHead)).To(sink3);

        return ClosedShape.Instance;
    }));
```

#### Collecting missed ticks

**Situation:** Given a regular (stream) source of ticks, instead of trying to backpressure the producer of the ticks
we want to keep a counter of the missed ticks instead and pass it down when possible.

We will use ``ConflateWithSeed`` to solve the problem. The seed version of conflate takes two functions:

* A seed function that produces the zero element for the folding process that happens when the upstream is faster than
  the downstream. In our case the seed function is a constant function that returns 0 since there were no missed ticks
  at that point.
* A fold function that is invoked when multiple upstream messages needs to be collapsed to an aggregate value due
  to the insufficient processing rate of the downstream. Our folding function simply increments the currently stored
  count of the missed ticks so far.

As a result, we have a flow of ``Int`` where the number represents the missed ticks. A number 0 means that we were
able to consume the tick fast enough (i.e. zero means: 1 non-missed tick + 0 missed ticks)

```csharp
var missed = Flow.Create<Tick>()
  .ConflateWithSeed(seed: _ => 0, aggregate: (missedTicks, tick) => missedTicks + 1);
```

#### Create a stream processor that repeats the last element seen

**Situation:** Given a producer and consumer, where the rate of neither is known in advance, we want to ensure that none
of them is slowing down the other by dropping earlier unconsumed elements from the upstream if necessary, and repeating
the last value for the downstream if necessary.

We have two options to implement this feature. In both cases we will use `GraphStage` to build our custom
element. In the first version we will use a provided initial value ``initial`` that will be used
to feed the downstream if no upstream element is ready yet. In the ``onPush()`` handler we just overwrite the
``currentValue`` variable and immediately relieve the upstream by calling ``pull()``. The downstream ``onPull`` handler
is very similar, we immediately relieve the downstream by emitting ``currentValue``.

```csharp
public sealed class HoldWithInitial<T> : GraphStage<FlowShape<T, T>>
{
    private sealed class Logic : GraphStageLogic
    {
        private readonly HoldWithInitial<T> _holder;
        private T _currentValue;
  
        public Logic(HoldWithInitial<T> holder) : base(holder.Shape)
        {
            _holder = holder;
            _currentValue = holder._initial;
  
            SetHandler(holder.In, onPush: () =>
            {
                _currentValue = Grab(holder.In);
                Pull(holder.In);
            });
  
            SetHandler(holder.Out, onPull: () => Push(holder.Out, _currentValue));
        }
  
        public override void PreStart() => Pull(_holder.In);
    }
  
    private readonly T _initial;
  
    public HoldWithInitial(T initial)
    {
        _initial = initial;
        Shape = new FlowShape<T, T>(In, Out);
    }
  
    public Inlet<T> In { get; } = new Inlet<T>("HoldWithInitial.in");
  
    public Outlet<T> Out { get; } = new Outlet<T>("HoldWithInitial.out");
  
    public override FlowShape<T, T> Shape { get; }
  
    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}
```

While it is relatively simple, the drawback of the first version is that it needs an arbitrary initial element which is not
always possible to provide. Hence, we create a second version where the downstream might need to wait in one single
case: if the very first element is not yet available.

We introduce a boolean variable ``waitingFirstValue`` to denote whether the first element has been provided or not
(alternatively an `Option` can be used for ``currentValue`` or if the element type is a value type
a null can be used with the same purpose). In the downstream ``onPull()`` handler the difference from the previous
version is that we check if we have received the first value and only emit if we have. This leads to that when the
first element comes in we must check if there possibly already was demand from downstream so that we in that case can
push the element directly.

```csharp
public sealed class HoldWithWait<T> : GraphStage<FlowShape<T, T>>
{
    private sealed class Logic : GraphStageLogic
    {
        private readonly HoldWithWait<T> _holder;
        private T _currentValue;
        private bool _waitingFirstValue = true;

        public Logic(HoldWithWait<T> holder) : base(holder.Shape)
        {
            _holder = holder;

            SetHandler(holder.In, onPush: () =>
            {
                _currentValue = Grab(holder.In);
                if (_waitingFirstValue)
                {
                    _waitingFirstValue = false;
                    if(IsAvailable(holder.Out))
                        Push(holder.Out, _currentValue);
                }
                Pull(holder.In);
            });

            SetHandler(holder.Out, onPull: () =>
            {
                if(!_waitingFirstValue)
                    Push(holder.Out, _currentValue);
            });
        }

        public override void PreStart() => Pull(_holder.In);
    }
    
    public HoldWithWait()
    {
        Shape = new FlowShape<T, T>(In, Out);
    }

    public Inlet<T> In { get; } = new Inlet<T>("HoldWithWait.in");

    public Outlet<T> Out { get; } = new Outlet<T>("HoldWithWait.out");

    public override FlowShape<T, T> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}
```

#### Globally limiting the rate of a set of streams

**Situation:** Given a set of independent streams that we cannot merge, we want to globally limit the aggregate
throughput of the set of streams.

One possible solution uses a shared actor as the global limiter combined with `SelectAsync` to create a reusable `Flow` that can be plugged into a stream to limit its rate.

As the first step we define an actor that will do the accounting for the global rate limit. The actor maintains
a timer, a counter for pending permit tokens and a queue for possibly waiting participants. The actor has
an ``open`` and ``closed`` state. The actor is in the ``open`` state while it has still pending permits. Whenever a
request for permit arrives as a ``WantToPass`` message to the actor the number of available permits is decremented
and we notify the sender that it can pass by answering with a ``MayPass`` message. If the amount of permits reaches
zero, the actor transitions to the ``closed`` state. In this state requests are not immediately answered, instead the reference
of the sender is added to a queue. Once the timer for replenishing the pending permits fires by sending a ``ReplenishTokens``
message, we increment the pending permits counter and send a reply to each of the waiting senders. If there are more
waiting senders than permits available we will stay in the ``closed`` state.

```csharp
public sealed class WantToPass
{
    public static readonly WantToPass Instance = new WantToPass();

    private WantToPass() { }
}

public sealed class MayPass
{
    public static readonly MayPass Instance = new MayPass();

    private MayPass() { }
}

public sealed class ReplenishTokens
{
    public static readonly ReplenishTokens Instance = new ReplenishTokens();

    private ReplenishTokens() { }
}


public class Limiter : ReceiveActor
{
    public static Props Props(int maxAvailableTokens, TimeSpan tokenRefreshPeriod, int tokenRefreshAmount)
        => Akka.Actor.Props.Create(() => new Limiter(maxAvailableTokens, tokenRefreshPeriod, tokenRefreshAmount));


    private readonly int _maxAvailableTokens;
    private readonly int _tokenRefreshAmount;
    private ImmutableList<IActorRef> _waitQueue;
    private int _permitTokens;
    private readonly ICancelable _replenishTimer;

    public Limiter(int maxAvailableTokens, TimeSpan tokenRefreshPeriod, int tokenRefreshAmount)
    {
        _maxAvailableTokens = maxAvailableTokens;
        _tokenRefreshAmount = tokenRefreshAmount;

        _waitQueue = ImmutableList.Create<IActorRef>();
        _permitTokens = maxAvailableTokens;
        _replenishTimer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(initialDelay: tokenRefreshPeriod,
            interval: tokenRefreshPeriod, receiver: Self, message: ReplenishTokens.Instance, sender: Nobody.Instance);

        Become(Open);
    }

    private void Open(object message)
    {
        message.Match()
            .With<ReplenishTokens>(() =>
            {
                _permitTokens = Math.Min(_permitTokens + _tokenRefreshAmount, _maxAvailableTokens);
            })
            .With<WantToPass>(() =>
            {
                _permitTokens--;
                Sender.Tell(MayPass.Instance);
                if(_permitTokens == 0)
                    Become(Closed);
            });
    }

    private void Closed(object message)
    {
        message.Match()
            .With<ReplenishTokens>(() =>
            {
                _permitTokens = Math.Min(_permitTokens + _tokenRefreshAmount, _maxAvailableTokens);
                ReleaseWaiting();
            })
            .With<WantToPass>(() =>
            {
                _waitQueue = _waitQueue.Add(Sender);
            });
    }

    private void ReleaseWaiting()
    {
        var toBeReleased = _waitQueue.GetRange(0, _permitTokens);
        _waitQueue = _waitQueue.RemoveRange(0, _permitTokens);
        _permitTokens -= toBeReleased.Count;
        toBeReleased.ForEach(s => s.Tell(MayPass.Instance));
        if(_permitTokens > 0)
            Become(Open);
    }

    protected override void PostStop()
    {
        _replenishTimer.Cancel();
        _waitQueue.ForEach(s => s.Tell(new Status.Failure(new IllegalStateException("Limiter stopped"))));
    }
}
```

To create a Flow that uses this global limiter actor we use the ``SelectAsync`` function with the combination of the ``Ask``
pattern. We also define a timeout, so if a reply is not received during the configured maximum wait period the returned
task from ``Ask`` will fail, which will fail the corresponding stream as well.

```csharp
public Flow<T, T, NotUsed> LimitGlobal<T>(IActorRef limiter, TimeSpan maxAllowedWait)
  => Flow.Create<T>().SelectAsync(4, element =>
  {
      var limiterTriggerTask = limiter.Ask<T>(WantToPass.Instance, maxAllowedWait);
      return limiterTriggerTask.ContinueWith(t => element);
  });
```

> [!NOTE]
> The global actor used for limiting introduces a global bottleneck. You might want to assign a dedicated dispatcher for this actor.

# Working with IO

#### Chunking up a stream of ByteStrings into limited size ByteStrings

**Situation:** Given a stream of ByteStrings we want to produce a stream of ByteStrings containing the same bytes in
the same sequence, but capping the size of ByteStrings. In other words we want to slice up ByteStrings into smaller
chunks if they exceed a size threshold.

This can be achieved with a single `GraphStage`. The main logic of our stage is in ``EmitChunk()`` which implements the following logic:

* if the buffer is empty, and upstream is not closed we pull for more bytes, if it is closed we complete
* if the buffer is nonEmpty, we split it according to the ``ChunkSize``. This will give a next chunk that we will emit,
  and an empty or non-empty remaining buffer.

Both `OnPush()` and `OnPull()` calls `EmitChunk()` the only difference is that the push handler also stores
the incoming chunk by appending to the end of the buffer.

```csharp
public class Chunker : GraphStage<FlowShape<ByteString, ByteString>>
{
    private sealed class Logic : GraphStageLogic
    {
        private readonly Chunker _chunker;
        private ByteString _buffer = ByteString.Empty;

        public Logic(Chunker chunker) : base(chunker.Shape)
        {
            _chunker = chunker;

            SetHandler(chunker.Out, onPull: () =>
            {
                if (IsClosed(chunker.In))
                    EmitChunk();
                else
                    Pull(chunker.In);
            });

            SetHandler(chunker.In, onPush: () =>
            {
                var element = Grab(chunker.In);
                _buffer += element;
                EmitChunk();
            }, onUpstreamFinish: () =>
            {
                if(_buffer.IsEmpty)
                    CompleteStage();
                // elements left in buffer, keep accepting downstream pulls
                // and push from buffer until buffer is emitted
            });
        }

        private void EmitChunk()
        {
            if (_buffer.IsEmpty)
            {
                if (IsClosed(_chunker.In))
                    CompleteStage();
                else
                    Pull(_chunker.In);
            }
            else
            {
                var t = _buffer.SplitAt(_chunker._chunkSize);
                var chunk = t.Item1;
                var nextBuffer = t.Item2;

                _buffer = nextBuffer;
                Push(_chunker.Out, chunk);
            }
        }
    }

    private readonly int _chunkSize;

    public Chunker(int chunkSize)
    {
        _chunkSize = chunkSize;
        Shape = new FlowShape<ByteString, ByteString>(In, Out);
    }

    public Inlet<ByteString> In { get; }= new Inlet<ByteString>("Chunker.in");

    public Outlet<ByteString> Out { get; } = new Outlet<ByteString>("Chunker.out");

    public override FlowShape<ByteString, ByteString> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}

var rawBytes = Source.Empty<ByteString>();
var chunkStream = rawBytes.Via(new Chunker(ChunkLimit));
```

#### Limit the number of bytes passing through a stream of ByteStrings

**Situation:** Given a stream of ByteStrings we want to fail the stream if more than a given maximum of bytes has been
consumed.

This recipe uses a `GraphStage` to implement the desired feature. In the only handler we override,
``onPush()`` we just update a counter and see if it gets larger than ``maximumBytes``. If a violation happens
we signal failure, otherwise we forward the chunk we have received.

```csharp
public class ByteLimiter : GraphStage<FlowShape<ByteString, ByteString>>
{
    private sealed class Logic : GraphStageLogic
    {
        private long _count;

        public Logic(ByteLimiter limiter) : base(limiter.Shape)
        {
            SetHandler(limiter.In, onPush: () =>
            {
                var chunk = Grab(limiter.In);
                _count += chunk.Count;
                if (_count > limiter._maximumBytes)
                    FailStage(new IllegalStateException("Too much bytes"));
                else
                    Push(limiter.Out, chunk);
            });

            SetHandler(limiter.Out, onPull: () => Pull(limiter.In));
        }
    }

    private readonly long _maximumBytes;

    public ByteLimiter(long maximumBytes)
    {
        _maximumBytes = maximumBytes;
        Shape = new FlowShape<ByteString, ByteString>(In, Out);
    }

    public Inlet<ByteString> In { get; } = new Inlet<ByteString>("ByteLimiter.in");

    public Outlet<ByteString> Out { get; } = new Outlet<ByteString>("ByteLimiter.out");

    public override FlowShape<ByteString, ByteString> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}

var limiter = Flow.Create<ByteString>().Via(new ByteLimiter(SizeLimit));
```

#### Compact ByteStrings in a stream of ByteStrings

**Situation:** After a long stream of transformations, due to their immutable, structural sharing nature ByteStrings may
refer to multiple original ByteString instances unnecessarily retaining memory. As the final step of a transformation
chain we want to have clean copies that are no longer referencing the original ByteStrings.

The recipe is a simple use of Select, calling the ``Compact()`` method of the `ByteString` elements. This does
copying of the underlying arrays, so this should be the last element of a long chain if used.

```csharp
var data = Source.Empty<ByteString>();
var compacted = data.Select(b => b.Compact());
```

#### Injecting keep-alive messages into a stream of ByteStrings

**Situation:** Given a communication channel expressed as a stream of ByteStrings we want to inject keep-alive messages
but only if this does not interfere with normal traffic.

There is a built-in operation that allows to do this directly:

```csharp
var injectKeepAlive = Flow.Create<ByteString>().KeepAlive(TimeSpan.FromSeconds(1), () => keepAliveMessage);
```
