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
        /// Flattens batches into elements. The one-element buffer pulls the next batch as soon as the current one
        /// is handed on, so a finished query completes right after its last element instead of waiting for more demand.
        /// </summary>
        /// <remarks>
        /// Why: <c>UnfoldAsync</c> learns that a query is over only when it is pulled, and <c>SelectMany</c> pulls only on
        /// downstream demand. A consumer that asked for exactly N elements and then waits for completion (the TCK does)
        /// would wait forever. Cost: one batch is read ahead of demand, and a stream that is cancelled may already have
        /// issued one more query. Spec 9.9 does not list this operator; it is a deliberate deviation.
        /// </remarks>
        public static Source<TElem, NotUsed> Flatten<TElem>(this Source<IReadOnlyList<TElem>, NotUsed> batches)
            where TElem : class
            => batches.Buffer(1, OverflowStrategy.Backpressure).SelectMany(static (IReadOnlyList<TElem> xs) => xs);
    }
}
