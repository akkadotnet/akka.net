//-----------------------------------------------------------------------
// <copyright file="ReliableDeliveryShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Cluster.Sharding.Delivery;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Tests.Delivery.TestConsumer;

namespace Akka.Cluster.Sharding.Tests.Delivery;

public class ReliableDeliveryShardingSpec : TestKit.Xunit2.TestKit
{
    public static Config Configuration = @"
        akka.loglevel = DEBUG
        akka.actor.provider = cluster
        akka.remote.dot-netty.tcp.port = 0
        akka.reliable-delivery.consumer-controller.flow-control-window = 20
    ";

    private int _idCount;

    public ReliableDeliveryShardingSpec(ITestOutputHelper output) : base(
        Configuration.WithFallback(ClusterSharding.DefaultConfig()), output: output)
    {
    }

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
    public async Task ReliableDelivery_with_Sharding_must_illustrate_Sharding_usage()
    {
        await JoinCluster();
        NextId();

        var consumerEndProbe = CreateTestProbe();
        var region = await ClusterSharding.Get(Sys).StartAsync($"TestConsumer-{_idCount}", s =>
                ShardingConsumerController.Create<Job>(c =>
                        PropsFor(DefaultConsumerDelay, 42, consumerEndProbe.Ref, c),
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

        var producerController =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>(ProducerId, region, Option<Props>.None,
                    ShardingProducerController.Settings.Create(Sys)), $"shardingController-{_idCount}");
        var producer = Sys.ActorOf(Props.Create(() => new TestShardingProducer(producerController)),
            $"producer-{_idCount}");

        // expecting 3 end messages, one for each entity: "entity-0", "entity-1", "entity-2"
        await consumerEndProbe.ReceiveNAsync(3, TimeSpan.FromSeconds(5)).ToListAsync();
    }

    [Fact]
    public async Task ReliableDelivery_with_Sharding_must_illustrate_Sharding_usage_with_several_producers()
    {
        await JoinCluster();
        NextId();

        var consumerEndProbe = CreateTestProbe();
        var region = await ClusterSharding.Get(Sys).StartAsync($"TestConsumer-{_idCount}", s =>
                ShardingConsumerController.Create<Job>(c =>
                        PropsFor(DefaultConsumerDelay, 42, consumerEndProbe.Ref, c),
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

        var shardingController1 =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>($"p1-{_idCount}", region, Option<Props>.None,
                    ShardingProducerController.Settings.Create(Sys)), $"shardingController1-{_idCount}");
        var producer1 = Sys.ActorOf(Props.Create(() => new TestShardingProducer(shardingController1)),
            $"p1-{_idCount}");

        var shardingController2 =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>($"p2-{_idCount}", region, Option<Props>.None,
                    ShardingProducerController.Settings.Create(Sys)), $"shardingController2-{_idCount}");
        var producer2 = Sys.ActorOf(Props.Create(() => new TestShardingProducer(shardingController2)),
            $"p2-{_idCount}");

        // expecting 3 end messages, one for each entity: "entity-0", "entity-1", "entity-2"
        var endMessages = await consumerEndProbe.ReceiveNAsync(3, TimeSpan.FromSeconds(5)).ToListAsync();

        var producerIds = endMessages.Cast<Collected>().SelectMany(c => c.ProducerIds).ToList();
        producerIds
            .Should().BeEquivalentTo($"p1-{_idCount}-entity-0", $"p1-{_idCount}-entity-1", $"p1-{_idCount}-entity-2",
                $"p2-{_idCount}-entity-0", $"p2-{_idCount}-entity-1", $"p2-{_idCount}-entity-2");
       
    }

    [Fact]
    public async Task ReliableDelivery_with_Sharding_must_reply_to_MessageWithConfirmation()
    {
        await JoinCluster();
        NextId();

        var consumerEndProbe = CreateTestProbe();
        var region = await ClusterSharding.Get(Sys).StartAsync($"TestConsumer-{_idCount}", s =>
                ShardingConsumerController.Create<Job>(c =>
                        PropsFor(DefaultConsumerDelay, 3, consumerEndProbe.Ref, c),
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

        var producerController =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>(ProducerId, region, Option<Props>.None,
                    ShardingProducerController.Settings.Create(Sys)), $"shardingController-{_idCount}");

        var producerProbe = CreateTestProbe();
        producerController.Tell(new ShardingProducerController.Start<Job>(producerProbe.Ref));

        var replyProbe = CreateTestProbe();
        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>())
            .AskNextTo(new ShardingProducerController.MessageWithConfirmation<Job>("entity-0", new Job("msg-1"),
                replyProbe.Ref));
        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>())
            .AskNextTo(new ShardingProducerController.MessageWithConfirmation<Job>("entity-0", new Job("msg-2"),
                replyProbe.Ref));
        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>())
            .AskNextTo(new ShardingProducerController.MessageWithConfirmation<Job>("entity-1", new Job("msg-3"),
                replyProbe.Ref));
        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>())
            .AskNextTo(new ShardingProducerController.MessageWithConfirmation<Job>("entity-0", new Job("msg-4"),
                replyProbe.Ref));

        await consumerEndProbe.ExpectMsgAsync<Collected>(); // entity-0 received 3 messages
        await consumerEndProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>())
            .AskNextTo(new ShardingProducerController.MessageWithConfirmation<Job>("entity-1", new Job("msg-5"),
                replyProbe.Ref));
        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>())
            .AskNextTo(new ShardingProducerController.MessageWithConfirmation<Job>("entity-1", new Job("msg-6"),
                replyProbe.Ref));

        await consumerEndProbe.ExpectMsgAsync<Collected>(); // entity-1 received 3 messages
    }

    [Fact]
    public async Task ReliableDelivery_with_Sharding_must_include_demand_information_in_RequestNext()
    {
        await JoinCluster();

        NextId();
        var shardingProbe = CreateTestProbe();
        var producerController =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>(ProducerId, shardingProbe.Ref, Option<Props>.None,
                    ShardingProducerController.Settings.Create(Sys)), $"shardingController-{_idCount}");
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ShardingProducerController.Start<Job>(producerProbe.Ref));

        var next1 = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        next1.EntitiesWithDemand.IsEmpty.Should().BeTrue();
        next1.BufferedForEntitiesWithoutDemand.Should().BeEquivalentTo(ImmutableDictionary<string, int>.Empty);

        next1.SendNextTo.Tell(new ShardingEnvelope("entity-1", new Job("msg-1")));
        // for the first message, no RequestNext until initial roundtrip
        await producerProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

        var seq1 = (ConsumerController.SequencedMessage<Job>)(await shardingProbe.ExpectMsgAsync<ShardingEnvelope>())
            .Message;
        seq1.Message.Message.Should().BeEquivalentTo(new Job("msg-1"));
        seq1.ProducerController.Tell(new ProducerController.Request(0L, 5L, true,
            false));

        var next2 = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        next2.EntitiesWithDemand.Should().BeEquivalentTo("entity-1");
        next2.BufferedForEntitiesWithoutDemand.Should().BeEquivalentTo(ImmutableDictionary<string, int>.Empty);

        next2.SendNextTo.Tell(new ShardingEnvelope("entity-1", new Job("msg-2")));
        var next3 = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        // could be sent immediately since had demand, and Request(requestUpToSeqNr-5)
        next3.EntitiesWithDemand.Should().BeEquivalentTo("entity-1");
        next3.BufferedForEntitiesWithoutDemand.Should().BeEquivalentTo(ImmutableDictionary<string, int>.Empty);

        next3.SendNextTo.Tell(new ShardingEnvelope("entity-1", new Job("msg-3")));
        var next4 = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        next4.EntitiesWithDemand.Should().BeEquivalentTo("entity-1");
        next4.BufferedForEntitiesWithoutDemand.Should().BeEquivalentTo(ImmutableDictionary<string, int>.Empty);

        next4.SendNextTo.Tell(new ShardingEnvelope("entity-1", new Job("msg-4")));
        var next5 = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        next5.EntitiesWithDemand.Should().BeEquivalentTo("entity-1");
        next5.BufferedForEntitiesWithoutDemand.Should().BeEquivalentTo(ImmutableDictionary<string, int>.Empty);

        next5.SendNextTo.Tell(new ShardingEnvelope("entity-1", new Job("msg-5")));
        // no more demand Request(requestUpToSeqNr-5)
        await producerProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        // but we can send more, which will be buffered
        next5.SendNextTo.Tell(new ShardingEnvelope("entity-1", new Job("msg-6")));

        var m1 = await shardingProbe.ExpectMsgAsync<ShardingEnvelope>();
        var m2 = await shardingProbe.ExpectMsgAsync<ShardingEnvelope>();
        var m3 = await shardingProbe.ExpectMsgAsync<ShardingEnvelope>();
        var seq5 = (ConsumerController.SequencedMessage<Job>)(await shardingProbe.ExpectMsgAsync<ShardingEnvelope>())
            .Message;
        seq5.Message.Message.Should().BeEquivalentTo(new Job("msg-5"));

        var next6 = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        next6.EntitiesWithDemand.IsEmpty.Should().BeTrue();
        next6.BufferedForEntitiesWithoutDemand.Should()
            .BeEquivalentTo(ImmutableDictionary<string, int>.Empty.Add("entity-1", 1));

        // and we can send to another entity
        next6.SendNextTo.Tell(new ShardingEnvelope("entity-2", new Job("msg-7")));
        await producerProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        var seq7 = (ConsumerController.SequencedMessage<Job>)(await shardingProbe.ExpectMsgAsync<ShardingEnvelope>())
            .Message;
        seq7.Message.Message.Should().BeEquivalentTo(new Job("msg-7"));
        seq7.ProducerController.Tell(new ProducerController.Request(0L, 5L, true,
            false));

        var next8 = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        next8.EntitiesWithDemand.Should().BeEquivalentTo("entity-2");
        next8.BufferedForEntitiesWithoutDemand.Should()
            .BeEquivalentTo(ImmutableDictionary<string, int>.Empty.Add("entity-1", 1));

        // when new demand the buffered messages will be sent
        seq5.ProducerController.Tell(new ProducerController.Request(5L, 10L, true,
            false));
        var seq6 = (ConsumerController.SequencedMessage<Job>)(await shardingProbe.ExpectMsgAsync<ShardingEnvelope>())
            .Message;
        seq6.Message.Message.Should().BeEquivalentTo(new Job("msg-6"));

        var next9 = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        next9.EntitiesWithDemand.Should().BeEquivalentTo("entity-1", "entity-2");
        next9.BufferedForEntitiesWithoutDemand.Should().BeEquivalentTo(ImmutableDictionary<string, int>.Empty);
    }

    [Fact]
    public async Task ReliableDelivery_with_Sharding_must_allow_restart_of_producer()
    {
        await JoinCluster();

        NextId();
        var shardingProbe = CreateTestProbe();
        var producerController =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>(ProducerId, shardingProbe.Ref, Option<Props>.None,
                    ShardingProducerController.Settings.Create(Sys)), $"shardingController-{_idCount}");
        var producerProbe = CreateTestProbe();
        producerController.Tell(new ShardingProducerController.Start<Job>(producerProbe.Ref));

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-1", new Job("msg-1")));
        var seq1 = (ConsumerController.SequencedMessage<Job>)(await shardingProbe.ExpectMsgAsync<ShardingEnvelope>())
            .Message;
        seq1.Message.Message.Should().BeEquivalentTo(new Job("msg-1"));
        seq1.ProducerController.Tell(new ProducerController.Request(0L, 5L, true,
            false));

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-1", new Job("msg-2")));
        shardingProbe.ExpectMsg<ShardingEnvelope>().Message.As<ConsumerController.SequencedMessage<Job>>().Message
            .Should().BeEquivalentTo(new Job("msg-2"));

        // restart producer, new Start
        var producerProbe2 = CreateTestProbe();
        producerController.Tell(new ShardingProducerController.Start<Job>(producerProbe2.Ref));

        (await producerProbe2.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-1", new Job("msg-3")));
        shardingProbe.ExpectMsg<ShardingEnvelope>().Message.As<ConsumerController.SequencedMessage<Job>>().Message
            .Should().BeEquivalentTo(new Job("msg-3"));
    }

    [Fact]
    public async Task
        ReliableDelivery_with_Sharding_must_deliver_unconfirmed_if_ShardingConsumerController_is_terminated()
    {
        await JoinCluster();

        // for example if ShardingConsumerController is rebalanced, but no more messages are sent to the entity
        NextId();

        var consumerIncarnation = new AtomicCounter(0);
        var consumerProbes = Enumerable.Range(0, 3).Select(c => CreateTestProbe()).ToList();

        var region = await ClusterSharding.Get(Sys).StartAsync($"TestConsumer-{_idCount}", s =>
                ShardingConsumerController.Create<Job>(c =>
                        Props.Create(() => new ProbeWrapper(consumerProbes[consumerIncarnation.GetAndIncrement()], c)),
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

        var shardingProducerSettings = ShardingProducerController.Settings.Create(Sys) with
        {
            ResendFirstUnconfirmedIdleTimeout = TimeSpan.FromMilliseconds(1500)
        };
        var shardingProducerController =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>(ProducerId, region, Option<Props>.None,
                    shardingProducerSettings), $"shardingController-{_idCount}");
        var producerProbe = CreateTestProbe();
        shardingProducerController.Tell(new ShardingProducerController.Start<Job>(producerProbe.Ref));

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-1", new Job("msg-1")));
        var delivery1 = await consumerProbes[0].ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery1.Message.Should().BeEquivalentTo(new Job("msg-1"));
        delivery1.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-1", new Job("msg-2")));
        var delivery2 = await consumerProbes[0].ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery2.Message.Should().BeEquivalentTo(new Job("msg-2"));
        delivery2.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-1", new Job("msg-3")));
        var delivery3 = await consumerProbes[0].ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        // msg-3 is not confirmed

        // terminate the probe
        await consumerProbes[0].GracefulStop(RemainingOrDefault);

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-1", new Job("msg-4")));
        var delivery3b = await consumerProbes[1].ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        // msg-3 is re-delivered
        delivery3b.Message.Should().BeEquivalentTo(new Job("msg-3"));
        delivery3b.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);

        var delivery3cor4 = await consumerProbes[1].ExpectMsgAsync<ConsumerController.Delivery<Job>>();

        if (delivery3cor4.Message.Equals(new Job("msg-3")))
        {
            /*
             * It is possible the ProducerController re-sends msg-3 again before it has processed its acknowledgement.
             * If the ConsumerController restarts between sending the acknowledgement and receiving that re-sent msg-3,
             * it will deliver msg-3 a second time. We then expect msg-4 next:
             */
            var delivery4 = await consumerProbes[1].ExpectMsgAsync<ConsumerController.Delivery<Job>>();
            delivery4.Message.Should().BeEquivalentTo(new Job("msg-4"));
        }
        else if (delivery3cor4.Message.Equals(new Job("msg-4")))
        {
            // OK!
        }
        else
        {
            throw new Exception($"Unexpected message {delivery3cor4.Message}");
        }

        // redeliver also when no more messages are sent to the entity
        Sys.Stop(consumerProbes[1]); // don't wait for termination

        var delivery4b = await consumerProbes[2].ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery4b.Message.Should().BeEquivalentTo(new Job("msg-4"));
    }

    [Fact]
    public async Task
        ReliableDelivery_with_Sharding_must_cleanup_unused_ProducerController()
    {
        await JoinCluster();

        // for example if ShardingConsumerController is rebalanced, but no more messages are sent to the entity
        NextId();

        var consumerEndProbe = CreateTestProbe();
        var region = await ClusterSharding.Get(Sys).StartAsync($"TestConsumer-{_idCount}", s =>
                ShardingConsumerController.Create<Job>(c =>
                        Props.Create(() => new ProbeWrapper(consumerEndProbe, c)),
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

        var shardingProducerSettings = ShardingProducerController.Settings.Create(Sys) with
        {
            CleanupUnusedAfter = TimeSpan.FromSeconds(1)
        };
        var shardingProducerController =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>(ProducerId, region, Option<Props>.None,
                    shardingProducerSettings), $"shardingController-{_idCount}");
        var producerProbe = CreateTestProbe();
        shardingProducerController.Tell(new ShardingProducerController.Start<Job>(producerProbe.Ref));

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-1", new Job("msg-1")));
        var delivery1 = await consumerEndProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery1.Message.Should().BeEquivalentTo(new Job("msg-1"));
        delivery1.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-1", new Job("msg-2")));
        var delivery2 = await consumerEndProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery2.Message.Should().BeEquivalentTo(new Job("msg-2"));
        delivery2.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-2", new Job("msg-3")));
        var delivery3 = await consumerEndProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery3.Message.Should().BeEquivalentTo(new Job("msg-3"));
        // msg-3 not Confirmed

        var next4 = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        next4.EntitiesWithDemand.Should().BeEquivalentTo("entity-1", "entity-2");

        await Task.Delay(TimeSpan.FromSeconds(2));

        next4.SendNextTo.Tell(new ShardingEnvelope("entity-2", new Job("msg-4")));
        var next5 = await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();
        next5.EntitiesWithDemand.Should().BeEquivalentTo("entity-2"); // entity-1 removed

        delivery3.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        var delivery4 = await consumerEndProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery4.Message.Should().BeEquivalentTo(new Job("msg-4"));
        delivery4.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);

        // send to entity-1 again
        next5.SendNextTo.Tell(new ShardingEnvelope("entity-1", new Job("msg-5")));
        var delivery5 = await consumerEndProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery5.Message.Should().BeEquivalentTo(new Job("msg-5"));
        delivery5.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
    }

    [Fact]
    public async Task
        ReliableDelivery_with_Sharding_must_cleanup_ConsumerController_when_ProducerController_is_terminated()
    {
        await JoinCluster();

        // for example if ShardingConsumerController is rebalanced, but no more messages are sent to the entity
        NextId();

        var consumerEndProbe = CreateTestProbe();
        var region = await ClusterSharding.Get(Sys).StartAsync($"TestConsumer-{_idCount}", s =>
                ShardingConsumerController.Create<Job>(c =>
                        Props.Create(() => new ProbeWrapper(consumerEndProbe, c)),
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

        var shardingProducerController1 =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>(ProducerId, region, Option<Props>.None,
                    ShardingProducerController.Settings.Create(Sys)), $"shardingController-{_idCount}");
        var producerProbe = CreateTestProbe();
        shardingProducerController1.Tell(new ShardingProducerController.Start<Job>(producerProbe.Ref));

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-1", new Job("msg-1")));
        var delivery1 = await consumerEndProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery1.Message.Should().BeEquivalentTo(new Job("msg-1"));
        delivery1.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-1", new Job("msg-2")));
        var delivery2 = await consumerEndProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery2.Message.Should().BeEquivalentTo(new Job("msg-2"));
        delivery2.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>();

        await shardingProducerController1.GracefulStop(RemainingOrDefault);

        var shardingProducerController2 =
            Sys.ActorOf(
                ShardingProducerController.Create<Job>(ProducerId, region, Option<Props>.None,
                    ShardingProducerController.Settings.Create(Sys)), $"shardingController2-{_idCount}");
        shardingProducerController2.Tell(new ShardingProducerController.Start<Job>(producerProbe.Ref));

        await EventFilter.Debug(start: "Starting ConsumerController").ExpectAsync(1,
            async () =>
            {
                (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
                    new ShardingEnvelope("entity-1", new Job("msg-3")));
            });

        var delivery3 = await consumerEndProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery3.Message.Should().BeEquivalentTo(new Job("msg-3"));
        delivery3.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);

        (await producerProbe.ExpectMsgAsync<ShardingProducerController.RequestNext<Job>>()).SendNextTo.Tell(
            new ShardingEnvelope("entity-1", new Job("msg-4")));
        var delivery4 = await consumerEndProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        delivery4.Message.Should().BeEquivalentTo(new Job("msg-4"));
        delivery4.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
    }

    private class ProbeWrapper : UntypedActor
    {
        private readonly IActorRef _consumerController;
        private readonly IActorRef _probeRef;

        public ProbeWrapper(IActorRef probeRef, IActorRef consumerController)
        {
            _probeRef = probeRef;
            _consumerController = consumerController;
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Terminated:
                    Context.Stop(Self);
                    break;
                default:
                    _probeRef.Forward(message);
                    break;
            }
        }

        protected override void PreStart()
        {
            _consumerController.Tell(new ConsumerController.Start<Job>(Self));
            Context.Watch(_probeRef);
        }
    }

    private class TestShardingProducer : ReceiveActor, IWithTimers
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly IActorRef _producerController;
        private IActorRef _sendNextAdapter;

        public TestShardingProducer(IActorRef producerController)
        {
            _producerController = producerController;
            Idle(0);
        }

        public ITimerScheduler Timers { get; set; } = null!;

        protected override void PreStart()
        {
            var self = Self;
            _sendNextAdapter =
                Context.ActorOf(
                    act =>
                    {
                        act.Receive<ShardingProducerController.RequestNext<Job>>((next, _) =>
                            self.Tell(new RequestNext(next.SendNextTo)));

                        act.OnPreStart = ctx =>
                            _producerController.Tell(new ShardingProducerController.Start<Job>(ctx.Self));
                    }, "sendNextAdapter");

            // simulate fast producer
            Timers.StartPeriodicTimer("tick", Tick.Instance, TimeSpan.FromMilliseconds(20));
        }

        private void Idle(int n)
        {
            Receive<Tick>(_ => { }); // ignore
            Receive<RequestNext>(next => { Become(() => Active(n + 1, next.SendNextTo)); });
        }

        private void Active(int n, IActorRef sendTo)
        {
            Receive<Tick>(_ =>
            {
                var msg = $"msg-{n}";
                var entityId = $"entity-{n % 3}";
                _log.Info("Sending {0} to {1}", msg, entityId);
                sendTo.Tell(new ShardingEnvelope(entityId, new Job(msg)));
                Become(() => Idle(n));
            });

            Receive<RequestNext>(next => { }); // already active
        }

        public sealed class Tick
        {
            private Tick()
            {
            }

            public static Tick Instance { get; } = new();
        }

        public sealed record RequestNext(IActorRef SendNextTo);
    }
}