//-----------------------------------------------------------------------
// <copyright file="StreamExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Generic;
using Akka.Streams;
using Akka.Streams.Dsl;

namespace Akka.Persistence.Embedded.Query.Internal
{
    internal static class StreamExtensions
    {
        /// <summary>
        /// Flattens batches into elements. The one-element buffer after the flatten lets a finished query complete right
        /// after its last element, and it reads one element ahead, not one batch.
        /// </summary>
        /// <remarks>
        /// The buffer is load-bearing. <c>UnfoldAsync</c> learns that a query is over only when it is pulled, and
        /// <c>SelectMany</c> pulls only on downstream demand. Without the buffer, a consumer that requests exactly N
        /// elements and then waits for completion (the TCK does) waits forever. With it, the stage pulls as soon as it has
        /// handed on the last element of a batch, so the next query runs once the current batch is drained.
        /// Cost: a stream that is cancelled may already have issued one more query.
        /// Akka.Persistence.Sql gets the same effect without a buffer operator: its by-id query has a
        /// <c>SelectAsync(1, ...)</c> after its <c>UnfoldAsync</c> and <c>SelectMany</c>, and its tag and all-events queries
        /// have <c>ConcatMany</c>.
        /// </remarks>
        public static Source<TElem, NotUsed> Flatten<TElem>(this Source<IReadOnlyList<TElem>, NotUsed> batches)
            where TElem : class
            => batches.SelectMany(static (IReadOnlyList<TElem> xs) => xs).Buffer(1, OverflowStrategy.Backpressure);
    }
}
