// -----------------------------------------------------------------------
//  <copyright file="TestProducerWithAsk.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Delivery;
using Akka.Event;
using Akka.Pattern;

namespace Akka.Tests.Delivery;

public sealed class TestProducerWithAsk : ReceiveActor, IWithTimers
{
    public static readonly TimeSpan DefaultAskTimeout = TimeSpan.FromSeconds(10);

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _producerController;
    private readonly IActorRef _replyProbe;

    public TestProducerWithAsk(TimeSpan delay, IActorRef replyProbe, IActorRef producerController)
    {
        Delay = delay;
        _replyProbe = replyProbe;
        _producerController = producerController;

        Timers.StartPeriodicTimer(Tick.Instance, Tick.Instance, Delay);
        Idle();
    }

    private TimeSpan Delay { get; }
    public int CurrentSequenceNr { get; private set; }

    public ITimerScheduler Timers { get; set; } = null!;

    private void Idle()
    {
        Receive<Tick>(_ => { }); // ignore
        Receive<ProducerController.RequestNext<TestConsumer.Job>>(next =>
        {
            CurrentSequenceNr++;
            Become(() => Active(next.SendNextTo));
        });

        Receive<Confirmed>(c => { _replyProbe.Tell(c.SeqNr); });

        Receive<AskTimeout>(_ => { _log.Warning("Timeout"); });
    }

    private void Active(IActorRef sendNextTo)
    {
        Receive<Tick>(_ =>
        {
            var msg = $"msg-{CurrentSequenceNr}";
            _log.Info("sent [{0}]", msg);
            var self = Self;
            sendNextTo.Ask<long>(
                    s => new ProducerController.MessageWithConfirmation<TestConsumer.Job>(new TestConsumer.Job(msg), s),
                    DefaultAskTimeout, default)
                .PipeTo(self, success: s => new Confirmed(s), failure: _ => AskTimeout.Instance);
            Become(Idle);
        });

        Receive<ProducerController.RequestNext<TestConsumer.Job>>(_ =>
        {
            throw new IllegalStateException("Unexpected RequestNext, already got one");
        });

        Receive<Confirmed>(c =>
        {
            _log.Info("Reply Confirmed [{0}]", c.SeqNr);
            _replyProbe.Tell(c.SeqNr);
        });

        Receive<AskTimeout>(_ => { _log.Warning("Timeout"); });
    }

    protected override void PreStart()
    {
        _producerController.Tell(new ProducerController.Start<TestConsumer.Job>(Self));
    }

    public sealed class Confirmed
    {
        public Confirmed(long seqNr)
        {
            SeqNr = seqNr;
        }

        public long SeqNr { get; }
    }

    public sealed class Tick
    {
        public static readonly Tick Instance = new();

        private Tick()
        {
        }
    }

    public sealed class AskTimeout
    {
        public static readonly AskTimeout Instance = new();

        private AskTimeout()
        {
        }
    }
}