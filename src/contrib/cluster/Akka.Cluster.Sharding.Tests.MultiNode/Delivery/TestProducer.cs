//-----------------------------------------------------------------------
// <copyright file="TestProducer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Sharding.Delivery;
using Akka.Event;

namespace Akka.Cluster.Sharding.Tests.MultiNode.Delivery;

internal class TestShardingProducer : ReceiveActor, IWithTimers, IWithStash
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _producerController;
    private readonly IActorRef _probe;

    public TestShardingProducer(IActorRef producerController, IActorRef probe)
    {
        _producerController = producerController;
        _probe = probe;
        Idle();
    }

    public ITimerScheduler Timers { get; set; } = null!;
    public IStash Stash { get; set; } = null!;
    public IActorRef SendNext { get; set; } = ActorRefs.Nobody;

    protected override void PreStart()
    {
        // simulate fast producer
        Timers.StartPeriodicTimer("tick", Tick.Instance, TimeSpan.FromMilliseconds(100));
        _producerController.Tell(new ShardingProducerController.Start<SlowStopConsumerEntity.Job>(Self));
    }

    private void Idle()
    {
        Receive<Tick>(_ => { }); // ignore
        
        Receive<int>(_ => Stash.Stash());
        
        Receive<ShardingProducerController.RequestNext<SlowStopConsumerEntity.Job>>(req =>
        {
            SendNext = req.SendNextTo;
            Become(Active);
        });
    }

    private void Active()
    {
        Receive<Tick>(_ =>
        {
            if(!Stash.IsEmpty)
                Stash.Unstash();
        });
        
        Receive<int>(n =>
        {
            _log.Info("Sending {0}", n);
            Become(Idle);
            var job = new SlowStopConsumerEntity.Job(n, _probe);
            SendNext.Tell(new ShardingEnvelope(n.ToString(), job));
        });

        Receive<ShardingProducerController.RequestNext<SlowStopConsumerEntity.Job>>(req =>
        {
            SendNext = req.SendNextTo;
        });
    }

    public sealed class Tick
    {
        private Tick() { }
        public static readonly Tick Instance = new();
    }
}
