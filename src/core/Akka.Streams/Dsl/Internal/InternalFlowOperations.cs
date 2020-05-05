//-----------------------------------------------------------------------
// <copyright file="InternalFlowOperations.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.IO;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Dsl.Internal
{
    using Fusing = Implementation.Fusing;

    /// <summary>
    /// TBD
    /// </summary>
    internal static class InternalFlowOperations
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        internal static Func<T, object> Identity<T>() => arg => arg;

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="op">TBD</param>
        /// <returns>TBD</returns>
        internal static IFlow<TOut2, TMat> AndThen<TOut2, TOut, TMat>(this IFlow<TOut, TMat> flow,
            SymbolicStage<TOut, TOut2> op)
        {
            return flow.Via(new SymbolicGraphStage<TOut, TOut2>(op));
        }

        /// <summary>
        /// Recover allows to send last element on failure and gracefully complete the stream
        /// Since the underlying failure signal onError arrives out-of-band, it might jump over existing elements.
        /// This stage can recover the failure signal, but not the skipped elements, which will be dropped.
        /// <para/>
        /// Throwing an exception inside Recover will be logged on ERROR level automatically.
        /// <para>
        /// Emits when element is available from the upstream or upstream is failed and <paramref name="partialFunc"/> returns an element
        /// </para>
        /// <para>
        /// Backpressures when downstream backpressures
        /// </para>
        /// <para>
        /// Completes when upstream completes or upstream failed with exception pf can handle
        /// </para>
        /// Cancels when downstream cancels 
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="partialFunc">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> Recover<TOut, TMat>(this IFlow<TOut, TMat> flow,
            Func<Exception, Option<TOut>> partialFunc)
        {
            return flow.Via(new Fusing.Recover<TOut>(partialFunc));
        }

        /// <summary>
        /// RecoverWith allows to switch to alternative Source on flow failure. It will stay in effect after
        /// a failure has been recovered so that each time there is a failure it is fed into the <paramref name="partialFunc"/> and a new
        /// Source may be materialized.
        /// <para>
        /// Since the underlying failure signal onError arrives out-of-band, it might jump over existing elements.
        /// This stage can recover the failure signal, but not the skipped elements, which will be dropped.
        /// </para>
        /// Throwing an exception inside RecoverWith will be logged on ERROR level automatically.
        /// <para>
        /// Emits when element is available from the upstream or upstream is failed and element is available from alternative Source
        /// </para>
        /// <para>
        /// Backpressures when downstream backpressures
        /// </para>
        /// <para>
        /// Completes when upstream completes or upstream failed with exception <paramref name="partialFunc"/> can handle
        /// </para>
        /// Cancels when downstream cancels 
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="partialFunc">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Use RecoverWithRetries instead. [1.1.2]")]
        public static IFlow<TOut, TMat> RecoverWith<TOut, TMat>(this IFlow<TOut, TMat> flow,
            Func<Exception, IGraph<SourceShape<TOut>, TMat>> partialFunc)
        {
            return RecoverWithRetries(flow, partialFunc, -1);
        }

        /// <summary>
        /// RecoverWithRetries allows to switch to alternative Source on flow failure. It will stay in effect after
        /// a failure has been recovered up to <paramref name="attempts"/> number of times so that each time there is a failure it is fed into the <paramref name="partialFunc"/> and a new
        /// Source may be materialized. Note that if you pass in 0, this won't attempt to recover at all. Passing in -1 will behave exactly the same as  <see cref="RecoverWith{TOut,TMat}"/>.
        /// <para>
        /// Since the underlying failure signal onError arrives out-of-band, it might jump over existing elements.
        /// This stage can recover the failure signal, but not the skipped elements, which will be dropped.
        /// </para>
        /// Throwing an exception inside RecoverWithRetries will be logged on ERROR level automatically.
        /// <para>
        /// Emits when element is available from the upstream or upstream is failed and element is available from alternative Source
        /// </para>
        /// <para>
        /// Backpressures when downstream backpressures
        /// </para>
        /// <para>
        /// Completes when upstream completes or upstream failed with exception <paramref name="partialFunc"/> can handle
        /// </para>
        /// Cancels when downstream cancels 
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="partialFunc">Receives the failure cause and returns the new Source to be materialized if any</param>
        /// <param name="attempts">Maximum number of retries or -1 to retry indefinitely</param>
        /// <exception cref="ArgumentException">if <paramref name="attempts"/> is a negative number other than -1</exception>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> RecoverWithRetries<TOut, TMat>(this IFlow<TOut, TMat> flow,
            Func<Exception, IGraph<SourceShape<TOut>, TMat>> partialFunc, int attempts)
        {
            return flow.Via(new Fusing.RecoverWith<TOut, TMat>(partialFunc, attempts));
        }

        /// <summary>
        /// While similar to <see cref="Recover{TOut,TMat}"/> this stage can be used to transform an error signal to a different one without logging
        /// it as an error in the process. So in that sense it is NOT exactly equivalent to Recover(e => throw e2) since Recover
        /// would log the e2 error. 
        /// <para>
        /// Since the underlying failure signal onError arrives out-of-band, it might jump over existing elements.
        /// This stage can recover the failure signal, but not the skipped elements, which will be dropped.
        /// </para>
        /// Similarily to <see cref="Recover{TOut,TMat}"/> throwing an exception inside SelectError will be logged.
        /// <para>
        /// Emits when element is available from the upstream or upstream is failed and <paramref name="selector"/> returns an element
        /// </para>
        /// <para>
        /// Backpressures when downstream backpressures
        /// </para>
        /// <para>
        /// Completes when upstream completes or upstream failed with exception returned by the <paramref name="selector"/>
        /// </para>
        /// Cancels when downstream cancels 
        /// </summary>
        /// <param name="flow">TBD</param>
        /// <param name="selector">Receives the failure cause and returns the new cause, return the original exception if no other should be applied</param>
        public static IFlow<TOut, TMat> SelectError<TOut, TMat>(this IFlow<TOut, TMat> flow, Func<Exception, Exception> selector)
        {
            return flow.Via(new Fusing.SelectError<TOut>(selector));
        }

        /// <summary>
        /// Transform this stream by applying the given <paramref name="mapper"/> function to each of the elements
        /// as they pass through this processing step.
        /// <para>
        /// Emits when the mapping function <paramref name="mapper"/> returns an element
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// <para>
        /// Cancels when downstream cancels
        /// </para>
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="mapper">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> Select<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow, Func<TIn, TOut> mapper)
        {
            return flow.Via(new Fusing.Select<TIn, TOut>(mapper));
        }

        /// <summary>
        /// Transform each input element into a sequence of output elements that is
        /// then flattened into the output stream.
        /// 
        /// The returned sequence MUST NOT contain null values,
        /// as they are illegal as stream elements - according to the Reactive Streams specification.
        /// <para>
        /// Emits when the mapping function <paramref name="mapConcater"/> returns an element or there are still remaining elements
        /// from the previously calculated collection
        /// </para>
        /// <para>
        /// Backpressures when downstream backpressures or there are still remaining elements from the
        /// previously calculated collection
        /// </para>
        /// <para>
        /// Completes when upstream completes and all remaining elements has been emitted
        /// </para>
        /// <para>
        /// Cancels when downstream cancels
        /// </para>
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="mapConcater">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> SelectMany<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow,
            Func<TIn, IEnumerable<TOut>> mapConcater)
        {
            return StatefulSelectMany(flow, () => mapConcater);
        }

        /// <summary>
        /// Transform each input element into an Enumerable of output elements that is
        /// then flattened into the output stream. The transformation is meant to be stateful,
        /// which is enabled by creating the transformation function <paramref name="mapConcaterFactory"/> a new for every materialization —
        /// the returned function will typically close over mutable objects to store state between
        /// invocations. For the stateless variant see <see cref="SelectMany{TIn,TOut,TMat}"/>.
        /// 
        /// The returned Enumerable MUST NOT contain null values,
        /// as they are illegal as stream elements - according to the Reactive Streams specification.
        /// 
        /// <para>
        /// Emits when the mapping function returns an element or there are still remaining elements
        /// from the previously calculated collection
        /// </para>
        /// <para>
        /// Backpressures when downstream backpressures or there are still remaining elements from the
        /// previously calculated collection
        /// </para>
        /// <para>
        /// Completes when upstream completes and all remaining elements has been emitted
        /// </para>
        /// <para>
        /// Cancels when downstream cancels
        /// </para>
        /// See also <see cref="SelectMany{TIn,TOut,TMat}"/>
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="mapConcaterFactory">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> StatefulSelectMany<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow,
            Func<Func<TIn, IEnumerable<TOut>>> mapConcaterFactory)
        {
            return flow.Via(new Fusing.StatefulSelectMany<TIn, TOut>(mapConcaterFactory));
        }

        /// <summary>
        /// Transform this stream by applying the given function <paramref name="asyncMapper"/> to each of the elements
        /// as they pass through this processing step. The function returns a <see cref="Task{TOut}"/> and the
        /// value of that task will be emitted downstream. The number of tasks
        /// that shall run in parallel is given as the first argument to <see cref="SelectAsync{TIn,TOut,TMat}"/>.
        /// These tasks may complete in any order, but the elements that
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
        /// Emits when the task returned by the provided function finishes for the next element in sequence
        /// </para>
        /// <para>
        /// Backpressures when the number of tasks reaches the configured parallelism and the downstream
        /// backpressures or the first task is not completed
        /// </para>
        /// <para>
        /// Completes when upstream completes and all tasks has been completed and all elements has been emitted
        /// </para>
        /// <para>
        /// Cancels when downstream cancels
        /// </para>
        /// </summary>
        /// <seealso cref="SelectAsyncUnordered{TIn,TOut,TMat}"/>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="parallelism">TBD</param>
        /// <param name="asyncMapper">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> SelectAsync<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow, int parallelism,
            Func<TIn, Task<TOut>> asyncMapper)
        {
            return flow.Via(new Fusing.SelectAsync<TIn, TOut>(parallelism, asyncMapper));
        }

        /// <summary>
        /// Transform this stream by applying the given function <paramref name="asyncMapper"/> to each of the elements
        /// as they pass through this processing step. The function returns a <see cref="Task"/> and the
        /// value of that task will be emitted downstreams. As many tasks as requested elements by
        /// downstream may run in parallel and each processed element will be emitted dowstream
        /// as soon as it is ready, i.e. it is possible that the elements are not emitted downstream
        /// in the same order as received from upstream.
        /// 
        /// If the group by function <paramref name="asyncMapper"/> throws an exception or if the <see cref="Task"/> is completed
        /// with failure and the supervision decision is <see cref="Supervision.Directive.Stop"/>
        /// the stream will be completed with failure.
        /// 
        /// If the group by function <paramref name="asyncMapper"/> throws an exception or if the<see cref="Task"/> is completed
        /// with failure and the supervision decision is <see cref="Supervision.Directive.Resume"/> or
        /// <see cref="Supervision.Directive.Restart"/> the element is dropped and the stream continues.
        /// <para>
        /// Emits when any of the tasks returned by the provided function complete
        /// </para>
        /// <para>
        /// Backpressures when the number of tasks reaches the configured parallelism and the downstream backpressures
        /// </para>
        /// <para>
        /// Completes when upstream completes and all tasks has been completed and all elements has been emitted
        /// </para>
        /// <para>
        /// Cancels when downstream cancels
        /// </para>
        /// </summary>
        /// <seealso cref="SelectAsync{TIn,TOut,TMat}"/>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="parallelism">TBD</param>
        /// <param name="asyncMapper">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> SelectAsyncUnordered<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow, int parallelism,
            Func<TIn, Task<TOut>> asyncMapper)
        {
            return flow.Via(new Fusing.SelectAsyncUnordered<TIn, TOut>(parallelism, asyncMapper));
        }

        /// <summary>
        /// Only pass on those elements that satisfy the given <paramref name="predicate"/>.
        /// <para>
        /// Emits when the given <paramref name="predicate"/> returns true for the element
        /// </para>
        /// <para>
        /// Backpressures when the given <paramref name="predicate"/> returns true for the element and downstream backpressures
        /// </para>
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="predicate">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> Where<T, TMat>(this IFlow<T, TMat> flow, Predicate<T> predicate)
        {
            return flow.Via(new Fusing.Where<T>(predicate));
        }

        /// <summary>
        /// Only pass on those elements that NOT satisfy the given <paramref name="predicate"/>.
        /// <para>
        /// Emits when the given <paramref name="predicate"/> returns true for the element
        /// </para>
        /// <para>
        /// Backpressures when the given <paramref name="predicate"/> returns true for the element and downstream backpressures
        /// </para>
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="predicate">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> WhereNot<T, TMat>(this IFlow<T, TMat> flow, Predicate<T> predicate)
        {
            return flow.Via(new Fusing.Where<T>(e => !predicate(e)));
        }

        /// <summary>
        /// Terminate processing (and cancel the upstream publisher) after <paramref name="predicate"/>
        /// returns false for the first time, including the first failed element iff inclusive is true
        /// Due to input buffering some elements may have been requested from upstream publishers
        /// that will then not be processed downstream of this step.
        /// 
        /// The stream will be completed without producing any elements if <paramref name="predicate"/> is false for
        /// the first stream element.
        /// <para>
        /// Emits when the <paramref name="predicate"/> is true
        /// </para>
        /// <para>
        /// Backpressures when downstream backpressures
        /// </para>
        /// <para>
        /// Completes when <paramref name="predicate"/> returned false (or 1 after predicate returns false if <paramref name="inclusive"/>) or upstream completes
        /// </para>
        /// <para>
        /// Cancels when <paramref name="predicate"/> returned false or downstream cancels
        /// </para>
        /// <seealso cref="Limit{T, TMat}(IFlow{T, TMat}, long)"/>
        /// <seealso cref="LimitWeighted{T, TMat}(IFlow{T, TMat}, long, Func{T, long})"/>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="predicate">TBD</param>
        /// <param name="inclusive">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> TakeWhile<T, TMat>(this IFlow<T, TMat> flow, Predicate<T> predicate, bool inclusive)
        {
            return flow.Via(new Fusing.TakeWhile<T>(predicate, inclusive));
        }

        /// <summary>
        /// Discard elements at the beginning of the stream while <paramref name="predicate"/> is true.
        /// All elements will be taken after <paramref name="predicate"/> returns false first time.
        /// <para>
        /// Emits when <paramref name="predicate"/> returned false and for all following stream elements
        /// </para>
        /// Backpressures when <paramref name="predicate"/> returned false and downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="predicate">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> SkipWhile<T, TMat>(this IFlow<T, TMat> flow, Predicate<T> predicate)
        {
            return flow.Via(new Fusing.SkipWhile<T>(predicate));
        }

        /// <summary>
        /// Transform this stream by applying the given function <paramref name="collector"/> to each of the elements
        /// on which the function is defined (read: returns not null) as they pass through this processing step.
        /// Non-matching elements are filtered out.
        /// <para>
        /// Emits when the provided function <paramref name="collector"/> is defined for the element
        /// </para>
        /// Backpressures when the function <paramref name="collector"/> is defined for the element and downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="collector">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> Collect<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow, Func<TIn, TOut> collector)
        {
            return flow.Via(new Fusing.Collect<TIn, TOut>(collector));
        }

        /// <summary>
        /// Chunk up this stream into groups of the given size, with the last group
        /// possibly smaller than requested due to end-of-stream.
        /// <paramref name="n"/> must be positive, otherwise <see cref="ArgumentException"/> is thrown.
        /// <para>
        /// Emits when the specified number of elements has been accumulated or upstream completed
        /// </para>
        /// Backpressures when a group has been assembled and downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="n">TBD</param>
        /// <exception cref="ArgumentException">Thrown, if <paramref name="n"/> is less than or equal zero.</exception>
        /// <returns>TBD</returns>
        public static IFlow<IEnumerable<T>, TMat> Grouped<T, TMat>(this IFlow<T, TMat> flow, int n)
        {
            return flow.Via(new Fusing.Grouped<T>(n));
        }

        /// <summary>
        /// Ensure stream boundedness by limiting the number of elements from upstream.
        /// If the number of incoming elements exceeds <paramref name="max"/>, it will signal
        /// upstream failure <see cref="StreamLimitReachedException"/> downstream.
        /// 
        /// Due to input buffering some elements may have been
        /// requested from upstream publishers that will then not be processed downstream
        /// of this step.
        /// 
        /// The stream will be completed without producing any elements if <paramref name="max"/> is zero
        /// or negative.
        /// <para>
        /// Emits when the specified number of elements to take has not yet been reached
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when the defined number of elements has been taken or upstream completes
        /// </para>
        /// Cancels when the defined number of elements has been taken or downstream cancels
        /// </summary>
        /// <seealso cref="Take{T,TMat}"/>
        /// <seealso cref="TakeWithin{T,TMat}"/>
        /// <seealso cref="TakeWhile{T,TMat}"/>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="max">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> Limit<T, TMat>(this IFlow<T, TMat> flow, long max)
        {
            return LimitWeighted(flow, max, _ => 1L);
        }

        /// <summary>
        /// Ensure stream boundedness by evaluating the cost of incoming elements
        /// using a cost function. Exactly how many elements will be allowed to travel downstream depends on the
        /// evaluated cost of each element. If the accumulated cost exceeds <paramref name="max"/>, it will signal
        /// upstream failure <see cref="StreamLimitReachedException"/> downstream.
        /// 
        /// Due to input buffering some elements may have been
        /// requested from upstream publishers that will then not be processed downstream
        /// of this step.
        /// 
        /// The stream will be completed without producing any elements if <paramref name="max"/> is zero
        /// or negative.
        /// <para>
        /// Emits when the specified number of elements to take has not yet been reached
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when the defined number of elements has been taken or upstream completes
        /// </para>
        /// Cancels when the defined number of elements has been taken or downstream cancels
        /// </summary>
        /// <seealso cref="Take{T,TMat}"/>
        /// <seealso cref="TakeWithin{T,TMat}"/>
        /// <seealso cref="TakeWhile{T,TMat}"/>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="costFunc">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> LimitWeighted<T, TMat>(this IFlow<T, TMat> flow, long max, Func<T, long> costFunc)
        {
            return flow.Via(new Fusing.LimitWeighted<T>(max, costFunc));
        }

        /// <summary>
        /// Apply a sliding window over the stream and return the windows as groups of elements, with the last group
        /// possibly smaller than requested due to end-of-stream.
        /// 
        /// <paramref name="n"/> must be positive, otherwise IllegalArgumentException is thrown.
        /// <paramref name="step"/> must be positive, otherwise IllegalArgumentException is thrown.
        /// <para>
        /// Emits when enough elements have been collected within the window or upstream completed
        /// </para>
        /// Backpressures when a window has been assembled and downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="n">TBD</param>
        /// <param name="step">TBD</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="n"/> or <paramref name="step"/> is less than or equal zero.</exception>
        /// <returns>TBD</returns>
        public static IFlow<IEnumerable<T>, TMat> Sliding<T, TMat>(this IFlow<T, TMat> flow, int n, int step = 1)
        {
            return flow.Via(new Fusing.Sliding<T>(n, step));
        }

        /// <summary>
        /// Similar to <see cref="Aggregate{TIn,TOut,TMat}"/> but is not a terminal operation,
        /// emits its current value which starts at <paramref name="zero"/> and then
        /// applies the current and next value to the given function <paramref name="scan"/>,
        /// emitting the next current value.
        /// 
        /// If the function <paramref name="scan"/> throws an exception and the supervision decision is
        /// <see cref="Supervision.Directive.Restart"/> current value starts at <paramref name="zero"/> again
        /// the stream will continue.
        /// <para>
        /// Emits when the function scanning the element returns a new element
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="zero">TBD</param>
        /// <param name="scan">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> Scan<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow, TOut zero,
            Func<TOut, TIn, TOut> scan)
        {
            return flow.Via(new Fusing.Scan<TIn, TOut>(zero, scan));
        }

        /// <summary>
        /// Similar to <see cref="Scan{TIn,TOut,TMat}"/> but with a asynchronous function,
        /// emits its current value which starts at <paramref name="zero"/> and then
        /// applies the current and next value to the given function <paramref name="scan"/>
        /// emitting a <see cref="Task{TOut}"/> that resolves to the next current value.
        /// 
        /// If the function <paramref name="scan"/> throws an exception and the supervision decision is
        /// <see cref="Supervision.Directive.Restart"/> current value starts at <paramref name="zero"/> again
        /// the stream will continue.
        /// 
        /// If the function <paramref name="scan"/> throws an exception and the supervision decision is
        /// <see cref="Supervision.Directive.Resume"/> current value starts at the previous
        /// current value, or zero when it doesn't have one, and the stream will continue.
        /// <para>
        /// Emits the <see cref="Task{TOut}"/> returned by <paramref name="scan"/> completes
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes upstream completes and the last task returned by <paramref name="scan"/> completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="zero">TBD</param>
        /// <param name="scan">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> ScanAsync<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow, TOut zero,
            Func<TOut, TIn, Task<TOut>> scan)
        {
            return flow.Via(new Fusing.ScanAsync<TIn, TOut>(zero, scan));
        }

        /// <summary>
        /// Similar to <see cref="Scan{TIn,TOut,TMat}"/> but only emits its result when the upstream completes,
        /// after which it also completes. Applies the given function <paramref name="fold"/> towards its current and next value,
        /// yielding the next current value.
        /// 
        /// If the function <paramref name="fold"/> throws an exception and the supervision decision is
        /// <see cref="Supervision.Directive.Restart"/> current value starts at <paramref name="zero"/> again
        /// the stream will continue.
        /// <para>
        /// Emits when upstream completes
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="zero">TBD</param>
        /// <param name="fold">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> Aggregate<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow, TOut zero,
            Func<TOut, TIn, TOut> fold)
        {
            return flow.Via(new Fusing.Aggregate<TIn, TOut>(zero, fold));
        }

        /// <summary>
        /// Similar to <see cref="Aggregate{TIn,TOut,TMat}"/> but with an asynchronous function.
        /// Applies the given function towards its current and next value,
        /// yielding the next current value.
        /// 
        /// If the function <paramref name="fold"/> returns a failure and the supervision decision is
        /// <see cref="Supervision.Directive.Restart"/> current value starts at <paramref name="zero"/> again
        /// the stream will continue.
        /// <para>
        /// Emits when upstream completes
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// 
        /// <seealso cref="Aggregate{TIn,TOut,TMat}"/>
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="zero">TBD</param>
        /// <param name="fold">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> AggregateAsync<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow, TOut zero,
            Func<TOut, TIn, Task<TOut>> fold)
        {
            return flow.Via(new Fusing.AggregateAsync<TIn, TOut>(zero, fold));
        }

        /// <summary>
        /// Similar to <see cref="Aggregate{TIn,TOut,TMat}"/> but uses first element as zero element.
        /// Applies the given function <paramref name="reduce"/> towards its current and next value,
        /// yielding the next current value. 
        /// <para>
        /// Emits when upstream completes
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="reduce">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TIn, TMat> Sum<TIn, TMat>(this IFlow<TIn, TMat> flow, Func<TIn, TIn, TIn> reduce)
        {
            return flow.Via(new Fusing.Sum<TIn>(reduce));
        }

        /// <summary>
        /// Intersperses stream with provided element, similar to how <see cref="string.Join(string,string[])"/>
        /// injects a separator between a collection's elements.
        /// 
        /// Additionally can inject start and end marker elements to stream.
        /// 
        /// In case you want to only prepend or only append an element (yet still use the intercept feature
        /// to inject a separator between elements, you may want to use the following pattern instead of the 3-argument
        /// version of intersperse (See <see cref="Concat{TIn,TOut}"/> for semantics details). 
        /// <para>
        /// Emits when upstream emits (or before with the <paramref name="start"/> element if provided)
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="start">TBD</param>
        /// <param name="inject">TBD</param>
        /// <param name="end">TBD</param>
        /// <exception cref="ArgumentNullException">Thrown when any of the <paramref name="start"/>, <paramref name="inject"/> or <paramref name="end"/> is undefined.</exception>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> Intersperse<T, TMat>(this IFlow<T, TMat> flow, T start, T inject, T end)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(start);
            ReactiveStreamsCompliance.RequireNonNullElement(inject);
            ReactiveStreamsCompliance.RequireNonNullElement(end);

            return flow.Via(new Fusing.Intersperse<T>(start, inject, end));
        }

        /// <summary>
        /// Intersperses stream with provided element, similar to how <see cref="string.Join(string,string[])"/>
        /// injects a separator between a collection's elements.
        /// 
        /// Additionally can inject start and end marker elements to stream.
        /// 
        /// In case you want to only prepend or only append an element (yet still use the intercept feature
        /// to inject a separator between elements, you may want to use the following pattern instead of the 3-argument
        /// version of intersperse (See <see cref="Concat{TIn,TOut}"/> for semantics details). 
        /// <para>
        /// Emits when upstream emits (or before with the <paramref name="inject"/> element if provided)
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inject"/> is undefined.</exception>
        public static IFlow<T, TMat> Intersperse<T, TMat>(this IFlow<T, TMat> flow, T inject)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(inject);

            return flow.Via(new Fusing.Intersperse<T>(inject));
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
        /// Emits when the configured time elapses since the last group has been emitted
        /// </para>
        /// Backpressures when the configured time elapses since the last group has been emitted
        /// <para>
        /// Completes when upstream completes (emits last group)
        /// </para>
        /// Cancels when downstream completes
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="n">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <exception cref="ArgumentException">Thrown if <paramref name="n"/> is less than or equal zero or <paramref name="timeout"/> is <see cref="TimeSpan.Zero"/>.</exception>
        /// <returns>TBD</returns>
        public static IFlow<IEnumerable<T>, TMat> GroupedWithin<T, TMat>(this IFlow<T, TMat> flow, int n,
            TimeSpan timeout)
        {
            if (n <= 0) throw new ArgumentException("n must be > 0", nameof(n));
            if (timeout == TimeSpan.Zero) throw new ArgumentException("Timeout must be non-zero", nameof(timeout));

            return flow.Via(new Fusing.GroupedWithin<T>(n, timeout));
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
        /// Emits when there is a pending element in the buffer and configured time for this element elapsed
        ///  <para/> * EmitEarly - strategy do not wait to emit element if buffer is full
        /// </para>
        /// Backpressures when depending on OverflowStrategy
        ///  <para/> * Backpressure - backpressures when buffer is full
        ///  <para/> * DropHead, DropTail, DropBuffer - never backpressures
        ///  <para/> * Fail - fails the stream if buffer gets full
        /// <para>
        /// Completes when upstream completes and buffered elements has been drained
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="of">Time to shift all messages.</param>
        /// <param name="strategy">Strategy that is used when incoming elements cannot fit inside the buffer</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> Delay<T, TMat>(this IFlow<T, TMat> flow, TimeSpan of,
            DelayOverflowStrategy? strategy = null)
        {
            return flow.Via(new Fusing.Delay<T>(of, strategy ?? DelayOverflowStrategy.DropTail));
        }

        /// <summary>
        /// Discard the given number of elements at the beginning of the stream.
        /// No elements will be dropped if <paramref name="n"/> is zero or negative.
        /// <para>
        /// Emits when the specified number of elements has been dropped already
        /// </para>
        /// Backpressures when the specified number of elements has been dropped and downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> Skip<T, TMat>(this IFlow<T, TMat> flow, long n)
        {
            return flow.Via(new Fusing.Skip<T>(n));
        }

        /// <summary>
        /// Discard the elements received within the given duration at beginning of the stream.
        /// <para>
        /// Emits when the specified time elapsed and a new upstream element arrives
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="duration">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> SkipWithin<T, TMat>(this IFlow<T, TMat> flow, TimeSpan duration)
        {
            return flow.Via(new Fusing.SkipWithin<T>(duration).WithAttributes(Attributes.CreateName("skipWithin")));
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
        /// Emits when the specified number of elements to take has not yet been reached
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when the defined number of elements has been taken or upstream completes
        /// </para>
        /// Cancels when the defined number of elements has been taken or downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> Take<T, TMat>(this IFlow<T, TMat> flow, long n)
        {
            return flow.Via(new Fusing.Take<T>(n));
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
        /// Emits when an upstream element arrives
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes or timer fires
        /// </para>
        /// Cancels when downstream cancels or timer fires
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="duration">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> TakeWithin<T, TMat>(this IFlow<T, TMat> flow, TimeSpan duration)
        {
            return flow.Via(new Fusing.TakeWithin<T>(duration).WithAttributes(Attributes.CreateName("takeWithin")));
        }

        /// <summary>
        /// Allows a faster upstream to progress independently of a slower subscriber by conflating elements into a summary
        /// until the subscriber is ready to accept them. For example a conflate step might average incoming numbers if the
        /// upstream publisher is faster.
        /// 
        /// This version of conflate allows to derive a seed from the first element and change the aggregated type to
        /// be different than the input type. See <see cref="Conflate{T,TMat}"/> for a simpler version that does not change types.
        /// 
        /// This element only rolls up elements if the upstream is faster, but if the downstream is faster it will not
        /// duplicate elements.
        /// <para>
        /// Emits when downstream stops backpressuring and there is a conflated element available
        /// </para>
        /// Backpressures when never
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TSeed">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="seed">Provides the first state for a conflated value using the first unconsumed element as a start</param> 
        /// <param name="aggregate">Takes the currently aggregated value and the current pending element to produce a new aggregate</param>
        /// <returns>TBD</returns>
        public static IFlow<TSeed, TMat> ConflateWithSeed<T, TMat, TSeed>(this IFlow<T, TMat> flow, Func<T, TSeed> seed,
            Func<TSeed, T, TSeed> aggregate)
        {
            return flow.Via(new Fusing.Batch<T, TSeed>(1L, elem => 0L, seed, aggregate));
        }

        /// <summary>
        /// Allows a faster upstream to progress independently of a slower subscriber by conflating elements into a summary
        /// until the subscriber is ready to accept them. For example a conflate step might average incoming numbers if the
        /// upstream publisher is faster.
        /// 
        /// This version of conflate does not change the output type of the stream. See <see cref="ConflateWithSeed{T,TMat,TSeed}"/>
        /// for a more flexible version that can take a seed function and transform elements while rolling up.
        /// 
        /// This element only rolls up elements if the upstream is faster, but if the downstream is faster it will not
        /// duplicate elements.
        /// <para>
        /// Emits when downstream stops backpressuring and there is a conflated element available
        /// </para>
        /// Backpressures when never
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="aggregate">Takes the currently aggregated value and the current pending element to produce a new aggregate</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> Conflate<T, TMat>(this IFlow<T, TMat> flow, Func<T, T, T> aggregate)
        {
            return ConflateWithSeed(flow, o => o, aggregate);
        }

        /// <summary>
        /// Allows a faster upstream to progress independently of a slower subscriber by aggregating elements into batches
        /// until the subscriber is ready to accept them.For example a batch step might store received elements in
        /// an array up to the allowed max limit if the upstream publisher is faster.
        ///
        /// This only rolls up elements if the upstream is faster, but if the downstream is faster it will not
        /// duplicate elements.
        ///
        /// Emits when downstream stops backpressuring and there is an aggregated element available
        ///
        /// Backpressures when there are <paramref name="max"/> batched elements and 1 pending element and downstream backpressures
        ///
        /// Completes when upstream completes and there is no batched/pending element waiting
        ///
        /// Cancels when downstream cancels
        ///
        /// See also <seealso cref="ConflateWithSeed{TOut,TMat,TSeed}"/>, <seealso cref="BatchWeighted{TOut,TOut2,TMat}"/>
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="max">maximum number of elements to batch before backpressuring upstream (must be positive non-zero)</param>
        /// <param name="seed">Provides the first state for a batched value using the first unconsumed element as a start</param>
        /// <param name="aggregate">Takes the currently batched value and the current pending element to produce a new aggregate</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut2, TMat> Batch<TOut, TOut2, TMat>(this IFlow<TOut, TMat> flow, long max,
            Func<TOut, TOut2> seed, Func<TOut2, TOut, TOut2> aggregate)
        {
            return flow.Via(new Fusing.Batch<TOut, TOut2>(max, ConstantFunctions.OneLong<TOut>(), seed, aggregate));
        }

        ///  <summary>
        /// Allows a faster upstream to progress independently of a slower subscriber by aggregating elements into batches
        /// until the subscriber is ready to accept them.For example a batch step might concatenate <see cref="ByteString"/>
        /// elements up to the allowed max limit if the upstream publisher is faster.
        ///
        /// This element only rolls up elements if the upstream is faster, but if the downstream is faster it will not
        /// duplicate elements.
        ///
        /// Batching will apply for all elements, even if a single element cost is greater than the total allowed limit.
        /// In this case, previous batched elements will be emitted, then the "heavy" element will be emitted (after
        /// being applied with the <paramref name="seed"/> function) without batching further elements with it, and then the rest of the
        /// incoming elements are batched.
        ///
        /// Emits when downstream stops backpressuring and there is a batched element available
        ///
        /// Backpressures when there are <paramref name="max"/> weighted batched elements + 1 pending element and downstream backpressures
        ///
        /// Completes when upstream completes and there is no batched/pending element waiting
        ///
        /// Cancels when downstream cancels
        ///
        /// See also <seealso cref="ConflateWithSeed{TOut,TMat,TSeed}"/>, <seealso cref="Batch{TOut,TOut2,TMat}"/>
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="max">maximum weight of elements to batch before backpressuring upstream (must be positive non-zero)</param>
        /// <param name="costFunction">a function to compute a single element weight</param>
        /// <param name="seed">Provides the first state for a batched value using the first unconsumed element as a start</param>
        /// <param name="aggregate">Takes the currently batched value and the current pending element to produce a new aggregate</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut2, TMat> BatchWeighted<TOut, TOut2, TMat>(this IFlow<TOut, TMat> flow, long max, Func<TOut, long> costFunction,
            Func<TOut, TOut2> seed, Func<TOut2, TOut, TOut2> aggregate)
        {
            return flow.Via(new Fusing.Batch<TOut, TOut2>(max, costFunction, seed, aggregate));
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
        /// Expand does not support <see cref="Supervision.Directive.Restart"/> and <see cref="Supervision.Directive.Resume"/>.
        /// Exceptions from the <paramref name="extrapolate"/> function will complete the stream with failure.
        /// <para>
        /// Emits when downstream stops backpressuring
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="extrapolate">Takes the current extrapolation state to produce an output element and the next extrapolation state.</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> Expand<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow,
            Func<TIn, IEnumerator<TOut>> extrapolate)
        {
            return flow.Via(new Fusing.Expand<TIn, TOut>(extrapolate));
        }

        /// <summary>
        /// Adds a fixed size buffer in the flow that allows to store elements from a faster upstream until it becomes full.
        /// Depending on the defined <see cref="OverflowStrategy"/> it might drop elements or backpressure the upstream if
        /// there is no space available
        /// <para>
        /// Emits when downstream stops backpressuring and there is a pending element in the buffer
        /// </para>
        /// Backpressures when depending on OverflowStrategy
        /// <para/> * Backpressure - backpressures when buffer is full
        /// <para/> * DropHead, DropTail, DropBuffer - never backpressures
        /// <para/> * Fail - fails the stream if buffer gets full
        /// <para>
        /// Completes when upstream completes and buffered elements has been drained
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="size">The size of the buffer in element count</param>
        /// <param name="strategy">Strategy that is used when incoming elements cannot fit inside the buffer</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> Buffer<T, TMat>(this IFlow<T, TMat> flow, int size, OverflowStrategy strategy)
        {
            return flow.Via(new Fusing.Buffer<T>(size, strategy));
        }

        /// <summary>
        /// Generic transformation of a stream with a custom processing <see cref="IStage{TIn, TOut}"/>.
        /// This operator makes it possible to extend the <see cref="Flow"/> API when there is no specialized
        /// operator that performs the transformation.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="stageFactory">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> Transform<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow,
            Func<IStage<TIn, TOut>> stageFactory)
        {
            return flow.Via(new PushPullGraphStage<TIn, TOut>(attr => stageFactory(), Attributes.None));
        }

        /// <summary>
        /// Takes up to <paramref name="n"/> elements from the stream and returns a pair containing a strict sequence of the taken element
        /// and a stream representing the remaining elements. If <paramref name="n"/> is zero or negative, then this will return a pair
        /// of an empty collection and a stream containing the whole upstream unchanged.
        /// <para>
        /// Emits when the configured number of prefix elements are available. Emits this prefix, and the rest
        /// as a substream
        /// </para>
        /// Backpressures when downstream backpressures or substream backpressures
        /// <para>
        /// Completes when prefix elements has been consumed and substream has been consumed
        /// </para>
        /// Cancels when downstream cancels or substream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<(IImmutableList<T>, Source<T, NotUsed>), TMat> PrefixAndTail<T, TMat>(
            this IFlow<T, TMat> flow, int n)
        {
            return flow.Via(new Fusing.PrefixAndTail<T>(n));
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
        /// is <see cref="Supervision.Directive.Stop"/> the stream and substreams will be completed
        /// with failure.
        /// 
        /// If the group by <paramref name="groupingFunc"/> throws an exception and the supervision decision
        /// is <see cref="Supervision.Directive.Resume"/> or <see cref="Supervision.Directive.Restart"/>
        /// the element is dropped and the stream and substreams continue.
        /// 
        /// Function <paramref name="groupingFunc"/>  MUST NOT return null. This will throw exception and trigger supervision decision mechanism.
        /// <para>
        /// Emits when an element for which the grouping function returns a group that has not yet been created.
        /// Emits the new group
        /// </para>
        /// Backpressures when there is an element pending for a group whose substream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels and all substreams cancel
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="maxSubstreams">TBD</param>
        /// <param name="groupingFunc">TBD</param>
        /// <param name="toFunc">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<T, TMat, TClosed> GroupBy<T, TMat, TKey, TClosed>(
            this IFlow<T, TMat> flow,
            int maxSubstreams,
            Func<T, TKey> groupingFunc,
            Func<IFlow<Source<T, NotUsed>, TMat>, Sink<Source<T, NotUsed>, Task>, TClosed> toFunc)
        {
            var merge = new GroupByMergeBack<T, TMat, TKey>(flow, maxSubstreams, groupingFunc);

            Func<Sink<T, TMat>, TClosed> finish = s =>
            {
                return toFunc(
                    flow.Via(new Fusing.GroupBy<T, TKey>(maxSubstreams, groupingFunc)),
                    Sink.ForEach<Source<T, NotUsed>>(e => e.RunWith(s, Fusing.GraphInterpreter.Current.Materializer)));
            };

            return new SubFlowImpl<T, T, TMat, TClosed>(Flow.Create<T, TMat>(), merge, finish);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TKey">TBD</typeparam>
        internal class GroupByMergeBack<TOut, TMat, TKey> : IMergeBack<TOut, TMat>
        {
            private readonly IFlow<TOut, TMat> _self;
            private readonly int _maxSubstreams;
            private readonly Func<TOut, TKey> _groupingFunc;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="self">TBD</param>
            /// <param name="maxSubstreams">TBD</param>
            /// <param name="groupingFunc">TBD</param>
            public GroupByMergeBack(IFlow<TOut, TMat> self,
                int maxSubstreams,
                Func<TOut, TKey> groupingFunc)
            {
                _self = self;
                _maxSubstreams = maxSubstreams;
                _groupingFunc = groupingFunc;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="T">TBD</typeparam>
            /// <param name="flow">TBD</param>
            /// <param name="breadth">TBD</param>
            /// <returns>TBD</returns>
            public IFlow<T, TMat> Apply<T>(Flow<TOut, T, TMat> flow, int breadth)
            {
                return _self.Via(new Fusing.GroupBy<TOut, TKey>(_maxSubstreams, _groupingFunc))
                    .Select(f => f.Via(flow))
                    .Via(new Fusing.FlattenMerge<Source<T, NotUsed>, T, NotUsed>(breadth));
            }
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
        /// In case the * first * element of the stream matches the predicate, the first
        /// substream emitted by splitWhen will start from that element. For example:
        /// 
        /// {{{
        /// true, false, false // first substream starts from the split-by element
        /// true, false        // subsequent substreams operate the same way
        /// }}}
        /// 
        /// The object returned from this method is not a normal <see cref="Source{TOut,TMat}"/> or <see cref="Flow{TIn,TOut,TMat}"/>,
        /// it is a <see cref="SubFlow{TOut,TMat,TClosed}"/>. This means that after this combinator all transformations
        /// are applied to all encountered substreams in the same fashion. Substream mode
        /// is exited either by closing the substream (i.e. connecting it to a <see cref="Sink{TIn,TMat}"/>)
        /// or by merging the substreams back together; see the <see cref="SubFlow{TOut,TMat,TClosed}.To{TMat2}"/> and 
        /// <see cref="SubFlow{TOut,TMat,TClosed}.MergeSubstreams"/> methods
        /// on <see cref="SubFlow{TOut,TMat,TClosed}"/> for more information.
        /// 
        /// It is important to note that the substreams also propagate back-pressure as
        /// any other stream, which means that blocking one substream will block the <see cref="SplitWhen{T,TMat,TClosed}"/>
        /// operator itself—and thereby all substreams—once all internal or
        /// explicit buffers are filled.
        /// 
        /// If the split <paramref name="predicate"/> throws an exception and the supervision decision
        /// is <see cref="Supervision.Directive.Stop"/> the stream and substreams will be completed
        /// with failure.
        /// 
        /// If the split <paramref name="predicate"/> throws an exception and the supervision decision
        /// is <see cref="Supervision.Directive.Resume"/> or <see cref="Supervision.Directive.Restart"/>
        /// the element is dropped and the stream and substreams continue.
        /// <para>
        /// Emits when an element for which the provided predicate is true, opening and emitting
        /// a new substream for subsequent element
        /// </para>
        /// Backpressures when there is an element pending for the next substream, but the previous
        /// is not fully consumed yet, or the substream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels and substreams cancel
        /// </summary>
        /// <seealso cref="SplitAfter{T,TMat,TVal}"/>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="substreamCancelStrategy">TBD</param>
        /// <param name="predicate">TBD</param>
        /// <param name="toFunc">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<T, TMat, TClosed> SplitWhen<T, TMat, TClosed>(this IFlow<T, TMat> flow,
            SubstreamCancelStrategy substreamCancelStrategy, Func<T, bool> predicate,
            Func<IFlow<Source<T, NotUsed>, TMat>, Sink<Source<T, NotUsed>, Task>, TClosed> toFunc)
        {
            var merge = new SplitWhenMergeBack<T, TMat>(flow, predicate, substreamCancelStrategy);

            Func<Sink<T, TMat>, TClosed> finish = s =>
            {
                return toFunc(flow.Via(Fusing.Split.When(predicate, substreamCancelStrategy)),
                    Sink.ForEach<Source<T, NotUsed>>(e => e.RunWith(s, Fusing.GraphInterpreter.Current.Materializer)));
            };

            return new SubFlowImpl<T, T, TMat, TClosed>(Flow.Create<T, TMat>(), merge, finish);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        internal class SplitWhenMergeBack<TOut, TMat> : IMergeBack<TOut, TMat>
        {
            private readonly IFlow<TOut, TMat> _self;
            private readonly Func<TOut, bool> _predicate;
            private readonly SubstreamCancelStrategy _substreamCancelStrategy;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="self">TBD</param>
            /// <param name="predicate">TBD</param>
            /// <param name="substreamCancelStrategy">TBD</param>
            public SplitWhenMergeBack(IFlow<TOut, TMat> self, Func<TOut, bool> predicate, SubstreamCancelStrategy substreamCancelStrategy)
            {
                _self = self;
                _predicate = predicate;
                _substreamCancelStrategy = substreamCancelStrategy;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="T">TBD</typeparam>
            /// <param name="flow">TBD</param>
            /// <param name="breadth">TBD</param>
            /// <returns>TBD</returns>
            public IFlow<T, TMat> Apply<T>(Flow<TOut, T, TMat> flow, int breadth)
            {
                return _self.Via(Fusing.Split.When(_predicate, _substreamCancelStrategy))
                    .Select(f => f.Via(flow))
                    .Via(new Fusing.FlattenMerge<Source<T, NotUsed>, T, NotUsed>(breadth));
            }
        }

        /// <summary>
        /// This operation applies the given predicate to all incoming elements and
        /// emits them to a stream of output streams.It * ends * the current substream when the
        /// predicate is true. This means that for the following series of predicate values,
        /// three substreams will be produced with lengths 2, 2, and 3:
        ///
        /// {{{
        /// false, true,        // elements go into first substream
        /// false, true,        // elements go into second substream
        /// false, false, true  // elements go into third substream
        /// }}}
        ///
        /// The object returned from this method is not a normal [[Source]] or[[Flow]],
        /// it is a <see cref="SubFlow{TOut,TMat,TClosed}"/>. This means that after this combinator all transformations
        ///are applied to all encountered substreams in the same fashion.Substream mode
        /// is exited either by closing the substream(i.e.connecting it to a [[Sink]])
        /// or by merging the substreams back together; see the <see cref="SubFlow{TOut,TMat,TClosed}.To{TMat2}"/> and <see cref="SubFlow{TOut,TMat,TClosed}.MergeSubstreams"/> methods
        /// on <see cref="SubFlow{TOut,TMat,TClosed}"/> for more information.
        ///
        /// It is important to note that the substreams also propagate back-pressure as
        /// any other stream, which means that blocking one substream will block the <see cref="SplitAfter{T,TMat,TClosed}"/>
        /// operator itself—and thereby all substreams—once all internal or
        /// explicit buffers are filled.
        ///
        /// If the split <paramref name="predicate"/> throws an exception and the supervision decision
        /// is <see cref="Supervision.Directive.Stop"/> the stream and substreams will be completed
        /// with failure.
        ///
        /// If the split <paramref name="predicate"/> throws an exception and the supervision decision
        /// is <see cref="Supervision.Directive.Resume"/> or <see cref="Supervision.Directive.Restart"/>
        /// the element is dropped and the stream and substreams continue.
        /// <para>
        /// Emits when an element passes through.When the provided predicate is true it emitts the element
        /// and opens a new substream for subsequent element
        /// </para>
        /// Backpressures when there is an element pending for the next substream, but the previous
        /// is not fully consumed yet, or the substream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels and substreams cancel
        /// </summary>
        /// <seealso cref="SplitWhen{T,TMat,TVal}"/>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="substreamCancelStrategy">TBD</param>
        /// <param name="predicate">TBD</param>
        /// <param name="toFunc">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<T, TMat, TClosed> SplitAfter<T, TMat, TClosed>(this IFlow<T, TMat> flow,
            SubstreamCancelStrategy substreamCancelStrategy, Func<T, bool> predicate,
            Func<IFlow<Source<T, NotUsed>, TMat>, Sink<Source<T, NotUsed>, Task>, TClosed> toFunc)
        {
            var merge = new SplitAfterMergeBack<T, TMat>(flow, predicate, substreamCancelStrategy);

            Func<Sink<T, TMat>, TClosed> finish = s =>
            {
                return toFunc(flow.Via(Fusing.Split.After(predicate, substreamCancelStrategy)),
                    Sink.ForEach<Source<T, NotUsed>>(e => e.RunWith(s, Fusing.GraphInterpreter.Current.Materializer)));
            };

            return new SubFlowImpl<T, T, TMat, TClosed>(Flow.Create<T, TMat>(), merge, finish);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        internal class SplitAfterMergeBack<TOut, TMat> : IMergeBack<TOut, TMat>
        {
            private readonly IFlow<TOut, TMat> _self;
            private readonly Func<TOut, bool> _predicate;
            private readonly SubstreamCancelStrategy _substreamCancelStrategy;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="self">TBD</param>
            /// <param name="predicate">TBD</param>
            /// <param name="substreamCancelStrategy">TBD</param>
            public SplitAfterMergeBack(IFlow<TOut, TMat> self, Func<TOut, bool> predicate, SubstreamCancelStrategy substreamCancelStrategy)
            {
                _self = self;
                _predicate = predicate;
                _substreamCancelStrategy = substreamCancelStrategy;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="T">TBD</typeparam>
            /// <param name="flow">TBD</param>
            /// <param name="breadth">TBD</param>
            /// <returns>TBD</returns>
            public IFlow<T, TMat> Apply<T>(Flow<TOut, T, TMat> flow, int breadth)
            {
                return _self.Via(Fusing.Split.After(_predicate, _substreamCancelStrategy))
                    .Select(f => f.Via(flow))
                    .Via(new Fusing.FlattenMerge<Source<T, NotUsed>, T, NotUsed>(breadth));
            }
        }

        /// <summary>
        /// Transform each input element into a <see cref="Source{TOut,TMat}"/> of output elements that is
        /// then flattened into the output stream by concatenation,
        /// fully consuming one Source after the other.
        /// <para>
        /// Emits when a currently consumed substream has an element available
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes and all consumed substreams complete
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="flatten">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> ConcatMany<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow,
            Func<TIn, IGraph<SourceShape<TOut>, TMat>> flatten)
        {
            return flow.Select(flatten).Via(new Fusing.FlattenMerge<IGraph<SourceShape<TOut>, TMat>, TOut, TMat>(1));
        }

        /// <summary>
        /// Transform each input element into a <see cref="Source{TOut,TMat}"/> of output elements that is
        /// then flattened into the output stream by merging, where at most <paramref name="breadth"/>
        /// substreams are being consumed at any given time.
        /// <para>
        /// Emits when a currently consumed substream has an element available
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes and all consumed substreams complete
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="breadth">TBD</param>
        /// <param name="flatten">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> MergeMany<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow, int breadth,
            Func<TIn, IGraph<SourceShape<TOut>, TMat>> flatten)
        {
            return flow.Select(flatten).Via(new Fusing.FlattenMerge<IGraph<SourceShape<TOut>, TMat>, TOut, TMat>(breadth));
        }

        /// <summary>
        /// If the first element has not passed through this stage before the provided timeout, the stream is failed
        /// with a <see cref="TimeoutException"/>.
        /// <para>
        /// Emits when upstream emits an element
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes or fails if timeout elapses before first element arrives
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> InitialTimeout<T, TMat>(this IFlow<T, TMat> flow, TimeSpan timeout)
        {
            return flow.Via(new Initial<T>(timeout));
        }

        /// <summary>
        /// If the completion of the stream does not happen until the provided timeout, the stream is failed
        /// with a <see cref="TimeoutException"/>.
        /// <para>
        /// Emits when upstream emits an element
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes or fails if timeout elapses before upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> CompletionTimeout<T, TMat>(this IFlow<T, TMat> flow, TimeSpan timeout)
        {
            return flow.Via(new Completion<T>(timeout));
        }

        /// <summary>
        /// If the time between two processed elements exceed the provided timeout, the stream is failed
        /// with a <see cref="TimeoutException"/>. 
        /// The timeout is checked periodically, so the resolution of the check is one period (equals to timeout value).
        /// <para>
        /// Emits when upstream emits an element
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes or fails if timeout elapses between two emitted elements
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> IdleTimeout<T, TMat>(this IFlow<T, TMat> flow, TimeSpan timeout)
        {
            return flow.Via(new Idle<T>(timeout));
        }

        /// <summary>
        /// If the time between the emission of an element and the following downstream demand exceeds the provided timeout,
        /// the stream is failed with a <see cref="TimeoutException"/>. The timeout is checked periodically,
        /// so the resolution of the check is one period (equals to timeout value).
        /// <para>
        /// Emits when upstream emits an element
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes or fails if timeout elapses between element emission and downstream demand.
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> BackpressureTimeout<T, TMat>(this IFlow<T, TMat> flow, TimeSpan timeout)
        {
            return flow.Via(new BackpressureTimeout<T>(timeout));
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
        /// Emits when upstream emits an element or if the upstream was idle for the configured period
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TIn2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="injectElement">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TIn2, TMat> KeepAlive<TIn, TIn2, TMat>(this IFlow<TIn, TMat> flow, TimeSpan timeout,
            Func<TIn2> injectElement) where TIn : TIn2
        {
            return flow.Via(new IdleInject<TIn, TIn2>(timeout, injectElement));
        }

        /// <summary>
        /// Sends elements downstream with speed limited to <paramref name="elements"/>/<paramref name="per"/>. In other words, this stage set the maximum rate
        /// for emitting messages. This combinator works for streams where all elements have the same cost or length.
        /// 
        /// Throttle implements the token bucket model. There is a bucket with a given token capacity (burst size or maximumBurst).
        /// Tokens drops into the bucket at a given rate and can be "spared" for later use up to bucket capacity
        /// to allow some burstiness. Whenever stream wants to send an element, it takes as many
        /// tokens from the bucket as number of elements. If there isn't any, throttle waits until the
        /// bucket accumulates enough tokens.
        /// 
        /// Parameter <paramref name="mode"/> manages behaviour when upstream is faster than throttle rate:
        /// <para/> - <see cref="ThrottleMode.Shaping"/> makes pauses before emitting messages to meet throttle rate
        /// <para/> - <see cref="ThrottleMode.Enforcing"/> fails with exception when upstream is faster than throttle rate. Enforcing
        ///  cannot emit elements that cost more than the maximumBurst
        /// <para>
        /// Emits when upstream emits an element and configured time per each element elapsed
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="elements">TBD</param>
        /// <param name="per">TBD</param>
        /// <param name="maximumBurst">TBD</param>
        /// <param name="mode">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when one of the following conditions is met.
        /// <ul>
        /// <li>The specified <paramref name="elements"/> is less than or equal to zero</li>
        /// <li>The specified <paramref name="per"/> timeout is equal to <see cref="TimeSpan.Zero"/>.</li>
        /// <li>The specified <paramref name="maximumBurst"/> is less than or equal zero in <see cref="ThrottleMode.Enforcing"/> <paramref name="mode"/>.</li>
        /// <li>The <see cref="TimeSpan.Ticks"/> in the specified <paramref name="per"/> is less than the specified <paramref name="elements"/>.</li>
        /// </ul>
        /// </exception>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> Throttle<T, TMat>(this IFlow<T, TMat> flow, int elements, TimeSpan per,
            int maximumBurst, ThrottleMode mode)
        {
            if (elements <= 0) throw new ArgumentException("Throttle elements must be > 0", nameof(elements));
            if (per == TimeSpan.Zero) throw new ArgumentException("Throttle per timeout must not be zero", nameof(per));
            if (mode == ThrottleMode.Enforcing && maximumBurst < 0)
                throw new ArgumentException("Throttle maximumBurst must be > 0 in Enforcing mode", nameof(maximumBurst));
            if (per.Ticks < elements)
                throw new ArgumentException("Rates larger than 1 unit / tick are not supported", nameof(elements));

            return flow.Via(new Throttle<T>(elements, per, maximumBurst, _ => 1, mode));
        }

        /// <summary>
        /// Sends elements downstream with speed limited to <paramref name="cost"/>/<paramref name="per"/>`. Cost is
        /// calculating for each element individually by calling <paramref name="calculateCost"/> function.
        /// This combinator works for streams when elements have different cost(length).
        /// Streams of <see cref="ByteString"/> for example.
        /// 
        /// Throttle implements the token bucket model. There is a bucket with a given token capacity (burst size or maximumBurst).
        /// Tokens drops into the bucket at a given rate and can be spared for later use up to bucket capacity
        /// to allow some burstiness. Whenever stream wants to send an element, it takes as many
        /// tokens from the bucket as element cost. If there isn't any, throttle waits until the
        /// bucket accumulates enough tokens. Elements that costs more than the allowed burst will be delayed proportionally
        /// to their cost minus available tokens, meeting the target rate.
        /// 
        /// Parameter <paramref name="mode"/> manages behaviour when upstream is faster than throttle rate:
        /// <para/> - <see cref="ThrottleMode.Shaping"/> makes pauses before emitting messages to meet throttle rate
        /// <para/> - <see cref="ThrottleMode.Enforcing"/> fails with exception when upstream is faster than throttle rate. Enforcing
        ///  cannot emit elements that cost more than the maximumBurst
        /// <para>
        /// Emits when upstream emits an element and configured time per each element elapsed
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="cost">TBD</param>
        /// <param name="per">TBD</param>
        /// <param name="maximumBurst">TBD</param>
        /// <param name="calculateCost">TBD</param>
        /// <param name="mode">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when one of the following conditions is met.
        /// <ul>
        /// <li>The specified <paramref name="cost"/> is less than or equal to zero</li>
        /// <li>The specified <paramref name="per"/> timeout is equal to <see cref="TimeSpan.Zero"/>.</li>
        /// <li>The specified <paramref name="maximumBurst"/> is less than or equal zero in <see cref="ThrottleMode.Enforcing"/> <paramref name="mode"/>.</li>
        /// <li>The <see cref="TimeSpan.Ticks"/> in the specified <paramref name="per"/> is less than the specified <paramref name="cost"/>.</li>
        /// </ul>
        /// </exception>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> Throttle<T, TMat>(this IFlow<T, TMat> flow, int cost, TimeSpan per,
            int maximumBurst, Func<T, int> calculateCost, ThrottleMode mode)
        {
            if(cost <= 0) throw new ArgumentException("cost must be > 0", nameof(cost));
            if (per == TimeSpan.Zero) throw new ArgumentException("Throttle per timeout must not be zero", nameof(per));
            if (mode == ThrottleMode.Enforcing && maximumBurst < 0)
                throw new ArgumentException("Throttle maximumBurst must be > 0 in Enforcing mode", nameof(maximumBurst));
            if (per.Ticks < cost)
                throw new ArgumentException("Rates larger than 1 unit / tick are not supported", nameof(cost));

            return flow.Via(new Throttle<T>(cost, per, maximumBurst, calculateCost, mode));
        }

        /// <summary>
        /// Detaches upstream demand from downstream demand without detaching the
        /// stream rates; in other words acts like a buffer of size 1.
        ///
        /// Emits when upstream emits an element
        ///
        /// Backpressures when downstream backpressures
        ///
        /// Completes when upstream completes
        ///
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> Detach<T, TMat>(this IFlow<T, TMat> flow)
        {
            return flow.Via(new Fusing.Detacher<T>());
        }

        /// <summary>
        /// Delays the initial element by the specified duration.
        /// <para>
        /// Emits when upstream emits an element if the initial delay is already elapsed
        /// </para>
        /// Backpressures when downstream backpressures or initial delay is not yet elapsed
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="delay">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> InitialDelay<T, TMat>(this IFlow<T, TMat> flow, TimeSpan delay)
        {
            return flow.Via(new DelayInitial<T>(delay));
        }

        /// <summary>
        /// Logs elements flowing through the stream as well as completion and erroring.
        /// 
        /// By default element and completion signals are logged on debug level, and errors are logged on Error level.
        /// This can be adjusted according to your needs by providing a custom <see cref="Attributes.LogLevels"/> attribute on the given Flow.
        /// <para>
        /// Emits when the mapping function returns an element
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="name">TBD</param>
        /// <param name="extract">TBD</param>
        /// <param name="log">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> Log<T, TMat>(this IFlow<T, TMat> flow, string name, Func<T, object> extract = null,
            ILoggingAdapter log = null)
        {
            return flow.Via(new Fusing.Log<T>(name, extract ?? Identity<T>(), log));
        }

        /// <summary>
        /// Combine the elements of current flow and the given <see cref="Source{TOut,TMat}"/> into a stream of tuples.
        /// <para>
        /// Emits when all of the inputs has an element available
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when any upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T1">TBD</typeparam>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<(T1, T2), TMat> Zip<T1, T2, TMat>(this IFlow<T1, TMat> flow,
            IGraph<SourceShape<T2>, TMat> other)
        {
            return flow.Via(ZipGraph<T1, T2, TMat>(other));
        }

        private static IGraph<FlowShape<T1, (T1, T2)>, TMat> ZipGraph<T1, T2, TMat>(
            IGraph<SourceShape<T2>, TMat> other)
        {
            return GraphDsl.Create(other, (builder, shape) =>
            {
                var zip = builder.Add(new Zip<T1, T2>());
                builder.From(shape).To(zip.In1);
                return new FlowShape<T1, (T1, T2)>(zip.In0, zip.Out);
            });
        }

        /// <summary>
        /// Put together the elements of current flow and the given <see cref="Source{TOut,TMat}"/>
        /// into a stream of combined elements using a combiner function.
        /// <para>
        /// Emits when all of the inputs has an element available
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when any upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T1">TBD</typeparam>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="T3">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T3, TMat> ZipWith<T1, T2, T3, TMat>(this IFlow<T1, TMat> flow,
            IGraph<SourceShape<T2>, TMat> other, Func<T1, T2, T3> combine)
        {
            return flow.Via(ZipWithGraph(other, combine));
        }

        private static IGraph<FlowShape<T1, T3>, TMat> ZipWithGraph<T1, T2, T3, TMat>(
            IGraph<SourceShape<T2>, TMat> other, Func<T1, T2, T3> combine)
        {
            return GraphDsl.Create(other, (builder, shape) =>
            {
                var zip = builder.Add(new ZipWith<T1, T2, T3>(combine));
                var r = builder.From(shape);
                r.To(zip.In1);
                return new FlowShape<T1, T3>(zip.In0, zip.Out);
            });
        }

        /// <summary>
        /// Combine the elements of current flow into a stream of tuples consisting
        /// of all elements paired with their index. Indices start at 0.
        /// 
        /// <para/>
        /// Emits when upstream emits an element and is paired with their index
        /// <para/>
        /// Backpressures when downstream backpressures
        /// <para/>
        /// Completes when upstream completes
        /// <para/>
        /// Cancels when downstream cancels
        /// </summary>
        public static IFlow<(T1, long), TMat> ZipWithIndex<T1, TMat>(this IFlow<T1, TMat> flow)
        {
            return flow.StatefulSelectMany<T1, (T1, long), TMat>(() =>
            {
                var index = 0L;
                return element => new[] {(element, index++)};
            });
        }

        /// <summary>
        /// Interleave is a deterministic merge of the given <see cref="Source{TOut,TMat}"/> with elements of this <see cref="IFlow{TOut,TMat}"/>.
        /// It first emits <paramref name="segmentSize"/> number of elements from this flow to downstream, then - same amount for <paramref name="graph"/>
        /// source, then repeat process.
        ///  
        /// After one of upstreams is complete than all the rest elements will be emitted from the second one
        /// 
        /// If it gets error from one of upstreams - stream completes with failure.
        /// <para>
        /// Emits when element is available from the currently consumed upstream
        /// </para>
        /// Backpressures when downstream backpressures. Signal to current
        /// upstream, switch to next upstream when received <paramref name="segmentSize"/> elements
        /// <para>
        /// Completes when the <see cref="IFlow{TOut,TMat}"/> and given <see cref="Source{TOut,TMat}"/> completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <example>
        /// <code>
        /// Source(List(1, 2, 3)).Interleave(List(4, 5, 6, 7), 2) // 1, 2, 4, 5, 3, 6, 7
        /// </code>
        /// </example>
        /// <typeparam name="T1">TBD</typeparam>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="graph">TBD</param>
        /// <param name="segmentSize">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T2, TMat> Interleave<T1, T2, TMat>(this IFlow<T1, TMat> flow,
            IGraph<SourceShape<T2>, TMat> graph, int segmentSize) where T1 : T2
        {
            return flow.Via(InterleaveGraph<T1, T2, TMat>(graph, segmentSize));
        }

        /// <summary>
        /// Interleave is a deterministic merge of the given <see cref="Source{TOut,TMat}"/> with elements of this <see cref="IFlow{T,TMat}"/>.
        /// It first emits <paramref name="segmentSize"/> number of elements from this flow to downstream, then - same amount for <paramref name="graph"/> source,
        /// then repeat process.
        ///
        /// After one of upstreams is complete than all the rest elements will be emitted from the second one
        ///
        /// If it gets error from one of upstreams - stream completes with failure.
        ///
        /// @see<see cref="Interleave{TIn,TOut}"/>.
        ///
        /// It is recommended to use the internally optimized <see cref="Keep.Left{TLeft,TRight}"/> and <see cref="Keep.Right{TLeft,TRight}"/> combiners
        /// where appropriate instead of manually writing functions that pass through one of the values.
        /// </summary>
        /// <typeparam name="T1">TBD</typeparam>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="graph">TBD</param>
        /// <param name="segmentSize">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T2, TMat3> InterleaveMaterialized<T1, T2, TMat, TMat2, TMat3>(this IFlow<T1, TMat> flow,
            IGraph<SourceShape<T2>, TMat2> graph, int segmentSize, Func<TMat, TMat2, TMat3> combine) where T1 : T2
        {
            return flow.ViaMaterialized(InterleaveGraph<T1, T2, TMat2>(graph, segmentSize), combine);
        }

        private static IGraph<FlowShape<T1, T2>, TMat> InterleaveGraph<T1, T2, TMat>(
            IGraph<SourceShape<T2>, TMat> graph, int segmentSize) where T1 : T2
        {
            return GraphDsl.Create(graph, (builder, shape) =>
            {
                // TODO Use Dsl.Interleave.Create
                var interleave = builder.Add(new Interleave<T1, T2>(2, segmentSize));
                var r = builder.From(shape);
                r.To(interleave.In(1));
                return new FlowShape<T1, T2>(interleave.In(0), interleave.Out);
            });
        }

        /// <summary>
        /// Merge the given <see cref="Source{TOut,TMat}"/> to this <see cref="IFlow{TOut,TMat}"/>, taking elements as they arrive from input streams,
        /// picking randomly when several elements ready.
        /// <para>
        /// Emits when one of the inputs has an element available
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when all upstreams complete
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <param name="eagerComplete">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> Merge<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow,
            IGraph<SourceShape<TOut>, TMat> other, bool eagerComplete = false) where TIn : TOut
        {
            return flow.Via(MergeGraph<TIn, TOut, TMat>(other, eagerComplete));
        }

        /// <summary>
        /// Merge the given <see cref="Source{TOut,TMat}"/> to this <see cref="IFlow{TOut,TMat}"/>, taking elements as they arrive from input streams,
        /// picking randomly when several elements ready.
        /// 
        /// @see <see cref="Merge{TIn,TOut,TMat}"/>
        /// 
        /// It is recommended to use the internally optimized <see cref="Keep.Left{TLeft,TRight}"/> and <see cref="Keep.Right{TLeft,TRight}"/> combiners
        /// where appropriate instead of manually writing functions that pass through one of the values.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="that">TBD</param>
        /// <param name="combine">TBD</param>
        /// <param name="eagerComplete">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat3> MergeMaterialized<TIn, TOut, TMat, TMat2, TMat3>(this IFlow<TIn, TMat> flow,
            IGraph<SourceShape<TOut>, TMat2> that, Func<TMat, TMat2, TMat3> combine, bool eagerComplete = false)
            where TIn : TOut
        {
            return flow.ViaMaterialized(MergeGraph<TIn, TOut, TMat2>(that, eagerComplete), combine);
        }

        private static IGraph<FlowShape<TIn, TOut>, TMat> MergeGraph<TIn, TOut, TMat>(
            IGraph<SourceShape<TOut>, TMat> other, bool eagerComplete = false) where TIn : TOut
        {
            return GraphDsl.Create(other, (builder, shape) =>
            {
                var merge = builder.Add(new Merge<TIn, TOut>(2, eagerComplete));
                var r = builder.From(shape);
                r.To(merge.In(1));
                return new FlowShape<TIn, TOut>(merge.In(0), merge.Out);
            });
        }

        /// <summary>
        /// Merge the given <see cref="Source{TOut,TMat}"/> to this <see cref="IFlow{TOut,TMat}"/>, taking elements as they arrive from input streams,
        /// picking always the smallest of the available elements(waiting for one element from each side
        /// to be available). This means that possible contiguity of the input streams is not exploited to avoid
        /// waiting for elements, this merge will block when one of the inputs does not have more elements(and
        /// does not complete).
        /// <para>
        /// Emits when one of the inputs has an element available
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when all upstreams complete
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <param name="orderFunc">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> MergeSorted<T, TMat>(this IFlow<T, TMat> flow, IGraph<SourceShape<T>, TMat> other,
            Func<T, T, int> orderFunc)
        {
            return flow.Via(MergeSortedGraph(other, orderFunc));
        }

        /// <summary>
        /// Merge the given <see cref="Source{TOut,TMat}"/> to this <see cref="IFlow{TOut,TMat}"/>, taking elements as they arrive from input streams,
        /// picking always the smallest of the available elements(waiting for one element from each side
        /// to be available). This means that possible contiguity of the input streams is not exploited to avoid
        /// waiting for elements, this merge will block when one of the inputs does not have more elements(and
        /// does not complete).
        /// <para>
        /// Emits when one of the inputs has an element available
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when all upstreams complete
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> MergeSorted<T, TMat>(this IFlow<T, TMat> flow, IGraph<SourceShape<T>, TMat> other)
            where T : IComparable<T>
        {
            return flow.Via(MergeSortedGraph(other, (x, y) => x.CompareTo(y)));
        }

        /// <summary>
        /// Merge the given <see cref="Source{TOut,TMat}"/> to this <see cref="IFlow{TOut,TMat}"/>, taking elements as they arrive from input streams,
        /// picking always the smallest of the available elements(waiting for one element from each side
        /// to be available). This means that possible contiguity of the input streams is not exploited to avoid
        /// waiting for elements, this merge will block when one of the inputs does not have more elements(and
        /// does not complete).
        /// <para>
        /// Emits when one of the inputs has an element available
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when all upstreams complete
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <param name="comparer">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> MergeSorted<T, TMat>(this IFlow<T, TMat> flow, IGraph<SourceShape<T>, TMat> other,
            IComparer<T> comparer)
        {
            return flow.Via(MergeSortedGraph(other, comparer.Compare));
        }

        private static IGraph<FlowShape<T, T>, TMat> MergeSortedGraph<T, TMat>(IGraph<SourceShape<T>, TMat> other,
            Func<T, T, int> compare)
        {
            return GraphDsl.Create(other, (builder, r) =>
            {
                var merge = builder.Add(new MergeSorted<T>(compare));
                builder.From(r).To(merge.In1);
                return new FlowShape<T, T>(merge.In0, merge.Out);
            });
        }

        /// <summary>
        /// Concatenate the given <see cref="Source{TOut,TMat}"/> to this <see cref="IFlow{TOut,TMat}"/>, meaning that once this
        /// Flow’s input is exhausted and all result elements have been generated,
        /// the Source’s elements will be produced.
        /// 
        /// Note that the <see cref="Source{TOut,TMat}"/> is materialized together with this <see cref="IFlow{TOut,TMat}"/> and just kept
        /// from producing elements by asserting back-pressure until its time comes.
        /// 
        /// If this <see cref="IFlow{TOut,TMat}"/> gets upstream error - no elements from the given <see cref="Source{TOut,TMat}"/> will be pulled.
        /// <para>
        /// Emits when element is available from current stream or from the given <see cref="Source{TOut,TMat}"/> when current is completed
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when given <see cref="Source{TOut,TMat}"/> completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        public static IFlow<T, TMat> Concat<T, TMat>(this IFlow<T, TMat> flow,
            IGraph<SourceShape<T>, TMat> other)
        {
            return flow.Via(ConcatGraph(other));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        internal static IGraph<FlowShape<T, T>, TMat> ConcatGraph<T, TMat>(
            IGraph<SourceShape<T>, TMat> other)
        {
            return GraphDsl.Create(other, (builder, shape) =>
            {
                var merge = builder.Add(Dsl.Concat.Create<T>());
                var r = builder.From(shape);
                r.To(merge.In(1));
                return new FlowShape<T, T>(merge.In(0), merge.Out);
            });
        }

        /// <summary>
        /// Prepend the given <see cref="Source{TOut,TMat}"/> to this <see cref="IFlow{TOut,TMat}"/>, meaning that before elements
        /// are generated from this <see cref="IFlow{TOut,TMat}"/>, the Source's elements will be produced until it
        /// is exhausted, at which point Flow elements will start being produced.
        ///
        /// Note that this <see cref="IFlow{TOut,TMat}"/> will be materialized together with the <see cref="Source{TOut,TMat}"/> and just kept
        /// from producing elements by asserting back-pressure until its time comes.
        ///
        /// If the given <see cref="Source{TOut,TMat}"/> gets upstream error - no elements from this <see cref="IFlow{TOut,TMat}"/> will be pulled.
        ///
        /// Emits when element is available from the given <see cref="Source{TOut,TMat}"/> or from current stream when the <see cref="Source{TOut,TMat}"/> is completed
        ///
        /// Backpressures when downstream backpressures
        ///
        /// Completes when this <see cref="IFlow{TOut,TMat}"/> completes
        ///
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="that">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> Prepend<TIn, TOut, TMat>(this IFlow<TIn, TMat> flow,
            IGraph<SourceShape<TOut>, TMat> that) where TIn : TOut
        {
            return flow.Via(PrependGraph<TIn, TOut, TMat>(that));
        }

        private static IGraph<FlowShape<TIn, TOut>, TMat> PrependGraph<TIn, TOut, TMat>(
            IGraph<SourceShape<TOut>, TMat> that) where TIn : TOut
        {
            return GraphDsl.Create(that, (builder, shape) =>
            {
                // TODO use Dsl.Concat.Create
                var merge = builder.Add(new Concat<TIn, TOut>());
                builder.From(shape).To(merge.In(0));
                return new FlowShape<TIn, TOut>(merge.In(1), merge.Out);
            });
        }

        /// <summary>
        /// Provides a secondary source that will be consumed if this stream completes without any
        /// elements passing by. As soon as the first element comes through this stream, the alternative
        /// will be cancelled.
        ///
        /// Note that this Flow will be materialized together with the <see cref="Source{TOut,TMat}"/> and just kept
        /// from producing elements by asserting back-pressure until its time comes or it gets
        /// cancelled.
        ///
        /// On errors the stage is failed regardless of source of the error.
        ///
        /// '''Emits when''' element is available from first stream or first stream closed without emitting any elements and an element
        ///                  is available from the second stream
        ///
        /// '''Backpressures when''' downstream backpressures
        ///
        /// '''Completes when''' the primary stream completes after emitting at least one element, when the primary stream completes
        ///                      without emitting and the secondary stream already has completed or when the secondary stream completes
        ///
        /// '''Cancels when''' downstream cancels and additionally the alternative is cancelled as soon as an element passes
        ///                    by from this stream.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="secondary">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat> OrElse<T, TMat>(this IFlow<T, TMat> flow, IGraph<SourceShape<T>, TMat> secondary)
            => flow.Via(OrElseGraph(secondary));

        private static IGraph<FlowShape<T, T>, TMat> OrElseGraph<T, TMat>(IGraph<SourceShape<T>, TMat> secondary)
        {
            return GraphDsl.Create(secondary, (builder, shape) =>
            {
                var orElse = builder.Add(Dsl.OrElse.Create<T>());
                builder.From(shape).To(orElse.In(1));
                return new FlowShape<T, T>(orElse.In(0), orElse.Out);
            });
        }

        /// <summary>
        /// Provides a secondary source that will be consumed if this stream completes without any
        /// elements passing by. As soon as the first element comes through this stream, the alternative
        /// will be cancelled.
        ///
        /// Note that this Flow will be materialized together with the <see cref="Source{TOut,TMat}"/> and just kept
        /// from producing elements by asserting back-pressure until its time comes or it gets
        /// cancelled.
        ///
        /// On errors the stage is failed regardless of source of the error.
        ///
        /// '''Emits when''' element is available from first stream or first stream closed without emitting any elements and an element
        ///                  is available from the second stream
        ///
        /// '''Backpressures when''' downstream backpressures
        ///
        /// '''Completes when''' the primary stream completes after emitting at least one element, when the primary stream completes
        ///                      without emitting and the secondary stream already has completed or when the secondary stream completes
        ///
        /// '''Cancels when''' downstream cancels and additionally the alternative is cancelled as soon as an element passes
        ///                    by from this stream.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="secondary">TBD</param>
        /// <param name="materializedFunction">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat3> OrElseMaterialized<T, TMat, TMat2, TMat3>(this IFlow<T, TMat> flow, IGraph<SourceShape<T>, TMat2> secondary, Func<TMat, TMat2, TMat3> materializedFunction)
            => flow.ViaMaterialized(OrElseGraph(secondary), materializedFunction);

        /// <summary>
        /// Attaches the given <seealso cref="Sink{TIn,TMat}"/> to this <see cref="IFlow{TOut,TMat}"/>, meaning that elements that passes
        /// through will also be sent to the <seealso cref="Sink{TIn,TMat}"/>.
        /// 
        /// @see <seealso cref="AlsoTo{TOut,TMat}"/>
        /// 
        /// It is recommended to use the internally optimized <seealso cref="Keep.Left{TLeft,TRight}"/> and <seealso cref="Keep.Right{TLeft,TRight}"/> combiners
        /// where appropriate instead of manually writing functions that pass through one of the values.
        /// </summary>
        public static IFlow<TOut, TMat3> AlsoToMaterialized<TOut, TMat, TMat2, TMat3>(
            this IFlow<TOut, TMat> flow, IGraph<SinkShape<TOut>, TMat2> that,
            Func<TMat, TMat2, TMat3> materializerFunction)
        {
            return flow.ViaMaterialized(AlsoToGraph(that), materializerFunction);
        }

        /// <summary>
        /// Attaches the given <seealso cref="Sink{TIn,TMat}"/> to this <see cref="IFlow{TOut,TMat}"/>, meaning that elements that passes
        /// through will also be sent to the <seealso cref="Sink{TIn,TMat}"/>.
        /// 
        /// Emits when element is available and demand exists both from the Sink and the downstream.
        ///
        /// Backpressures when downstream or Sink backpressures
        ///
        /// Completes when upstream completes
        ///
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="that">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<TOut, TMat> AlsoTo<TOut, TMat>(this IFlow<TOut, TMat> flow, IGraph<SinkShape<TOut>, TMat> that)
        {
            return flow.Via(AlsoToGraph(that));
        }

        private static IGraph<FlowShape<TOut, TOut>, TMat> AlsoToGraph<TOut, TMat>(IGraph<SinkShape<TOut>, TMat> that)
        {
            return GraphDsl.Create(that, (b, r) =>
            {
                var broadcast = b.Add(new Broadcast<TOut>(2));
                b.From(broadcast.Out(1)).To(r);
                return new FlowShape<TOut, TOut>(broadcast.In, broadcast.Out(0));
            });
        }

        ///<summary>
        /// Materializes to <see cref="Task{NotUsed}"/> that completes on getting termination message.
        /// The task completes with success when received complete message from upstream or cancel
        /// from downstream. It fails with the same error when received error message from
        /// downstream.
        ///
        /// It is recommended to use the internally optimized <see cref="Keep.Left{TLeft,TRight}"/> and <see cref="Keep.Right{TLeft,TRight}"/> combiners
        /// where appropriate instead of manually writing functions that pass through one of the values.
        ///</summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="materializerFunction">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat2> WatchTermination<T, TMat, TMat2>(this IFlow<T, TMat> flow,
            Func<TMat, Task, TMat2> materializerFunction)
        {
            return flow.ViaMaterialized(Fusing.GraphStages.TerminationWatcher<T>(), materializerFunction);
        }

        /// <summary>
        /// Materializes to <see cref="IFlowMonitor"/> that allows monitoring of the the current flow. All events are propagated
        /// by the monitor unchanged. Note that the monitor inserts a memory barrier every time it processes an
        /// event, and may therefor affect performance.
        /// The <paramref name="combine"/> function is used to combine the <see cref="IFlowMonitor"/> with this flow's materialized value.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public static IFlow<T, TMat2> Monitor<T, TMat, TMat2>(this IFlow<T, TMat> flow,
            Func<TMat, IFlowMonitor, TMat2> combine)
        {
            return flow.ViaMaterialized(new Fusing.MonitorFlow<T>(), combine);
        }

        /// <summary>
        /// The operator fails with an <see cref="WatchedActorTerminatedException"/> if the target actor is terminated.
        /// 
        /// '''Emits when''' upstream emits 
        /// '''Backpressures when''' downstream backpressures 
        /// '''Completes when''' upstream completes 
        /// '''Fails when''' the watched actor terminates 
        /// '''Cancels when''' downstream cancels
        /// </summary>
        public static IFlow<T, TMat> Watch<T, TMat>(this IFlow<T, TMat> flow, IActorRef actorRef) => flow.Via(new Fusing.Watch<T>(actorRef));

        //TODO: there is no HKT in .NET, so we cannot simply do `to` method, which evaluates to either Source ⇒ IRunnableGraph, or Flow ⇒ Sink

    }
}
