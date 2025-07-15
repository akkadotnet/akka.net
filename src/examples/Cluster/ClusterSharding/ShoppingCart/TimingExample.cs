//-----------------------------------------------------------------------
// <copyright file="TimingExample.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Cluster.Sharding.Delivery;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Event;
using Akka.Util;
using ShoppingCart;

namespace Akka.Cluster.Sharding.Examples.ShoppingCart;

/// <summary>
/// Example demonstrating how to use the timing and tracing capabilities
/// of Akka.Cluster.Sharding.Delivery
/// </summary>
public class TimingExample
{
    /*
    public static async Task Main()
    {
        var config = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                actor {
                    provider = cluster
                }
                remote {
                    dot-netty.tcp {
                        hostname = ""127.0.0.1""
                        port = 2551
                    }
                }
                cluster {
                    seed-nodes = [""akka.tcp://TimingExample@127.0.0.1:2551""]
                }
                reliable-delivery {
                    sharding {
                        producer-controller {
                            buffer-size = 1000
                            internal-ask-timeout = 60s
                            cleanup-unused-after = 60s
                            resend-first-unconfirmed-idle-timeout = 10s
                        }
                        consumer-controller {
                            buffer-size = 1000
                            allow-bypass = false
                        }
                    }
                }
            }");

        using var system = ActorSystem.Create("TimingExample", config);

        // Start cluster
        var cluster = Cluster.Get(system);
        cluster.Join(cluster.SelfAddress);

        // Create ShardRegion with timing-enabled consumer controller
        var shardRegion = ClusterSharding.Get(system).Start(
            typeName: "ShoppingCart",
            entityProps: ShardingConsumerController.Create<CartItem>(
                consumerProps: consumerController => Props.Create(() => new ShoppingCart(consumerController)),
                settings: ShardingConsumerController.Settings.Create(system)),
            settings: ClusterShardingSettings.Create(system),
            messageExtractor: new MessageExtractor(10));

        // Create producer controller
        var producerController = system.ActorOf(
            ShardingProducerController.Create<CartItem>(
                producerId: "timing-producer",
                shardRegion: shardRegion,
                durableQueue: Option<Props>.None,
                settings: ShardingProducerController.Settings.Create(system)),
            "producer-controller");

        // Create producer actor
        var producer = system.ActorOf(Props.Create(() => new TimingProducer(producerController)), "producer");

        // Start the producer
        producer.Tell(new ShardingProducerController.Start<CartItem>(producer));

        // Send some messages to test timing
        for (int i = 0; i < 5; i++)
        {
            var item = new CartItem($"item-{i}", $"Product {i}", 10.0m + i);
            producer.Tell(new ShardingEnvelope($"user-{i % 3}", item));

            await Task.Delay(100); // Small delay between messages
        }

        // Wait for messages to be processed
        await Task.Delay(2000);

        Console.WriteLine("Timing example completed. Check logs for timing information.");
        await system.Terminate();
    }

    private static Option<(string, object)> ExtractEntityId(object message)
    {
        if (message is ShardingEnvelope envelope)
        {
            return (envelope.EntityId, envelope.Message);
        }
        return ("", message);
    }

    private static string ExtractShardId(object message)
    {
        if (message is ShardingEnvelope envelope)
        {
            return envelope.EntityId.GetHashCode().ToString();
        }
        return "0";
    }
}

/// <summary>
/// Producer actor that demonstrates timing capabilities
/// </summary>
public class TimingProducer : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private IActorRef? _sendNext;

    public TimingProducer(IActorRef producerController)
    {
        Receive<ShardingProducerController.RequestNext<CartItem>>(next =>
        {
            _log.Info("Received RequestNext - ready to send messages");
            _sendNext = next.SendNextTo;
            Become(Active);
        });

        Receive<ShardingEnvelope>(envelope =>
        {
            if (_sendNext != null)
            {
                _log.Info("Sending message to entity [{0}]", envelope.EntityId);
                _sendNext.Tell(envelope);
                Become(Idle);
            }
            else
            {
                Stash.Stash();
            }
        });
    }

    private void Active()
    {
        Receive<ShardingEnvelope>(envelope =>
        {
            _log.Info("Sending message to entity [{0}]", envelope.EntityId);
            _sendNext!.Tell(envelope);
            Become(Idle);
        });

        Receive<ShardingProducerController.RequestNext<CartItem>>(next =>
        {
            _sendNext = next.SendNextTo;
        });
    }

    private void Idle()
    {
        Receive<ShardingEnvelope>(envelope =>
        {
            Stash.Stash();
        });

        Receive<ShardingProducerController.RequestNext<CartItem>>(next =>
        {
            _sendNext = next.SendNextTo;
            Stash.Unstash();
            Become(Active);
        });
    }

    public IStash Stash { get; set; } = null!;
}

/// <summary>
/// Shopping cart entity that demonstrates timing capabilities
/// </summary>
public class ShoppingCart : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _consumerController;

    public ShoppingCart(IActorRef consumerController)
    {
        _consumerController = consumerController;

        Receive<ConsumerController.Start<CartItem>>(start =>
        {
            _log.Info("ShoppingCart started - ready to receive messages");
            Context.Watch(start.DeliverTo);
            Become(Active);
        });
    }

    private void Active()
    {
        Receive<ConsumerController.Delivery<CartItem>>(delivery =>
        {
            _log.Info("Received delivery for item: {0}", delivery.Message);

            // Simulate some processing time
            Task.Delay(50).ContinueWith(_ =>
            {
                delivery.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
            });
        });

        Receive<Terminated>(terminated =>
        {
            _log.Info("Consumer terminated");
            Context.Stop(Self);
        });
    }
}

/// <summary>
/// Simple cart item for demonstration
/// </summary>
public class CartItem
{
    public CartItem(string id, string name, decimal price)
    {
        Id = id;
        Name = name;
        Price = price;
    }

    public string Id { get; }
    public string Name { get; }
    public decimal Price { get; }

    public override string ToString()
    {
        return $"CartItem({Id}, {Name}, {Price:C})";
    }
*/
}