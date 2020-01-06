---
uid: streams-builtin-stages
title: Overview of built-in stages and their semantics
---

# Source stages
These built-in sources are available from ``akka.stream.scaladsl.Source``:



#### FromEnumerator

Stream the values from an ``Enumerator``, requesting the next value when there is demand. The enumerator will be created anew
for each materialization, which is the reason the method takes a function rather than an enumerator directly.

If the enumerator perform blocking operations, make sure to run it on a separate dispatcher.

**emits** the next value returned from the enumerator

**completes** when the enumerator reaches its end

#### From

Stream the values of an ``IEnumerable<T>``.

**emits** the next value of the enumerable

**completes** when the last element of the enumerable has been emitted


#### Single

Stream a single object

**emits** the value once

**completes** when the single value has been emitted

#### Repeat

Stream a single object repeatedly

**emits** the same value repeatedly when there is demand

**completes** never

#### Cycle

Stream iterator in cycled manner. Internally new iterator is being created to cycle the one provided via argument meaning
when original iterator runs out of elements process will start all over again from the beginning of the iterator
provided by the evaluation of provided parameter. If method argument provides empty iterator stream will be terminated with
exception.

**emits** the next value returned from cycled iterator

**completes** never

#### Tick

A periodical repetition of an arbitrary object. Delay of first tick is specified
separately from interval of the following ticks.

**emits** periodically, if there is downstream backpressure ticks are skipped

**completes** never

#### FromTask

Send the single value of the ``Task`` when it completes and there is demand.
If the task fails the stream is failed with that exception.

**emits** the task completes

**completes** after the task has completed

#### Unfold

Stream the result of a function as long as it returns not ``null``, the value inside the option
consists of a tuple where the first value is a state passed back into the next call to the function allowing
to pass a state. The first invocation of the provided fold function will receive the ``zero`` state.

Can be used to implement many stateful sources without having to touch the more low level ``GraphStage`` API.

**emits** when there is demand and the unfold function over the previous state returns non null value

**completes** when the unfold function returns an null value

#### UnfoldAsync

Just like ``Unfold`` but the fold function returns a ``Task`` which will cause the source to
complete or emit when it completes.

Can be used to implement many stateful sources without having to touch the more low level ``GraphStage`` API.

**emits** when there is demand and unfold state returned task completes with not null value

**completes** when the task returned by the unfold function completes with an null value

#### Empty

Complete right away without ever emitting any elements. Useful when you have to provide a source to
an API but there are no elements to emit.

**emits** never

**completes** directly

#### Maybe

Materialize a ``TaskCompletionSource<T>`` that if completed with a ``T`` will emit that `T` and then complete
the stream, or if completed with ``null`` complete the stream right away.

**emits** when the returned promise is completed with not null value

**completes** after emitting not null value, or directly if the promise is completed with null value

#### Failed

Fail directly with a user specified exception.

**emits** never

**completes** fails the stream directly with the given exception

#### Lazily

Defers creation and materialization of a `Source` until there is demand.

**emits** depends on the wrapped `Source`

**completes** depends on the wrapped `Source`

#### ActorPublisher

Wrap an actor extending ``ActorPublisher`` as a source.

**emits** depends on the actor implementation

**completes** when the actor stops

#### ActorRef

Materialize an ``IActorRef``, sending messages to it will emit them on the stream. The actor contain
a buffer but since communication is one way, there is no back pressure. Handling overflow is done by either dropping
elements or failing the stream, the strategy is chosen by the user.

**emits** when there is demand and there are messages in the buffer or a message is sent to the actorref

**completes** when the actorref is sent ``Akka.Actor.Status.Success`` or ``PoisonPill``

#### PreMaterialize

Materializes this Source, immediately returning (1) its materialized value, and (2) a new Source that can consume elements 'into' the pre-materialized one.

Useful for when you need a materialized value of a Source when handing it out to someone to materialize it for you.

#### Combine

Combine several sources, using a given strategy such as merge or concat, into one source.

**emits** when there is demand, but depending on the strategy

**completes** when all sources has completed

#### UnfoldResource

Wrap any resource that can be opened, queried for next element (in a blocking way) and closed using three distinct functions into a source.

**emits** when there is demand and read function returns value

**completes**  when read function returns ``None``

#### UnfoldResourceAsync

Wrap any resource that can be opened, queried for next element (in a blocking way) and closed using three distinct functions into a source.
Functions return ``Task`` to achieve asynchronous processing

**emits** when there is demand and ``Task`` from read function returns value

**completes** when ``Task`` from read function returns ``None``

#### Queue

Materialize a ``SourceQueue`` onto which elements can be pushed for emitting from the source. The queue contains
a buffer, if elements are pushed onto the queue faster than the source is consumed the overflow will be handled with
a strategy specified by the user. Functionality for tracking when an element has been emitted is available through
``SourceQueue.Offer``.

**emits** when there is demand and the queue contains elements

**completes** when downstream completes

#### AsSubscriber

Integration with Reactive Streams, materializes into a ``Reactive.Streams.ISubscriber``.


#### FromPublisher

Integration with Reactive Streams, subscribes to a ``Reactive.Streams.IPublisher``.


#### ZipN

Combine the elements of multiple streams into a stream of sequences.

**emits** when all of the inputs has an element available

**completes** when any upstream completes


#### ZipWithN

Combine the elements of multiple streams into a stream of sequences using a combiner function.

**emits** when all of the inputs has an element available

**completes** when any upstream completes


# Sink stages

These built-in sinks are available from ``Akka.Stream.DSL.Sink``:


#### First

Materializes into a ``Task`` which completes with the first value arriving,
after this the stream is canceled. If no element is emitted, the task is be failed.

**cancels** after receiving one element

**backpressures** never

#### FirstOrDefault
Materializes into a ``Task<T>`` which completes with the first value arriving,
or a ``default(T)`` if the stream completes without any elements emitted.

**cancels** after receiving one element

**backpressures** never

#### Last

Materializes into a ``Task`` which will complete with the last value emitted when the stream
completes. If the stream completes with no elements the task is failed.

**cancels** never

**backpressures** never

#### LasrOrDefault

Materialize a ``Task<T>`` which completes with the last value
emitted when the stream completes. if the stream completes with no elements the task is
completed with default(T).

**cancels** never

**backpressures** never

#### Ignore

Consume all elements but discards them. Useful when a stream has to be consumed but there is no use to actually
do anything with the elements.

**cancels** never

**backpressures** never

#### Cancelled

Immediately cancel the stream

**cancels** immediately

#### Seq

Collect values emitted from the stream into a collection, the collection is available through a ``Task`` or
which completes when the stream completes. Note that the collection is bounded to ``int.MaxValue``,
if more element are emitted the sink will cancel the stream

**cancels** If too many values are collected

#### Foreach

Invoke a given procedure for each element received. Note that it is not safe to mutate shared state from the procedure.

The sink materializes into a  ``Task`` which completes when the
stream completes, or fails if the stream fails.

Note that it is not safe to mutate state from the procedure.

**cancels** never

**backpressures** when the previous procedure invocation has not yet completed


#### ForeachParallel

Like ``Foreach`` but allows up to ``parallellism`` procedure calls to happen in parallel.

**cancels** never

**backpressures** when the previous parallel procedure invocations has not yet completed


#### OnComplete

Invoke a callback when the stream has completed or failed.

**cancels** never

**backpressures** never


#### Aggregate

Fold over emitted element with a function, where each invocation will get the new element and the result from the
previous fold invocation. The first invocation will be provided the ``zero`` value.

Materializes into a task that will complete with the last state when the stream has completed.

This stage allows combining values into a result without a global mutable state by instead passing the state along
between invocations.

**cancels** never

**backpressures** when the previous fold function invocation has not yet completed

#### Sum

Apply a reduction function on the incoming elements and pass the result to the next invocation. The first invocation
receives the two first elements of the flow.

Materializes into a task that will be completed by the last result of the reduction function.

**cancels** never

**backpressures** when the previous reduction function invocation has not yet completed


#### Combine

Combine several sinks into one using a user specified strategy

**cancels** depends on the strategy

**backpressures** depends on the strategy


#### ActorRef

Send the elements from the stream to an ``IActorRef``. No backpressure so care must be taken to not overflow the inbox.

**cancels** when the actor terminates

**backpressures** never


#### ActorRefWithAck

Send the elements from the stream to an ``IActorRef`` which must then acknowledge reception after completing a message,
to provide back pressure onto the sink.

**cancels** when the actor terminates

**backpressures** when the actor acknowledgment has not arrived.

#### PreMaterialize

Materializes this Sink, immediately returning (1) its materialized value, and (2) a new Sink that can consume elements 'into' the pre-materialized one.

Useful for when you need a materialized value of a Sink when handing it out to someone to materialize it for you.


#### ActorSubscriber

Create an actor from a ``Props`` upon materialization, where the actor implements ``ActorSubscriber``, which will
receive the elements from the stream.

Materializes into an ``IActorRef`` to the created actor.

**cancels** when the actor terminates

**backpressures** depends on the actor implementation


#### AsPublisher

Integration with Reactive Streams, materializes into a ``Reactive.Streams.IPublisher``.


#### FromSubscriber

Integration with Reactive Streams, wraps a ``Reactive.Streams.ISubscriber`` as a sink




# Additional Sink and Source converters

Sources and sinks for integrating with ``System.IO.Stream`` can be found on
``StreamConverters``. As they are blocking APIs the implementations of these stages are run on a separate
dispatcher configured through the ``akka.stream.blocking-io-dispatcher``.

#### FromOutputStream

Create a sink that wraps an ``Stream``. Takes a function that produces an ``Stream``, when the sink is
materialized the function will be called and bytes sent to the sink will be written to the returned ``Stream``.

Materializes into a ``Task`` which will complete with a ``IOResult`` when the stream
completes.

Note that a flow can be materialized multiple times, so the function producing the ``Stream`` must be able
to handle multiple invocations.

The ``Stream`` will be closed when the stream that flows into the ``Sink`` is completed, and the ``Sink``
will cancel its inflow when the ``Stream`` is no longer writable.

#### AsInputStream

Create a sink which materializes into an ``Stream`` that can be read to trigger demand through the sink.
Bytes emitted through the stream will be available for reading through the ``Stream``

The ``Stream`` will be ended when the stream flowing into this ``Sink`` completes, and the closing the
``Stream`` will cancel the inflow of this ``Sink``.

#### FromInputStream

Create a source that wraps an ``Stream``. Takes a function that produces an ``Stream``, when the source is
materialized the function will be called and bytes from the ``Stream`` will be emitted into the stream.

Materializes into a ``Task`` which will complete with a ``IOResult`` when the stream
completes.

Note that a flow can be materialized multiple times, so the function producing the ``Stream`` must be able
to handle multiple invocations.

The ``Stream`` will be closed when the ``Source`` is canceled from its downstream, and reaching the end of the
``Stream`` will complete the ``Source``.

#### AsOutputStream

Create a source that materializes into an ``Stream``. When bytes are written to the ``Stream`` they
are emitted from the source

The ``Stream`` will no longer be writable when the ``Source`` has been canceled from its downstream, and
closing the ``Stream`` will complete the ``Source``.

File IO Sinks and Sources
-------------------------
Sources and sinks for reading and writing files can be found on ``FileIO``.

#### FromFile

Emit the contents of a file, as ``ByteString`` s, materializes into a ``Task`` which will be completed with
a ``IOResult`` upon reaching the end of the file or if there is a failure.

#### ToFile

Create a sink which will write incoming ``ByteString`` s to a given file.



Flow stages
-----------

All flows by default backpressure if the computation they encapsulate is not fast enough to keep up with the rate of
incoming elements from the preceding stage. There are differences though how the different stages handle when some of
their downstream stages backpressure them.

Most stages stop and propagate the failure downstream as soon as any of their upstreams emit a failure.
This happens to ensure reliable teardown of streams and cleanup when failures happen. Failures are meant to
be to model unrecoverable conditions, therefore they are always eagerly propagated.
For in-band error handling of normal errors (dropping elements if a map fails for example) you should use the
supervision support, or explicitly wrap your element types in a proper container that can express error or success
states (for example ``try`` in C#).


Simple processing stages
------------------------

These stages can transform the rate of incoming elements since there are stages that emit multiple elements for a
single input (e.g. `ConcatMany`) or consume multiple elements before emitting one output (e.g. ``Where``).
However, these rate transformations are data-driven, i.e. it is the incoming elements that define how the
rate is affected. This is in contrast with [Backpressure aware stages](#backpressure-aware-stages) which can change their processing behavior
depending on being backpressured by downstream or not.

#### AlsoTo

Attaches the given `Sink` to this `Flow`, meaning that elements that passes through will also be sent to the `Sink`.

**emits** when an element is available and demand exists both from the Sink and the downstream

**backpressures** when downstream or Sink backpressures

**completes** when upstream completes

#### Select

Transform each element in the stream by calling a mapping function with it and passing the returned value downstream.

**emits** when the mapping function returns an element

**backpressures** when downstream backpressures

**completes** when upstream completes

#### SelectMany

Transform each element into zero or more elements that are individually passed downstream.

**emits** when the mapping function returns an element or there are still remaining elements from the previously calculated collection

**backpressures** when downstream backpressures or there are still available elements from the previously calculated collection

**completes** when upstream completes and all remaining elements has been emitted

#### StatefulSelectMany

Transform each element into zero or more elements that are individually passed downstream. The difference to ``SelectMany`` is that
the transformation function is created from a factory for every materialization of the flow.

**emits** when the mapping function returns an element or there are still remaining elements from the previously calculated collection

**backpressures** when downstream backpressures or there are still available elements from the previously calculated collection

**completes** when upstream completes and all remaining elements has been emitted

#### Where

Filter the incoming elements using a predicate. If the predicate returns true the element is passed downstream, if
it returns false the element is discarded.

**emits** when the given predicate returns true for the element

**backpressures** when the given predicate returns true for the element and downstream backpressures

**completes** when upstream completes

#### Collect

Apply a partial function to each incoming element, if the partial function is defined for a value the returned
value is passed downstream. Can often replace ``Where`` followed by ``Select`` to achieve the same in one single stage.

**emits** when the provided partial function is defined for the element

**backpressures** the partial function is defined for the element and downstream backpressures

**completes** when upstream completes

#### Grouped

Accumulate incoming events until the specified number of elements have been accumulated and then pass the collection of
elements downstream.

**emits** when the specified number of elements has been accumulated or upstream completed

**backpressures** when a group has been assembled and downstream backpressures

**completes** when upstream completes

#### Sliding

Provide a sliding window over the incoming stream and pass the windows as groups of elements downstream.

Note: the last window might be smaller than the requested size due to end of stream.

**emits** the specified number of elements has been accumulated or upstream completed

**backpressures** when a group has been assembled and downstream backpressures

**completes** when upstream completes

#### Scan

Emit its current value which starts at ``zero`` and then applies the current and next value to the given function
emitting the next current value.

Note that this means that scan emits one element downstream before and upstream elements will not be requested until
the second element is required from downstream.

**emits** when the function scanning the element returns a new element

**backpressures** when downstream backpressures

**completes** when upstream completes

#### ScanAsync

Just like `Scan` but receiving a function that results in a `Task` to the next value.

**emits** when the `Task` resulting from the function scanning the element resolves to the next value

**backpressures** when downstream backpressures

**completes** when upstream completes and the last `Task` is resolved

#### Aggregate

Start with current value ``zero`` and then apply the current and next value to the given function, when upstream
complete the current value is emitted downstream.

**emits** when upstream completes

**backpressures** when downstream backpressures

**completes** when upstream completes

#### AggregateAsync

Just like `Aggregate` but receiving a function that results in a `Task` to the next value.

**emits** when upstream completes and the last `Task` is resolved

**backpressures** when downstream backpressures

**completes** when upstream completes and the last `Task` is resolved

#### Skip

Skip ``n`` elements and then pass any subsequent element downstream.

**emits** when the specified number of elements has been skipped already

**backpressures** when the specified number of elements has been skipped and downstream backpressures

**completes** when upstream completes

#### Take

Pass ``n`` incoming elements downstream and then complete

**emits** while the specified number of elements to take has not yet been reached

**backpressures** when downstream backpressures

**completes** when the defined number of elements has been taken or upstream completes


#### TakeWhile

Pass elements downstream as long as a predicate function return true for the element include the element
when the predicate first return false and then complete.

**emits** while the predicate is true and until the first false result

**backpressures** when downstream backpressures

**completes** when predicate returned false or upstream completes

#### SkipWhile

Skip elements as long as a predicate function return true for the element

**emits** when the predicate returned false and for all following stream elements

**backpressures** predicate returned false and downstream backpressures

**completes** when upstream completes

#### Recover

Allow sending of one last element downstream when a failure has happened upstream.

Throwing an exception inside `Recover` _will_ be logged on ERROR level automatically.

**emits** when the element is available from the upstream or upstream is failed and pf returns an element

**backpressures** when downstream backpressures, not when failure happened

**completes** when upstream completes or upstream failed with exception pf can handle

#### RecoverWith

Allow switching to alternative Source when a failure has happened upstream.

Throwing an exception inside `RecoverWith` _will_ be logged on ERROR level automatically.

**emits** the element is available from the upstream or upstream is failed and pf returns alternative Source

**backpressures** downstream backpressures, after failure happened it backpressures to alternative Source

**completes** upstream completes or upstream failed with exception pf can handle

#### RecoverWithRetries

RecoverWithRetries allows to switch to alternative Source on flow failure. It will stay in effect after
a failure has been recovered up to `attempts` number of times so that each time there is a failure
it is fed into the `function` and a new Source may be materialized. Note that if you pass in 0, this won't
attempt to recover at all. Passing -1 will behave exactly the same as  `RecoverWith`.

Since the underlying failure signal OnError arrives out-of-band, it might jump over existing elements.
This stage can recover the failure signal, but not the skipped elements, which will be dropped.

**emits**  when element is available from the upstream or upstream is failed and element is available from alternative Source

**backpressures** when downstream backpressures

**completes** when upstream completes or upstream failed with exception function can handle

#### SelectError

While similar to `Recover` this stage can be used to transform an error signal to a different one *without* logging
it as an error in the process. So in that sense it is NOT exactly equivalent to ``Recover(e -> throw e2)`` since recover
would log the `e2` error.

Since the underlying failure signal OnError arrives out-of-band, it might jump over existing elements.
This stage can recover the failure signal, but not the skipped elements, which will be dropped.

Similarly to `Recover` throwing an exception inside `SelectError` _will_ be logged on ERROR level automatically.

**emits**  when element is available from the upstream or upstream is failed and function returns an element

**backpressures** when downstream backpressures

**completes** when upstream completes or upstream failed with exception function can handle

#### Detach

Detach upstream demand from downstream demand without detaching the stream rates.

**emits** when the upstream stage has emitted and there is demand

**backpressures** when downstream backpressures

**completes** when upstream completes


#### Throttle

Limit the throughput to a specific number of elements per time unit, or a specific total cost per time unit, where
a function has to be provided to calculate the individual cost of each element.

**emits** when upstream emits an element and configured time per each element elapsed

**backpressures** when downstream backpressures

**completes** when upstream completes


# Asynchronous processing stages

These stages encapsulate an asynchronous computation, properly handling backpressure while taking care of the asynchronous
operation at the same time (usually handling the completion of a Task).


#### SelectAsync

Pass incoming elements to a function that return a ``Task`` result. When the task arrives the result is passed
downstream. Up to ``n`` elements can be processed concurrently, but regardless of their completion time the incoming
order will be kept when results complete. For use cases where order does not matter ``SelectAsyncUnordered`` can be used.

If a Task fails, the stream also fails (unless a different supervision strategy is applied)

**emits** when the Task returned by the provided function finishes for the next element in sequence

**backpressures** when the number of tasks reaches the configured parallelism and the downstream backpressures

**completes** when upstream completes and all tasks has been completed and all elements has been emitted

#### SelectAsyncUnordered

Like ``SelectAsync`` but ``Task`` results are passed downstream as they arrive regardless of the order of the elements
that triggered them.

If a Task fails, the stream also fails (unless a different supervision strategy is applied)

**emits** any of the tasks returned by the provided function complete

**backpressures** when the number of tasks reaches the configured parallelism and the downstream backpressures

**completes** upstream completes and all tasks has been completed  and all elements has been emitted


# Timer driven stages

These stages process elements using timers, delaying, dropping or grouping elements for certain time durations.

#### TakeWithin

Pass elements downstream within a timeout and then complete.

**emits** when an upstream element arrives

**backpressures** downstream backpressures

**completes** upstream completes or timer fires


#### SkipWithin

Skip elements until a timeout has fired

**emits** after the timer fired and a new upstream element arrives

**backpressures** when downstream backpressures

**completes** upstream completes

#### GroupedWithin

Chunk up the stream into groups of elements received within a time window, or limited by the given number of elements,
whichever happens first.

**emits** when the configured time elapses since the last group has been emitted

**backpressures** when the group has been assembled (the duration elapsed) and downstream backpressures

**completes** when upstream completes


#### InitialDelay

Delay the initial element by a user specified duration from stream materialization.

**emits** upstream emits an element if the initial delay already elapsed

**backpressures** downstream backpressures or initial delay not yet elapsed

**completes** when upstream completes


#### Delay

Delay every element passed through with a specific duration.

**emits** there is a pending element in the buffer and configured time for this element elapsed

**backpressures** differs, depends on ``OverflowStrategy`` set

**completes** when upstream completes and buffered elements has been drained


# Backpressure aware stages

These stages are aware of the backpressure provided by their downstreams and able to adapt their behavior to that signal.

#### Conflate

Allow for a slower downstream by passing incoming elements and a summary into an aggregate function as long as
there is backpressure. The summary value must be of the same type as the incoming elements, for example the sum or
average of incoming numbers, if aggregation should lead to a different type ``ConflateWithSeed`` can be used:

**emits** when downstream stops backpressuring and there is a conflated element available

**backpressures** when the aggregate function cannot keep up with incoming elements

**completes** when upstream completes

#### ConflateWithSeed

Allow for a slower downstream by passing incoming elements and a summary into an aggregate function as long as there
is backpressure. When backpressure starts or there is no backpressure element is passed into a ``seed`` function to
transform it to the summary type.

**emits** when downstream stops backpressuring and there is a conflated element available

**backpressures** when the aggregate or seed functions cannot keep up with incoming elements

**completes** when upstream completes

#### Batch

Allow for a slower downstream by passing incoming elements and a summary into an aggregate function as long as there
is backpressure and a maximum number of batched elements is not yet reached. When the maximum number is reached and
downstream still backpressures batch will also backpressure.

When backpressure starts or there is no backpressure element is passed into a ``seed`` function to transform it
to the summary type.

Will eagerly pull elements, this behavior may result in a single pending (i.e. buffered) element which cannot be
aggregated to the batched value.

**emits** when downstream stops backpressuring and there is a batched element available

**backpressures** when batched elements reached the max limit of allowed batched elements & downstream backpressures

**completes** when upstream completes and a "possibly pending" element was drained


#### BatchWeighted

Allow for a slower downstream by passing incoming elements and a summary into an aggregate function as long as there
is backpressure and a maximum weight batched elements is not yet reached. The weight of each element is determined by
applying ``costFunction``. When the maximum total weight is reached and downstream still backpressures batch will also
backpressure.

Will eagerly pull elements, this behavior may result in a single pending (i.e. buffered) element which cannot be
aggregated to the batched value.

**emits** downstream stops backpressuring and there is a batched element available

**backpressures** batched elements reached the max weight limit of allowed batched elements & downstream backpressures

**completes** upstream completes and a "possibly pending" element was drained

#### Expand

Allow for a faster downstream by expanding the last incoming element to an ``Enumerator``

**emits** when downstream stops backpressuring

**backpressures** when downstream backpressures

**completes** when upstream completes

#### Buffer (Backpressure)

Allow for a temporarily faster upstream events by buffering ``size`` elements. When the buffer is full backpressure
is applied.

**emits** when downstream stops backpressuring and there is a pending element in the buffer

**backpressures** when buffer is full

**completes** when upstream completes and buffered elements has been drained

#### Buffer (Drop)

Allow for a temporarily faster upstream events by buffering ``size`` elements. When the buffer is full elements are
dropped according to the specified ``OverflowStrategy``:

* ``dropHead`` drops the oldest element in the buffer to make space for the new element
* ``dropTail`` drops the youngest element in the buffer to make space for the new element
* ``dropBuffer`` drops the entire buffer and buffers the new element
* ``dropNew`` drops the new element

**emits** when downstream stops backpressuring and there is a pending element in the buffer

**backpressures** never (when dropping cannot keep up with incoming elements)

**completes** upstream completes and buffered elements has been drained

#### Buffer (Fail)

Allow for a temporarily faster upstream events by buffering ``size`` elements. When the buffer is full the stage fails
the flow with a ``BufferOverflowException``.

**emits** when downstream stops backpressuring and there is a pending element in the buffer

**backpressures** never, fails the stream instead of backpressuring when buffer is full

**completes** when upstream completes and buffered elements has been drained


# Nesting and flattening stages

These stages either take a stream and turn it into a stream of streams (nesting) or they take a stream that contains
nested streams and turn them into a stream of elements instead (flattening).

# PrefixAndTail

Take up to `n` elements from the stream (less than `n` only if the upstream completes before emitting `n` elements)
and returns a pair containing a strict sequence of the taken element and a stream representing the remaining elements.

**emits** when the configured number of prefix elements are available. Emits this prefix, and the rest as a substream

**backpressures** when downstream backpressures or substream backpressures

**completes** when prefix elements has been consumed and substream has been consumed


#### GroupBy

Demultiplex the incoming stream into separate output streams.

**emits** an element for which the grouping function returns a group that has not yet been created. Emits the new group
there is an element pending for a group whose substream backpressures

**completes** when upstream completes (Until the end of stream it is not possible to know whether new substreams will be needed or not)

#### SplitWhen

Split off elements into a new substream whenever a predicate function return ``true``.

**emits** an element for which the provided predicate is true, opening and emitting a new substream for subsequent elements

**backpressures** when there is an element pending for the next substream, but the previous is not fully consumed yet, or the substream backpressures

**completes** when upstream completes (Until the end of stream it is not possible to know whether new substreams will be needed or not)

#### SplitAfter

End the current substream whenever a predicate returns ``true``, starting a new substream for the next element.

**emits** when an element passes through. When the provided predicate is true it emits the element * and opens a new substream for subsequent element

**backpressures** when there is an element pending for the next substream, but the previous is not fully consumed yet, or the substream backpressures

**completes** when upstream completes (Until the end of stream it is not possible to know whether new substreams will be needed or not)

#### ConcatMany

Transform each input element into a ``Source`` whose elements are then flattened into the output stream through
concatenation. This means each source is fully consumed before consumption of the next source starts.

**emits** when the current consumed substream has an element available

**backpressures** when downstream backpressures

**completes** when upstream completes and all consumed substreams complete


#### MergeMany

Transform each input element into a ``Source`` whose elements are then flattened into the output stream through
merging. The maximum number of merged sources has to be specified.

**emits** when one of the currently consumed substreams has an element available

**backpressures** when downstream backpressures

**completes** when upstream completes and all consumed substreams complete


# Time aware stages

Those stages operate taking time into consideration.

#### InitialTimeout

If the first element has not passed through this stage before the provided timeout, the stream is failed
with a ``TimeoutException``.

**emits** when upstream emits an element

**backpressures** when downstream backpressures

**completes** when upstream completes or fails if timeout elapses before first element arrives

**cancels** when downstream cancels

#### CompletionTimeout

If the completion of the stream does not happen until the provided timeout, the stream is failed
with a ``TimeoutException``.

**emits** when upstream emits an element

**backpressures** when downstream backpressures

**completes** when upstream completes or fails if timeout elapses before upstream completes

**cancels** when downstream cancels

#### IdleTimeout

If the time between two processed elements exceeds the provided timeout, the stream is failed
with a ``TimeoutException``. The timeout is checked periodically, so the resolution of the
check is one period (equals to timeout value).

**emits** when upstream emits an element

**backpressures** when downstream backpressures

**completes** when upstream completes or fails if timeout elapses between two emitted elements

**cancels** when downstream cancels

#### BackpressureTimeout

If the time between the emission of an element and the following downstream demand exceeds the provided timeout,
the stream is failed with a ``TimeoutException``. The timeout is checked periodically, so the resolution of the
check is one period (equals to timeout value).

**emits** when upstream emits an element

**backpressures** when downstream backpressures

**completes** when upstream completes or fails if timeout elapses between element emission and downstream demand.

**cancels** when downstream cancels

#### KeepAlive

Injects additional (configured) elements if upstream does not emit for a configured amount of time.

**emits** when upstream emits an element or if the upstream was idle for the configured period

**backpressures** when downstream backpressures

**completes** when upstream completes

**cancels** when downstream cancels

#### InitialDelay

Delays the initial element by the specified duration.

**emits** when upstream emits an element if the initial delay is already elapsed

**backpressures** when downstream backpressures or initial delay is not yet elapsed

**completes** when upstream completes

**cancels** when downstream cancels


# Fan-in stages

These stages take multiple streams as their input and provide a single output combining the elements from all of
the inputs in different ways.

#### Merge

Merge multiple sources. Picks elements randomly if all sources has elements ready.

**emits** when one of the inputs has an element available

**backpressures** when downstream backpressures

**completes** when all upstreams complete (This behavior is changeable to completing when any upstream completes by setting ``eagerComplete=true``.)

#### MergeSorted

Merge multiple sources. Waits for one element to be ready from each input stream and emits the
smallest element.

**emits** when all of the inputs have an element available

**backpressures** when downstream backpressures

**completes** when all upstreams complete

#### MergePreferred

Merge multiple sources. Prefer one source if all sources has elements ready.

**emits** when one of the inputs has an element available, preferring a defined input if multiple have elements available

**backpressures** when downstream backpressures

**completes** when all upstreams complete (This behavior is changeable to completing when any upstream completes by setting `eagerComplete=true`.)

#### MergePrioritized

Merge multiple sources. Prefer sources depending on priorities if all sources has elements ready. If a subset of all
sources has elements ready the relative priorities for those sources are used to prioritise.

**emits** when one of the inputs has an element available, preferring inputs based on their priorities if multiple have elements available

**backpressures** when downstream backpressures

**completes** when all upstreams complete (This behavior is changeable to completing when any upstream completes by setting `eagerComplete=true`.)

#### Zip

Combines elements from each of multiple sources into tuples and passes the tuples downstream.

**emits** when all of the inputs have an element available

**backpressures** when downstream backpressures

**completes** when any upstream completes

#### ZipWith

Combines elements from multiple sources through a ``combine`` function and passes the
returned value downstream.

**emits** when all of the inputs have an element available

**backpressures** when downstream backpressures

**completes** when any upstream completes  

#### ZipWithIndex

Zips elements of current flow with its indices.

**emits** when upstream emits an element and is paired with their index

**backpressures** when downstream backpressures

**completes** when any upstream completes

#### Concat

After completion of the original upstream the elements of the given source will be emitted.

**emits** when the current stream has an element available; if the current input completes, it tries the next one

**backpressures** when downstream backpressures

**completes** when all upstreams complete

#### Prepend

Prepends the given source to the flow, consuming it until completion before the original source is consumed.

If materialized values needs to be collected ``prependMat`` is available.

**emits** when the given stream has an element available; if the given input completes, it tries the current one

**backpressures** when downstream backpressures

**completes** when all upstreams complete

#### OrElse

If the primary source completes without emitting any elements, the elements from the secondary source
are emitted. If the primary source emits any elements the secondary source is cancelled.

Note that both sources are materialized directly and the secondary source is backpressured until it becomes
the source of elements or is cancelled.

Signal errors downstream, regardless which of the two sources emitted the error.

**emits** when an element is available from first stream or first stream closed without emitting any elements and an element
is available from the second stream

**backpressures** when downstream backpressures

**completes** the primary stream completes after emitting at least one element, when the primary stream completes
without emitting and the secondary stream already has completed or when the secondary stream completes

#### Interleave

Emits a specifiable number of elements from the original source, then from the provided source and repeats. If one
source completes the rest of the other stream will be emitted.

**emits** when element is available from the currently consumed upstream

**backpressures** when upstream backpressures

**completes** when both upstreams have completed

# Fan-out stages

These have one input and multiple outputs. They might route the elements between different outputs, or emit elements on
multiple outputs at the same time.

#### Unzip

Takes a stream of two element tuples and unzips the two elements into two different downstreams.

**emits** when all of the outputs stops backpressuring and there is an input element available

**backpressures** when any of the outputs backpressures

**completes** when upstream completes

#### UnzipWith

Splits each element of input into multiple downstreams using a function

**emits** when all of the outputs stops backpressuring and there is an input element available

**backpressures** when any of the outputs backpressures

**completes** when upstream completes

#### broadcast

Emit each incoming element each of ``n`` outputs.

**emits** when all of the outputs stops backpressuring and there is an input element available

**backpressures** when any of the outputs backpressures

**completes** when upstream completes

#### Balance

Fan-out the stream to several streams. Each upstream element is emitted to the first available downstream consumer.

**emits** when any of the outputs stops backpressuring; emits the element to the first available output

**backpressures** when all of the outputs backpressure

**completes** when upstream completes


#### Partition

Fan-out the stream to several streams. emitting an incoming upstream element to one downstream consumer according
to the partitioner function applied to the element

**emits** when an element is available from the input and the chosen output has demand

**backpressures** when the currently chosen output back-pressures

**completes** when upstream completes and no output is pending

**cancels** when when all downstreams cancel


# Watching status stages

#### WatchTermination

Materializes to a ``Task`` that will be completed with Done or failed depending whether the upstream of the stage has been completed or failed.
The stage otherwise passes through elements unchanged.

**emits** when input has an element available

**backpressures** when output backpressures

**completes** when upstream completes


#### Monitor

Materializes to a ``FlowMonitor`` that monitors messages flowing through or completion of the stage. The stage otherwise
passes through elements unchanged. Note that the ``FlowMonitor`` inserts a memory barrier every time it processes an
event, and may therefore affect performance.

**emits** when upstream emits an element

**backpressures** when downstream **backpressures**

**completes** when upstream completes
