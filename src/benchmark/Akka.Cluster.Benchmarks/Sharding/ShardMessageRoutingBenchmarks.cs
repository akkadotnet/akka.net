//-----------------------------------------------------------------------
// <copyright file="ShardMessageRoutingBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster.Sharding;
using Akka.Routing;
using BenchmarkDotNet.Attributes;
using static Akka.Cluster.Benchmarks.Sharding.ShardingHelper;

namespace Akka.Cluster.Benchmarks.Sharding
{
    [Config(typeof(MonitoringConfig))]
    public class ShardMessageRoutingBenchmarks
    {
        [Params(StateStoreMode.Persistence, StateStoreMode.DData)]
        public StateStoreMode StateMode;

        [Params(10000)]
        public int MsgCount;

        public int BatchSize = 20;

        private ActorSystem _sys1;
        private ActorSystem _sys2;

        private IActorRef _shardRegion1;
        private IActorRef _shardRegion2;
        private IActorRef _localRouter;

        private string _entityOnSys1;
        private string _entityOnSys2;

        private ShardedMessage _messageToSys1;
        private ShardedMessage _messageToSys2;

        private IActorRef _batchActor;
        private Task _batchComplete;

#if (DEBUG)
        [GlobalSetup]
        public void Setup()
        {
            var config = StateMode switch
            {
                StateStoreMode.Persistence => CreatePersistenceConfig(),
                StateStoreMode.DData => CreateDDataConfig(),
                _ => null
            };

            _sys1 = ActorSystem.Create("BenchSys", config);
            _sys2 = ActorSystem.Create("BenchSys", config);

            var c1 = Cluster.Get(_sys1);
            var c2 = Cluster.Get(_sys2);

            c1.JoinAsync(c1.SelfAddress).Wait();
            c2.JoinAsync(c1.SelfAddress).Wait();

            _shardRegion1 = StartShardRegion(_sys1);
            _shardRegion2 = StartShardRegion(_sys2);

            _localRouter = _sys1.ActorOf(Props.Create<ShardedProxyEntityActor>(_shardRegion1).WithRouter(new RoundRobinPool(50)));

            var s1Asks = new List<Task<ShardedEntityActor.ResolveResp>>(20);
            var s2Asks = new List<Task<ShardedEntityActor.ResolveResp>>(20);

            foreach (var i in Enumerable.Range(0, 20))
            {
                s1Asks.Add(_shardRegion1.Ask<ShardedEntityActor.ResolveResp>(new ShardingEnvelope(i.ToString(), ShardedEntityActor.Resolve.Instance), TimeSpan.FromSeconds(3)));
                s2Asks.Add(_shardRegion2.Ask<ShardedEntityActor.ResolveResp>(new ShardingEnvelope(i.ToString(), ShardedEntityActor.Resolve.Instance), TimeSpan.FromSeconds(3)));
            }

            // wait for all Ask operations to complete
            Task.WhenAll(s1Asks.Concat(s2Asks)).Wait();

            _entityOnSys2 = s1Asks.First(x => x.Result.Addr.Equals(c2.SelfAddress)).Result.EntityId;
            _entityOnSys1 = s2Asks.First(x => x.Result.Addr.Equals(c1.SelfAddress)).Result.EntityId;

            _messageToSys1 = new ShardedMessage(_entityOnSys1, 10);
            _messageToSys2 = new ShardedMessage(_entityOnSys2, 10);
        }
#else
        [GlobalSetup]
        public async Task Setup()
        {
            var config = StateMode switch
            {
                StateStoreMode.Persistence => CreatePersistenceConfig(),
                StateStoreMode.DData => CreateDDataConfig(),
                _ => null
            };

            _sys1 = ActorSystem.Create("BenchSys", config);
            _sys2 = ActorSystem.Create("BenchSys", config);

            var c1 = Cluster.Get(_sys1);
            var c2 = Cluster.Get(_sys2);

            await c1.JoinAsync(c1.SelfAddress);
            await c2.JoinAsync(c1.SelfAddress);

            _shardRegion1 = StartShardRegion(_sys1);
            _shardRegion2 = StartShardRegion(_sys2);

            _localRouter = _sys1.ActorOf(Props.Create<ShardedProxyEntityActor>(_shardRegion1).WithRouter(new RoundRobinPool(1000)));

            var s1Asks = new List<Task<ShardedEntityActor.ResolveResp>>(20);
            var s2Asks = new List<Task<ShardedEntityActor.ResolveResp>>(20);

            foreach (var i in Enumerable.Range(0, 20))
            {
                s1Asks.Add(_shardRegion1.Ask<ShardedEntityActor.ResolveResp>(new ShardingEnvelope(i.ToString(), ShardedEntityActor.Resolve.Instance), TimeSpan.FromSeconds(3)));
                s2Asks.Add(_shardRegion2.Ask<ShardedEntityActor.ResolveResp>(new ShardingEnvelope(i.ToString(), ShardedEntityActor.Resolve.Instance), TimeSpan.FromSeconds(3)));
            }

            // wait for all Ask operations to complete
            await Task.WhenAll(s1Asks.Concat(s2Asks));

            _entityOnSys2 = s1Asks.First(x => x.Result.Addr.Equals(c2.SelfAddress)).Result.EntityId;
            _entityOnSys1 = s2Asks.First(x => x.Result.Addr.Equals(c1.SelfAddress)).Result.EntityId;

            _messageToSys1 = new ShardedMessage(_entityOnSys1, 10);
            _messageToSys2 = new ShardedMessage(_entityOnSys2, 10);
        }
#endif

        [IterationSetup]
        public void PerIteration()
        {
            var tcs = new TaskCompletionSource<bool>();
            _batchComplete = tcs.Task;
            _batchActor = _sys1.ActorOf(Props.Create(() => new BulkSendActor(tcs, MsgCount)));
        }

        [Benchmark]
        public async Task SingleRequestResponseToLocalEntity()
        {
            for (var i = 0; i < MsgCount; i++)
                await _shardRegion1.Ask<ShardedMessage>(_messageToSys1);
        }

        [Benchmark]
        public async Task StreamingToLocalEntity()
        {
            _batchActor.Tell(new BulkSendActor.BeginSend(_messageToSys1, _shardRegion1, BatchSize));
            await _batchComplete;
        }

        [Benchmark]
        public async Task SingleRequestResponseToRemoteEntity()
        {
            for (var i = 0; i < MsgCount; i++)
                await _shardRegion1.Ask<ShardedMessage>(_messageToSys2);
        }


        [Benchmark]
        public async Task SingleRequestResponseToRemoteEntityWithLocalProxy()
        {
            for (var i = 0; i < MsgCount; i++)
                await _localRouter.Ask<ShardedMessage>(new SendShardedMessage(_messageToSys2.EntityId, _messageToSys2));
        }

        [Benchmark]
        public async Task StreamingToRemoteEntity()
        {
            _batchActor.Tell(new BulkSendActor.BeginSend(_messageToSys2, _shardRegion1, BatchSize));
            await _batchComplete;
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            _sys1.Stop(_batchActor);
        }

        [GlobalCleanup]
        public async Task Cleanup()
        {
            var t1 = _sys1.Terminate();
            var t2 = _sys2.Terminate();
            await Task.WhenAll(t1, t2);
        }
    }
}