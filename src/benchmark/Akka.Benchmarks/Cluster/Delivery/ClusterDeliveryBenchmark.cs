using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster.Sharding;
using Akka.Cluster.Sharding.Delivery;
using Akka.Configuration;
using Akka.Persistence.Delivery;
using Akka.Util;
using BenchmarkDotNet.Attributes;
using FluentAssertions.Extensions;

namespace Akka.Benchmarks.Cluster.Delivery;

[Config(typeof(MacroBenchmarkConfig))]
public class ClusterDeliveryBenchmark
{
    private static readonly Config Config = 
        """
        akka.loglevel = WARNING
        akka.actor.provider = cluster
        akka.persistence.journal.plugin = "akka.persistence.journal.inmem"
        akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.inmem"
        akka.remote.dot-netty.tcp.port = 0
        """;
    
    private ActorSystem _system;
    private IActorRef? _producer;
    private IActorRef? _region;
    private IActorRef? _controller;
    private IActorRef? _aggregator;

    //[Params(3000)]
    private const int MessageCount = 800;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _system = ActorSystem.Create("BenchmarkSystem", Config);
        
        // Join cluster
        var tcs = new TaskCompletionSource();
        var cluster = Akka.Cluster.Cluster.Get(_system);
        cluster.RegisterOnMemberUp(() =>
        {
            tcs.SetResult();
        });
        cluster.Join(cluster.SelfAddress);
        tcs.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        
        // Get the test completed aggregator
        _aggregator = _system.ActorOf(Props.Create(() => new AggregateActor(MessageCount)));
        
        // Register the sharding region for later use
        _region = ClusterSharding.Get(_system).StartAsync(
            typeName: "TestConsumer", 
            entityPropsFactory: id => ShardingConsumerController.Create<Job>(
                c => Props.Create(() => new TestConsumerEntity(id, c, _aggregator)),
                ShardingConsumerController.Settings.Create(_system)),
            settings: ClusterShardingSettings.Create(_system),
            messageExtractor: new MessageExtractor())
            .WaitAsync(3.Seconds()).GetAwaiter().GetResult();
        
        // Create the ShardingProducerController
        _controller = _system.ActorOf(
            ShardingProducerController.Create<Job>(
                producerId: "test-producer",
                shardRegion: _region!,
                durableQueue: Option<Props>.None, 
                settings: ShardingProducerController.Settings.Create(_system)
            ),
            "producerController"
        );
        
        // Create the producer actor
        _producer = _system.ActorOf(Props.Create(() => new ProducerActor(_controller)), "producer");
    }

    [GlobalCleanup]
    public void Teardown()
    {
        _system.Terminate().WaitAsync(30.Seconds()).GetAwaiter().GetResult();
        _aggregator = null;
        _producer = null;
        _region = null;
        _controller = null;
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _aggregator.Ask<Done>(Reset.Instance).GetAwaiter().GetResult();
    }
    
    [Benchmark(OperationsPerInvoke = MessageCount)]
    public async Task ClusterShardingDeliveryMessageThroughputBenchmark()
    {
        foreach (var message in Enumerable.Range(0, MessageCount))
        {
            _producer.Tell(message);
        }
        
        await _aggregator.Ask<Completed>(GetCompleted.Instance);
    }
}
