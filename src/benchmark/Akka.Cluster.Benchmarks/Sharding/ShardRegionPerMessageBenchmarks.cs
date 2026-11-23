//-----------------------------------------------------------------------
// <copyright file="ShardRegionPerMessageBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster.Sharding;
using BenchmarkDotNet.Attributes;
using static Akka.Cluster.Benchmarks.Sharding.ShardingHelper;

namespace Akka.Cluster.Benchmarks.Sharding
{
    /// <summary>
    /// Steady-state, one-way cost of a message routed through a local <see cref="ShardRegion"/>
    /// (region -> shard -> already-started entity, <see cref="HashCodeMessageExtractor"/>),
    /// compared with a plain tell of the same message to the same kind of actor, and with
    /// a plain three-hop forwarding chain that has the same number of mailbox hops as sharding.
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class ShardRegionPerMessageBenchmarks
    {
        public const int MsgCount = 100_000;
        public const int EntityCount = 100;
        public const int ShardCount = 20;

        private ActorSystem _sys;
        private IActorRef _region;
        private IActorRef[] _plainEntities;
        private IActorRef _plainChainHead;
        private ShardedMessage[] _messages;
        private readonly CountdownSignal _signal = new();

        [GlobalSetup]
        public async Task Setup()
        {
            _sys = ActorSystem.Create("BenchSys", CreateDDataConfig());
            var cluster = Cluster.Get(_sys);
            await cluster.JoinAsync(cluster.SelfAddress);

            var signal = _signal;
            _region = ClusterSharding.Get(_sys).Start(
                typeName: "counting",
                entityPropsFactory: _ => Props.Create(() => new CountingEntityActor(signal)),
                settings: ClusterShardingSettings.Create(_sys),
                messageExtractor: new ShardMessageExtractor(ShardCount));

            _messages = Enumerable.Range(0, EntityCount).Select(i => new ShardedMessage(i.ToString(), i)).ToArray();

            // start every entity before measuring
            _signal.Reset(EntityCount);
            foreach (var m in _messages)
                _region.Tell(m);
            await _signal.Task.WaitAsync(TimeSpan.FromSeconds(30));

            _plainEntities = Enumerable.Range(0, EntityCount)
                .Select(i => _sys.ActorOf(Props.Create(() => new CountingEntityActor(signal)), "plain-" + i))
                .ToArray();

            var shardStandIns = Enumerable.Range(0, ShardCount)
                .Select(s => _sys.ActorOf(Props.Create(() => new IndexForwarder(_plainEntities, EntityCount)), "shard-standin-" + s))
                .ToArray();
            _plainChainHead = _sys.ActorOf(Props.Create(() => new IndexForwarder(shardStandIns, ShardCount)), "region-standin");
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = MsgCount)]
        public Task PlainTell()
        {
            _signal.Reset(MsgCount);
            var entities = _plainEntities;
            var messages = _messages;
            for (var i = 0; i < MsgCount; i++)
            {
                var n = i % EntityCount;
                entities[n].Tell(messages[n], ActorRefs.NoSender);
            }
            return _signal.Task;
        }

        [Benchmark(OperationsPerInvoke = MsgCount)]
        public Task PlainThreeHopForward()
        {
            _signal.Reset(MsgCount);
            var head = _plainChainHead;
            var messages = _messages;
            for (var i = 0; i < MsgCount; i++)
                head.Tell(messages[i % EntityCount], ActorRefs.NoSender);
            return _signal.Task;
        }

        [Benchmark(OperationsPerInvoke = MsgCount)]
        public Task ShardRegionTell()
        {
            _signal.Reset(MsgCount);
            var region = _region;
            var messages = _messages;
            for (var i = 0; i < MsgCount; i++)
                region.Tell(messages[i % EntityCount], ActorRefs.NoSender);
            return _signal.Task;
        }

        [GlobalCleanup]
        public async Task Cleanup()
        {
            await _sys.Terminate();
        }

        public sealed class CountdownSignal
        {
            private int _remaining;
            private TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task Task => _tcs.Task;

            public void Reset(int count)
            {
                _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _remaining, count);
            }

            public void Signal()
            {
                if (Interlocked.Decrement(ref _remaining) == 0)
                    _tcs.TrySetResult(true);
            }
        }

        private sealed class CountingEntityActor : UntypedActor
        {
            private readonly CountdownSignal _signal;

            public CountingEntityActor(CountdownSignal signal) => _signal = signal;

            protected override void OnReceive(object message)
            {
                if (message is ShardedMessage)
                    _signal.Signal();
                else
                    Unhandled(message);
            }
        }

        /// <summary>
        /// Stand-in for a routing hop: forwards by array index, with no lookups and no allocation of its own.
        /// </summary>
        private sealed class IndexForwarder : UntypedActor
        {
            private readonly IActorRef[] _targets;
            private readonly int _modulo;

            public IndexForwarder(IActorRef[] targets, int modulo)
            {
                _targets = targets;
                _modulo = modulo;
            }

            protected override void OnReceive(object message)
            {
                if (message is ShardedMessage m)
                    _targets[m.Message % _modulo].Forward(m);
                else
                    Unhandled(message);
            }
        }
    }
}
