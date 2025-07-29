using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Cluster.Sharding.Delivery;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Util;
using DeliveryProfile;
using JetBrains.Profiler.SelfApi;

public static class Program
{
    private static readonly Config Config = 
        """
        akka.loglevel = WARNING
        akka.actor.provider = cluster
        akka.persistence.journal.plugin = "akka.persistence.journal.inmem"
        akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.inmem"
        akka.remote.dot-netty.tcp.port = 0
        # akka.reliable-delivery.sharding.producer-controller.buffer-size = 10000
        # akka.reliable-delivery.sharding.consumer-controller.buffer-size = 10000
        akka.reliable-delivery.consumer-controller.flow-control-window = 1000
        """;
    
    private static ActorSystem? _system;
    private static IActorRef? _producer;
    private static IActorRef? _region;
    private static IActorRef? _controller;
    private static IActorRef? _aggregator;

    //[Params(3000)]
    private const int MessageCount = 3000;

    public static async Task Main(string[] args)
    {
        /*
        await DotTrace.InitAsync();
        var traceConfig = new DotTrace.Config()
            .SaveToDir("G:\\dotTraceSnapshots\\ClusterShardingDelivery")
            .UseTimelineProfilingType(true);
            
        DotTrace.Attach(traceConfig);
        */
        
        
        _system = ActorSystem.Create("BenchmarkSystem", Config);
        
        // Join cluster
        var tcs = new TaskCompletionSource();
        var cluster = Akka.Cluster.Cluster.Get(_system);
        cluster.RegisterOnMemberUp(() =>
        {
            tcs.SetResult();
        });
        cluster.Join(cluster.SelfAddress);
        await tcs.Task;
        
        // Get the test completed aggregator
        _aggregator = _system.ActorOf(Props.Create(() => new AggregateActor(MessageCount)));
        
        // Register the sharding region for later use
        _region = await ClusterSharding.Get(_system).StartAsync(
            typeName: "TestConsumer", 
            entityPropsFactory: id => ShardingConsumerController.Create<Job>(
                c => Props.Create(() => new TestConsumerEntity(id, c, _aggregator)),
                ShardingConsumerController.Settings.Create(_system)),
            settings: ClusterShardingSettings.Create(_system),
            messageExtractor: new MessageExtractor());
        
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
        _producer = _system.ActorOf(Props.Create(() => new ProducerActor(_controller, true)), "producer");
        
        // Debug
        var consumerSettings = ConsumerController.Settings.Create(_system);
        Console.WriteLine($"ConsumerController.Settings.FlowControlWindow: {consumerSettings.FlowControlWindow}");
        var shardingProducerSettings = ShardingProducerController.Settings.Create(_system);
        Console.WriteLine($"ShardingProducerController.Settings.BufferSize: {shardingProducerSettings.BufferSize}");
        var shardingConsumerSettings = ShardingConsumerController.Settings.Create(_system);
        Console.WriteLine($"ShardingConsumerController.Settings.BufferSize: {shardingConsumerSettings.BufferSize}");

        // DotTrace.StartCollectingData();
        foreach (var message in Enumerable.Range(0, MessageCount))
        {
            _producer.Tell(message);
        }

        await _aggregator.Ask<Completed>(GetCompleted.Instance);
        // DotTrace.SaveData();

        await _system.Terminate();
    }
}