// -----------------------------------------------------------------------
//  <copyright file="TestProducer.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Delivery;
using Akka.Event;

namespace Akka.Tests.Delivery;

/// <summary>
/// INTERNAL API.
/// </summary>
public sealed class TestProducer : ReceiveActor, IWithTimers
{
    public static readonly TimeSpan DefaultProducerDelay = TimeSpan.FromMilliseconds(20);

    public sealed class Tick
    {
        public static readonly Tick Instance = new();

        private Tick()
        {
        }
    }

    public int CurrentSequenceNr { get; private set; }
    public TimeSpan Delay { get; }
    public ITimerScheduler Timers { get; set; } = null!;
    private readonly IActorRef _producerController;
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public TestProducer(TimeSpan delay, IActorRef producerController)
    {
        Delay = delay;
        _producerController = producerController;
        if (Delay == TimeSpan.Zero)
            ActiveNoDelay();
        else
        {
            Timers.StartPeriodicTimer("tick", Tick.Instance, Delay);
            Idle();
        }
    }

    protected override void PreStart()
    {
        _producerController.Tell(new ProducerController.Start<TestConsumer.Job>(Self));
    }

    private void ActiveNoDelay()
    {
        // no ticks in this behavior
        Receive<ProducerController.RequestNext<TestConsumer.Job>>(r =>
        {
            SendMessage(CurrentSequenceNr, r.SendNextTo);
            CurrentSequenceNr++;
        });
    }

    private void Active(IActorRef sendNextTo)
    {
        Receive<Tick>(_ =>
        {
            SendMessage(CurrentSequenceNr, sendNextTo);
            Become(Idle);
        });

        Receive<ProducerController.RequestNext<TestConsumer.Job>>(_ =>
        {
            throw new InvalidOperationException("Unexpected WriteNext message, already have one.");
        });
    }

    private void Idle()
    {
        Receive<Tick>(_ => { }); // ignore
        Receive<ProducerController.RequestNext<TestConsumer.Job>>(j =>
        {
            CurrentSequenceNr++;
            Become(() => Active(j.SendNextTo));
        });
    }

    private void SendMessage(int n, IActorRef sendTo)
    {
        var msg = $"msg-{n}";
        _log.Info("Sent [{0}]", n);
        sendTo.Tell(new TestConsumer.Job(msg));
    }
}