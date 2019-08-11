---
uid: custom-stream-processing
title: Custom stream processing
---

# Custom stream processing
While the processing vocabulary of Akka Streams is quite rich (see the [Streams Cookbook](xref:streams-cookbook) for examples) it is sometimes necessary to define new transformation stages either because some functionality is missing from the stock operations, or for performance reasons. In this part we show how to build custom processing stages and graph junctions of various kinds.

> [!NOTE]
> A custom graph stage should not be the first tool you reach for, defining graphs using flows and the graph DSL is in general easier and does to a larger extent protect you from mistakes that might be easy to make with a custom `GraphStage`

## Custom processing with GraphStage
The `GraphStage` abstraction can be used to create arbitrary graph processing stages with any number of input or output ports. It is a counterpart of the `GraphDSL.Create()` method which creates new stream processing stages by composing others. Where `GraphStage` differs is that it creates a stage that is itself not divisible into smaller ones, and allows state to be maintained inside it in a safe way.

As a first motivating example, we will build a new `Source` that will simply emit numbers from 1 until it is cancelled. To start, we need to define the "interface" of our stage, which is called shape in Akka Streams terminology (this is explained in more detail in the section [Modularity, Composition and Hierarchy](xref:streams-modularity)). This is how this looks like:

```csharp
using Akka.Streams.Stage;

class NumbersSource : GraphStage<SourceShape<int>>
{
    //this is where the actual (possibly statefull) logic will live
    private sealed class Logic : GraphStageLogic
    {
        public Logic(NumbersSource source) : base(source.Shape)
        {
        }
    }

    // Define the (sole) output port of this stage 
    public Outlet<int> Out { get; } = new Outlet<int>("NumbersSource");

    // Define the shape of this tage, which is SourceShape with the port we defined above
    public override SourceShape<int> Shape => new SourceShape<int>(Out);

    //this is where the actual logic will be created
    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}
```

As you see, in itself the `GraphStage` only defines the ports of this stage and a shape that contains the ports. It also has, a currently unimplemented method called `CreateLogic`. If you recall, stages are reusable in multiple materializations, each resulting in a different executing entity. In the case of `GraphStage` the actual running logic is modelled as an instance of a `GraphStageLogic` which will be created by the materializer by calling the `CreateLogic` method. In other words, all we need to do is to create a suitable logic that will emit the numbers we want.

> [!NOTE]
> It is very important to keep the `GraphStage` object itself immutable and reusable. All mutable state needs to be confined to the `GraphStageLogic` that is created for every materialization.

In order to emit from a `Source` in a backpressured stream one needs first to have demand from downstream. To receive the necessary events one needs to register a callback with the output port (`Outlet`). This callback will receive events related to the lifecycle of the port. In our case we need to override `onPull` which indicates that we are free to emit a single element. There is another callback, `onDownstreamFinish` which is called if the downstream cancelled. Since the default behavior of that callback is to stop the stage, we don't need to override it. In the `onPull` callback we will simply emit the next number. This is how it looks like in the end:

```csharp
using Akka.Streams.Stage;

class NumbersSource : GraphStage<SourceShape<int>>
{
    //this is where the actual (possibly statefull) logic will live
    private sealed class Logic : GraphStageLogic
    {
        // All state MUST be inside the GraphStageLogic,
        // never inside the enclosing GraphStage.
        // This state is safe to access and modify from all the
        // callbacks that are provided by GraphStageLogic and the
        // registered handlers.
        private int _counter = 1;

        public Logic(NumbersSource source) : base(source.Shape)
        {
            SetHandler(source.Out, onPull: () => Push(source.Out, _counter++));
        }
    }

    // Define the (sole) output port of this stage 
    public Outlet<int> Out { get; } = new Outlet<int>("NumbersSource");

    // Define the shape of this stage, which is SourceShape with the port we defined above
    public override SourceShape<int> Shape => new SourceShape<int>(Out);

    //this is where the actual logic will be created
    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}
```

Instances of the above `GraphStage` are subclasses of `Graph<SourceShape<int>, NotUsed>` which means that they are already usable in many situations, but do not provide the DSL methods we usually have for other `Source`s. In order to convert this `Graph`to a proper `Source` we need to wrap it using `Source.FromGraph` (see [Modularity, Composition and Hierarchy](xref:streams-modularity) for more details about graphs and DSLs). Now we can use the source as any other built-in one:

```csharp
// A GraphStage is a proper Graph, just like what GraphDSL.Create would return
var sourceGraph = new NumbersSource();

// Create a Source from the Graph to access the DSL
var mySource = Source.FromGraph(sourceGraph);

// Returns 55
var result1Task = mySource.Take(10).RunAggregate(0, (sum, next) => sum + next, materializer);

// Returns 5050
var result2Task = mySource.Take(100).RunAggregate(0, (sum, next) => sum + next, materializer);
```

### Port states, InHandler and OutHandler

In order to interact with a port (`Inlet` or `Outlet`) of the stage we need to be able to receive events and generate new events belonging to the port. From the `GraphStageLogic` the following operations are available on an output port:

   * `Push(out,elem)` pushes an element to the output port. Only possible after the port has been pulled by downstream.
   * `Complete(out)` closes the output port normally.
   * `Fail(out,exception)` closes the port with a failure signal.

The events corresponding to an *output* port can be received in an `Action` registered to the output port using `SetHandler(out, action)`. This handler has two callbacks:

  * `onPull` is called when the output port is ready to emit the next element, `Push(out, elem)` is now allowed to be called on this port.
  * `onDownstreamFinish` is called once the downstream has cancelled and no longer allows messages to be pushed to it. No more `onPull` will arrive after this event. If not overridden this will default to stopping the stage.

Also, there are two query methods available for output ports:

 * `IsAvailable(out)` returns true if the port can be pushed
 * `IsClosed(out)` returns true if the port is closed. At this point the port can not be pushed and will not be pulled anymore.

The relationship of the above operations, events and queries are summarized in the state machine below. Green shows the initial state while orange indicates the end state. If an operation is not listed for a state, then it is invalid to call it while the port is in that state. If an event is not listed for a state, then that event cannot happen in that state.

![outport transitions1](/images/outport_transitions1.png)

The following operations are available for *input* ports:

 * `Pull(in)` requests a new element from an input port. This is only possible after the port has been pushed by upstream.
 * `Grab(in)` acquires the element that has been received during an `onPush` It cannot be called again until the port is pushed again by the upstream.
 * `Cancel(in)` closes the input port.

The events corresponding to an *input* port can be received in an `Action` registered to the input port using `setHandler(in, action)`. This handler has three callbacks:


* `onPush` is called when the output port has now a new element. Now it is possible to acquire this element using `Grab(in)` and/or call `Pull(in)` on the port to request the next element. It is not mandatory to grab the element, but if it is pulled while the element has not been grabbed it will drop the buffered element.
* `onUpstreamFinish` is called once the upstream has completed and no longer can be pulled for new elements. No more `onPush` will arrive after this event. If not overridden this will default to stopping the stage.
* `onUpstreamFailure` is called if the upstream failed with an exception and no longer can be pulled for new elements. No more `onPush` will arrive after this event. If not overridden this will default to failing the stage.

Also, there are three query methods available for input ports:

* `IsAvailable(in)` returns true if the port can be grabbed.
* `HasBeenPulled(in)` returns true if the port has been already pulled. Calling `Pull(in)` in this state is illegal.
* `IsClosed(in)` returns true if the port is closed. At this point the port can not be pulled and will not be pushed anymore.

The relationship of the above operations, events and queries are summarized in the state machine below. Green shows the initial state while orange indicates the end state. If an operation is not listed for a state, then it is invalid to call it while the port is in that state. If an event is not listed for a state, then that event cannot happen in that state.

![Inport transitions](/images/inport_transitions1.png)

Finally, there are two methods available for convenience to complete the stage and all of its ports:

* `CompleteStage()` is equivalent to closing all output ports and cancelling all input ports.
* `FailStage(exception)` is equivalent to failing all output ports and cancelling all input ports.

In some cases it is inconvenient and error prone to react on the regular state machine events with the signal based API described above. For those cases there is a API which allows for a more declarative sequencing of actions which will greatly simplify some use cases at the cost of some extra allocations. The difference between the two APIs could be described as that the first one is signal driven from the outside, while this API is more active and drives its surroundings.

The operations of this part of the :class:`GraphStage` API are:

* `Emit(out, elem)` and `EmitMultiple(out, Enumerable(elem1, elem2))` replaces the `OutHandler` with a handler that emits one or more elements when there is demand, and then reinstalls the current handlers
* `Read(in, andThen, onClose)` and `ReadMany(in, n, andThen, onClose)` replaces the `InHandler` with a handler that reads one or more elements as they are pushed and allows the handler to react once the requested number of elements has been read.
* `AbortEmitting(out)` and `AbortReading(in)` which will cancel an ongoing emit or read

Note that since the above methods are implemented by temporarily replacing the handlers of the stage you should never call `SetHandler` while they are running `Emit` or `Read` as that interferes with how they are implemented. The following methods are safe to call after invoking `Emit` and `Read` (and will lead to actually running the operation when those are done): `Complete(out)`, `CompleteStage()`, `Emit`, `EmitMultiple`, `AbortEmitting()` and `AbortReading()`

An example of how this API simplifies a stage can be found below in the second version of the Duplicator.

### Custom linear processing stages using GraphStage

Graph stages allows for custom linear processing stages through letting them have one input and one output and using `FlowShape` as their shape.

Such a stage can be illustrated as a box with two flows as it is seen in the illustration below. Demand flowing upstream leading to elements flowing downstream.

![graph stage conceptual](/images/graph_stage_conceptual1.png)

To illustrate these concepts we create a small `GraphStage` that implements the `Map` transformation.

![graph stage map](/images/graph_stage_map1.png)

Map calls `Push(out)` from the `onPush` handler and it also calls `Pull()` from the `onPull` handler resulting in the conceptual wiring above, and fully expressed in code below:

```csharp
class Map<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
{
    private sealed class Logic : GraphStageLogic
    {
        public Logic(Map<TIn, TOut> map) : base(map.Shape)
        {
            SetHandler(map.In, onPush: () => Push(map.Out, map._func(Grab(map.In))));
            SetHandler(map.Out, onPull: ()=> Pull(map.In));
        }
    }

    private readonly Func<TIn, TOut> _func;

    public Map(Func<TIn, TOut> func)
    {
        _func = func;
        Shape = new FlowShape<TIn, TOut>(In, Out);
    }

    public Inlet<TIn> In { get; } = new Inlet<TIn>("Map.in");

    public Outlet<TOut> Out { get; } = new Outlet<TOut>("Map.out");

    public override FlowShape<TIn, TOut> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}
```

Map is a typical example of a one-to-one transformation of a stream where demand is passed along upstream elements passed on downstream.

To demonstrate a many-to-one stage we will implement filter. The conceptual wiring of `Filter` looks like this:

![Graph stage filter](/images/graph_stage_filter1.png)

As we see above, if the given predicate matches the current element we are propagating it downwards, otherwise we return the "ball" to our upstream so that we get the new element. This is achieved by modifying the map example by adding a conditional in the `onPush` handler and decide between a `Pull(in)` or `Push(out)` call (and of course not having a mapping f function).

```csharp
class Filter<T> : GraphStage<FlowShape<T, T>>
{
    private sealed class Logic : GraphStageLogic
    {
        public Logic(Filter<T> filter) : base(filter.Shape)
        {
            SetHandler(filter.In, onPush: () =>
            {
                var element = Grab(filter.In);
                if(filter._predicate(element))
                    Push(filter.Out, element);
                else
                    Pull(filter.In);
            });

            SetHandler(filter.Out, onPull: ()=> Pull(filter.In));
        }
    }

    private readonly Predicate<T> _predicate;

    public Filter(Predicate<T> predicate)
    {
        _predicate = predicate;
        Shape = new FlowShape<T, T>(In, Out);
    }

    public Inlet<T> In { get; } = new Inlet<T>("Filter.in");

    public Outlet<T> Out { get; } = new Outlet<T>("Filter.out");

    public override FlowShape<T, T> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}
```

To complete the picture we define a one-to-many transformation as the next step. We chose a straightforward example stage that emits every upstream element twice downstream. The conceptual wiring of this stage looks like this:

![Graph stage duplicate](/images/graph_stage_duplicate1.png)

This is a stage that has state: an option with the last element it has seen indicating if it has duplicated this last element already or not. We must also make sure to emit the extra element if the upstream completes.

```csharp
class Duplicator<T> : GraphStage<FlowShape<T, T>>
{
    private sealed class Logic : GraphStageLogic
    {
        Option<T> _lastElement = Option<T>.None;

        public Logic(Duplicator<T> duplicator) : base(duplicator.Shape)
        {
            SetHandler(duplicator.In,
                onPush: () =>
                {
                    var element = Grab(duplicator.In);
                    _lastElement = element;
                    Push(duplicator.Out, element);
                },
                onUpstreamFinish: () =>
                {
                    if(_lastElement.HasValue)
                        Emit(duplicator.Out, _lastElement.Value);

                    Complete(duplicator.Out);
                });

            SetHandler(duplicator.Out, onPull: () =>
            {
                if (_lastElement.HasValue)
                {
                    Push(duplicator.Out, _lastElement.Value);
                    _lastElement = Option<T>.None;
                }
                else
                    Pull(duplicator.In);
            });
        }
    }
    
    public Duplicator(Predicate<T> predicate)
    {
        Shape = new FlowShape<T, T>(In, Out);
    }

    public Inlet<T> In { get; } = new Inlet<T>("Duplicator.in");

    public Outlet<T> Out { get; } = new Outlet<T>("Duplicator.out");

    public override FlowShape<T, T> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}
```

In this case a pull from downstream might be consumed by the stage itself rather than passed along upstream as the stage might contain an element it wants to push. Note that we also need to handle the case where the upstream closes while the stage still has elements it wants to push downstream. This is done by overriding onUpstreamFinish in the InHandler and provide custom logic that should happen when the upstream has been finished.

This example can be simplified by replacing the usage of a mutable state with calls to `EmitMultiple` which will replace the handlers, emit each of multiple elements and then reinstate the original handlers:

```csharp
class Duplicator<T> : GraphStage<FlowShape<T, T>>
{
    private sealed class Logic : GraphStageLogic
    {
        public Logic(Duplicator<T> duplicator) : base(duplicator.Shape)
        {

            SetHandler(duplicator.In, onPush: () =>
            {
                var element = Grab(duplicator.In);
                // this will temporarily suspend this handler until the two elems
                // are emitted and then reinstates it
                EmitMultiple(duplicator.Out, new[] {element, element});
            });

            SetHandler(duplicator.Out, onPull: () => Pull(duplicator.In));
        }
    }
    
    public Duplicator(Predicate<T> predicate)
    {
        Shape = new FlowShape<T, T>(In, Out);
    }

    public Inlet<T> In { get; } = new Inlet<T>("Duplicator.in");

    public Outlet<T> Out { get; } = new Outlet<T>("Duplicator.out");

    public override FlowShape<T, T> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}
```

Finally, to demonstrate all of the stages above, we put them together into a processing chain, which conceptually would correspond to the following structure:

![Graph stage chain](/images/graph_stage_chain1.png)

In code this is only a few lines, using the `Via` use our custom stages in a stream:

```csharp
var resultTask = Source.From(new [] {1,2,3,4,5})
        .Via(new Filter<int>(n => n%2 ==0))
        .Via(new Duplicator<int>())
        .Via(new Map<int, int>(n=>n/2))
        .RunAggregate(0, (sum, next) => sum + next, materializer);
```

If we attempt to draw the sequence of events, it shows that there is one "event token" in circulation in a potential chain of stages, just like our conceptual "railroad tracks" representation predicts.

![Graph stage tracks](/images/graph_stage_tracks_11.png)

### Completion

Completion handling usually (but not exclusively) comes into the picture when processing stages need to emit a few more elements after their upstream source has been completed. We have seen an example of this in our first `Duplicator` implementation where the last element needs to be doubled even after the upstream neighbor stage has been completed. This can be done by overriding the `onUpstreamFinish` callback in `SetHandler(in, action)`.

Stages by default automatically stop once all of their ports (input and output) have been closed externally or internally. It is possible to opt out from this behavior by invoking `SetKeepGoing(true)` (which is not supported from the stage's constructor and usually done in `PreStart`). In this case the stage **must** be explicitly closed by calling `CompleteStage()` or `FailStage(exception)`. This feature carries the risk of leaking streams and actors, therefore it should be used with care.

### Logging inside GraphStages

Logging debug or other important information in your stages is often a very good idea, especially when developing
more advances stages which may need to be debugged at some point.
 
The `Log` property is provided to enable you to easily obtain a `LoggingAdapter`
inside of a `GraphStage` as long as the `Materializer` you're using is able to provide you with a logger.
In that sense, it serves a very similar purpose as `ActorLogging` does for Actors. 

> [!NOTE]
> Please note that you can always simply use a logging library directly inside a Stage.
Make sure to use an asynchronous appender however, to not accidentally block the stage when writing to files etc.

The stage gets access to the `Log` property which it can safely use from any ``GraphStage`` callbacks:

```csharp
private sealed class RandomLettersSource : GraphStage<SourceShape<string>>
{
	#region internal classes

	private sealed class Logic : GraphStageLogic
	{
		public Logic(RandomLettersSource stage) : base(stage.Shape)
		{
			SetHandler(stage.Out, onPull: () =>
			{
				var c = NextChar(); // ASCII lower case letters

				Log.Debug($"Randomly generated: {c}");	

				Push(stage.Out, c.ToString());
			});
		}

		private static char NextChar() => (char) ThreadLocalRandom.Current.Next('a', 'z'1);
	}

	#endregion

    public RandomLettersSource()
    {
	    Shape = new SourceShape<string>(Out);
    }

    private Outlet<string> Out { get; } = new Outlet<string>("RandomLettersSource.out");

    public override SourceShape<string> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}


[Fact]
public void A_GraphStageLogic_must_support_logging_in_custom_graphstage()
{
	const int n = 10;
	EventFilter.Debug(start: "Randomly generated").Expect(n, () =>
	{
		Source.FromGraph(new RandomLettersSource())
			.Take(n)
			.RunWith(Sink.Ignore<string>(), Materializer)
			.Wait(TimeSpan.FromSeconds(3));
	});
}
```

> [!NOTE]
> **SPI Note:** If you're implementing a Materializer, you can add this ability to your materializer by implementing 
`IMaterializerLoggingProvider` in your `Materializer`.

### Using timers

It is possible to use timers in `GraphStages` by using `TimerGraphStageLogic` as the base class for the returned logic. Timers can be scheduled by calling one of `ScheduleOnce(key,delay)`, `SchedulePeriodically(key,period)` or `SchedulePeriodicallyWithInitialDelay(key,delay,period)` and passing an object as a key for that timer (can be any object, for example a String). The `OnTimer(key)` method needs to be overridden and it will be called once the timer of key fires. It is possible to cancel a timer using `CancelTimer(key)` and check the status of a timer with `IsTimerActive(key)`. Timers will be automatically cleaned up when the stage completes.

Timers can not be scheduled from the constructor of the logic, but it is possible to schedule them from the `PreStart()` lifecycle hook.

In this sample the stage toggles between open and closed, where open means no elements are passed through. The stage starts out as closed but as soon as an element is pushed downstream the gate becomes open for a duration of time during which it will consume and drop upstream messages:

```csharp
class TimedGate<T> : GraphStage<FlowShape<T, T>>
{
    private readonly TimeSpan _silencePeriod;

    private sealed class Logic : TimerGraphStageLogic
    {
        private bool _open;

        public Logic(TimedGate<T> gate) : base(gate.Shape)
        {
            SetHandler(gate.In, onPush: () =>
            {
                var element = Grab(gate.In);
                if (_open)
                    Pull(gate.In);
                else
                {
                    Push(gate.Out, element);
                    _open = true;
                    ScheduleOnce("Close", gate._silencePeriod);
                }
            });

            SetHandler(gate.Out, onPull: () => Pull(gate.In));
        }

        protected internal override void OnTimer(object timerKey) => _open = false;
    }

    public TimedGate(TimeSpan silencePeriod)
    {
        _silencePeriod = silencePeriod;
        Shape = new FlowShape<T, T>(In, Out);
    }

    public Inlet<T> In { get; } = new Inlet<T>("TimedGate.in");

    public Outlet<T> Out { get; } = new Outlet<T>("TimedGate.out");

    public override FlowShape<T, T> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}
```

### Using asynchronous side-channels

In order to receive asynchronous events that are not arriving as stream elements (for example a completion of a task or a callback from a 3rd party API) one must acquire a `AsyncCallback` by calling `GetAsyncCallback()` from the stage logic. The method `GetAsyncCallback` takes as a parameter a callback that will be called once the asynchronous event fires. It is important to **not call the callback directly**, instead, the external API must `invoke` the returned `Action`. The execution engine will take care of calling the provided callback in a thread-safe way. The callback can safely access the state of the `GraphStageLogic` implementation.

Sharing the `AsyncCallback` from the constructor risks race conditions, therefore it is recommended to use the `PreStart()` lifecycle hook instead.

This example shows an asynchronous side channel graph stage that starts dropping elements when a future completes:

```csharp
class KillSwitch<T> : GraphStage<FlowShape<T, T>>
{
    private sealed class Logic : GraphStageLogic
    {
        private readonly KillSwitch<T> _killSwitch;

        public Logic(KillSwitch<T> killSwitch) : base(killSwitch.Shape)
        {
            _killSwitch = killSwitch;

            SetHandler(killSwitch.In, onPush: () => Push(killSwitch.Out, Grab(killSwitch.In)));
            SetHandler(killSwitch.Out, onPull: () => Pull(killSwitch.In));
        }

        public override void PreStart()
        {
            var callback = GetAsyncCallback(CompleteStage);
            _killSwitch._killSwitch.ContinueWith(_ => callback());
        }
    }

    private readonly Task _killSwitch;

    public KillSwitch(Task killSwitch)
    {
        _killSwitch = killSwitch;
        Shape = new FlowShape<T, T>(In, Out);
    }

    public Inlet<T> In { get; } = new Inlet<T>("KillSwitch.in");

    public Outlet<T> Out { get; } = new Outlet<T>("KillSwitch.out");

    public override FlowShape<T, T> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}
```

### Integration with actors

**This section is a stub and will be extended in the next release This is an experimental feature***

It is possible to acquire an ActorRef that can be addressed from the outside of the stage, similarly how `AsyncCallback` allows injecting asynchronous events into a stage logic. This reference can be obtained by calling `GetStageActorRef(receive)` passing in a function that takes a `Tuple` of the sender `IActorRef` and the received message. This reference can be used to watch other actors by calling its `Watch(ref)` or `Unwatch(ref)` methods. The reference can be also watched by external actors. The current limitations of this `IActorRef` are:

* they are not location transparent, they cannot be accessed via remoting.
* they cannot be returned as materialized values.
* they cannot be accessed from the constructor of the `GraphStageLogic`, but they can be accessed from the `PreStart()` method.

### Custom materialized values

Custom stages can return materialized values instead of `NotUsed` by inheriting from `GraphStageWithMaterializedValue` instead of the simpler `GraphStage`. The difference is that in this case the method `CreateLogicAndMaterializedValue(inheritedAttributes)` needs to be overridden, and in addition to the stage logic the materialized value must be provided

> [!WARNING]
> There is no built-in synchronization of accessing this value from both of the thread where the logic runs and the thread that got hold of the materialized value. It is the responsibility of the programmer to add the necessary (non-blocking) synchronization and visibility guarantees to this shared object.

In this sample the materialized value is a task containing the first element to go through the stream:

```csharp
class FirstValue<T> : GraphStageWithMaterializedValue<FlowShape<T, T>, Task<T>>
{
    private sealed class Logic : GraphStageLogic
    {
        public Logic(FirstValue<T> first, TaskCompletionSource<T> completion) : base(first.Shape)
        {
            SetHandler(first.In, onPush: () =>
            {
                var element = Grab(first.In);
                completion.SetResult(element);
                Push(first.Out, element);

                // replace handler with one just forwarding
                SetHandler(first.In, onPush: () => Push(first.Out, Grab(first.In)));
            });

            SetHandler(first.Out, onPull: () => Pull(first.In));
        }
    }
    
    public FirstValue( )
    {
        Shape = new FlowShape<T, T>(In, Out);
    }

    public Inlet<T> In { get; } = new Inlet<T>("FirstValue.in");

    public Outlet<T> Out { get; } = new Outlet<T>("FirstValue.out");

    public override FlowShape<T, T> Shape { get; }

    public override ILogicAndMaterializedValue<Task<T>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
    {
        var completion = new TaskCompletionSource<T>();
        var logic = new Logic(this, completion);
        return new LogicAndMaterializedValue<Task<T>>(logic, completion.Task);
    }
}
```

## Using attributes to affect the behavior of a stage

**This section is a stub and will be extended in the next release**

Stages can access the `Attributes` object created by the materializer. This contains all the applied (inherited) attributes applying to the stage, ordered from least specific (outermost) towards the most specific (innermost) attribute. It is the responsibility of the stage to decide how to reconcile this inheritance chain to a final effective decision.

See [Modularity, Composition and Hierarchy](xref:streams-modularity) for an explanation on how attributes work.

### Rate decoupled graph stages

Sometimes it is desirable to decouple the rate of the upstream and downstream of a stage, synchronizing only when needed.

This is achieved in the model by representing a `GraphStage` as a *boundary* between two regions where the demand sent upstream is decoupled from the demand that arrives from downstream. One immediate consequence of this difference is that an `onPush` call does not always lead to calling `Push` and an `onPull` call does not always lead to calling `Pull`.

One of the important use-case for this is to build buffer-like entities, that allow independent progress of upstream and downstream stages when the buffer is not full or empty, and slowing down the appropriate side if the buffer becomes empty or full.

The next diagram illustrates the event sequence for a buffer with capacity of two elements in a setting where the downstream demand is slow to start and the buffer will fill up with upstream elements before any demand is seen from downstream.

![Graph stage detached](/images/graph_stage_detached_tracks_11.png)

Another scenario would be where the demand from downstream starts coming in before any element is pushed into the buffer stage.

![graph stage detached](/images/graph_stage_detached_tracks_21.png)

The first difference we can notice is that our `Buffer` stage is automatically pulling its upstream on initialization. The buffer has demand for up to two elements without any downstream demand.

The following code example demonstrates a buffer class corresponding to the message sequence chart above.

```csharp
class TwoBuffer<T> : GraphStage<FlowShape<T, T>>
{
    private sealed class Logic : GraphStageLogic
    {
        private readonly TwoBuffer<T> _buffer;
        private readonly Queue<T> _queue;
        private bool _downstreamWaiting = false;

        public Logic(TwoBuffer<T> buffer) : base(buffer.Shape)
        {
            _buffer = buffer;
            _queue = new Queue<T>();

            SetHandler(buffer.In, OnPush, OnUpstreamFinish);
            SetHandler(buffer.Out, OnPull);
        }

        private bool BufferFull => _queue.Count == 2;

        private void OnPush()
        {
            var element = Grab(_buffer.In);
            _queue.Enqueue(element);
            if (_downstreamWaiting)
            {
                _downstreamWaiting = false;
                var bufferedElement = _queue.Dequeue();
                Push(_buffer.Out, bufferedElement);
            }
            if(!BufferFull)
                Pull(_buffer.In);
        }

        private void OnUpstreamFinish()
        {
            if (_queue.Count != 0)
            {
                // emit the rest if possible
                EmitMultiple(_buffer.Out, _queue);
            }

            CompleteStage();
        }

        private void OnPull()
        {
            if (_queue.Count == 0)
                _downstreamWaiting = true;
            else
            {
                var element = _queue.Dequeue();
                Push(_buffer.Out, element);
            }

            if(!BufferFull && !HasBeenPulled(_buffer.In))
                Pull(_buffer.In);
        }
    }

    public TwoBuffer()
    {
        Shape = new FlowShape<T, T>(In, Out);
    }

    public Inlet<T> In { get; } = new Inlet<T>("TwoBuffer.in");

    public Outlet<T> Out { get; } = new Outlet<T>("TwoBuffer.out");

    public override FlowShape<T, T> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
}
```

## Thread safety of custom processing stages

**All of the above custom stages (linear or graph) provide a few simple guarantees that implementors can rely on.**

* The callbacks exposed by all of these classes are never called concurrently.
* The state encapsulated by these classes can be safely modified from the provided callbacks, without any further synchronization.

In essence, the above guarantees are similar to what `Actor`'s provide, if one thinks of the state of a custom stage as state of an actor, and the callbacks as the `receive` block of the actor.

> [!WARNING]
> It is **not** safe to access the state of any custom stage outside of the callbacks that it provides, just like it is unsafe to access the state of an actor from the outside. This means that Future callbacks should not close over internal state of custom stages because such access can be concurrent with the provided callbacks, leading to undefined behavior.


## Resources and the stage lifecycle

If a stage manages a resource with a lifecycle, for example objects that need to be shutdown when they are not
used anymore it is important to make sure this will happen in all circumstances when the stage shuts down.

Cleaning up resources should be done in `GraphStageLogic.PostStop` and not in the `InHandler` and `OutHandler`
callbacks. The reason for this is that when the stage itself completes or is failed there is no signal from the upstreams
for the downstreams. Even for stages that do not complete or fail in this manner, this can happen when the
`Materializer` is shutdown or the `ActorSystem` is terminated while a stream is still running, what is called an
"abrupt termination".


## Extending Flow Combinators with Custom Operators

The most general way of extending any `Source`, `Flow` or `SubFlow` (e.g. from `GroupBy`) is demonstrated above: create a graph of flow-shape like the `Filter` example given above and use the `.Via(...)` combinator to integrate it into your stream topology. This works with all `IFlow` sub-types, including the ports that you connect with the graph DSL.

Advanced .Net users may wonder whether it is possible to write extension methods that enrich `IFlow` to allow nicer syntax. The short answer is that .Net does not support this in a fully generic fashion, the problem is that it is impossible to abstract over the kind of stream that is being extended because Source, Flow and SubFlow differ in the number and kind of their type parameters.

A lot simpler is the task of just adding an extension method to `Source` and `Flow` as shown below:

```csharp
public static class Extensions
{
    public static Flow<TIn, TOut, TMat> Filter<TIn, TOut, TMat>(this Flow<TIn, TOut, TMat> flow,
        Predicate<TOut> predicate) => flow.Via(new Filter<TOut>(predicate));

    public static Source<TOut, TMat> Filter<TOut, TMat>(this Source<TOut, TMat> source,
        Predicate<TOut> predicate) => source.Via(new Filter<TOut>(predicate));
}
```

you can then replace the `.Via` call from above with the extension method:

```csharp
var resultTask = Source.From(new [] {1,2,3,4,5})
    .Filter(n => n % 2 == 0)
    .Via(new Duplicator<int>())
    .Via(new Map<int, int>(n=>n/2))
    .RunAggregate(0, (sum, next) => sum + next, materializer);
```

If you try to write this for `SubFlow`, though, you will run into the same issue as when trying to unify the two solutions above, only on a higher level (the type constructors needed for that unification would have rank two, meaning that some of their type arguments are type constructors themselves when trying to extend the solution shown in the linked sketch the author encountered such a density of compiler StackOverflowErrors and IDE failures that he gave up).
