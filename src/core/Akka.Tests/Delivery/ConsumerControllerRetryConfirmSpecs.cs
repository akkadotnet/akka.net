// -----------------------------------------------------------------------
//  <copyright file="ConsumerControllerRetryConfirmSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;
using static Akka.Tests.Delivery.TestConsumer;

namespace Akka.Tests.Delivery;

public class ConsumerControllerRetryConfirmSpecs : TestKit.Xunit2.TestKit
{
    public static readonly Config Config = @"
        akka.reliable-delivery.consumer-controller {
        flow-control-window = 20
        resend-interval-min = 1s
        retry-confirmation = true
    }";

    public ConsumerControllerRetryConfirmSpecs(ITestOutputHelper outputHelper) : base(
        Config.WithFallback(TestSerializer.Config).WithFallback(ZeroLengthSerializer.Config), output: outputHelper)
    {
    }

    private int _idCount = 0;
    private int NextId() => _idCount++;
    private string ProducerId => $"p-{_idCount}";

    [Fact]
    public async Task ConsumerController_must_resend_Delivery_on_confirmation_retry()
    {
        var id = NextId();
        var consumerProbe = CreateTestProbe();
        var consumerController = Sys.ActorOf(ConsumerController.Create<Job>(Sys, Option<IActorRef>.None),
            $"consumerController-{id}");
        var producerControllerProbe = CreateTestProbe();

        consumerController.Tell(new ConsumerController.Start<Job>(consumerProbe.Ref));
        consumerController.Tell(new ConsumerController.RegisterToProducerController<Job>(producerControllerProbe.Ref));
        await producerControllerProbe.ExpectMsgAsync<ProducerController.RegisterConsumer<Job>>();

        consumerController.Tell(SequencedMessage(ProducerId, 1, producerControllerProbe.Ref));
        
        await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
        
        // expected resend
        await consumerProbe.ExpectMsgAsync<ConsumerController.Delivery<Job>>();
    }

}