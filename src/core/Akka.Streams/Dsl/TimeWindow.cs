//-----------------------------------------------------------------------
// <copyright file="TimeWindow.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Annotations;

namespace Akka.Streams.Dsl
{
    public static class TimeWindow
    {
        /// <summary>
        /// EXPERIMENTAL API       
        /// <para>
        /// Aggregates data for predefined amount of time. The computed aggregate is emitted after window expires, thereafter a new window starts.        
        /// <para/>
        /// For example:        
        /// <code>
        /// Source.Tick(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), 1)
        ///     .Via(TimeWindow.Create&lt;int, int&gt;(TimeSpan.FromMilliseconds(500), x => x, (x, y) => x + y, eager: false));
        /// </code>        
        /// will emit sum of 1s after each 500ms - so you could expect a stream of 5s if timers were ideal
        /// If "eager" had been true in the following example, you would have observed initial 1 with no delay, followed by stream of sums
        /// </para>
        /// </summary>
        /// <typeparam name="T">type of incoming element</typeparam>
        /// <typeparam name="TState">type of outgoing (aggregated) element</typeparam>
        /// <param name="duration">window duration</param>
        /// <param name="seed">provides the initial state when a new window starts</param>
        /// <param name="aggregate">produces updated aggregate</param>
        /// <param name="eager">if true emits the very first seed with no delay, otherwise the first element emitted is the result of the first time-window aggregation</param>
        /// <returns>flow implementing time-window aggregation</returns>
        [ApiMayChange]
        public static Flow<T, TState, NotUsed> Create<T, TState>(TimeSpan duration, Func<T, TState> seed, Func<TState, T, TState> aggregate, bool eager = true)
        {
            return Flow.Create<T>().ConflateWithSeed(seed, aggregate).Via(new Pulse<TState>(duration, eager));
        }
    }
}
