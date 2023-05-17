//-----------------------------------------------------------------------
// <copyright file="DurableShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Cluster.Sharding.Delivery;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Event;
using Akka.Persistence.Delivery;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using static Akka.Tests.Delivery.TestConsumer;

namespace Akka.Cluster.Sharding.Tests.Delivery;

public class DurableShardingSpec : AkkaSpec
{
    public static readonly Config Config = @"
        akka.actor.provider = cluster
        akka.remote.dot-netty.tcp.port = 0
        akka.reliable-delivery.consumer-controller.flow-control-window = 20
        akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
        akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
    ";

    public DurableShardingSpec(ITestOutputHelper output) : base(Config, output)
    {
        // TODO: add journal operations subscriptions, once that's properly supported in Akka.Persistence
    }

    private int _idCount;

    private string ProducerId => $"p-{_idCount}";

    private int NextId()
    {
        return _idCount++;
    }

    private async Task JoinCluster()
    {
        var cluster = Cluster.Get(Sys);
        await cluster.JoinAsync(cluster.SelfAddress);
        await AwaitAssertAsync(() => Assert.True(cluster.IsUp));
    }

    [Fact]
    public async Task ReliableDelivery_with_sharding_and_durable_queue_must_load_initial_state_and_resend_unconfirmed()
    {
        await JoinCluster();
        NextId();

        // <SpawnDurableConsumer>
        var consumerProbe = CreateTestProbe();
        var sharding = await ClusterSharding.Get(Sys).StartAsync($"TestConsumer-{_idCount}", _ =>
                ShardingConsumerController.Create<Job>(c =>
                        Props.Create(() => new Consumer(c, consumerProbe)),
                    ShardingConsumerController.Settings.Create(Sys)), ClusterShardingSettings.Create(Sys),
            HashCodeMessageExtractor.Create(10,
                o =>
                {
                    if (o is ShardingEnvelope se)
                        return se.EntityId;
                    return string.Empty;
                }, o =>
                {
                    if (o is ShardingEnvelope se)
                        return se.Message;
                    return o;
                }));
        // </SpawnDurableConsumer>

        // <SpawnDurableProducer>
        var durableQueueProps = EventSourcedProducerQueue.Create<Job>(ProducerId, Sys);
        var shardingProducerController =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>(ProducerId, sharding, durableQueueProps,
                    ShardingProducerController.Settings.Create(Sys)), $"shardingProducerController-{_idCount}");
        var producerProbe = CreateTestProbe();
        shardingProducerController.Tell(new ShardingProducerController.Start<Job>(producerProbe.Ref));
        // </SpawnDurableProducer>

        for (var i = 1; i <= 4; i++)
        {
            (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
                new ShardingEnvelope("entity-1", new Job($"msg-{i}")));
            // TODO: need journal operations queries here to verify that the message was persisted
        }

        var delivery1 = await consumerProbe.ExpectMsgAsync<JobDelivery>();
        delivery1.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        // TODO: need journal operations queries here to verify that the Confirmed was persisted
        
        var delivery2 = await consumerProbe.ExpectMsgAsync<JobDelivery>();
        delivery2.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        // TODO: need journal operations queries here to verify that the Confirmed was persisted

        await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        
        // let the initial messages reach the ShardingConsumerController before stopping ShardingProducerController
        var delivery3 = await consumerProbe.ExpectMsgAsync<JobDelivery>();
        delivery3.Msg.Should().Be(new Job("msg-3"));
        delivery3.SeqNr.Should().Be(3);

        await Task.Delay(1000);
        
        Sys.Log.Info("Stopping [{0}]", shardingProducerController);
        Watch(shardingProducerController);
        Sys.Stop(shardingProducerController);
        await ExpectTerminatedAsync(shardingProducerController);
        
        var shardingProducerController2 =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>(ProducerId, sharding, durableQueueProps,
                    ShardingProducerController.Settings.Create(Sys)), $"shardingProducerController2-{_idCount}");
        shardingProducerController2.Tell(new ShardingProducerController.Start<Job>(producerProbe.Ref));
        
        // delivery3 and delivery4 are still from old shardingProducerController, that were queued in ConsumerController
        delivery3.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        // that confirmation goes to old dead shardingProducerController, and therefore not stored
        // TODO: need journal operations queries here to verify that the Confirmed WAS NOT persisted
        
        var delivery4 = await consumerProbe.ExpectMsgAsync<JobDelivery>();
        delivery4.Msg.Should().Be(new Job("msg-4"));
        delivery4.SeqNr.Should().Be(4);
        delivery4.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        // that confirmation goes to old dead shardingProducerController, and therefore not stored
        // TODO: need journal operations queries here to verify that the Confirmed WAS NOT persisted
        
        // now the unconfirmed are re-delivered
        var redelivery3 = await consumerProbe.ExpectMsgAsync<JobDelivery>();
        redelivery3.Msg.Should().Be(new Job("msg-3"));
        redelivery3.SeqNr.Should().Be(1); // new ProducerController and there starting at 1
        redelivery3.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        // TODO: need journal operations queries here to verify that the Confirmed was persisted
        
        var redelivery4 = await consumerProbe.ExpectMsgAsync<JobDelivery>();
        redelivery4.Msg.Should().Be(new Job("msg-4"));
        redelivery4.SeqNr.Should().Be(2);
        redelivery4.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        // TODO: need journal operations queries here to verify that the Confirmed was persisted
        
        var next5 = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        next5.SendNextTo.Tell(new ShardingEnvelope("entity-1", new Job("msg-5")));
        // TODO: need journal operations queries here to verify that the message was persisted
        
        
        // the consumer controller may have stopped after msg-5, so allow for resend on timeout (10-15s)
        var delivery5 = await consumerProbe.ExpectMsgAsync<JobDelivery>(TimeSpan.FromSeconds(20));
        delivery5.Msg.Should().Be(new Job("msg-5"));
        delivery5.SeqNr.Should().Be(3); // 3, instead of 5, because SeqNr reset upon ProducerController restart
        delivery5.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        // TODO: need journal operations queries here to verify that the Confirmed was persisted
    }

    [Fact]
    public async Task ReliableDelivery_with_sharding_and_durable_queue_must_reply_to_MessageWithConfirmation_after_storage()
    {
        await JoinCluster();
        NextId();

        var consumerProbe = CreateTestProbe();
        var sharding = await ClusterSharding.Get(Sys).StartAsync($"TestConsumer-{_idCount}", _ =>
                ShardingConsumerController.Create<Job>(c =>
                        Props.Create(() => new Consumer(c, consumerProbe)),
                    ShardingConsumerController.Settings.Create(Sys)), ClusterShardingSettings.Create(Sys),
            HashCodeMessageExtractor.Create(10,
                o =>
                {
                    if (o is ShardingEnvelope se)
                        return se.EntityId;
                    return string.Empty;
                }, o =>
                {
                    if (o is ShardingEnvelope se)
                        return se.Message;
                    return o;
                }));

        var durableQueueProps = EventSourcedProducerQueue.Create<Job>(ProducerId, Sys);
        var shardingProducerController =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>(ProducerId, sharding, durableQueueProps,
                    ShardingProducerController.Settings.Create(Sys)), $"shardingProducerController-{_idCount}");
        var producerProbe = CreateTestProbe();
        shardingProducerController.Tell(new ShardingProducerController.Start<Job>(producerProbe.Ref));
        
        var replyProbe = CreateTestProbe();
        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>())
            .AskNextTo(new ShardingProducerController.MessageWithConfirmation<Job>("entity-1", new Job("msg-1"),
                replyProbe.Ref));
        await replyProbe.ExpectMsgAsync<Done>();
        
        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>())
            .AskNextTo(new ShardingProducerController.MessageWithConfirmation<Job>("entity-2", new Job("msg-2"),
                replyProbe.Ref));
        await replyProbe.ExpectMsgAsync<Done>();
    }

    private class Consumer : ReceiveActor
    {
        private readonly TestProbe _consumerProbe;
        private readonly IActorRef _consumerController;
        private readonly IActorRef _deliveryAdapter;

        public Consumer(IActorRef consumerController, TestProbe consumerProbe)
        {
            _consumerController = consumerController;
            _consumerProbe = consumerProbe;

            var self = Self;
            _deliveryAdapter = Context.ActorOf(
                act =>
                {
                    act.Receive<ConsumerController.Delivery<Job>>((delivery, _) =>
                    {
                        self.Forward(new JobDelivery(delivery.Message, delivery.ConfirmTo, delivery.ProducerId,
                            delivery.SeqNr));
                    });
                }, "delivery-adapter");

            Receive<JobDelivery>(job => { _consumerProbe.Ref.Tell(job); });
        }

        protected override void PreStart()
        {
            _consumerController.Tell(new ConsumerController.Start<Job>(_deliveryAdapter));
        }
    }
}