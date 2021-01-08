//-----------------------------------------------------------------------
// <copyright file="IntervalBasedRateLimiter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Annotations;

namespace Akka.Streams.Dsl
{
    public static class IntervalBasedRateLimiter
    {
        /// <summary>
        /// Specialized type of rate limiter which emits batches of elements (with size limited by the <paramref name="maxBatchSize"/> parameter)
        /// with a minimum time interval of <paramref name="minInterval"/>.
        /// <para />
        /// Because the next emit is scheduled after we downstream the current batch, the effective throughput,
        /// depending on the minimal interval length, may never reach the maximum allowed one.
        /// You can minimize these delays by sending bigger batches less often.
        /// </summary>
        /// <typeparam name="T">type of element</typeparam>
        /// <param name="minInterval">minimal pause to be kept before downstream the next batch. Should be >= 10 milliseconds.</param>
        /// <param name="maxBatchSize">maximum number of elements to send in the single batch</param>
        /// <returns></returns>
        [ApiMayChange]
        public static IGraph<FlowShape<T, IEnumerable<T>>, NotUsed> Create<T>(TimeSpan minInterval, int maxBatchSize)
        {
            return Flow.Create<T>().GroupedWithin(maxBatchSize, minInterval).Via(new DelayFlow<IEnumerable<T>>(minInterval));
        }
    }
}
