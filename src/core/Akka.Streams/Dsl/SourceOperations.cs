using System;
using System.Collections.Generic;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    public static class SourceOperations
    {
        /// <summary>
        /// Recover allows to send last element on failure and gracefully complete the stream
        /// Since the underlying failure signal onError arrives out-of-band, it might jump over existing elements.
        /// This stage can recover the failure signal, but not the skipped elements, which will be dropped.
        /// <para>
        /// '''Emits when''' element is available from the upstream or upstream is failed and pf returns an element
        /// </para>
        /// <para>
        /// '''Backpressures when''' downstream backpressures
        /// </para>
        /// <para>
        /// '''Completes when''' upstream completes or upstream failed with exception pf can handle
        /// </para>
        /// '''Cancels when''' downstream cancels 
        /// </summary>
        public static Source<TOut, TMat> Recover<TIn, TOut, TMat>(this Source<TOut, TMat> flow, Func<Exception, TOut> partialFunc) where TOut : class
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Recover(flow, partialFunc);
        }

        /// <summary>
        /// Transform this stream by applying the given function to each of the elements
        /// as they pass through this processing step.
        /// <para>
        /// '''Emits when''' the mapping function returns an element
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// <para>
        /// '''Cancels when''' downstream cancels
        /// </para>
        /// </summary>
        public static Source<TOut, TMat> Map<TIn, TOut, TMat>(this Source<TIn, TMat> flow, Func<TIn, TOut> mapper)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Map(flow, mapper);
        }

        /// <summary>
        /// Transform each input element into a sequence of output elements that is
        /// then flattened into the output stream.
        /// 
        /// The returned sequence MUST NOT contain `null` values,
        /// as they are illegal as stream elements - according to the Reactive Streams specification.
        /// <para>
        /// '''Emits when''' the mapping function returns an element or there are still remaining elements
        /// from the previously calculated collection
        /// </para>
        /// <para>
        /// '''Backpressures when''' downstream backpressures or there are still remaining elements from the
        /// previously calculated collection
        /// </para>
        /// <para>
        /// '''Completes when''' upstream completes and all remaining elements has been emitted
        /// </para>
        /// <para>
        /// '''Cancels when''' downstream cancels
        /// </para>
        /// </summary>
        public static Source<TOut2, TMat> MapConcat<TOut1, TOut2, TMat>(this Source<TOut1, TMat> flow, Func<TOut1, IEnumerable<TOut2>> mapConcater)
        {
            return (Source<TOut2, TMat>)InternalFlowOperations.MapConcat(flow, mapConcater);
        }

        /// <summary>
        /// Transform this stream by applying the given function to each of the elements
        /// as they pass through this processing step. The function returns a `Future` and the
        /// value of that future will be emitted downstream. The number of Futures
        /// that shall run in parallel is given as the first argument to ``mapAsync``.
        /// These Futures may complete in any order, but the elements that
        /// are emitted downstream are in the same order as received from upstream.
        /// 
        /// If the group by function <paramref name="asyncMapper"/> throws an exception or if the <see cref="Task"/> is completed
        /// with failure and the supervision decision is <see cref="Supervision.Directive.Stop"/>
        /// the stream will be completed with failure.
        /// 
        /// If the group by function <paramref name="asyncMapper"/> throws an exception or if the <see cref="Task"/> is completed
        /// with failure and the supervision decision is <see cref="Supervision.Directive.Resume"/> or
        /// <see cref="Supervision.Directive.Restart"/> the element is dropped and the stream continues.
        /// <para>
        /// '''Emits when''' the Task returned by the provided function finishes for the next element in sequence
        /// </para>
        /// <para>
        /// '''Backpressures when''' the number of futures reaches the configured parallelism and the downstream
        /// backpressures or the first future is not completed
        /// </para>
        /// <para>
        /// '''Completes when''' upstream completes and all futures has been completed and all elements has been emitted
        /// </para>
        /// <para>
        /// '''Cancels when''' downstream cancels
        /// </para>
        /// </summary>
        public static Source<TOut, TMat> MapAsync<TIn, TOut, TMat>(this Source<TIn, TMat> flow, int parallelism, Func<TIn, Task<TOut>> asyncMapper)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.MapAsync(flow, parallelism, asyncMapper);
        }

        /// <summary>
        /// Transform this stream by applying the given function to each of the elements
        /// as they pass through this processing step. The function returns a <see cref="Task"/> and the
        /// value of that future will be emitted downstreams. As many futures as requested elements by
        /// downstream may run in parallel and each processed element will be emitted dowstream
        /// as soon as it is ready, i.e. it is possible that the elements are not emitted downstream
        /// in the same order as received from upstream.
        /// 
        /// If the group by function <paramref name="asyncMapper"/> throws an exception or if the <see cref="Task"/> is completed
        /// with failure and the supervision decision is <see cref="Stop"/>
        /// the stream will be completed with failure.
        /// 
        /// If the group by function <paramref name="asyncMapper"/> throws an exception or if the<see cref="Task"/> is completed
        /// with failure and the supervision decision is <see cref="Directive.Resume"/> or
        /// <see cref="Restart"/> the element is dropped and the stream continues.
        /// <para>
        /// '''Emits when''' any of the Futures returned by the provided function complete
        /// </para>
        /// <para>
        /// '''Backpressures when''' the number of futures reaches the configured parallelism and the downstream backpressures
        /// </para>
        /// <para>
        /// '''Completes when''' upstream completes and all futures has been completed and all elements has been emitted
        /// </para>
        /// <para>
        /// '''Cancels when''' downstream cancels
        /// </para>
        /// </summary>
        public static Source<TOut, TMat> MapAsyncUnordered<TIn, TOut, TMat>(this Source<TIn, TMat> flow, int parallelism, Func<TIn, Task<TOut>> asyncMapper)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.MapAsyncUnordered(flow, parallelism, asyncMapper);
        }

        /// <summary>
        /// Only pass on those elements that satisfy the given predicate.
        /// <para>
        /// '''Emits when''' the given predicate returns true for the element
        /// </para>
        /// <para>
        /// '''Backpressures when''' the given predicate returns true for the element and downstream backpressures
        /// </para>
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> Filter<TIn, TOut, TMat>(this Source<TOut, TMat> flow, Predicate<TOut> predicate)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Filter(flow, predicate);
        }

        /// <summary>
        /// Only pass on those elements that NOT satisfy the given predicate.
        /// <para>
        /// '''Emits when''' the given predicate returns true for the element
        /// </para>
        /// <para>
        /// '''Backpressures when''' the given predicate returns true for the element and downstream backpressures
        /// </para>
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> FilterNot<TIn, TOut, TMat>(this Source<TOut, TMat> flow, Predicate<TOut> predicate)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.FilterNot(flow, predicate);
        }

        /// <summary>
        /// Terminate processing (and cancel the upstream publisher) after predicate
        /// returns false for the first time. Due to input buffering some elements may have been
        /// requested from upstream publishers that will then not be processed downstream
        /// of this step.
        /// 
        /// The stream will be completed without producing any elements if predicate is false for
        /// the first stream element.
        /// <para>
        /// '''Emits when''' the predicate is true
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' predicate returned false or upstream completes
        /// </para>
        /// '''Cancels when''' predicate returned false or downstream cancels
        /// </summary>
        public static Source<TOut, TMat> TakeWhile<TIn, TOut, TMat>(this Source<TOut, TMat> flow, Predicate<TOut> predicate)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.TakeWhile(flow, predicate);
        }

        /// <summary>
        /// Discard elements at the beginning of the stream while predicate is true.
        /// All elements will be taken after predicate returns false first time.
        /// <para>
        /// '''Emits when''' predicate returned false and for all following stream elements
        /// </para>
        /// '''Backpressures when''' predicate returned false and downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> DropWhile<TIn, TOut, TMat>(this Source<TOut, TMat> flow, Predicate<TOut> predicate)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.DropWhile(flow, predicate);
        }

        /// <summary>
        /// Transform this stream by applying the given partial function to each of the elements
        /// on which the function is defined (read: returns not null) as they pass through this processing step.
        /// Non-matching elements are filtered out.
        /// <para>
        /// '''Emits when''' the provided partial function is defined for the element
        /// </para>
        /// '''Backpressures when''' the partial function is defined for the element and downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut2, TMat> Collect<TOut1, TOut2, TMat>(this Source<TOut1, TMat> flow, Func<TOut1, TOut2> collector) where TOut2 : class
        {
            return (Source<TOut2, TMat>)InternalFlowOperations.Collect(flow, collector);
        }

        /// <summary>
        /// Chunk up this stream into groups of the given size, with the last group
        /// possibly smaller than requested due to end-of-stream.
        /// <paramref name="n"/> must be positive, otherwise <see cref="ArgumentException"/> is thrown.
        /// <para>
        /// '''Emits when''' the specified number of elements has been accumulated or upstream completed
        /// </para>
        /// '''Backpressures when''' a group has been assembled and downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        /// <exception cref="ArgumentException">Thrown, if <paramref name="n"/> is less than or equal zero.</exception>
        public static Source<IEnumerable<TOut>, TMat> Grouped<TIn, TOut, TMat>(this Source<TOut, TMat> flow, int n)
        {
            return (Source<IEnumerable<TOut>, TMat>)InternalFlowOperations.Grouped(flow, n);
        }

        /// <summary>
        /// Apply a sliding window over the stream and return the windows as groups of elements, with the last group
        /// possibly smaller than requested due to end-of-stream.
        /// 
        /// <paramref name="n"/> must be positive, otherwise IllegalArgumentException is thrown.
        /// <paramref name="step"/> must be positive, otherwise IllegalArgumentException is thrown.
        /// <para>
        /// '''Emits when''' enough elements have been collected within the window or upstream completed
        /// </para>
        /// '''Backpressures when''' a window has been assembled and downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when <paramref name="n"/> or <paramref name="step"/> is less than or equal zero.</exception>
        public static Source<IEnumerable<TOut>, TMat> Sliding<TIn, TOut, TMat>(this Source<TOut, TMat> flow, int n, int step = 1)
        {
            return (Source<IEnumerable<TOut>, TMat>)InternalFlowOperations.Sliding(flow, n, step);
        }

        /// <summary>
        /// Similar to <see cref="Implementation.Fusing.Fold{TIn,TOut}"/> but is not a terminal operation,
        /// emits its current value which starts at <paramref name="zero"/> and then
        /// applies the current and next value to the given function <paramref name="scan"/>,
        /// emitting the next current value.
        /// 
        /// If the function <paramref name="scan"/> throws an exception and the supervision decision is
        /// <see cref="Directive.Restart"/> current value starts at <paramref name="zero"/> again
        /// the stream will continue.
        /// <para>
        /// '''Emits when''' the function scanning the element returns a new element
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut2, TMat> Scan<TIn, TOut1, TOut2, TMat>(this Source<TOut1, TMat> flow, TOut2 zero, Func<TOut2, TOut1, TOut2> scan)
        {
            return (Source<TOut2, TMat>)InternalFlowOperations.Scan(flow, zero, scan);
        }

        /// <summary>
        /// Similar to `scan` but only emits its result when the upstream completes,
        /// after which it also completes. Applies the given function towards its current and next value,
        /// yielding the next current value.
        /// 
        /// If the function `f` throws an exception and the supervision decision is
        /// [[akka.stream.Supervision.Restart]] current value starts at `zero` again
        /// the stream will continue.
        /// <para>
        /// '''Emits when''' upstream completes
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut2, TMat> Fold<TIn, TOut1, TOut2, TMat>(this Source<TOut1, TMat> flow, TOut2 zero, Func<TOut2, TOut1, TOut2> fold)
        {
            return (Source<TOut2, TMat>)InternalFlowOperations.Fold(flow, zero, fold);
        }

        /// <summary>
        /// Intersperses stream with provided element, similar to how <see cref="string.Join(string,string[])"/>
        /// injects a separator between a collection's elements.
        /// 
        /// Additionally can inject start and end marker elements to stream.
        /// 
        /// In case you want to only prepend or only append an element (yet still use the `intercept` feature
        /// to inject a separator between elements, you may want to use the following pattern instead of the 3-argument
        /// version of intersperse (See <see cref="Concat{T}"/> for semantics details). 
        /// <para>
        /// '''Emits when''' upstream emits (or before with the `start` element if provided)
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when any of the <paramref name="start"/>, <paramref name="inject"/> or <paramref name="end"/> is null.</exception>
        public static Source<TOut, TMat> Intersperse<TIn, TOut, TMat>(this Source<TOut, TMat> flow, TOut start, TOut inject, TOut end)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Intersperse(flow, start, inject, end);
        }

        /// <summary>
        /// Intersperses stream with provided element, similar to how <see cref="string.Join(string,string[])"/>
        /// injects a separator between a collection's elements.
        /// 
        /// Additionally can inject start and end marker elements to stream.
        /// 
        /// In case you want to only prepend or only append an element (yet still use the `intercept` feature
        /// to inject a separator between elements, you may want to use the following pattern instead of the 3-argument
        /// version of intersperse (See <see cref="Concat{T}"/> for semantics details). 
        /// <para>
        /// '''Emits when''' upstream emits (or before with the `start` element if provided)
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inject"/> is null.</exception>
        public static Source<TOut, TMat> Intersperse<TIn, TOut, TMat>(this Source<TOut, TMat> flow, TOut inject)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Intersperse(flow, inject);
        }

        /// <summary>
        /// Chunk up this stream into groups of elements received within a time window,
        /// or limited by the given number of elements, whatever happens first.
        /// Empty groups will not be emitted if no elements are received from upstream.
        /// The last group before end-of-stream will contain the buffered elements
        /// since the previously emitted group.
        /// 
        /// <paramref name="n"/> must be positive, and <paramref name="timeout"/> must be greater than 0 seconds, otherwise
        /// <see cref="ArgumentException"/> is thrown.
        /// <para>
        /// '''Emits when''' the configured time elapses since the last group has been emitted
        /// </para>
        /// '''Backpressures when''' the configured time elapses since the last group has been emitted
        /// <para>
        /// '''Completes when''' upstream completes (emits last group)
        /// </para>
        /// '''Cancels when''' downstream completes
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if <paramref name="n"/> is less than or equal zero or <paramref name="timeout"/> is <see cref="TimeSpan.Zero"/>.</exception>
        public static Source<IEnumerable<TOut>, TMat> GroupedWithin<TIn, TOut, TMat>(this Source<TOut, TMat> flow, int n, TimeSpan timeout)
        {
            return (Source<IEnumerable<TOut>, TMat>)InternalFlowOperations.GroupedWithin(flow, n, timeout);
        }

        /// <summary>
        /// Shifts elements emission in time by a specified amount. It allows to store elements
        /// in internal buffer while waiting for next element to be emitted. Depending on the defined
        /// <see cref="DelayOverflowStrategy"/> it might drop elements or backpressure the upstream if
        /// there is no space available in the buffer.
        /// 
        /// Delay precision is 10ms to avoid unnecessary timer scheduling cycles
        /// 
        /// Internal buffer has default capacity 16. You can set buffer size by calling <see cref="Attributes.CreateInputBuffer"/>
        /// <para>
        /// '''Emits when''' there is a pending element in the buffer and configured time for this element elapsed
        ///  * EmitEarly - strategy do not wait to emit element if buffer is full
        /// </para>
        /// '''Backpressures when''' depending on OverflowStrategy
        ///  * Backpressure - backpressures when buffer is full
        ///  * DropHead, DropTail, DropBuffer - never backpressures
        ///  * Fail - fails the stream if buffer gets full
        /// <para>
        /// '''Completes when''' upstream completes and buffered elements has been drained
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        /// <param name="of">Time to shift all messages.</param>
        /// <param name="strategy">Strategy that is used when incoming elements cannot fit inside the buffer</param>
        public static Source<TOut, TMat> Delay<TIn, TOut, TMat>(this Source<TOut, TMat> flow, TimeSpan of, DelayOverflowStrategy? strategy = null)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Delay(flow, of, strategy);
        }

        /// <summary>
        /// Discard the given number of elements at the beginning of the stream.
        /// No elements will be dropped if <paramref name="n"/> is zero or negative.
        /// <para>
        /// '''Emits when''' the specified number of elements has been dropped already
        /// </para>
        /// '''Backpressures when''' the specified number of elements has been dropped and downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> Drop<TIn, TOut, TMat>(this Source<TOut, TMat> flow, long n)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Drop(flow, n);
        }

        /// <summary>
        /// Discard the elements received within the given duration at beginning of the stream.
        /// <para>
        /// '''Emits when''' the specified time elapsed and a new upstream element arrives
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> DropWithin<TIn, TOut, TMat>(this Source<TOut, TMat> flow, TimeSpan duration)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.DropWithin(flow, duration);
        }

        /// <summary>
        /// Terminate processing (and cancel the upstream publisher) after the given
        /// number of elements. Due to input buffering some elements may have been
        /// requested from upstream publishers that will then not be processed downstream
        /// of this step.
        /// 
        /// The stream will be completed without producing any elements if <paramref name="n"/> is zero
        /// or negative.
        /// <para>
        /// '''Emits when''' the specified number of elements to take has not yet been reached
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' the defined number of elements has been taken or upstream completes
        /// </para>
        /// '''Cancels when''' the defined number of elements has been taken or downstream cancels
        /// </summary>
        public static Source<TOut, TMat> Take<TIn, TOut, TMat>(this Source<TOut, TMat> flow, long n)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Take(flow, n);
        }

        /// <summary>
        /// Terminate processing (and cancel the upstream publisher) after the given
        /// duration. Due to input buffering some elements may have been
        /// requested from upstream publishers that will then not be processed downstream
        /// of this step.
        /// 
        /// Note that this can be combined with <see cref="Take{T,TMat}"/> to limit the number of elements
        /// within the duration.
        /// <para>
        /// '''Emits when''' an upstream element arrives
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes or timer fires
        /// </para>
        /// '''Cancels when''' downstream cancels or timer fires
        /// </summary>
        public static Source<TOut, TMat> TakeWithin<TIn, TOut, TMat>(this Source<TOut, TMat> flow, TimeSpan duration)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.TakeWithin(flow, duration);
        }

        /// <summary>
        /// Allows a faster upstream to progress independently of a slower subscriber by conflating elements into a summary
        /// until the subscriber is ready to accept them. For example a conflate step might average incoming numbers if the
        /// upstream publisher is faster.
        /// 
        /// This version of conflate allows to derive a seed from the first element and change the aggregated type to
        /// be different than the input type. See <see cref="Conflate{TIn,TOut,TMat}"/> for a simpler version that does not change types.
        /// 
        /// This element only rolls up elements if the upstream is faster, but if the downstream is faster it will not
        /// duplicate elements.
        /// <para>
        /// '''Emits when''' downstream stops backpressuring and there is a conflated element available
        /// </para>
        /// '''Backpressures when''' never
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        /// <param name="seed">Provides the first state for a conflated value using the first unconsumed element as a start</param> 
        /// <param name="aggregate">Takes the currently aggregated value and the current pending element to produce a new aggregate</param>
        public static Source<TSeed, TMat> ConflateWithSeed<TIn, TOut, TMat, TSeed>(this Source<TOut, TMat> flow, Func<TOut, TSeed> seed, Func<TSeed, TOut, TSeed> aggregate)
        {
            return (Source<TSeed, TMat>)InternalFlowOperations.ConflateWithSeed(flow, seed, aggregate);
        }

        /// <summary>
        /// Allows a faster upstream to progress independently of a slower subscriber by conflating elements into a summary
        /// until the subscriber is ready to accept them. For example a conflate step might average incoming numbers if the
        /// upstream publisher is faster.
        /// 
        /// This version of conflate does not change the output type of the stream. See <see cref="ConflateWithSeed{TIn,TOut,TMat,TSeed}"/>
        /// for a more flexible version that can take a seed function and transform elements while rolling up.
        /// 
        /// This element only rolls up elements if the upstream is faster, but if the downstream is faster it will not
        /// duplicate elements.
        /// <para>
        /// '''Emits when''' downstream stops backpressuring and there is a conflated element available
        /// </para>
        /// '''Backpressures when''' never
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        /// <param name="seed">Provides the first state for a conflated value using the first unconsumed element as a start</param> 
        /// <param name="aggregate">Takes the currently aggregated value and the current pending element to produce a new aggregate</param>
        public static Source<TOut, TMat> Conflate<TIn, TOut, TMat>(this Source<TOut, TMat> flow, Func<TOut, TOut, TOut> aggregate)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Conflate(flow, aggregate);
        }

        /// <summary>
        /// Allows a faster downstream to progress independently of a slower publisher by extrapolating elements from an older
        /// element until new element comes from the upstream. For example an expand step might repeat the last element for
        /// the subscriber until it receives an update from upstream.
        /// 
        /// This element will never "drop" upstream elements as all elements go through at least one extrapolation step.
        /// This means that if the upstream is actually faster than the upstream it will be backpressured by the downstream
        /// subscriber.
        /// 
        /// Expand does not support <see cref="Restart"/> and <see cref="Directive.Resume"/>.
        /// Exceptions from the <paramref name="seed"/> or <paramref name="extrapolate"/> functions will complete the stream with failure.
        /// <para>
        /// '''Emits when''' downstream stops backpressuring
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        /// <param name="extrapolate">Takes the current extrapolation state to produce an output element and the next extrapolation state.</param>
        public static Source<TOut2, TMat> Expand<TIn, TOut1, TOut2, TMat>(this Source<TOut1, TMat> flow, Func<TOut1, IEnumerator<TOut2>> extrapolate)
        {
            return (Source<TOut2, TMat>)InternalFlowOperations.Expand<TOut1, TOut2, TMat>(flow, extrapolate);
        }

        /// <summary>
        /// Adds a fixed size buffer in the flow that allows to store elements from a faster upstream until it becomes full.
        /// Depending on the defined <see cref="OverflowStrategy"/> it might drop elements or backpressure the upstream if
        /// there is no space available
        /// <para>
        /// '''Emits when''' downstream stops backpressuring and there is a pending element in the buffer
        /// </para>
        /// '''Backpressures when''' depending on OverflowStrategy
        ///  * Backpressure - backpressures when buffer is full
        ///  * DropHead, DropTail, DropBuffer - never backpressures
        ///  * Fail - fails the stream if buffer gets full
        /// <para>
        /// '''Completes when''' upstream completes and buffered elements has been drained
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        /// <param name="size">The size of the buffer in element count</param>
        /// <param name="strategy">Strategy that is used when incoming elements cannot fit inside the buffer</param>
        public static Source<TOut, TMat> Buffer<TIn, TOut, TMat>(this Source<TOut, TMat> flow, int size, OverflowStrategy strategy)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Buffer(flow, size, strategy);
        }

        /// <summary>
        /// Generic transformation of a stream with a custom processing <see cref="IStage{TIn,TOut}"/>.
        /// This operator makes it possible to extend the <see cref="Flow"/> API when there is no specialized
        /// operator that performs the transformation.
        /// </summary>
        public static Source<TOut2, TMat> Transform<TIn, TOut1, TOut2, TMat>(this Source<TOut1, TMat> flow, Func<IStage<TOut1, TOut2>> stageFactory)
        {
            return (Source<TOut2, TMat>)InternalFlowOperations.Transform(flow, stageFactory);
        }

        /// <summary>
        /// Takes up to <paramref name="n"/> elements from the stream and returns a pair containing a strict sequence of the taken element
        /// and a stream representing the remaining elements. If <paramref name="n"/> is zero or negative, then this will return a pair
        /// of an empty collection and a stream containing the whole upstream unchanged.
        /// <para>
        /// '''Emits when''' the configured number of prefix elements are available. Emits this prefix, and the rest
        /// as a substream
        /// </para>
        /// '''Backpressures when''' downstream backpressures or substream backpressures
        /// <para>
        /// '''Completes when''' prefix elements has been consumed and substream has been consumed
        /// </para>
        /// '''Cancels when''' downstream cancels or substream cancels
        /// </summary> 
        public static Source<Tuple<IEnumerable<TOut>, Source<TOut, Unit>>, TMat> PrefixAndTail<TIn, TOut, TMat>(this Source<TOut, TMat> flow, int n)
        {
            return (Source<Tuple<IEnumerable<TOut>, Source<TOut, Unit>>, TMat>)InternalFlowOperations.PrefixAndTail(flow, n);
        }

        /// <summary>
        /// This operation demultiplexes the incoming stream into separate output
        /// streams, one for each element key. The key is computed for each element
        /// using the given function. When a new key is encountered for the first time
        /// it is emitted to the downstream subscriber together with a fresh
        /// flow that will eventually produce all the elements of the substream
        /// for that key. Not consuming the elements from the created streams will
        /// stop this processor from processing more elements, therefore you must take
        /// care to unblock (or cancel) all of the produced streams even if you want
        /// to consume only one of them.
        /// 
        /// If the group by function <paramref name="groupingFunc"/> throws an exception and the supervision decision
        /// is <see cref="Stop"/> the stream and substreams will be completed
        /// with failure.
        /// 
        /// If the group by <paramref name="groupingFunc"/> throws an exception and the supervision decision
        /// is <see cref="Directive.Resume"/> or <see cref="Restart"/>
        /// the element is dropped and the stream and substreams continue.
        /// <para>
        /// '''Emits when''' an element for which the grouping function returns a group that has not yet been created.
        /// Emits the new group
        /// </para>
        /// '''Backpressures when''' there is an element pending for a group whose substream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels and all substreams cancel
        /// </summary> 
        public static Source<KeyValuePair<TKey, Source<TVal, TMat>>, TMat> GroupBy<TIn, TOut, TMat, TKey, TVal>(this Source<TOut, TMat> flow, Func<TOut, TKey> groupingFunc) where TVal : TOut
        {
            return (Source<KeyValuePair<TKey, Source<TVal, TMat>>, TMat>)InternalFlowOperations.GroupBy<TOut, TMat, TKey, TVal>(flow, groupingFunc);
        }

        /// <summary>
        /// This operation applies the given predicate to all incoming elements and
        /// emits them to a stream of output streams, always beginning a new one with
        /// the current element if the given predicate returns true for it. This means
        /// that for the following series of predicate values, three substreams will
        /// be produced with lengths 1, 2, and 3:
        /// 
        /// {{{
        /// false,             // element goes into first substream
        /// true, false,       // elements go into second substream
        /// true, false, false // elements go into third substream
        /// }}}
        /// 
        /// In case the *first* element of the stream matches the predicate, the first
        /// substream emitted by splitWhen will start from that element. For example:
        /// 
        /// {{{
        /// true, false, false // first substream starts from the split-by element
        /// true, false        // subsequent substreams operate the same way
        /// }}}
        /// 
        /// The object returned from this method is not a normal [[Source]] or [[Flow]],
        /// it is a [[SubFlow]]. This means that after this combinator all transformations
        /// are applied to all encountered substreams in the same fashion. Substream mode
        /// is exited either by closing the substream (i.e. connecting it to a [[Sink]])
        /// or by merging the substreams back together; see the `to` and `mergeBack` methods
        /// on [[SubFlow]] for more information.
        /// 
        /// It is important to note that the substreams also propagate back-pressure as
        /// any other stream, which means that blocking one substream will block the `splitWhen`
        /// operator itself—and thereby all substreams—once all internal or
        /// explicit buffers are filled.
        /// 
        /// If the split <paramref name="predicate"/> throws an exception and the supervision decision
        /// is <see cref="Stop"/> the stream and substreams will be completed
        /// with failure.
        /// 
        /// If the split <paramref name="predicate"/> throws an exception and the supervision decision
        /// is <see cref="Directive.Resume"/> or <see cref="Restart"/>
        /// the element is dropped and the stream and substreams continue.
        /// <para>
        /// '''Emits when''' an element for which the provided predicate is true, opening and emitting
        /// a new substream for subsequent element
        /// </para>
        /// '''Backpressures when''' there is an element pending for the next substream, but the previous
        /// is not fully consumed yet, or the substream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels and substreams cancel
        /// </summary>
        public static Source<Source<TVal, TMat>, TMat> SplitWhen<TIn, TOut, TMat, TVal>(this Source<TOut, TMat> flow, Predicate<TOut> predicate) where TVal : TOut
        {
            return (Source<Source<TVal, TMat>, TMat>)InternalFlowOperations.SplitWhen<TOut, TMat, TVal>(flow, predicate);
        }

        /// <summary>
        /// This operation applies the given predicate to all incoming elements and
        /// emits them to a stream of output streams.It* ends* the current substream when the
        /// predicate is true. This means that for the following series of predicate values,
        /// three substreams will be produced with lengths 2, 2, and 3:
        ///
        /// {{{
        /// false, true,        // elements go into first substream
        /// false, true,        // elements go into second substream
        /// false, false, true  // elements go into third substream
        /// }}}
        ///
        /// The object returned from this method is not a normal[[Source]] or[[Flow]],
        /// it is a[[SubFlow]]. This means that after this combinator all transformations
        ///are applied to all encountered substreams in the same fashion.Substream mode
        /// is exited either by closing the substream(i.e.connecting it to a [[Sink]])
        /// or by merging the substreams back together; see the `to` and `mergeBack` methods
        /// on[[SubFlow]] for more information.
        ///
        /// It is important to note that the substreams also propagate back-pressure as
        /// any other stream, which means that blocking one substream will block the `splitAfter`
        /// operator itself—and thereby all substreams—once all internal or
        /// explicit buffers are filled.
        ///
        /// If the split <paramref name="predicate"/> throws an exception and the supervision decision
        /// is <see cref="Stop"/> the stream and substreams will be completed
        /// with failure.
        ///
        /// If the split <paramref name="predicate"/> throws an exception and the supervision decision
        /// is <see cref="Directive.Resume"/> or <see cref="Restart"/>
        /// the element is dropped and the stream and substreams continue.
        /// <para>
        /// '''Emits when''' an element passes through.When the provided predicate is true it emitts the element
        /// and opens a new substream for subsequent element
        /// </para>
        /// '''Backpressures when''' there is an element pending for the next substream, but the previous
        /// is not fully consumed yet, or the substream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels and substreams cancel
        /// </summary>
        public static Source<Source<TVal, TMat>, TMat> SplitAfter<TIn, TOut, TMat, TVal>(this Source<TOut, TMat> flow, Predicate<TOut> predicate) where TVal : TOut
        {
            return (Source<Source<TVal, TMat>, TMat>)InternalFlowOperations.SplitAfter<TOut, TMat, TVal>(flow, predicate);
        }

        /// <summary>
        /// Transform each input element into a `Source` of output elements that is
        /// then flattened into the output stream by concatenation,
        /// fully consuming one Source after the other.
        /// <para>
        /// '''Emits when''' a currently consumed substream has an element available
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes and all consumed substreams complete
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut2, TMat> FlatMapConcat<TIn, TOut1, TOut2, TMat>(this Source<TOut1, TMat> flow, Func<TOut1, IGraph<SourceShape<TOut2>, TMat>> flatten)
        {
            return (Source<TOut2, TMat>)InternalFlowOperations.FlatMapConcat(flow, flatten);
        }

        /// <summary>
        /// Transform each input element into a `Source` of output elements that is
        /// then flattened into the output stream by merging, where at most <paramref name="breadth"/>
        /// substreams are being consumed at any given time.
        /// <para>
        /// '''Emits when''' a currently consumed substream has an element available
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes and all consumed substreams complete
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut2, TMat> FlatMapMerge<TIn, TOut1, TOut2, TMat>(this Source<TOut1, TMat> flow, int breadth, Func<TOut1, IGraph<SourceShape<TOut2>, TMat>> flatten)
        {
            return (Source<TOut2, TMat>)InternalFlowOperations.FlatMapMerge(flow, breadth, flatten);
        }

        /// <summary>
        /// If the first element has not passed through this stage before the provided timeout, the stream is failed
        /// with a <see cref="TimeoutException"/>.
        /// <para>
        /// '''Emits when''' upstream emits an element
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes or fails if timeout elapses before first element arrives
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> InitialTimeout<TIn, TOut, TMat>(this Source<TOut, TMat> flow, TimeSpan timeout)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.InitialTimeout(flow, timeout);
        }

        /// <summary>
        /// If the completion of the stream does not happen until the provided timeout, the stream is failed
        /// with a <see cref="TimeoutException"/>.
        /// <para>
        /// '''Emits when''' upstream emits an element
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes or fails if timeout elapses before upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> CompletionTimeout<TIn, TOut, TMat>(this Source<TOut, TMat> flow, TimeSpan timeout)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.CompletionTimeout(flow, timeout);
        }

        /// <summary>
        /// If the time between two processed elements exceed the provided timeout, the stream is failed
        /// with a <see cref="TimeoutException"/>.
        /// <para>
        /// '''Emits when''' upstream emits an element
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes or fails if timeout elapses between two emitted elements
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> IdleTimeout<TIn, TOut, TMat>(this Source<TOut, TMat> flow, TimeSpan timeout)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.IdleTimeout(flow, timeout);
        }

        /// <summary>
        /// Injects additional elements if the upstream does not emit for a configured amount of time. In other words, this
        /// stage attempts to maintains a base rate of emitted elements towards the downstream.
        /// 
        /// If the downstream backpressures then no element is injected until downstream demand arrives. Injected elements
        /// do not accumulate during this period.
        /// 
        /// Upstream elements are always preferred over injected elements.
        /// <para>
        /// '''Emits when''' upstream emits an element or if the upstream was idle for the configured period
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TInjected, TMat> KeepAlive<TIn, TOut, TInjected, TMat>(this Source<TOut, TMat> flow, TimeSpan timeout, Func<TInjected> injectElement) where TOut : TInjected
        {
            return (Source<TInjected, TMat>)InternalFlowOperations.KeepAlive(flow, timeout, injectElement);
        }

        /// <summary>
        /// Sends elements downstream with speed limited to `<paramref name="elements"/>/<paramref name="per"/>`. In other words, this stage set the maximum rate
        /// for emitting messages. This combinator works for streams where all elements have the same cost or length.
        /// 
        /// Throttle implements the token bucket model. There is a bucket with a given token capacity (burst size or maximumBurst).
        /// Tokens drops into the bucket at a given rate and can be "spared" for later use up to bucket capacity
        /// to allow some burstyness. Whenever stream wants to send an element, it takes as many
        /// tokens from the bucket as number of elements. If there isn't any, throttle waits until the
        /// bucket accumulates enough tokens.
        /// 
        /// Parameter <paramref name="mode"/> manages behaviour when upstream is faster than throttle rate:
        ///  - <see cref="ThrottleMode.Shaping"/> makes pauses before emitting messages to meet throttle rate
        ///  - <see cref="ThrottleMode.Enforcing"/> fails with exception when upstream is faster than throttle rate. Enforcing
        ///  cannot emit elements that cost more than the maximumBurst
        /// <para>
        /// '''Emits when''' upstream emits an element and configured time per each element elapsed
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        /// <exception cref="ArgumentException">Thow when <paramref name="elements"/> is less than or equal zero, 
        /// or <paramref name="per"/> timeout is equal <see cref="TimeSpan.Zero"/> 
        /// or <paramref name="maximumBurst"/> is less than or equal zero in in <see cref="ThrottleMode.Enforcing"/> <paramref name="mode"/>.</exception>
        public static Source<TOut, TMat> Throttle<TIn, TOut, TMat>(this Source<TOut, TMat> flow, int elements, TimeSpan per, int maximumBurst, ThrottleMode mode)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Throttle(flow, elements, per, maximumBurst, mode);
        }

        /// <summary>
        /// Sends elements downstream with speed limited to `<paramref name="cost"/>/<paramref name="per"/>`. Cost is
        /// calculating for each element individually by calling <paramref name="calculateCost"/> function.
        /// This combinator works for streams when elements have different cost(length).
        /// Streams of <see cref="ByteString"/> for example.
        /// 
        /// Throttle implements the token bucket model. There is a bucket with a given token capacity (burst size or maximumBurst).
        /// Tokens drops into the bucket at a given rate and can be `spared` for later use up to bucket capacity
        /// to allow some burstyness. Whenever stream wants to send an element, it takes as many
        /// tokens from the bucket as element cost. If there isn't any, throttle waits until the
        /// bucket accumulates enough tokens. Elements that costs more than the allowed burst will be delayed proportionally
        /// to their cost minus available tokens, meeting the target rate.
        /// 
        /// Parameter <paramref name="mode"/> manages behaviour when upstream is faster than throttle rate:
        ///  - <see cref="ThrottleMode.Shaping"/> makes pauses before emitting messages to meet throttle rate
        ///  - <see cref="ThrottleMode.Enforcing"/> fails with exception when upstream is faster than throttle rate. Enforcing
        ///  cannot emit elements that cost more than the maximumBurst
        /// <para>
        /// '''Emits when''' upstream emits an element and configured time per each element elapsed
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> Throttle<TIn, TOut, TMat>(this Source<TOut, TMat> flow, int cost, TimeSpan per, int maximumBurst, Func<TOut, int> calculateCost, ThrottleMode mode)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Throttle(flow, cost, per, maximumBurst, calculateCost, mode);
        }

        /// <summary>
        /// Delays the initial element by the specified duration.
        /// <para>
        /// '''Emits when''' upstream emits an element if the initial delay already elapsed
        /// </para>
        /// '''Backpressures when''' downstream backpressures or initial delay not yet elapsed
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> InitialDelay<TIn, TOut, TMat>(this Source<TOut, TMat> flow, TimeSpan delay)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.InitialDelay(flow, delay);
        }

        /// <summary>
        /// Logs elements flowing through the stream as well as completion and erroring.
        /// 
        /// By default element and completion signals are logged on debug level, and errors are logged on Error level.
        /// This can be adjusted according to your needs by providing a custom <see cref="Attributes.LogLevels"/> attribute on the given Flow.
        /// <para>
        /// '''Emits when''' the mapping function returns an element
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> Log<TIn, TOut, TMat>(this Source<TOut, TMat> flow, string name, ILoggingAdapter log, Func<TOut, object> extract = null)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.Log(flow, name, log, extract);
        }

        /// <summary>
        /// Combine the elements of current flow and the given [[Source]] into a stream of tuples.
        /// <para>
        /// '''Emits when''' all of the inputs has an element available
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' any upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<KeyValuePair<T1, T2>, TMat> Zip<TIn, T1, T2, TMat>(this Source<T1, TMat> flow, IGraph<SourceShape<T2>, TMat> other)
        {
            return (Source<KeyValuePair<T1, T2>, TMat>)InternalFlowOperations.Zip(flow, other);
        }

        /// <summary>
        /// Put together the elements of current flow and the given [[Source]]
        /// into a stream of combined elements using a combiner function.
        /// <para>
        /// '''Emits when''' all of the inputs has an element available
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' any upstream completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<T3, TMat> ZipWith<TIn, T1, T2, T3, TMat>(this Source<T1, TMat> flow, IGraph<SourceShape<T2>, TMat> other, Func<T1, T2, T3> combine)
        {
            return (Source<T3, TMat>)InternalFlowOperations.ZipWith(flow, other, combine);
        }

        /// <summary>
        /// Interleave is a deterministic merge of the given [[Source]] with elements of this [[Flow]].
        /// It first emits <paramref name="segmentSize"/> number of elements from this flow to downstream, then - same amount for <paramref name="other"/>
        /// source, then repeat process.
        ///  
        /// After one of upstreams is complete than all the rest elements will be emitted from the second one
        /// 
        /// If it gets error from one of upstreams - stream completes with failure.
        /// <para>
        /// '''Emits when''' element is available from the currently consumed upstream
        /// </para>
        /// '''Backpressures when''' downstream backpressures. Signal to current
        /// upstream, switch to next upstream when received `segmentSize` elements
        /// <para>
        /// '''Completes when''' the [[Flow]] and given [[Source]] completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        /// <example>
        /// <code>
        /// Source(List(1, 2, 3)).Interleave(List(4, 5, 6, 7), 2) // 1, 2, 4, 5, 3, 6, 7
        /// </code>
        /// </example>
        public static Source<T2, TMat> Interleave<TIn, T1, T2, TMat>(this Source<T1, TMat> flow, IGraph<SourceShape<T2>, TMat> other, int segmentSize) where T1 : T2
        {
            return (Source<T2, TMat>)InternalFlowOperations.Interleave(flow, other, segmentSize);
        }

        /// <summary>
        /// Merge the given [[Source]] to this [[Flow]], taking elements as they arrive from input streams,
        /// picking randomly when several elements ready.
        /// <para>
        /// '''Emits when''' one of the inputs has an element available
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' all upstreams complete
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut2, TMat> Merge<TIn, TOut1, TOut2, TMat>(this Source<TOut1, TMat> flow, IGraph<SourceShape<TOut2>, TMat> other) where TOut1 : TOut2
        {
            return (Source<TOut2, TMat>)InternalFlowOperations.Merge(flow, other);
        }

        /// <summary>
        /// Merge the given [[Source]] to this [[Flow]], taking elements as they arrive from input streams,
        /// picking always the smallest of the available elements(waiting for one element from each side
        /// to be available). This means that possible contiguity of the input streams is not exploited to avoid
        /// waiting for elements, this merge will block when one of the inputs does not have more elements(and
        /// does not complete).
        /// <para>
        /// '''Emits when''' one of the inputs has an element available
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' all upstreams complete
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> MergeOrdered<TOut, TMat>(this Source<TOut, TMat> flow, IGraph<SourceShape<TOut>, TMat> other, Func<TOut, TOut, int> orderFunc)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.MergeOrdered(flow, other, orderFunc);
        }

        /// <summary>
        /// Merge the given [[Source]] to this [[Flow]], taking elements as they arrive from input streams,
        /// picking always the smallest of the available elements(waiting for one element from each side
        /// to be available). This means that possible contiguity of the input streams is not exploited to avoid
        /// waiting for elements, this merge will block when one of the inputs does not have more elements(and
        /// does not complete).
        /// <para>
        /// '''Emits when''' one of the inputs has an element available
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' all upstreams complete
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> MergeOrdered<TOut, TMat>(this Source<TOut, TMat> flow, IGraph<SourceShape<TOut>, TMat> other)
            where TOut : IComparable<TOut>
        {
            return (Source<TOut, TMat>)InternalFlowOperations.MergeOrdered(flow, other);
        }

        /// <summary>
        /// Merge the given [[Source]] to this [[Flow]], taking elements as they arrive from input streams,
        /// picking always the smallest of the available elements(waiting for one element from each side
        /// to be available). This means that possible contiguity of the input streams is not exploited to avoid
        /// waiting for elements, this merge will block when one of the inputs does not have more elements(and
        /// does not complete).
        /// <para>
        /// '''Emits when''' one of the inputs has an element available
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' all upstreams complete
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut, TMat> MergeOrdered<TOut, TMat>(this Source<TOut, TMat> flow, IGraph<SourceShape<TOut>, TMat> other, IComparer<TOut> comparer)
        {
            return (Source<TOut, TMat>)InternalFlowOperations.MergeOrdered(flow, other, comparer);
        }

        /// <summary>
        /// Concatenate the given [[Source]] to this [[Flow]], meaning that once this
        /// Flow’s input is exhausted and all result elements have been generated,
        /// the Source’s elements will be produced.
        /// 
        /// Note that the [[Source]] is materialized together with this Flow and just kept
        /// from producing elements by asserting back-pressure until its time comes.
        /// 
        /// If this [[Flow]] gets upstream error - no elements from the given [[Source]] will be pulled.
        /// <para>
        /// '''Emits when''' element is available from current stream or from the given [[Source]] when current is completed
        /// </para>
        /// '''Backpressures when''' downstream backpressures
        /// <para>
        /// '''Completes when''' given [[Source]] completes
        /// </para>
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static Source<TOut2, TMat> Concat<TOut1, TOut2, TMat>(this Source<TOut1, TMat> flow, IGraph<SourceShape<TOut2>, TMat> other) where TOut1 : TOut2
        {
            return (Source<TOut2, TMat>)InternalFlowOperations.Concat(flow, other);
        }
    }
}