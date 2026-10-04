//-----------------------------------------------------------------------
// <copyright file="Implicits.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Dsl;

namespace Akka.Streams.Extra
{
    /// <summary>
    /// Provides time measurement utilities on Stream elements.
    /// 
    /// See <see cref="Extra.Timed"/>
    /// </summary>
    public static class TimedSourceDsl
    {
        /// <summary>
        /// Measures time from receiving the first element and completion events - one for each subscriber of this <see cref="IFlow{TOut,TMat}"/>.
        /// </summary>
        /// <typeparam name="TIn">The element type of the source and input to the measured operations.</typeparam>
        /// <typeparam name="TOut">The element type emitted by the measured operations.</typeparam>
        /// <typeparam name="TMat">The source's materialized value type.</typeparam>
        /// <typeparam name="TMat2">The materialized value type returned by the measured operations.</typeparam>
        /// <param name="source">The source whose elements pass through the measured operations.</param>
        /// <param name="measuredOps">The source transformation whose elapsed processing time is measured.</param>
        /// <param name="onComplete">The callback that receives elapsed time when the measured stream terminates.</param>
        /// <returns>The transformed source with the materialized value returned by <paramref name="measuredOps"/>.</returns>
        public static Source<TOut, TMat2> Timed<TIn, TOut, TMat, TMat2>(this Source<TIn, TMat> source,
            Func<Source<TIn, TMat>, Source<TOut, TMat2>> measuredOps, Action<TimeSpan> onComplete)
            => TimedOps.Timed(source, measuredOps, onComplete);

        /// <summary>
        /// Measures rolling interval between immediately subsequent "matching(o: O)" elements.
        /// </summary>
        /// <typeparam name="TIn">The source element type.</typeparam>
        /// <typeparam name="TMat">The source's materialized value type.</typeparam>
        /// <param name="source">The source whose matching elements are timed.</param>
        /// <param name="matching">Selects the elements used to measure consecutive intervals.</param>
        /// <param name="onInterval">The callback that receives each measured interval.</param>
        /// <returns>The source with its element and materialized value types preserved.</returns>
        public static Source<TIn, TMat> TimedIntervalBetween<TIn, TMat>(this Source<TIn, TMat> source,
            Func<TIn, bool> matching, Action<TimeSpan> onInterval)
            => (Source<TIn, TMat>)TimedIntervalBetweenOps.TimedIntervalBetween(source, matching, onInterval);
    }

    /// <summary>
    /// Provides time measurement utilities on Stream elements.
    /// 
    /// See <see cref="Extra.Timed"/>
    /// </summary>
    public static class TimedFlowDsl
    {
        /// <summary>
        /// Measures time from receiving the first element and completion events - one for each subscriber of this <see cref="IFlow{TOut,TMat}"/>.
        /// </summary>
        /// <typeparam name="TIn">The input element type of the flow.</typeparam>
        /// <typeparam name="TOut">The output element type of the flow before applying <paramref name="measuredOps"/>.</typeparam>
        /// <typeparam name="TOut2">The output element type produced by the measured operations.</typeparam>
        /// <typeparam name="TMat">The flow's materialized value type.</typeparam>
        /// <typeparam name="TMat2">The materialized value type returned by the measured operations.</typeparam>
        /// <param name="flow">The flow whose output passes through the measured operations.</param>
        /// <param name="measuredOps">The flow transformation whose elapsed processing time is measured.</param>
        /// <param name="onComplete">The callback that receives elapsed time when the measured stream terminates.</param>
        /// <returns>The transformed flow with the materialized value returned by <paramref name="measuredOps"/>.</returns>
        public static Flow<TIn, TOut2, TMat2> Timed<TIn, TOut, TOut2, TMat, TMat2>(this Flow<TIn, TOut, TMat> flow,
            Func<Flow<TIn, TOut, TMat>, Flow<TIn, TOut2, TMat2>> measuredOps, Action<TimeSpan> onComplete)
            => TimedOps.Timed(flow, measuredOps, onComplete);

        /// <summary>
        /// Measures rolling interval between immediately subsequent "matching(o: O)" elements.
        /// </summary>
        /// <typeparam name="TIn">The input element type of the flow.</typeparam>
        /// <typeparam name="TOut">The output element type whose matching values are timed.</typeparam>
        /// <typeparam name="TMat">The flow's materialized value type.</typeparam>
        /// <param name="flow">The flow whose matching output elements are timed.</param>
        /// <param name="matching">Selects the output elements used to measure consecutive intervals.</param>
        /// <param name="onInterval">The callback that receives each measured interval.</param>
        /// <returns>The flow with its input, output, and materialized value types preserved.</returns>
        public static Flow<TIn, TOut, TMat> TimedIntervalBetween<TIn, TOut, TMat>(this Flow<TIn, TOut, TMat> flow,
            Func<TOut, bool> matching, Action<TimeSpan> onInterval)
            => (Flow<TIn, TOut, TMat>)TimedIntervalBetweenOps.TimedIntervalBetween(flow, matching, onInterval);
    }
}
