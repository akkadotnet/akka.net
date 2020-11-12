//-----------------------------------------------------------------------
// <copyright file="ShardingQueriesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Cluster.Sharding.Shard;

namespace Akka.Cluster.Sharding.Tests
{
    public class ShardingQueriesSpec : AkkaSpec
    {
        private ImmutableHashSet<string> shards;
        private ImmutableHashSet<string> failures;
        private readonly TimeSpan timeout;

        private static Config SpecConfig =>
            ClusterSingletonManager.DefaultConfig()
            .WithFallback(ClusterSharding.DefaultConfig());

        public ShardingQueriesSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            shards = ImmutableHashSet.Create("a", "b", "busy");
            failures = ImmutableHashSet.Create("busy");
            timeout = ClusterShardingSettings.Create(Sys).ShardRegionQueryTimeout;
        }

        private bool NonEmpty<T>(ShardsQueryResult<T> qr) =>
            qr.Total > 0 && qr.Queried > 0;

        private bool IsTotalFailed<T>(ShardsQueryResult<T> qr) =>
            NonEmpty(qr) && qr.Failed.Count == qr.Total;

        private bool IsAllSubsetFailed<T>(ShardsQueryResult<T> qr) =>
            NonEmpty(qr) && qr.Queried < qr.Total && qr.Failed.Count == qr.Queried;

        [Fact]
        public void ShardsQueryResult_must_reflect_nothing_to_acquire_metadata_from_0_shards()
        {
            var qr = ShardsQueryResult<ShardState>.Create(ImmutableList<(string shard, Task<ShardState> task)>.Empty, 0, timeout);
            qr.Total.Should().Be(qr.Queried);
            IsTotalFailed(qr).Should().BeFalse(); // you'd have to make > 0 attempts in order to fail
            IsAllSubsetFailed(qr).Should().BeFalse(); // same
            qr.ToString().Should().Be("Shard region had zero shards to gather metadata from.");
        }

        [Fact]
        public void ShardsQueryResult_must_partition_failures_and_responses_by_type_and_by_convention_failed_Left_T_Right()
        {
            void Assert<T>(ImmutableHashSet<(string shard, Task<T> task)> responses)
            {
                var results = responses.Union(failures.Select(i => (i, Task.FromException<T>(new AskTimeoutException("failed"))))).ToImmutableList();
                var qr = ShardsQueryResult<T>.Create(results, shards.Count, timeout);
                qr.Failed.Should().BeEquivalentTo(failures);
                qr.Responses.Should().BeEquivalentTo(responses.Select(i => i.task.Result));
                IsTotalFailed(qr).Should().BeFalse();
                IsAllSubsetFailed(qr).Should().BeFalse();
                qr.ToString().Should().Be($"Queried [3] shards: [2] responsive, [1] failed after {timeout}.");
            }

            Assert<ShardStats>(ImmutableHashSet.Create<(string shard, Task<ShardStats> task)>(
                ("a", Task.FromResult(new ShardStats("a", 1))),
                ("b", Task.FromResult(new ShardStats("b", 1)))));

            Assert<CurrentShardState>(ImmutableHashSet.Create<(string shard, Task<CurrentShardState> task)>(
                ("a", Task.FromResult(new CurrentShardState("a", ImmutableHashSet.Create("a1")))),
                ("b", Task.FromResult(new CurrentShardState("b", ImmutableHashSet.Create("b1"))))));
        }

        [Fact]
        public void ShardsQueryResult_must_detect_a_subset_query_not_all_queried()
        {
            void Assert<T>(ImmutableHashSet<(string shard, Task<T> task)> responses)
            {
                var results = responses.Union(failures.Select(i => (i, Task.FromException<T>(new AskTimeoutException("failed"))))).ToImmutableList();
                var qr = ShardsQueryResult<T>.Create(results, shards.Count + 1, timeout);
                (qr.Total > qr.Queried).Should().BeTrue();
                (qr.Queried < shards.Count).Should().BeFalse();
                qr.ToString().Should().Be($"Queried [3] shards of [4]: [2] responsive, [1] failed after {timeout}.");
            }

            Assert<ShardStats>(ImmutableHashSet.Create<(string shard, Task<ShardStats> task)>(
                ("a", Task.FromResult(new ShardStats("a", 1))),
                ("b", Task.FromResult(new ShardStats("b", 1)))));

            Assert<CurrentShardState>(ImmutableHashSet.Create<(string shard, Task<CurrentShardState> task)>(
                ("a", Task.FromResult(new CurrentShardState("a", ImmutableHashSet.Create("a1")))),
                ("b", Task.FromResult(new CurrentShardState("b", ImmutableHashSet.Create("b1"))))));
        }

        [Fact]
        public void ShardsQueryResult_must_partition_when_all_failed()
        {
            var results = ImmutableList.Create<(string shard, Task<ShardState> task)>(
                ("c", Task.FromException<ShardState>(new AskTimeoutException("failed"))),
                ("d", Task.FromCanceled<ShardState>(new System.Threading.CancellationToken(true))));
            var qr = ShardsQueryResult<ShardState>.Create(results, results.Count, timeout);
            qr.Total.Should().Be(qr.Queried);
            IsTotalFailed(qr).Should().BeTrue();
            IsAllSubsetFailed(qr).Should().BeFalse(); // not a subset
            qr.ToString().Should().Be($"Queried [2] shards: [0] responsive, [2] failed after {timeout}.");
        }
    }
}
