//-----------------------------------------------------------------------
// <copyright file="HashCodeMessageExtractorBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Benchmarks.Configurations;
using Akka.Cluster.Sharding;
using BenchmarkDotNet.Attributes;

namespace Akka.Cluster.Benchmarks.Sharding
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class HashCodeMessageExtractorBenchmarks
    {
        private ShardingEnvelope _m1 = new ShardingEnvelope("foo", 1);
        private ShardedMessage _m2 = new ShardedMessage("foo", 1);
        private IMessageExtractor _extractor = new ShardMessageExtractor();

        [Benchmark]
        public void RouteShardEnvelope()
        {
            _extractor.EntityId(_m1);
            _extractor.EntityMessage(_m1);
            _extractor.ShardId(_m1);
        }
        
        [Benchmark]
        public void RouteTypedMessage()
        {
            _extractor.EntityId(_m2);
            _extractor.EntityMessage(_m2);
            _extractor.ShardId(_m2);
        }
    }
}