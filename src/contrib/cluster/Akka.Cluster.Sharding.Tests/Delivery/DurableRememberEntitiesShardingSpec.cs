//-----------------------------------------------------------------------
// <copyright file="DurableShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
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
using FluentAssertions;
using static Akka.Tests.Delivery.TestConsumer;

namespace Akka.Cluster.Sharding.Tests.Delivery;

public class DurableRememberEntitiesShardingSpec : AkkaSpec
{
    private static readonly Config Config = 
        """
        akka.loglevel = DEBUG
        akka.actor.provider = cluster
        akka.persistence.journal.plugin = "akka.persistence.journal.inmem"
        akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.inmem"
        akka.remote.dot-netty.tcp.port = 0
        
        akka.cluster.sharding.remember-entities = on
        akka.cluster.sharding.state-store-mode = ddata
        # no leaks between test runs thank you
        akka.cluster.sharding.distributed-data.durable.keys = []
        akka.cluster.sharding.verbose-debug-logging = on
        akka.cluster.sharding.fail-on-invalid-entity-state-transition = on
        akka.cluster.sharding.entity-restart-backoff = 250ms
        """;

    public DurableRememberEntitiesShardingSpec(ITestOutputHelper output) : base(Config, output)
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
    public async Task ReliableDelivery_with_remember_entity_sharding_must_allow_consumer_to_passivate_self_using_Passivate()
    {
        await JoinCluster();
        NextId();

        var consumerProbe = CreateTestProbe();
        var sharding = await ClusterSharding.Get(system: Sys).StartAsync(
            typeName: $"TestConsumer-{_idCount}", 
            entityPropsFactory: _ => ShardingConsumerController.Create<Job>(
                c => Props.Create(() => new Consumer(c, consumerProbe)),
                ShardingConsumerController.Settings.Create(Sys)), settings: ClusterShardingSettings.Create(Sys),
            messageExtractor: HashCodeMessageExtractor.Create(10, o => string.Empty, o => o));

        var durableQueueProps = EventSourcedProducerQueue.Create<Job>(ProducerId, Sys);
        var shardingProducerController = Sys.ActorOf(
            props: ShardingProducerController.Create<Job>(
                ProducerId, sharding, durableQueueProps, ShardingProducerController.Settings.Create(Sys)), 
            name: $"shardingProducerController-{_idCount}");
        var producerProbe = CreateTestProbe();
        shardingProducerController.Tell(new ShardingProducerController.Start<Job>(producerProbe.Ref));
        
        var replyProbe = CreateTestProbe();
        var next = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        next.AskNextTo(
            msgWithConfirmation: new ShardingProducerController.MessageWithConfirmation<Job>(EntityId: "entity-1", Message: new Job("ping"),
            ReplyTo: replyProbe.Ref));
        await replyProbe.ExpectMsgAsync<Done>();

        consumerProbe.ExpectMsg("pong");
        var entity = consumerProbe.LastSender;
        await consumerProbe.WatchAsync(entity);
        
        next = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        next.AskNextTo(
            msgWithConfirmation: new ShardingProducerController.MessageWithConfirmation<Job>(EntityId: "entity-1", Message: new Job("passivate"),
                ReplyTo: replyProbe.Ref));
        await replyProbe.ExpectMsgAsync<Done>();
        
        consumerProbe.ExpectMsg("passivate");
        await consumerProbe.ExpectTerminatedAsync(entity);
    }

    private class Consumer : ReceiveActor
    {
        private readonly IActorRef _consumerController;
        public Consumer(IActorRef consumerController, IActorRef consumerProbe)
        {
            _consumerController = consumerController;

            Receive<ConsumerController.Delivery<Job>>(delivery =>
            {
                Sender.Tell(ConsumerController.Confirmed.Instance);
                switch (delivery.Message.Payload)
                {
                    case "stop":
                        Context.Stop(Self);
                        break;
                    case "ping":
                        consumerProbe.Tell("pong");
                        break;
                    case "passivate":
                        consumerProbe.Tell("passivate");
                        Context.Parent.Tell(new Passivate(PoisonPill.Instance));
                        break;
                }
            });
        }

        protected override void PreStart()
        {
            _consumerController.Tell(new ConsumerController.Start<Job>(Self));
        }
    }
}
