---
uid: streams-integration
title: Integration
---

# Integration

## Integrating with Actors
For piping the elements of a stream as messages to an ordinary actor you can use ``Ask`` in a 
``SelectAsync`` or use ``Sink.ActorRefWithAck``.

Messages can be sent to a stream with ``Source.Queue`` or via the ``IActorRef`` that is 
materialized by ``Source.ActorRef``.

### SelectAsync + Ask
A nice way to delegate some processing of elements in a stream to an actor is to use ``Ask`` 
in ``SelectAsync``. The back-pressure of the stream is maintained by the ``Task`` of the ``Ask``
and the mailbox of the actor will not be filled with more messages than the given 
``parallelism`` of the ``SelectAsync`` stage.

```csharp
var words = Source.From(new [] { "hello", "hi" });
words
	.SelectAsync(5, elem => _actorRef.Ask(elem, TimeSpan.FromSeconds(5)))
	.Select(elem => (string)elem)
	.Select(elem => elem.ToLower())
	.RunWith(Sink.Ignore<string>(), _actorMaterializer);
```

Note that the messages received in the actor will be in the same order as the stream elements, 
i.e. the `parallelism` does not change the ordering of the messages. There is a performance 
advantage of using parallelism > 1 even though the actor will only process one message at a time 
because then there is already a message in the mailbox when the actor has completed previous message. 

The actor must reply to the `Sender` for each message from the stream. That reply will complete 
the `CompletionStage` of the `Ask` and it will be the element that is emitted downstreams 
from `SelectAsync`.

```csharp
public class Translator : ReceiveActor
{
	public Translator()
	{
		Receive<string>(word => {
			// ... process message
			string reply = word.ToUpper();
			// reply to the ask
			Sender.Tell(reply, Self);
		});
	}
}
```
The stream can be completed with failure by sending `Akka.Actor.Status.Failure` as reply from the actor.

If the `Ask` fails due to timeout the stream will be completed with `TimeoutException` failure. 
If that is not desired outcome you can use `Recover` on the `Ask` `CompletionStage`.

If you don't care about the replies you can use `Sink.Ignore` after the `SelectAsync` stage 
and then actor is effectively a sink of the stream.

The same pattern can be used with [Actor routers](xref:routers). Then you can use 
`SelectAsyncUnordered` for better efficiency if you don't care about the order of the emitted 
downstream elements (the replies).

### Sink.ActorRefWithAck
The sink sends the elements of the stream to the given `IActorRef` that sends back back-pressure signal.
First element is always `OnInitMessage`, then stream is waiting for the given acknowledgement message 
from the given actor which means that it is ready to process elements. 
It also requires the given acknowledgement message after each stream element to make back-pressure work.

If the target actor terminates the stream will be cancelled. When the stream is completed successfully 
the  given `OnCompleteMessage` will be sent to the destination actor. When the stream 
is completed with failure a `Akka.Actor.Status.Failure` message will be sent to the destination actor.

> [!NOTE]
>Using `Sink.ActorRef` or ordinary `Tell` from a `Select` or `ForEach` stage means that there is 
>no back-pressure signal from the destination actor, i.e. if the actor is not consuming the messages 
>fast enough the mailbox of the actor will grow, unless you use a bounded mailbox with zero 
>`mailbox-push-timeout-time` or use a rate limiting stage in front. 
>It's often better to use `Sink.ActorRefWithAck` or `Ask` in `SelectAsync`, though. 

### Source.Queue
`Source.Queue` can be used for emitting elements to a stream from an actor (or from anything running 
outside the stream). The elements will be buffered until the stream can process them. You can `Offer`
elements to  the queue and they will be emitted to the stream if there is demand from downstream, 
otherwise they will be buffered until request for demand is received.

Use overflow strategy `Akka.Streams.OverflowStrategy.Backpressure` to avoid dropping of elements 
if the  buffer is full.

`ISourceQueueWithComplete.OfferAsync` returns `Task<IQueueOfferResult>` 
which completes with `QueueOfferResult.Enqueued` if element was added to buffer or sent downstream. 
It completes with `QueueOfferResult.Dropped` if element was dropped. It can also complete with
`QueueOfferResult.Failure` when stream failed or `QueueOfferResult.QueueClosed` 
when downstream is completed.

When used from an actor you typically `pipe` the result of the `Task` back to the actor 
to continue processing.

### Source.ActorRef
Messages sent to the actor that is materialized by ``Source.ActorRef`` will be emitted to the
stream if there is demand from downstream, otherwise they will be buffered until request for
demand is received.

Depending on the defined `OverflowStrategy` it might drop elements if there is no space
available in the buffer. The strategy ``OverflowStrategy.Backpressure`` is not supported
for this Source type, i.e. elements will be dropped if the buffer is filled by sending 
at a rate that is faster than the stream can consume. You should consider using ``Source.Queue`` 
if you want a backpressured actor interface.

The stream can be completed successfully by sending ``Akka.Actor.PoisonPill`` or
``Akka.Actor.Status.Success`` to the actor reference.

The stream can be completed with failure by sending ``Akka.Actor.Status.Failure`` to the
actor reference.

The actor will be stopped when the stream is completed, failed or cancelled from downstream,
i.e. you can watch it to get notified when that happens.

## Integrating with External Services
Stream transformations and side effects involving external non-stream based services can be
performed with ``SelectAsync`` or ``SelectAsyncUnordered``.

For example, sending emails to the authors of selected tweets using an external
email service:

```csharp
Task<int> Send(Email mail)
```

We start with the tweet stream of authors:

```csharp
var authors = tweets
    .Where(t => t.HashTags.Contains("Akka.Net"))
    .Select(t => t.Author);
```

Assume that we can lookup their email address using:

```csharp
Task<string> LookupEmail(string handle)
```

Transforming the stream of authors to a stream of email addresses by using the ``LookupEmail``
service can be done with ``SelectAsync``:

```csharp
var emailAddresses = authors
    .SelectAsync(4, author => AddressSystem.LookupEmail(author.Handle))
    .Collect(s => string.IsNullOrWhiteSpace(s) ? null : s);
```

Finally, sending the emails:

```csharp
var sendEmails = emailAddresses.SelectAsync(4, address =>
    EmailServer.Send(
        new Email(to: address, title: "Akka.Net", body: "I like your tweet"))
    )
    .To(Sink.Ignore<int>());

sendEmails.Run(materializer);
```

``SelectAsync`` is applying the given function that is calling out to the external service to
each of the elements as they pass through this processing step. The function returns a `Task`
and the value of that task will be emitted downstreams. The number of Tasks
that shall run in parallel is given as the first argument to ``SelectAsync``.
These Tasks may complete in any order, but the elements that are emitted
downstream are in the same order as received from upstream.

That means that back-pressure works as expected. For example if the ``EmailServer.Send``
is the bottleneck it will limit the rate at which incoming tweets are retrieved and
email addresses looked up.

The final piece of this pipeline is to generate the demand that pulls the tweet
authors information through the emailing pipeline: we attach a ``Sink.Ignore``
which makes it all run. If our email process would return some interesting data
for further transformation then we would of course not ignore it but send that
result stream onwards for further processing or storage.

Note that ``SelectAsync`` preserves the order of the stream elements. In this example the order
is not important and then we can use the more efficient ``SelectAsyncUnordered``:

```csharp
var authors = tweets
    .Where(t => t.HashTags.Contains("Akka.Net"))
    .Select(t => t.Author);
    
var emailAddresses = authors
    .SelectAsyncUnordered(4, author => AddressSystem.LookupEmail(author.Handle))
    .Collect(s => string.IsNullOrWhiteSpace(s) ? null : s);
    
var sendEmails = emailAddresses.SelectAsyncUnordered(4, address =>
    EmailServer.Send(
        new Email(to: address, title: "Akka.Net", body: "I like your tweet"))
    )
    .To(Sink.Ignore<int>());

sendEmails.Run(materializer);
```

In the above example the services conveniently returned a `Task` of the result.
If that is not the case you need to wrap the call in a `Task`. 


For a service that is exposed as an actor, or if an actor is used as a gateway in front of an
external service, you can use ``Ask``:

```csharp
var akkaTweets = tweets.Where(t => t.HashTags.Contains("Akka.Net"));

var saveTweets = akkaTweets
    .SelectAsync(4, tweet => database.Ask<DbResult>(new Save(tweet), TimeSpan.FromSeconds(3)))
    .To(Sink.Ignore<DbResult>());
```

Note that if the ``Ask`` is not completed within the given timeout the stream is completed with failure.
If that is not desired outcome you can use ``Recover`` on the ``Ask`` `Task`.


### Illustrating ordering and parallelism
Let us look at another example to get a better understanding of the ordering
and parallelism characteristics of ``SelectAsync`` and ``SelectAsyncUnordered``.

Several ``SelectAsync`` and ``SelectAsyncUnordered`` tasks may run concurrently.
The number of concurrent tasks are limited by the downstream demand.
For example, if 5 elements have been requested by downstream there will be at most 5
tasks in progress.

``SelectAsync`` emits the task results in the same order as the input elements
were received. That means that completed results are only emitted downstream
when earlier results have been completed and emitted. One slow call will thereby
delay the results of all successive calls, even though they are completed before
the slow call.

``SelectAsyncUnordered`` emits the task results as soon as they are completed, i.e.
it is possible that the elements are not emitted downstream in the same order as
received from upstream. One slow call will thereby not delay the results of faster
successive calls as long as there is downstream demand of several elements.

Here is a fictive service that we can use to illustrate these aspects.

```csharp
public class SometimesSlowService
{
    private readonly AtomicCounter runningCount = new AtomicCounter();
  
    public Task<string> Convert(string s)
    {
        Console.WriteLine($"running {s} {runningCount.IncrementAndGet()}");
  
        return Task.Run(() =>
        {
            if(s != "" && char.IsLower(s[0]))
                Thread.Sleep(500);
            else
                Thread.Sleep(20);
            Console.WriteLine($"completed {s} {runningCount.GetAndDecrement()}");
  
            return s.ToUpper();
        });
    }
}
```

Elements starting with a lower case character are simulated to take longer time to process.

Here is how we can use it with ``SelectAsync``:


```csharp
var service = new SometimesSlowService();
var settings = ActorMaterializerSettings.Create(sys).WithInputBuffer(4, 4);
var materializer = sys.Materializer(settings);

Source.From(new[] {"a", "B", "C", "D", "e", "F", "g", "H", "i", "J"})
    .Select(x =>
    {
        Console.WriteLine($"before {x}");
        return x;
    })
    .SelectAsync(4, service.Convert)
    .RunForeach(x => Console.WriteLine($"after: {x}"), materializer);
```

The output may look like this:

```
before: a
before: B
before: C
before: D
running: a (1)
running: B (2)
before: e
running: C (3)
before: F
running: D (4)
before: g
before: H
completed: C (3)
completed: B (2)
completed: D (1)
completed: a (0)
after: A
after: B
running: e (1)
after: C
after: D
running: F (2)
before: i
before: J
running: g (3)
running: H (4)
completed: H (2)
completed: F (3)
completed: e (1)
completed: g (0)
after: E
after: F
running: i (1)
after: G
after: H
running: J (2)
completed: J (1)
completed: i (0)
after: I
after: J
```

Note that ``after`` lines are in the same order as the ``before`` lines even
though elements are ``completed`` in a different order. For example ``H``
is ``completed`` before ``g``, but still emitted afterwards.

The numbers in parenthesis illustrates how many calls that are in progress at
the same time. Here the downstream demand and thereby the number of concurrent
calls are limited by the buffer size (4) of the `ActorMaterializerSettings`.

Here is how we can use the same service with ``SelectAsyncUnordered``:

```csharp
var service = new SometimesSlowService(_output);
var settings = ActorMaterializerSettings.Create(sys).WithInputBuffer(4, 4);
var materializer = sys.Materializer(settings);

var result = Source.From(new[] {"a", "B", "C", "D", "e", "F", "g", "H", "i", "J"})
    .Select(x =>
    {
        Console.WriteLine($"before {x}");
        return x;
    })
    .SelectAsync(4, service.Convert)
    .RunForeach(x => Console.WriteLine($"after: {x}"), materializer);
``` 

The output may look like this:

```
before: a
before: B
before: C
before: D
running: a (1)
running: B (2)
before: e
running: C (3)
before: F
running: D (4)
before: g
before: H
completed: B (3)
completed: C (1)
completed: D (2)
after: B
after: D
running: e (2)
after: C
running: F (3)
before: i
before: J
completed: F (2)
after: F
running: g (3)
running: H (4)
completed: H (3)
after: H
completed: a (2)
after: A
running: i (3)
running: J (4)
completed: J (3)
after: J
completed: e (2)
after: E
completed: g (1)
after: G
completed: i (0)
after: I
```

Note that ``after`` lines are not in the same order as the ``before`` lines. For example ``H`` overtakes the slow ``G``.

The numbers in parenthesis illustrates how many calls that are in progress at
the same time. Here the downstream demand and thereby the number of concurrent
calls are limited by the buffer size (4) of the `ActorMaterializerSettings`.

### Integrating with Observables

Starting from version 1.3.2, Akka.Streams offers integration with observables - both as possible sources and sinks for incoming events. In order to expose Akka.Streams runnable graph as an observable, use `Sink.AsObservable<T>` method. Example:

```csharp
IObservable<int> observable = Source.From(new []{ 1, 2, 3 })
    .RunWith(Sink.AsObservable<int>(), materializer);
```

In order to use an observable as an input source to Akka graph, you may want to use `Source.FromObservable<T>` method. Example:

```csharp
await Source.FromObservable(observable, maxBufferCapacity: 128, overflowStrategy: OverflowStrategy.DropHead)
    .RunForEach(Console.WriteLine, materializer);
```

You may notice two extra parameters here. One of the advantages of Akka.Streams (and reactive streams in general) over Reactive Extensions is notion of backpressure - absent in Rx.NET. This puts a constraint of rate limiting the events incoming form upstream. If an observable will be producing events faster, than downstream is able to consume them, source stage will start to buffer them up to a provided `maxBufferCapacity` limit. Once that limit is reached, an overflow strategy will be applied. There are several different overflow strategies to choose from:

- `OverflowStrategy.DropHead` (default) will drop the oldest element. In this mode source works in circular buffer fashion.
- `OverflowStrategy.DropTail` will cause a current element to replace a one set previously in a buffer.
- `OverflowStrategy.DropNew` will cause current event to be dropped. This effectively will cause dropping any new incoming events until a buffer will get some free space.
- `OverflowStrategy.Fail` will cause a `BufferOverflowException` to be send as an error signal.
- `OverflowStrategy.DropBuffer` will cause a whole buffer to be cleared once it's limit has been reached.

Any other `OverflowStrategy` option is not supported by `Source.FromObservable` stage.

### Integrating with event handlers

C# events can also be used as a potential source of an Akka.NET stream. It's possible using `Source.FromEvent` methods. Example:

```csharp
Source.FromEvent<RoutedEventArgs>(
    addHandler: h => button.Click += h, 
    removeHandler: h => button.Click -= h,
    maxBufferCapacity: 128,
    overflowStrategy: OverflowStrategy.DropHead)
    .RunForEach(e => Console.WriteLine($"Captured click from {e.Source}"), materializer);

// using custom delegate adapter
Source.FromEvent<EventHandler<RoutedEventArgs>, RoutedEventArgs>(
    conversion: onNext => (sender, eventArgs) => onNext(eventArgs),
    addHandler: h => button.Click += h, 
    removeHandler: h => button.Click -= h)
    .RunForEach(e => Console.WriteLine($"Captured click from {e.Source}"), materializer);
```

Just like in case of `Source.FromObservable`, `Source.FromEvents` can take optional parameters used to configure buffering strategy applied for incoming events.


### Integrating with Reactive Streams
`Reactive Streams` defines a standard for asynchronous stream processing with non-blocking
back pressure. It makes it possible to plug together stream libraries that adhere to the standard.
Akka Streams is one such library.

- Reactive Streams: http://reactive-streams.org/


The two most important interfaces in Reactive Streams are the `IPublisher` and `ISubscriber`.

```csharp
Reactive.Streams.IPublisher
Reactive.Streams.ISubscriber
```

Let us assume that a library provides a publisher of tweets:

```csharp
IPublisher<Tweet> Tweets
```

and another library knows how to store author handles in a database:

```csharp
ISubscriber<Author> Storage
```

Using an Akka Streams Flow we can transform the stream and connect those:

```csharp
var authors = Flow.Create<Tweet>()
    .Where(t => t.HashTags.Contains("Akka.net"))
    .Select(t => t.Author);

Source.FromPublisher(tweets)
    .Via(authors)
    .To(Sink.FromSubscriber(storage))
    .Run(materializer);
```

The `Publisher` is used as an input `Source` to the flow and the
`Subscriber` is used as an output `Sink`.

A `Flow` can also be also converted to a `RunnableGraph<IProcessor<In, Out>>` which
materializes to a `IProcessor` when ``Run()`` is called. ``Run()`` itself can be called multiple
times, resulting in a new `Processor` instance each time.

```csharp
var processor = authors.ToProcessor().Run(materializer);
tweets.Subscribe(processor);
processor.Subscribe(storage);
```

A publisher can be connected to a subscriber with the ``Subscribe`` method.

It is also possible to expose a `Source` as a `Publisher`
by using the Publisher-`Sink`:

```csharp
var authorPublisher = Source.FromPublisher(tweets)
    .Via(authors)
    .RunWith(Sink.AsPublisher<Author>(fanout: false), materializer);

authorPublisher.Subscribe(storage);
```

A publisher that is created with ``Sink.AsPublisher(fanout = false)`` supports only a single subscription.
Additional subscription attempts will be rejected with an `IllegalStateException`.

A publisher that supports multiple subscribers using fan-out/broadcasting is created as follows:

```csharp
ISubscriber<Author> Storage
ISubscriber<Author> Alert
```

```csharp
var authorPublisher = Source.FromPublisher(tweets)
    .Via(authors)
    .RunWith(Sink.AsPublisher<Author>(fanout: true), materializer);

authorPublisher.Subscribe(storage);
authorPublisher.Subscribe(alert);
```

The input buffer size of the stage controls how far apart the slowest subscriber can be from the fastest subscriber
before slowing down the stream.

To make the picture complete, it is also possible to expose a `Sink` as a `Subscriber`
by using the Subscriber-`Source`:

```csharp
var tweetSubscriber = authors.To(Sink.FromSubscriber(storage))
    .RunWith(Source.AsSubscriber<Tweet>(), materializer);

tweets.Subscribe(tweetSubscriber);
```

It is also possible to use re-wrap `Processor` instances as a `Flow` by
passing a factory function that will create the `Processor` instances:

```csharp
Func<IMaterializer, IProcessor<int, int>> createProcessor = 
    mat => Flow.Create<int>().ToProcessor().Run(mat);

var flow = Flow.FromProcessor(()=> createProcessor(materializer));
```

Please note that a factory is necessary to achieve reusability of the resulting `Flow`.

### Implementing Reactive Streams Publisher or Subscriber

As described above any Akka Streams ``Source`` can be exposed as a Reactive Streams ``Publisher`` 
and any ``Sink`` can be exposed as a Reactive Streams ``Subscriber``. Therefore we recommend that you 
implement Reactive Streams integrations with built-in stages or [custom stages](xref:custom-stream-processing).

For historical reasons the `ActorPublisher` and `ActorSubscriber`  are
provided to support implementing Reactive Streams `Publisher` class and `Subscriber` class with
an `Actor` class.

These can be consumed by other Reactive Stream libraries or used as an Akka Streams `Source` class or `Sink` class.

> [!WARNING]
> `ActorPublisher` class and `ActorSubscriber` class will probably be deprecated in 
> future versions of Akka.

> [!WARNING]
> `ActorPublisher` class and `ActorSubscriber` class cannot be used with remote actors,
> because if signals of the Reactive Streams protocol (e.g. ``Request``) are lost the
> the stream may deadlock.

### ActorPublisher
Extend `Akka.Streams.Actor.ActorPublisher` to implement a stream publisher that keeps track of the subscription life cycle and requested elements.

Here is an example of such an actor. It dispatches incoming jobs to the attached subscriber:

```csharp
public sealed class Job
{
    public Job(string payload)
    {
        Payload = payload;
    }

    public string Payload { get; }
}

public sealed class JobAccepted
{
    public static JobAccepted Instance { get; } = new JobAccepted();

    private JobAccepted() { }
}

public sealed class JobDenied
{
    public static JobDenied Instance { get; } = new JobDenied();

    private JobDenied() { }
}

public class JobManager : Actors.ActorPublisher<Job>
{
    public static Props Props { get; } = Props.Create<JobManager>();
    
    private List<Job> _buffer;
    private const int MaxBufferSize = 100;

    public JobManager()
    {
        _buffer = new List<Job>();
    }

    protected override bool Receive(object message)
    {
        return message.Match()
            .With<Job>(job =>
            {
                if (_buffer.Count == MaxBufferSize)
                    Sender.Tell(JobDenied.Instance);
                else
                {
                    Sender.Tell(JobAccepted.Instance);
                    if (_buffer.Count == 0 && TotalDemand > 0)
                        OnNext(job);
                    else
                    {
                        _buffer.Add(job);
                        DeliverBuffer();
                    }
                }
            })
            .With<Request>(DeliverBuffer)
            .With<Cancel>(() => Context.Stop(Self))
            .WasHandled;
    }

    private void DeliverBuffer()
    {
        if (TotalDemand > 0)
        {
            // totalDemand is a Long and could be larger than
            // what _buffer.Take and Skip can accept
            if (TotalDemand < int.MaxValue)
            {
                var use = _buffer.Take((int) TotalDemand).ToList();
                _buffer = _buffer.Skip((int) TotalDemand).ToList();
                use.ForEach(OnNext);
            }
            else
            {
                var use = _buffer.Take(int.MaxValue).ToList();
                _buffer = _buffer.Skip(int.MaxValue).ToList();
                use.ForEach(OnNext);
                DeliverBuffer();
            }
        }
    }
}
```

You send elements to the stream by calling ``OnNext``. You are allowed to send as many
elements as have been requested by the stream subscriber. This amount can be inquired with
``TotalDemand``. It is only allowed to use ``OnNext`` when ``IsActive`` and ``TotalDemand > 0``,
otherwise ``OnNext`` will throw ``IllegalStateException``.

When the stream subscriber requests more elements the ``ActorPublisherMessage.Request`` message
is delivered to this actor, and you can act on that event. The ``TotalDemand``
is updated automatically.

When the stream subscriber cancels the subscription the ``ActorPublisherMessage.Cancel`` message
is delivered to this actor. After that subsequent calls to ``OnNext`` will be ignored.

You can complete the stream by calling ``OnComplete``. After that you are not allowed to
call ``OnNext``, ``OnError`` and ``OnComplete``.

You can terminate the stream with failure by calling ``OnError``. After that you are not allowed to
call ``OnNext``, ``OnError`` and ``OnComplete``.

If you suspect that this ``ActorPublisher`` may never get subscribed to, you can set the ``SubscriptionTimeout``
property to provide a timeout after which this Publisher should be considered canceled. The actor will be notified when
the timeout triggers via an ``ActorPublisherMessage.SubscriptionTimeoutExceeded`` message and MUST then perform
cleanup and stop itself.

If the actor is stopped the stream will be completed, unless it was not already terminated with
failure, completed or canceled.

More detailed information can be found in the API documentation.

This is how it can be used as input `Source` to a `Flow`:

```csharp
var jobManagerSource = Source.ActorPublisher<Job>(JobManager.Props);
var actorRef = Flow.Create<Job>()
    .Select(job => job.Payload.ToUpper())
    .Select(elem =>
    {
        Console.WriteLine(elem);
        return elem;
    })
    .To(Sink.Ignore<string>())
    .RunWith(jobManagerSource, materializer);

actorRef.Tell(new Job("a"));
actorRef.Tell(new Job("b"));
actorRef.Tell(new Job("c"));
```

You can only attach one subscriber to this publisher. Use a ``Broadcast``-element or attach a ``Sink.AsPublisher(true)`` to enable multiple subscribers.

### ActorSubscriber
Extend `Akka.Streams.Actor.ActorSubscriber` to make your class a stream subscriber with full control of stream back pressure. It will receive `OnNext`, `OnComplete` and `OnError` messages from the stream. It can also receive other, non-stream messages, in the same way as any actor.

Here is an example of such an actor. It dispatches incoming jobs to child worker actors:

```csharp
public class Message
{
    public int Id { get; }

    public IActorRef ReplyTo { get; }

    public Message(int id, IActorRef replyTo)
    {
        Id = id;
        ReplyTo = replyTo;
    }
}

public class Work
{
    public Work(int id)
    {
        Id = id;
    }

    public int Id { get; }
}

public class Reply
{
    public Reply(int id)
    {
        Id = id;
    }

    public int Id { get; }
}

public class Done
{
    public Done(int id)
    {
        Id = id;
    }

    public int Id { get; }
}

public class WorkerPool : Actors.ActorSubscriber
{
    public static Props Props { get; } = Props.Create<WorkerPool>();
        
    private class Strategy : MaxInFlightRequestStrategy
    {
        private readonly Dictionary<int, IActorRef> _queue;

        public Strategy(int max, Dictionary<int, IActorRef> queue) : base(max)
        {
            _queue = queue;
        }

        public override int InFlight => _queue.Count;
    }

    private const int MaxQueueSize = 10;
    private readonly Dictionary<int, IActorRef> _queue;
    private readonly Router _router;

    public WorkerPool()
    {
        _queue = new Dictionary<int, IActorRef>();
        var routees = new Routee[]
        {
            new ActorRefRoutee(Context.ActorOf<Worker>()),
            new ActorRefRoutee(Context.ActorOf<Worker>()),
            new ActorRefRoutee(Context.ActorOf<Worker>())
        };
        _router = new Router(new RoundRobinRoutingLogic(), routees);
        RequestStrategy = new Strategy(MaxQueueSize, _queue);
    }

    public override IRequestStrategy RequestStrategy { get; }

    protected override bool Receive(object message)
    {
        return message.Match()
            .With<OnNext>(next =>
            {
                var msg = next.Element as Message;
                if (msg != null)
                {
                    _queue.Add(msg.Id, msg.ReplyTo);
                    if (_queue.Count > MaxQueueSize)
                        throw new IllegalStateException($"Queued too many : {_queue.Count}");
                    _router.Route(new Work(msg.Id), Self);
                }
            })
            .With<Reply>(reply =>
            {
                _queue[reply.Id].Tell(new Done(reply.Id));
                _queue.Remove(reply.Id);
            })
            .WasHandled;
    }
}

public class Worker : ReceiveActor
{
    public Worker()
    {
        Receive<Work>(work =>
        {
            //...
            Sender.Tell(new Reply(work.Id));
        });
    }
}
```

Subclass must define the ``RequestStrategy`` to control stream back pressure.
After each incoming message the ``ActorSubscriber`` will automatically invoke
the ``IRequestStrategy.RequestDemand`` and propagate the returned demand to the stream.

* The provided ``WatermarkRequestStrategy`` is a good strategy if the actor performs work itself.
* The provided ``MaxInFlightRequestStrategy`` is useful if messages are queued internally or
  delegated to other actors.
* You can also implement a custom ``IRequestStrategy`` or call ``Request`` manually together with
  ``ZeroRequestStrategy`` or some other strategy. In that case
  you must also call ``Request`` when the actor is started or when it is ready, otherwise
  it will not receive any elements.

More detailed information can be found in the API documentation.

This is how it can be used as output `Sink` to a `Flow`:

```csharp
var n = 118;
Source.From(Enumerable.Range(1, n))
    .Select(x => new Message(x, replyTo))
    .RunWith(Sink.ActorSubscriber<Message>(WorkerPool.Props), materializer);
```