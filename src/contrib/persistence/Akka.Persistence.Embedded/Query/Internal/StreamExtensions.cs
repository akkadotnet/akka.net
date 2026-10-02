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
        public static Source<TElem, NotUsed> Flatten<TElem>(this Source<IReadOnlyList<TElem>, NotUsed> batches)
            where TElem : class
            => batches.Buffer(1, OverflowStrategy.Backpressure).SelectMany(static (IReadOnlyList<TElem> xs) => xs);
    }
}
