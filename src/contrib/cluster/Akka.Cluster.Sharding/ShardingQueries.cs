//-----------------------------------------------------------------------
// <copyright file="ShardingQueries.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;

    /// <summary>
    /// The result of a group query and metadata.
    /// </summary>
    internal sealed class ShardsQueryResult<T>
    {
        /// <summary>
        ///
        /// </summary>
        /// <param name="failed">
        /// the queries to shards that failed or did not reply within the configured
        /// `timeout`. This could be indicative of several states, for example still
        /// in initialization, restart, heavily loaded and busy, where returning
        /// zero entities is not indicative of the reason
        /// </param>
        /// <param name="responses">the responses received from the query</param>
        /// <param name="total">the total number of shards tracked versus a possible subset</param>
        /// <param name="timeout">the timeout used to query the shards per region, for reporting metadata</param>
        public ShardsQueryResult(
              IImmutableSet<ShardId> failed,
              IImmutableList<T> responses,
              int total,
              TimeSpan timeout)
        {
            Failed = failed;
            Responses = responses;
            Total = total;
            Timeout = timeout;
        }

        /// <summary>
        /// The number of shards queried, which could equal the `total` or,
        /// be a subset if this was a retry of those that failed.
        /// </summary>
        public int Queried => Failed.Count + Responses.Count;

        public IImmutableSet<string> Failed { get; }

        public IImmutableList<T> Responses { get; }

        public int Total { get; }

        public TimeSpan Timeout { get; }

        public override string ToString()
        {
            if (Total == 0)
                return "Shard region had zero shards to gather metadata from.";
            else
            {
                var shardsOf = (Queried < Total) ? $"shards of [{Total}]:" : "shards:";
                return $"Queried [{Queried}] {shardsOf} [{Responses.Count}] responsive, [{Failed.Count}] failed after {Timeout}.";
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ps">the results of actors queried that did not reply by
        ///     the timeout or returned another failure and those that did</param>
        /// <param name="total">the total number of actors tracked versus a possible subset</param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public static ShardsQueryResult<T> Create(IImmutableList<(ShardId shard, Task<T> task)> ps, int total, TimeSpan timeout)
        {
            return new ShardsQueryResult<T>(
                failed: ps.Where(i => i.task.IsCanceled || i.task.IsFaulted).Select(i => i.shard).ToImmutableHashSet(),
                responses: ps.Where(i => !i.task.IsCanceled && !i.task.IsFaulted).Select(i => i.task.Result).ToImmutableList(),
                total, timeout);
        }
    }
}
