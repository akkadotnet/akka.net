using System.Collections.Generic;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Cluster.Sharding.Delivery;
using Akka.Delivery;
using Akka.Event;

namespace Akka.Benchmarks.Cluster.Delivery;

#region Messages

internal record Job(int Payload);

internal class GetCompleted
{
    public static readonly GetCompleted Instance = new();
    private GetCompleted() { }
}

internal class Completed
{
    public static readonly Completed Instance = new();
    private Completed() { }
}

internal class Reset
{
    public static readonly Reset Instance = new();
    private Reset() { }
}

internal class Start
{
    public static readonly Start Instance = new();
    private Start() { }
}

#endregion

#region Classes

// The entity actor
internal class TestConsumerEntity : ReceiveActor
{
    private readonly IActorRef _aggregator;
    private readonly string _entityId;
    private readonly IActorRef _consumerController;
    private readonly ILoggingAdapter _log;
    
    public TestConsumerEntity(string entityId, IActorRef consumerController, IActorRef aggregator)
    {
        _entityId = entityId;
        _consumerController = consumerController;
        _aggregator = aggregator;
        _log = Context.GetLogger();

        Receive<ConsumerController.Delivery<Job>>(delivery =>
        {
            _aggregator.Tell(Done.Instance);
            delivery.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        });
    }

    protected override void PreStart()
    {
        _consumerController.Tell(new ConsumerController.Start<Job>(Self));
    }
}

// Message extractor for sharding
internal class MessageExtractor() : HashCodeMessageExtractor(10)
{
    public override string EntityId(object message) =>
        message is Job cmd ? (cmd.Payload % 100).ToString() : string.Empty;
}

// The producer actor
internal class ProducerActor : ReceiveActor
{
    private IActorRef _sendNext;
    private readonly IActorRef _producerController;
    private readonly ILoggingAdapter _log;
    private readonly int _maxCount;
    private int _count;

    public ProducerActor(IActorRef producerController, int maxCount)
    {
        _log = Context.GetLogger();
        _producerController = producerController;
        _maxCount = maxCount;
        Become(Idle);
    }

    protected override void PreStart()
    {
        _producerController.Tell(new ShardingProducerController.Start<Job>(Self));
    }

    private void Idle()
    {
        _count = 0;
        Receive<Start>(_ => Become(Active));
        Receive<ShardingProducerController.RequestNext<Job>>(next =>
        {
            _sendNext = next.SendNextTo;
        });
    }

    private void Active()
    {
        if (_sendNext != null)
        {
            _sendNext.Tell(new ShardingEnvelope(_count.ToString(), new Job(_count)));
            _sendNext = null;
            _count++;
        }
        
        Receive<ShardingProducerController.RequestNext<Job>>(next =>
        {
            next.SendNextTo.Tell(new ShardingEnvelope(_count.ToString(), new Job(_count)));
            _count++;
            if(_count >= _maxCount)
                Become(Idle);
        });
    }
}

internal class AggregateActor : ReceiveActor
{
    private IActorRef? _reportTo;
    private readonly int _totalMessageCount;
    private int _messageCount;

    public AggregateActor(int messageCount)
    {
        _totalMessageCount = messageCount;
        Receive<Done>(_ =>
        {
            _messageCount++;
            if (_messageCount < _totalMessageCount || _reportTo == null) 
                return;
            
            _reportTo.Tell(Completed.Instance);
        });
        Receive<Reset>(_ =>
        {
            _messageCount = 0;
            Sender.Tell(Done.Instance);
        });
        Receive<GetCompleted>(_ =>
        {
            if (_messageCount >= _totalMessageCount)
            {
                Sender.Tell(Completed.Instance);
                return;
            }

            _reportTo = Sender;
        });
    }
}

#endregion
