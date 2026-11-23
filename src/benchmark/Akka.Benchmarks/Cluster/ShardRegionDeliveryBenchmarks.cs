//-----------------------------------------------------------------------
// <copyright file="ShardRegionDeliveryBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Benchmarks.Dispatch;
using Akka.Cluster.Sharding;
using Akka.Configuration;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Cluster
{
    /// <summary>
    /// Steady-state cost of routing one message through a local ShardRegion -> Shard -> entity
    /// in a single-node cluster. All entities are started before measurement. The region, its
    /// shards and the entities run on a calling-thread executor, so each Tell executes the whole
    /// region -> shard -> entity chain inline on the benchmark thread: the time per operation is
    /// the summed per-message work of the three actors (plus mailbox enqueue/run), without
    /// thread-pool scheduling noise. Default sharding settings (passivate-idle-entity-after = 120s,
    /// remember-entities off) and a <see cref="HashCodeMessageExtractor"/> over a plain message.
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class ShardRegionDeliveryBenchmarks
    {
        private const int MessagesPerInvoke = 10_000;
        private const int EntityCount = 1_000;
        private const int NumberOfShards = 30;

        private static readonly Config Config = ConfigurationFactory.ParseString(@"
            akka.loglevel = WARNING
            akka.actor.provider = cluster
            akka.remote.dot-netty.tcp.hostname = 127.0.0.1
            akka.remote.dot-netty.tcp.port = 0
            akka.cluster.sharding.use-dispatcher = sharding-calling-thread-dispatcher
            sharding-calling-thread-dispatcher {
                executor = """ + typeof(CallingThreadExecutorConfigurator).AssemblyQualifiedName + @"""
            }");

        private ActorSystem _system;
        private IActorRef _region;
        private EntityMessage[] _messages;
        private readonly Counter _counter = new();

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("ShardRegionDeliveryBenchmarks", Config);
            var cluster = Akka.Cluster.Cluster.Get(_system);
            var up = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            cluster.RegisterOnMemberUp(() => up.TrySetResult(true));
            cluster.Join(cluster.SelfAddress);
            if (!up.Task.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("single-node cluster did not come up");

            var counter = _counter;
            _region = ClusterSharding.Get(_system).StartAsync(
                    "entity",
                    Props.Create(() => new EntityActor(counter)).WithDispatcher("sharding-calling-thread-dispatcher"),
                    ClusterShardingSettings.Create(_system),
                    new Extractor(NumberOfShards))
                .GetAwaiter().GetResult();

            _messages = new EntityMessage[EntityCount];
            for (var i = 0; i < EntityCount; i++)
                _messages[i] = new EntityMessage("entity-" + i);

            // start every entity (first message to each shard goes through the coordinator)
            foreach (var m in _messages)
                _region.Tell(m, ActorRefs.NoSender);
            AwaitCount(EntityCount, TimeSpan.FromSeconds(60));
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _system.Terminate().Wait(TimeSpan.FromSeconds(30));
        }

        [Benchmark(OperationsPerInvoke = MessagesPerInvoke)]
        public void DeliverToStartedEntity()
        {
            var target = Volatile.Read(ref _counter.Count) + MessagesPerInvoke;
            var messages = _messages;
            var region = _region;
            for (var i = 0; i < MessagesPerInvoke; i++)
                region.Tell(messages[i % EntityCount], ActorRefs.NoSender);
            AwaitCount(target, TimeSpan.FromSeconds(30));
        }

        private void AwaitCount(long target, TimeSpan timeout)
        {
            if (Volatile.Read(ref _counter.Count) >= target) return;
            var sw = Stopwatch.StartNew();
            var spin = new SpinWait();
            while (Volatile.Read(ref _counter.Count) < target)
            {
                if (sw.Elapsed > timeout)
                    throw new TimeoutException($"only {_counter.Count} of {target} messages delivered");
                spin.SpinOnce();
            }
        }

        private sealed class Counter
        {
            public long Count;
        }

        private sealed class EntityMessage
        {
            public EntityMessage(string entityId) => EntityId = entityId;
            public string EntityId { get; }
        }

        private sealed class Extractor : HashCodeMessageExtractor
        {
            public Extractor(int maxNumberOfShards) : base(maxNumberOfShards)
            {
            }

            public override string EntityId(object message) => message is EntityMessage m ? m.EntityId : null;
        }

        private sealed class EntityActor : UntypedActor
        {
            private readonly Counter _counter;

            public EntityActor(Counter counter) => _counter = counter;

            protected override void OnReceive(object message)
            {
                if (message is EntityMessage)
                    Interlocked.Increment(ref _counter.Count);
                else
                    Unhandled(message);
            }
        }
    }
}
