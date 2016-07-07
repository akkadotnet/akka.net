//-----------------------------------------------------------------------
// <copyright file="Implicits.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public static Source<TOut, TMat2> Timed<TIn, TOut, TMat, TMat2>(this Source<TIn, TMat> source,
            Func<Source<TIn, TMat>, Source<TOut, TMat2>> measuredOps, Action<TimeSpan> onComplete)
            => TimedOps.Timed(source, measuredOps, onComplete);

        /// <summary>
        /// Measures rolling interval between immediately subsequent "matching(o: O)" elements.
        /// </summary>
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
        public static Flow<TIn, TOut2, TMat2> Timed<TIn, TOut, TOut2, TMat, TMat2>(this Flow<TIn, TOut, TMat> flow,
            Func<Flow<TIn, TOut, TMat>, Flow<TIn, TOut2, TMat2>> measuredOps, Action<TimeSpan> onComplete)
            => TimedOps.Timed(flow, measuredOps, onComplete);

        /// <summary>
        /// Measures rolling interval between immediately subsequent "matching(o: O)" elements.
        /// </summary>
        public static Flow<TIn, TOut, TMat> TimedIntervalBetween<TIn, TOut, TMat>(this Flow<TIn, TOut, TMat> flow,
            Func<TOut, bool> matching, Action<TimeSpan> onInterval)
            => (Flow<TIn, TOut, TMat>)TimedIntervalBetweenOps.TimedIntervalBetween(flow, matching, onInterval);
    }
}