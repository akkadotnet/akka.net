// //-----------------------------------------------------------------------
// // <copyright file="ShardSpawnBenchmarks.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster.Sharding;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using static Akka.Cluster.Benchmarks.Sharding.ShardingHelper;

namespace Akka.Cluster.Benchmarks.Sharding
{
    [Config(typeof(MonitoringConfig))]
    [SimpleJob(RunStrategy.ColdStart, targetCount:1, warmupCount:0, launchCount:5)]
    public class ShardSpawnBenchmarks
    {
        [Params(StateStoreMode.Persistence, StateStoreMode.DData)]
        public StateStoreMode StateMode;

        [Params(1000)]
        public int EntityCount;

        [Params(true, false)]
        public bool RememberEntities;

        public int BatchSize = 20;

        private ActorSystem _sys1;
        private ActorSystem _sys2;

        private IActorRef _shardRegion1;
        private IActorRef _shardRegion2;

        public static int _shardRegionId = 0;
        
        
        [GlobalSetup]
        public async Task Setup()
        {
            var config = StateMode switch
            {
                StateStoreMode.Persistence => CreatePersistenceConfig(RememberEntities),
                StateStoreMode.DData => CreateDDataConfig(RememberEntities),
                _ => null
            };

            _sys1 = ActorSystem.Create("BenchSys", config);
            _sys2 = ActorSystem.Create("BenchSys", config);

            var c1 = Cluster.Get(_sys1);
            var c2 = Cluster.Get(_sys2);

            await c1.JoinAsync(c1.SelfAddress);
            await c2.JoinAsync(c1.SelfAddress);
        }

        [IterationSetup]
        public void IterationSetup()
        {
            /*
             * Create a new set of shard regions each time, so all of the shards are freshly allocated
             * on each benchmark run. 
             */
            _shardRegion1 = StartShardRegion(_sys1, "entities" + _shardRegionId);
            _shardRegion2 = StartShardRegion(_sys2, "entities" + _shardRegionId);
            _shardRegionId++;
        }

        [Benchmark]
        public async Task SpawnEntities()
        {
            for (var i = 0; i < EntityCount; i++)
            {
                var msg = new ShardedMessage(i.ToString(), i);
                await _shardRegion1.Ask<ShardedMessage>(msg);
            }
        }
        
        [GlobalCleanup]
        public async Task Cleanup()
        {
            var t2 = CoordinatedShutdown.Get(_sys2).Run(CoordinatedShutdown.ActorSystemTerminateReason.Instance);
            var t1 = CoordinatedShutdown.Get(_sys1).Run(CoordinatedShutdown.ActorSystemTerminateReason.Instance);
           
            await Task.WhenAll(t1, t2);
        }
    }
}