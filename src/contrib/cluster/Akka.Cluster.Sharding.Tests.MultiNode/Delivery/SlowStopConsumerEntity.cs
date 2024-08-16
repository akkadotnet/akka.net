//-----------------------------------------------------------------------
// <copyright file="SlowStopConsumerEntity.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Delivery;
using Akka.Event;

namespace Akka.Cluster.Sharding.Tests.MultiNode.Delivery;

/// <summary>
/// INTERNAL API
/// </summary>
public sealed class SlowStopConsumerEntity : ReceiveActor, IWithTimers
{
    private readonly IActorRef _consumerController;

    public SlowStopConsumerEntity(string persistenceId, IActorRef consumerController)
    {
        _consumerController = consumerController;
        
        Receive<ConsumerController.Delivery<Job>>(delivery =>
        {
            var job = delivery.Message;
            job.Probe.Tell(job.Payload);
            delivery.ConfirmTo.Tell(ConsumerController.Confirmed.Instance);
        });

        Receive<Stop>(_ =>
        {
            Timers.StartSingleTimer(ActualStop.Instance, ActualStop.Instance, TimeSpan.FromMilliseconds(50));
        });

        Receive<ActualStop>(_ => Context.Stop(Self));
    }

    protected override void PreStart()
    {
        _consumerController.Tell(new ConsumerController.Start<Job>(Self));
    }

    public sealed class Stop: ConsumerController.IConsumerCommand<Job>
    {
        public static readonly Stop Instance = new();
        private Stop() { }
    }
    
    public sealed class ActualStop
    {
        public static readonly ActualStop Instance = new();
        private ActualStop() { }
    }

    public sealed record Job(int Payload, IActorRef Probe);
    
    public ITimerScheduler Timers { get; set; } = null!;
}
