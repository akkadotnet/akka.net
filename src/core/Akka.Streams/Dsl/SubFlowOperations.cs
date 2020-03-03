//-----------------------------------------------------------------------
// <copyright file="SubFlowOperations.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Event;
using Akka.IO;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.Util;
using Akka.Util;

// ReSharper disable UnusedMember.Global

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class SubFlowOperations
    {
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
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="partialFunc">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<Option<TOut>, TMat, TClosed> Recover<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, Func<Exception, Option<TOut>> partialFunc)
        {
            return (SubFlow<Option<TOut>, TMat, TClosed>)InternalFlowOperations.Recover(flow, partialFunc);
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
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="partialFunc">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Use RecoverWithRetries instead. [1.1.2]")]
        public static SubFlow<TOut, TMat, TClosed> RecoverWith<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow,
            Func<Exception, IGraph<SourceShape<TOut>, TMat>> partialFunc)
        {
            return RecoverWithRetries(flow, partialFunc, -1);
        }

        /// <summary>
        /// RecoverWithRetries  allows to switch to alternative Source on flow failure. It will stay in effect after
        /// a failure has been recovered up to <paramref name="attempts"/> number of times so that each time there is a failure it is fed into the <paramref name="partialFunc"/> and a new
        /// Source may be materialized. Note that if you pass in 0, this won't attempt to recover at all. Passing in -1 will behave exactly the same as  <see cref="RecoverWithRetries{TOut,TMat,TClosed}"/>.
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
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="partialFunc">Receives the failure cause and returns the new Source to be materialized if any</param>
        /// <param name="attempts">Maximum number of retries or -1 to retry indefinitely</param>
        /// <exception cref="ArgumentException">if <paramref name="attempts"/> is a negative number other than -1</exception>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> RecoverWithRetries<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow,
            Func<Exception, IGraph<SourceShape<TOut>, TMat>> partialFunc, int attempts)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.RecoverWithRetries(flow, partialFunc, attempts);
        }

        /// <summary>
        /// While similar to <see cref="Recover{TOut,TMat,TClosed}"/> this stage can be used to transform an error signal to a different one without logging
        /// it as an error in the process. So in that sense it is NOT exactly equivalent to Recover(e => throw e2) since Recover
        /// would log the e2 error. 
        /// <para>
        /// Since the underlying failure signal onError arrives out-of-band, it might jump over existing elements.
        /// This stage can recover the failure signal, but not the skipped elements, which will be dropped.
        /// </para>
        /// Similarily to <see cref="Recover{TOut,TMat,TClosed}"/> throwing an exception inside SelectError will be logged.
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
        public static SubFlow<TOut, TMat, TClosed> SelectError<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, Func<Exception, Exception> selector)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.SelectError(flow, selector);
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
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="mapper">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Select<TIn, TOut, TMat, TClosed>(this SubFlow<TIn, TMat, TClosed> flow, Func<TIn, TOut> mapper)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Select(flow, mapper);
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
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="mapConcater">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> SelectMany<TOut1, TOut2, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow, Func<TOut1, IEnumerable<TOut2>> mapConcater)
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.SelectMany(flow, mapConcater);
        }

        /// <summary>
        /// Transform each input element into an Enumerable of output elements that is
        /// then flattened into the output stream. The transformation is meant to be stateful,
        /// which is enabled by creating the transformation function <paramref name="mapConcaterFactory"/> a new for every materialization —
        /// the returned function will typically close over mutable objects to store state between
        /// invocations. For the stateless variant see <see cref="SelectMany{TOut1,TOut2,TMat,TClosed}"/>.
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
        /// See also <see cref="SelectMany{TOut1,TOut2,TMat,TClosed}"/>
        /// </summary>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="mapConcaterFactory">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> StatefulSelectMany<TOut1, TOut2, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow,
            Func<Func<TOut1, IEnumerable<TOut2>>> mapConcaterFactory)
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.StatefulSelectMany(flow, mapConcaterFactory);
        }

        /// <summary>
        /// Transform this stream by applying the given function <paramref name="asyncMapper"/> to each of the elements
        /// as they pass through this processing step. The function returns a <see cref="Task{TOut}"/> and the
        /// value of that task will be emitted downstream. The number of tasks
        /// that shall run in parallel is given as the first argument to <see cref="SelectAsync{TIn,TOut,TMat,TClosed}"/>.
        /// These tasks may complete in any order, but the elements that
        /// are emitted downstream are in the same order as received from upstream.
        /// 
        /// If the group by function <paramref name="asyncMapper"/> throws an exception or if the <see cref="Task"/> is completed
        /// with failure and the supervision decision is <see cref="Directive.Stop"/>
        /// the stream will be completed with failure.
        /// 
        /// If the group by function <paramref name="asyncMapper"/> throws an exception or if the <see cref="Task"/> is completed
        /// with failure and the supervision decision is <see cref="Directive.Resume"/> or
        /// <see cref="Directive.Restart"/> the element is dropped and the stream continues.
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
        /// <seealso cref="SelectAsyncUnordered{TIn,TOut,TMat,TClosed}"/>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="parallelism">TBD</param>
        /// <param name="asyncMapper">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> SelectAsync<TIn, TOut, TMat, TClosed>(this SubFlow<TIn, TMat, TClosed> flow, int parallelism, Func<TIn, Task<TOut>> asyncMapper)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.SelectAsync(flow, parallelism, asyncMapper);
        }

        /// <summary>
        /// Transform this stream by applying the given function <paramref name="asyncMapper"/> to each of the elements
        /// as they pass through this processing step. The function returns a <see cref="Task"/> and the
        /// value of that task will be emitted downstream.  The number of tasks
        /// that shall run in parallel is given as the first argument to <see cref="SelectAsyncUnordered{TIn,TOut,TMat,TClosed}"/>.
        /// Each processed element will be emitted dowstream
        /// as soon as it is ready, i.e. it is possible that the elements are not emitted downstream
        /// in the same order as received from upstream.
        /// 
        /// If the group by function <paramref name="asyncMapper"/> throws an exception or if the <see cref="Task"/> is completed
        /// with failure and the supervision decision is <see cref="Directive.Stop"/>
        /// the stream will be completed with failure.
        /// 
        /// If the group by function <paramref name="asyncMapper"/> throws an exception or if the<see cref="Task"/> is completed
        /// with failure and the supervision decision is <see cref="Directive.Resume"/> or
        /// <see cref="Directive.Restart"/> the element is dropped and the stream continues.
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
        /// <seealso cref="SelectAsync{TIn,TOut,TMat,TClosed}"/>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="parallelism">TBD</param>
        /// <param name="asyncMapper">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> SelectAsyncUnordered<TIn, TOut, TMat, TClosed>(this SubFlow<TIn, TMat, TClosed> flow, int parallelism, Func<TIn, Task<TOut>> asyncMapper)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.SelectAsyncUnordered(flow, parallelism, asyncMapper);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="predicate">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Where<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, Predicate<TOut> predicate)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Where(flow, predicate);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="predicate">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> WhereNot<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, Predicate<TOut> predicate)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.WhereNot(flow, predicate);
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
        /// <seealso cref="Limit{T,TMat,TClosed}"/> <seealso cref="LimitWeighted{T,TMat,TClosed}"/>
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="predicate">TBD</param>
        /// <param name="inclusive">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> TakeWhile<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, Predicate<TOut> predicate, bool inclusive = false)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.TakeWhile(flow, predicate, inclusive);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="predicate">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> SkipWhile<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, Predicate<TOut> predicate)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.SkipWhile(flow, predicate);
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
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="collector">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> Collect<TOut1, TOut2, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow, Func<TOut1, TOut2> collector)
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.Collect(flow, collector);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="n">TBD</param>
        /// <exception cref="ArgumentException">Thrown, if <paramref name="n"/> is less than or equal zero.</exception>
        /// <returns>TBD</returns>
        public static SubFlow<IEnumerable<TOut>, TMat, TClosed> Grouped<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, int n)
        {
            return (SubFlow<IEnumerable<TOut>, TMat, TClosed>)InternalFlowOperations.Grouped(flow, n);
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
        /// <seealso cref="Take{TOut,TMat,TClosed}"/>
        /// <seealso cref="TakeWithin{TOut,TMat,TClosed}"/>
        /// <seealso cref="TakeWhile{TOut,TMat,TClosed}"/>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="max">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<T, TMat, TClosed> Limit<T, TMat, TClosed>(this SubFlow<T, TMat, TClosed> flow, long max)
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
        /// <seealso cref="Take{TOut,TMat,TClosed}"/>
        /// <seealso cref="TakeWithin{TOut,TMat,TClosed}"/>
        /// <seealso cref="TakeWhile{TOut,TMat,TClosed}"/>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="costFunc">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<T, TMat, TClosed> LimitWeighted<T, TMat, TClosed>(this SubFlow<T, TMat, TClosed> flow, long max, Func<T, long> costFunc)
        {
            return (SubFlow<T, TMat, TClosed>) InternalFlowOperations.LimitWeighted(flow, max, costFunc);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="n">TBD</param>
        /// <param name="step">TBD</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="n"/> or <paramref name="step"/> is less than or equal zero.</exception>
        /// <returns>TBD</returns>
        public static SubFlow<IEnumerable<TOut>, TMat, TClosed> Sliding<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, int n, int step = 1)
        {
            return (SubFlow<IEnumerable<TOut>, TMat, TClosed>)InternalFlowOperations.Sliding(flow, n, step);
        }

        /// <summary>
        /// Similar to <see cref="Aggregate{TOut1,TOut2,TMat,TClosed}"/> but is not a terminal operation,
        /// emits its current value which starts at <paramref name="zero"/> and then
        /// applies the current and next value to the given function <paramref name="scan"/>,
        /// emitting the next current value.
        /// 
        /// If the function <paramref name="scan"/> throws an exception and the supervision decision is
        /// <see cref="Directive.Restart"/> current value starts at <paramref name="zero"/> again
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
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="zero">TBD</param>
        /// <param name="scan">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> Scan<TOut1, TOut2, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow, TOut2 zero, Func<TOut2, TOut1, TOut2> scan)
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.Scan(flow, zero, scan);
        }

        /// <summary>
        /// Similar to <see cref="Scan{TOut1,TOut2,TMat,TClosed}"/> but with a asynchronous function,
        /// emits its current value which starts at <paramref name="zero"/> and then
        /// applies the current and next value to the given function <paramref name="scan"/>
        /// emitting a <see cref="Task{TOut}"/> that resolves to the next current value.
        /// 
        /// If the function <paramref name="scan"/> throws an exception and the supervision decision is
        /// <see cref="Directive.Restart"/> current value starts at <paramref name="zero"/> again
        /// the stream will continue.
        /// 
        /// If the function <paramref name="scan"/> throws an exception and the supervision decision is
        /// <see cref="Directive.Resume"/> current value starts at the previous
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
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="zero">TBD</param>
        /// <param name="scan">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> ScanAsync<TOut1, TOut2, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow, TOut2 zero, Func<TOut2, TOut1, Task<TOut2>> scan)
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.ScanAsync(flow, zero, scan);
        }

        /// <summary>
        /// Similar to <see cref="Scan{TOut1,TOut2,TMat,TClosed}"/> but only emits its result when the upstream completes,
        /// after which it also completes. Applies the given function <paramref name="fold"/> towards its current and next value,
        /// yielding the next current value.
        /// 
        /// If the function <paramref name="fold"/> throws an exception and the supervision decision is
        /// <see cref="Directive.Restart"/> current value starts at <paramref name="zero"/> again
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
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="zero">TBD</param>
        /// <param name="fold">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> Aggregate<TOut1, TOut2, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow, TOut2 zero, Func<TOut2, TOut1, TOut2> fold)
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.Aggregate(flow, zero, fold);
        }

        /// <summary>
        /// Similar to <see cref="Aggregate{TIn,TOut1,TOut2,TMat}"/> but with an asynchronous function.
        /// Applies the given function towards its current and next value,
        /// yielding the next current value.
        /// 
        /// If the function <paramref name="fold"/> returns a failure and the supervision decision is
        /// <see cref="Directive.Restart"/> current value starts at <paramref name="zero"/> again
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
        /// <seealso cref="Aggregate{TIn,TOut1,TOut2,TMat}"/>
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="zero">TBD</param>
        /// <param name="fold">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> AggregateAsync<TIn, TOut, TMat, TClosed>(this SubFlow<TIn, TMat, TClosed> flow, TOut zero,
            Func<TOut, TIn, Task<TOut>> fold)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.AggregateAsync(flow, zero, fold);
        }

        /// <summary>
        /// Similar to <see cref="Aggregate{TOut1,TOut2,TMat,TClosed}"/> but uses first element as zero element.
        /// Applies the given function <paramref name="reduce"/> towards its current and next value,
        /// yielding the next current value. 
        /// 
        /// If the stream is empty (i.e. completes before signaling any elements),
        /// the sum stage will fail its downstream with a <see cref="NoSuchElementException"/>,
        /// which is semantically in-line with that standard library collections do in such situations.
        /// <para>
        /// Emits when upstream completes
        /// </para>
        /// Backpressures when downstream backpressures
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="reduce">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Sum<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, Func<TOut, TOut, TOut> reduce)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Sum(flow, reduce);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="start">TBD</param>
        /// <param name="inject">TBD</param>
        /// <param name="end">TBD</param>
        /// <exception cref="ArgumentNullException">Thrown when any of the <paramref name="start"/>, <paramref name="inject"/> or <paramref name="end"/> is undefined.</exception>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Intersperse<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, TOut start, TOut inject, TOut end)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Intersperse(flow, start, inject, end);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="inject">TBD</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inject"/> is undefined.</exception>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Intersperse<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, TOut inject)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Intersperse(flow, inject);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="n">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <exception cref="ArgumentException">Thrown if <paramref name="n"/> is less than or equal zero or <paramref name="timeout"/> is <see cref="TimeSpan.Zero"/>.</exception>
        /// <returns>TBD</returns>
        public static SubFlow<IEnumerable<TOut>, TMat, TClosed> GroupedWithin<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, int n, TimeSpan timeout)
        {
            return (SubFlow<IEnumerable<TOut>, TMat, TClosed>)InternalFlowOperations.GroupedWithin(flow, n, timeout);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="of">Time to shift all messages.</param>
        /// <param name="strategy">Strategy that is used when incoming elements cannot fit inside the buffer</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Delay<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, TimeSpan of, DelayOverflowStrategy? strategy = null)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Delay(flow, of, strategy);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Skip<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, long n)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Skip(flow, n);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="duration">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> SkipWithin<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, TimeSpan duration)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.SkipWithin(flow, duration);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Take<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, long n)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Take(flow, n);
        }

        /// <summary>
        /// Terminate processing (and cancel the upstream publisher) after the given
        /// duration. Due to input buffering some elements may have been
        /// requested from upstream publishers that will then not be processed downstream
        /// of this step.
        /// 
        /// Note that this can be combined with <see cref="Take{TOut,TMat,TClosed}"/> to limit the number of elements
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="duration">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> TakeWithin<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, TimeSpan duration)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.TakeWithin(flow, duration);
        }

        /// <summary>
        /// Allows a faster upstream to progress independently of a slower subscriber by conflating elements into a summary
        /// until the subscriber is ready to accept them. For example a conflate step might average incoming numbers if the
        /// upstream publisher is faster.
        /// 
        /// This version of conflate allows to derive a seed from the first element and change the aggregated type to
        /// be different than the input type. See <see cref="Conflate{TOut,TMat,TClosed}"/> for a simpler version that does not change types.
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TSeed">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="seed">Provides the first state for a conflated value using the first unconsumed element as a start</param> 
        /// <param name="aggregate">Takes the currently aggregated value and the current pending element to produce a new aggregate</param>
        /// <returns>TBD</returns>
        public static SubFlow<TSeed, TMat, TClosed> ConflateWithSeed<TOut, TMat, TSeed, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, Func<TOut, TSeed> seed, Func<TSeed, TOut, TSeed> aggregate)
        {
            return (SubFlow<TSeed, TMat, TClosed>)InternalFlowOperations.ConflateWithSeed(flow, seed, aggregate);
        }

        /// <summary>
        /// Allows a faster upstream to progress independently of a slower subscriber by conflating elements into a summary
        /// until the subscriber is ready to accept them. For example a conflate step might average incoming numbers if the
        /// upstream publisher is faster.
        /// 
        /// This version of conflate does not change the output type of the stream. See <see cref="ConflateWithSeed{TOut,TMat,TSeed,TClosed}"/>
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="aggregate">Takes the currently aggregated value and the current pending element to produce a new aggregate</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Conflate<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, Func<TOut, TOut, TOut> aggregate)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Conflate(flow, aggregate);
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
        /// See also <seealso cref="ConflateWithSeed{TOut,TMat,TSeed,TClosed}"/>, <seealso cref="BatchWeighted{TOut,TOut2,TMat,TClosed}"/>
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="max">maximum number of elements to batch before backpressuring upstream (must be positive non-zero)</param>
        /// <param name="seed">Provides the first state for a batched value using the first unconsumed element as a start</param>
        /// <param name="aggregate">Takes the currently batched value and the current pending element to produce a new aggregate</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> Batch<TOut, TOut2, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, long max,
            Func<TOut, TOut2> seed, Func<TOut2, TOut, TOut2> aggregate)
        {
            return (SubFlow<TOut2, TMat, TClosed>) InternalFlowOperations.Batch(flow, max, seed, aggregate);
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
        /// See also <seealso cref="ConflateWithSeed{TOut,TMat,TSeed,TClosed}"/>, <seealso cref="Batch{TOut,TOut2,TMat,TClosed}"/>
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="max">maximum weight of elements to batch before backpressuring upstream (must be positive non-zero)</param>
        /// <param name="costFunction">a function to compute a single element weight</param>
        /// <param name="seed">Provides the first state for a batched value using the first unconsumed element as a start</param>
        /// <param name="aggregate">Takes the currently batched value and the current pending element to produce a new aggregate</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> BatchWeighted<TOut, TOut2, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, long max, Func<TOut, long> costFunction,
            Func<TOut, TOut2> seed, Func<TOut2, TOut, TOut2> aggregate)
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.BatchWeighted(flow, max, costFunction, seed, aggregate);
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
        /// Expand does not support <see cref="Directive.Restart"/> and <see cref="Directive.Resume"/>.
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
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="extrapolate">Takes the current extrapolation state to produce an output element and the next extrapolation state.</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> Expand<TOut1, TOut2, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow, Func<TOut1, IEnumerator<TOut2>> extrapolate)
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.Expand(flow, extrapolate);
        }

        /// <summary>
        /// Adds a fixed size buffer in the flow that allows to store elements from a faster upstream until it becomes full.
        /// Depending on the defined <see cref="OverflowStrategy"/> it might drop elements or backpressure the upstream if
        /// there is no space available
        /// <para>
        /// Emits when downstream stops backpressuring and there is a pending element in the buffer
        /// </para>
        /// Backpressures when downstream backpressures or depending on OverflowStrategy:
        /// <para/> * Backpressure - backpressures when buffer is full
        /// <para/> * DropHead, DropTail, DropBuffer - never backpressures
        /// <para/> * Fail - fails the stream if buffer gets full
        /// <para>
        /// Completes when upstream completes and buffered elements has been drained
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="size">The size of the buffer in element count</param>
        /// <param name="strategy">Strategy that is used when incoming elements cannot fit inside the buffer</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Buffer<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, int size, OverflowStrategy strategy)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Buffer(flow, size, strategy);
        }

        /// <summary>
        /// Generic transformation of a stream with a custom processing <see cref="IStage{TIn, TOut}"/>.
        /// This operator makes it possible to extend the <see cref="Flow"/> API when there is no specialized
        /// operator that performs the transformation.
        /// </summary>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="stageFactory">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Use Via(GraphStage) instead. [1.1.2]")]
        public static SubFlow<TOut2, TMat, TClosed> Transform<TOut1, TOut2, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow, Func<IStage<TOut1, TOut2>> stageFactory)
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.Transform(flow, stageFactory);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<(IImmutableList<TOut>, Source<TOut, NotUsed>), TMat, TClosed> PrefixAndTail<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, int n)
        {
            return (SubFlow<(IImmutableList<TOut>, Source<TOut, NotUsed>), TMat, TClosed>)InternalFlowOperations.PrefixAndTail(flow, n);
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
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="flatten">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> ConcatMany<TOut1, TOut2, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow, Func<TOut1, IGraph<SourceShape<TOut2>, TMat>> flatten)
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.ConcatMany(flow, flatten);
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
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="breadth">TBD</param>
        /// <param name="flatten">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> MergeMany<TOut1, TOut2, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed>  flow, int breadth, Func<TOut1, IGraph<SourceShape<TOut2>, TMat>> flatten)
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.MergeMany(flow, breadth, flatten);
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
        public static SubFlow<(TOut1, long), TMat, TClosed> ZipWithIndex<TOut1, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow)
        {
            return (SubFlow<(TOut1, long), TMat, TClosed>)InternalFlowOperations.ZipWithIndex(flow);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> InitialTimeout<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, TimeSpan timeout)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.InitialTimeout(flow, timeout);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> CompletionTimeout<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, TimeSpan timeout)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.CompletionTimeout(flow, timeout);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> IdleTimeout<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, TimeSpan timeout)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.IdleTimeout(flow, timeout);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> BackpressureTimeout<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, TimeSpan timeout)
        {
            return (SubFlow<TOut, TMat, TClosed>) InternalFlowOperations.BackpressureTimeout(flow, timeout);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TInjected">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="injectElement">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TInjected, TMat, TClosed> KeepAlive<TOut, TInjected, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, TimeSpan timeout, Func<TInjected> injectElement) where TOut : TInjected
        {
            return (SubFlow<TInjected, TMat, TClosed>)InternalFlowOperations.KeepAlive(flow, timeout, injectElement);
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
        /// Backpressures when downstream backpressures or the incoming rate is higher than the speed limit
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="elements">TBD</param>
        /// <param name="per">TBD</param>
        /// <param name="maximumBurst">TBD</param>
        /// <param name="mode">TBD</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="elements"/> is less than or equal zero, 
        /// or <paramref name="per"/> timeout is equal <see cref="TimeSpan.Zero"/> 
        /// or <paramref name="maximumBurst"/> is less than or equal zero in in <see cref="ThrottleMode.Enforcing"/> <paramref name="mode"/>.</exception>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Throttle<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, int elements, TimeSpan per, int maximumBurst, ThrottleMode mode)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Throttle(flow, elements, per, maximumBurst, mode);
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
        /// Backpressures when downstream backpressures or the incoming rate is higher than the speed limit
        /// <para>
        /// Completes when upstream completes
        /// </para>
        /// Cancels when downstream cancels
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="cost">TBD</param>
        /// <param name="per">TBD</param>
        /// <param name="maximumBurst">TBD</param>
        /// <param name="calculateCost">TBD</param>
        /// <param name="mode">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Throttle<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, int cost, TimeSpan per, int maximumBurst, Func<TOut, int> calculateCost, ThrottleMode mode)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Throttle(flow, cost, per, maximumBurst, calculateCost, mode);
        }

        /// <summary>
        /// Attaches the given <seealso cref="Sink{TIn,TMat}"/> to this <see cref="IFlow{TOut,TMat}"/>, meaning that elements that passes
        /// through will also be sent to the <seealso cref="Sink{TIn,TMat}"/>.
        /// 
        /// @see <seealso cref="AlsoTo{TOut,TMat,TClosed}"/>
        /// 
        /// It is recommended to use the internally optimized <seealso cref="Keep.Left{TLeft,TRight}"/> and <seealso cref="Keep.Right{TLeft,TRight}"/> combiners
        /// where appropriate instead of manually writing functions that pass through one of the values.
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="that">TBD</param>
        /// <param name="materializerFunction">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat3, TClosed> AlsoToMaterialized<TOut, TMat, TMat2, TMat3, TClosed>(
            this SubFlow<TOut, TMat, TClosed> flow, IGraph<SinkShape<TOut>, TMat2> that,
            Func<TMat, TMat2, TMat3> materializerFunction)
        {
            return (SubFlow<TOut, TMat3, TClosed>) InternalFlowOperations.AlsoToMaterialized(flow, that, materializerFunction);
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
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="that">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> AlsoTo<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, IGraph<SinkShape<TOut>, TMat> that)
        {
            return (SubFlow<TOut, TMat, TClosed>) InternalFlowOperations.AlsoTo(flow, that);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="materializerFunction">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat2, TClosed> WatchTermination<TOut, TMat, TMat2, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, Func<TMat, Task, TMat2> materializerFunction)
        {
            return (SubFlow<TOut, TMat2, TClosed>) InternalFlowOperations.WatchTermination(flow, materializerFunction);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Detach<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Detach(flow);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="delay">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> InitialDelay<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, TimeSpan delay)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.InitialDelay(flow, delay);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="name">TBD</param>
        /// <param name="extract">TBD</param>
        /// <param name="log">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Log<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, string name, Func<TOut, object> extract = null, ILoggingAdapter log = null)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Log(flow, name, extract, log);
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
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<(T1, T2), TMat, TClosed> Zip<T1, T2, TMat, TClosed>(this SubFlow<T1, TMat, TClosed> flow, IGraph<SourceShape<T2>, TMat> other)
        {
            return (SubFlow<(T1, T2), TMat, TClosed>)InternalFlowOperations.Zip(flow, other);
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
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<T3, TMat, TClosed> ZipWith<T1, T2, T3, TMat, TClosed>(this SubFlow<T1, TMat, TClosed> flow, IGraph<SourceShape<T2>, TMat> other, Func<T1, T2, T3> combine)
        {
            return (SubFlow<T3, TMat, TClosed>)InternalFlowOperations.ZipWith(flow, other, combine);
        }

        /// <summary>
        /// Interleave is a deterministic merge of the given <see cref="Source{TOut,TMat}"/> with elements of this <see cref="IFlow{TOut,TMat}"/>.
        /// It first emits <paramref name="segmentSize"/> number of elements from this flow to downstream, then - same amount for <paramref name="other"/>
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
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <param name="segmentSize">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<T2, TMat, TClosed> Interleave<T1, T2, TMat, TClosed>(this SubFlow<T1, TMat, TClosed> flow, IGraph<SourceShape<T2>, TMat> other, int segmentSize) where T1 : T2
        {
            return (SubFlow<T2, TMat, TClosed>)InternalFlowOperations.Interleave(flow, other, segmentSize);
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
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="graph">TBD</param>
        /// <param name="segmentSize">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<T2, TMat3, TClosed> InterleaveMaterialized<T1, T2, TMat, TMat2, TMat3, TClosed>(this SubFlow<T1, TMat, TClosed> flow,
            IGraph<SourceShape<T2>, TMat2> graph, int segmentSize, Func<TMat, TMat2, TMat3> combine) where T1 : T2
        {
            return (SubFlow<T2, TMat3, TClosed>)InternalFlowOperations.InterleaveMaterialized(flow, graph, segmentSize, combine);
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
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <param name="eagerComplete">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> Merge<TOut1, TOut2, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow, IGraph<SourceShape<TOut2>, TMat> other, bool eagerComplete = false) where TOut1 : TOut2
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.Merge(flow, other, eagerComplete);
        }


        /// <summary>
        /// Merge the given <see cref="Source{TOut,TMat}"/> to this <see cref="IFlow{TOut,TMat}"/>, taking elements as they arrive from input streams,
        /// picking randomly when several elements ready.
        /// 
        /// @see <see cref="Merge{TOut1,TOut2,TMat,TClosed}"/>
        /// 
        /// It is recommended to use the internally optimized <see cref="Keep.Left{TLeft,TRight}"/> and <see cref="Keep.Right{TLeft,TRight}"/> combiners
        /// where appropriate instead of manually writing functions that pass through one of the values.
        /// </summary>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="that">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat3, TClosed> MergeMaterialized<TOut1, TOut2, TMat, TMat2, TMat3, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow,
            IGraph<SourceShape<TOut2>, TMat2> that, Func<TMat, TMat2, TMat3> combine)
            where TOut1 : TOut2
        {
            return (SubFlow<TOut2, TMat3, TClosed>)InternalFlowOperations.MergeMaterialized(flow, that, combine);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <param name="orderFunc">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> MergeSorted<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, IGraph<SourceShape<TOut>, TMat> other, Func<TOut, TOut, int> orderFunc)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.MergeSorted(flow, other, orderFunc);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> MergeSorted<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, IGraph<SourceShape<TOut>, TMat> other)
            where TOut : IComparable<TOut>
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.MergeSorted(flow, other);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <param name="comparer">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> MergeSorted<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, IGraph<SourceShape<TOut>, TMat> other, IComparer<TOut> comparer)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.MergeSorted(flow, other, comparer);
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut, TMat, TClosed> Concat<TOut, TMat, TClosed>(this SubFlow<TOut, TMat, TClosed> flow, IGraph<SourceShape<TOut>, TMat> other)
        {
            return (SubFlow<TOut, TMat, TClosed>)InternalFlowOperations.Concat(flow, other);
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
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="that">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<TOut2, TMat, TClosed> Prepend<TOut1, TOut2, TMat, TClosed>(this SubFlow<TOut1, TMat, TClosed> flow,
            IGraph<SourceShape<TOut2>, TMat> that) where TOut1 : TOut2
        {
            return (SubFlow<TOut2, TMat, TClosed>)InternalFlowOperations.Prepend(flow, that);
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
        /// <typeparam name="TClosed">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="secondary">TBD</param>
        /// <returns>TBD</returns>
        public static SubFlow<T, TMat, TClosed> OrElse<T, TMat, TClosed>(this SubFlow<T, TMat, TClosed> flow, IGraph<SourceShape<T>, TMat> secondary)
            => (SubFlow<T, TMat, TClosed>)InternalFlowOperations.OrElse(flow, secondary);
    }
}
