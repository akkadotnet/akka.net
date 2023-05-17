// -----------------------------------------------------------------------
//  <copyright file="ReliableDeliveryRandomSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Event;
using Akka.Util;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Delivery;

public class ReliableDeliveryRandomSpecs : TestKit.Xunit2.TestKit
{
    internal static readonly Config Config = @"akka.reliable-delivery.consumer-controller{
            flow-control-window = 20
            resend-interval-min = 500ms
            resend-interval-max = 2s
    }";

    public ReliableDeliveryRandomSpecs(ITestOutputHelper output) : this(output, Config)
    {
    }

    protected ReliableDeliveryRandomSpecs(ITestOutputHelper output, Config config) : base(
        config.WithFallback(TestSerializer.Config), output: output)
    {
    }

    private int _idCount = 0;
    private int NextId() => _idCount++;

    private string ProducerId => $"p-{_idCount}";

    private async Task Test(int numberOfMessages, double producerDropProbability, double consumerDropProbability,
        Option<double> durableFailProbability, bool resendLost)
    {
        var consumerControllerSettings = ConsumerController.Settings.Create(Sys) with { OnlyFlowControl = !resendLost };
        var consumerDelay = ThreadLocalRandom.Current.Next(40).Milliseconds();
        var producerDelay = ThreadLocalRandom.Current.Next(40).Milliseconds();
        var durableDelay = durableFailProbability.HasValue
            ? ThreadLocalRandom.Current.Next(40).Milliseconds()
            : TimeSpan.Zero;
        Sys.Log.Info("consumerDropProbability [{0}], producerDropProbability [{1}], " +
                     "consumerDelay [{2}], producerDelay [{3}], durableFailProbability [{4}], durableDelay [{5}]",
            consumerDropProbability, producerDropProbability,
            consumerDelay, producerDelay, durableFailProbability, durableDelay);

        // RandomFlakyNetwork to simulate lost messages from producerController to consumerController
        double ConsumerDrop(object msg)
        {
            return msg switch
            {
                ConsumerController.SequencedMessage<TestConsumer.Job> _ => consumerDropProbability,
                _ => 0
            };
        }

        var consumerEndProbe = CreateTestProbe();
        var consumerController = Sys.ActorOf(ConsumerController.CreateWithFuzzing<TestConsumer.Job>(Sys, Option<IActorRef>.None, ConsumerDrop, consumerControllerSettings), $"consumer-controller-{_idCount}");

        var consumer =
            Sys.ActorOf(
                TestConsumer.PropsFor(consumerDelay, numberOfMessages, consumerEndProbe.Ref, consumerController),
                $"consumer-{_idCount}");

        // RandomFlakyNetwork to simulate lost messages from consumerController to producerController
        double ProducerDrop(object msg)
        {
            return msg switch
            {
                ProducerController.Request _ => producerDropProbability,
                ProducerController.Resend _ => producerDropProbability,
                ProducerController.RegisterConsumer<TestConsumer.Job> _ => producerDropProbability,
                _ => 0
            };
        }

        var stateHolder = new AtomicReference<DurableProducerQueueStateHolder<TestConsumer.Job>>(DurableProducerQueueStateHolder<TestConsumer.Job>.Empty);
        var durableQueue = durableFailProbability.Select(p =>
        {
            return TestDurableProducerQueue.CreateProps(durableDelay, stateHolder,
                _ => ThreadLocalRandom.Current.NextDouble() < p);
        });

        var producerController = Sys.ActorOf(
            ProducerController.CreateWithFuzzing<TestConsumer.Job>(Sys, ProducerId, ProducerDrop, durableQueue, 
                ProducerController.Settings.Create(Sys)), $"producer-controller-{_idCount}");

        var producer = Sys.ActorOf(Props.Create(() => new TestProducer(producerDelay, producerController)),
            $"producer-{_idCount}");

        consumerController.Tell(
            new ConsumerController.RegisterToProducerController<TestConsumer.Job>(producerController));

        await consumerEndProbe.ExpectMsgAsync<TestConsumer.Collected>(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task ReliableDelivery_with_random_failures_must_work_with_flaky_network()
    {
        NextId();
        var consumerDropProbability = 0.1 + ThreadLocalRandom.Current.NextDouble() * 0.2;
        var producerDropProbability = 0.1 + ThreadLocalRandom.Current.NextDouble() * 0.2;

        await Test(numberOfMessages: 63, producerDropProbability: producerDropProbability,
            consumerDropProbability: consumerDropProbability, Option<double>.None, true);
    }
    
    [Fact]
    public async Task ReliableDelivery_with_random_failures_must_work_with_flaky_DurableProducerQueue()
    {
        NextId();
        var durableFailProbability = 0.1 + ThreadLocalRandom.Current.NextDouble() * 0.1;

        await Test(numberOfMessages: 31, producerDropProbability: 0.0,
            consumerDropProbability:0.0, durableFailProbability, true);
    }
    
    [Fact]
    public async Task ReliableDelivery_with_random_failures_must_work_with_flaky_network_and_flaky_DurableProducerQueue()
    {
        NextId();
        var consumerDropProbability = 0.1 + ThreadLocalRandom.Current.NextDouble() * 0.1;
        var producerDropProbability = 0.1 + ThreadLocalRandom.Current.NextDouble() * 0.1;
        var durableFailProbability = 0.1 + ThreadLocalRandom.Current.NextDouble() * 0.1;
        

        await Test(numberOfMessages: 17, producerDropProbability: producerDropProbability,
            consumerDropProbability: consumerDropProbability, durableFailProbability, true);
    }
    
    [Fact]
    public async Task ReliableDelivery_with_random_failures_must_work_with_flaky_network_without_resending()
    {
        NextId();
        var consumerDropProbability = 0.1 + ThreadLocalRandom.Current.NextDouble() * 0.4;
        var producerDropProbability = 0.1 + ThreadLocalRandom.Current.NextDouble() * 0.3;

        await Test(numberOfMessages: 63, producerDropProbability: producerDropProbability,
            consumerDropProbability: consumerDropProbability, Option<double>.None, false);
    }
}