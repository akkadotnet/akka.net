#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;
using Akka.Util.Extensions;
using Akka.Util.Internal;
using static Akka.Delivery.ConsumerController;
using static Akka.Delivery.ProducerController;

namespace Akka.Delivery.Internal;

/// <summary>
///     INTERNAL API
/// </summary>
/// <typeparam name="T">
///     The types of messages handled by the <see cref="ConsumerController" /> and
///     <see cref="ProducerController" />.
/// </typeparam>
internal sealed class ConsumerController<T> : ReceiveActor, IWithTimers, IWithStash
{
    /// <summary>
    ///     Used only for testing to simulate network failures.
    /// </summary>
    private readonly Func<object, double>? _fuzzingControl;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly RetryTimer _retryTimer;
    private readonly Serialization.Serialization _serialization = Context.System.Serialization;

    private Option<IActorRef> _producerControllerRegistration;

    public ConsumerController(Option<IActorRef> producerControllerRegistration, ConsumerController.Settings settings,
        Func<object, double>? fuzzingControl = null)
    {
        _producerControllerRegistration = producerControllerRegistration;
        Settings = settings;
        _fuzzingControl = fuzzingControl;
        _retryTimer = new RetryTimer(Settings.ResendIntervalMin, Settings.ResendIntervalMax, Timers);

        WaitForStart();
    }

    public ConsumerController.Settings Settings { get; }
    public State CurrentState { get; private set; }
    public bool ResendLost => !Settings.OnlyFlowControl;
    public IStash Stash { get; set; } = null!;

    public ITimerScheduler Timers { get; set; } = null!;

    protected internal override bool AroundReceive(Receive receive, object message)
    {
        // TESTING PURPOSES ONLY - used to simulate network failures.
        if (_fuzzingControl != null && ThreadLocalRandom.Current.NextDouble() < _fuzzingControl(message))
            return true;
        return base.AroundReceive(receive, message);
    }

    private void WaitForStart()
    {
        // this Timer will get overwritten by the RetryTimer once we exit the WaitForStart stage
        Timers.StartPeriodicTimer(Retry.Instance, Retry.Instance, Settings.ResendIntervalMin);
        var stopping = false;

        Receive<RegisterToProducerController<T>>(reg =>
        {
            reg.ProducerController.Tell(new RegisterConsumer<T>(Self));
            _producerControllerRegistration = reg.ProducerController.AsOption();
        });

        Receive<ConsumerController.Start<T>>(start =>
        {
            AssertLocalConsumer(start.DeliverTo);
            Context.WatchWith(start.DeliverTo, new ConsumerTerminated(start.DeliverTo));

            _log.Debug("Received Start, unstash [{0}] messages", Stash.Count);
            CurrentState = InitialState(start, _producerControllerRegistration, stopping);
            Stash.Unstash();
            Timers.CancelAll(); // clear recurring timers
            _retryTimer.Start(); // let the RetryTimer take over
            Become(Active);
        });

        Receive<SequencedMessage<T>>(seqMsg =>
        {
            if (Stash.IsFull)
            {
                _log.Debug(
                    "Received SequencedMessage seqNr [{0}], stashing before Start, discarding message because Stash is full.",
                    seqMsg.SeqNr);
            }
            else
            {
                _log.Debug("Received SequencedMessage seqNr [{0}], stashing before Start, stashed size [{1}]",
                    seqMsg.SeqNr,
                    Stash.Count + 1);
                Stash.Stash();
            }
        });

        Receive<DeliverThenStop<T>>(_ =>
        {
            if (Stash.IsEmpty)
                Context.Stop(Self);
            else
                stopping = true;
        });

        Receive<Retry>(_ =>
        {
            if (_producerControllerRegistration.HasValue)
            {
                _log.Debug("Retry sending RegisterConsumer to [{0}]", _producerControllerRegistration);
                _producerControllerRegistration.Value.Tell(new RegisterConsumer<T>(Self));
            }
        });

        Receive<ConsumerTerminated>(terminated =>
        {
            _log.Debug("Consumer [{0}] terminated.", terminated.Consumer);
            Context.Stop(Self);
        });
    }

    /// <summary>
    ///     Expecting a SequencedMessage from ProducerController, that will be delivered to the consumer if
    ///     the seqNr is right.
    /// </summary>
    private void Active()
    {
        Receive<SequencedMessage<T>>(seqMsg =>
        {
            var pid = seqMsg.ProducerId;
            var seqNr = seqMsg.SeqNr;
            var expectedSeqNr = CurrentState.ReceivedSeqNr + 1;

            _retryTimer.Reset();

            if (CurrentState.IsProducerChanged(seqMsg))
            {
                if (seqMsg.First && _log.IsDebugEnabled)
                    _log.Debug("Received first SequencedMessage from seqNr [{0}], delivering to consumer.", seqNr);
                ReceiveChangedProducer(seqMsg);
            }
            else if (CurrentState.Registering.HasValue)
            {
                _log.Debug(
                    "Received SequencedMessage seqNr [{0}], discarding message because registering to new ProducerController.",
                    seqNr);
                Stash.Unstash();
            }
            else if (CurrentState.IsNextExpected(seqMsg))
            {
                if (_log.IsDebugEnabled) // should really be tracing loglevel
                    _log.Debug("Received SequencedMessage seqNr [{0}], delivering to consumer.", seqNr);

                CurrentState = CurrentState.Copy(receivedSeqNr: seqNr);
                Deliver(seqMsg);
            }
            else if (seqNr > expectedSeqNr)
            {
                var msg = "Received SequencedMessage seqNr [{0}], but expected [{1}]," +
                          (ResendLost ? " Requesting resend from expected seqNr." : " delivering to consumer anyway.");
                _log.Debug(msg, seqNr, expectedSeqNr);
                if (ResendLost)
                {
                    seqMsg.ProducerController.Tell(new Resend(expectedSeqNr));
                    Stash.ClearStash();
                    _retryTimer.Start();
                    Become(Resending);
                }
                else
                {
                    CurrentState = CurrentState.Copy(receivedSeqNr: seqNr);
                    Deliver(seqMsg);
                }
            }
            else // seqNr < expectedSeqNr
            {
                _log.Debug("Received duplicate SequencedMessage seqNr [{0}], expected [{1}].", seqNr, expectedSeqNr);
                Stash.Unstash();
                if (seqMsg.First) CurrentState = RetryRequest();
            }
        });

        Receive<Retry>(retry =>
        {
            ReceiveRetry(() =>
            {
                CurrentState = RetryRequest();
                Active();
            });
        });

        Receive<Confirmed>(ReceiveUnexpectedConfirmed);

        Receive<ConsumerController.Start<T>>(start => { ReceiveStart(start, Active); });

        Receive<ConsumerTerminated>(t => ReceiveConsumerTerminated(t.Consumer));

        Receive<RegisterToProducerController<T>>(controller =>
        {
            ReceiveRegisterToProducerController(controller, Active);
        });

        Receive<DeliverThenStop<T>>(_ => { ReceiveDeliverThenStop(Active); });
    }

    /// <summary>
    ///     It has detected a missing seqNr and requested a <see cref="Resend" />. Expecting a
    ///     <see cref="SequencedMessage{T}" /> from the
    ///     ProducerController with the missing seqNr. Other <see cref="SequencedMessage{T}" /> with different seqNr will be
    ///     discarded since they were in flight before the <see cref="Resend" /> request and will anyway be sent again.
    /// </summary>
    private void Resending()
    {
        if (Stash.NonEmpty)
            throw new IllegalStateException("Stash should have been cleared before resending.");

        Receive<SequencedMessage<T>>(seqMsg =>
        {
            var seqNr = seqMsg.SeqNr;
            if (CurrentState.IsProducerChanged(seqMsg))
            {
                if (seqMsg.First && _log.IsDebugEnabled) // TODO: should really be trace
                    _log.Debug("Received first SequencedMessage from seqNr [{0}], delivering to consumer.", seqNr);
                ReceiveChangedProducer(seqMsg);
            }
            else if (CurrentState.Registering.HasValue)
            {
                _log.Debug(
                    "Received SequencedMessage seqNr [{0}], discarding message because registering to new ProducerController.",
                    seqNr);
            }
            else if (CurrentState.IsNextExpected(seqMsg))
            {
                _log.Debug("Received missing SequencedMessage seqNr [{0}].", seqNr);
                CurrentState = CurrentState.Copy(receivedSeqNr: seqNr);
                Deliver(seqMsg);
            }
            else
            {
                _log.Debug("Received SequencedMessage seqNr [{0}], discarding message because waiting for [{1}].",
                    seqNr, CurrentState.ReceivedSeqNr + 1);

                if (seqMsg.First)
                    CurrentState = RetryRequest();
            }
        });

        Receive<Retry>(r =>
        {
            ReceiveRetry(() =>
            {
                _log.Debug("Retry sending Resend [{0}].", CurrentState.ReceivedSeqNr + 1);
                CurrentState.ProducerController.Tell(new Resend(CurrentState.ReceivedSeqNr + 1));
                Resending();
            });
        });

        Receive<Confirmed>(ReceiveUnexpectedConfirmed);
        Receive<ConsumerController.Start<T>>(start => { ReceiveStart(start, Resending); });
        Receive<ConsumerTerminated>(t => ReceiveConsumerTerminated(t.Consumer));
        Receive<RegisterToProducerController<T>>(controller =>
        {
            ReceiveRegisterToProducerController(controller, Active);
        });
        Receive<DeliverThenStop<T>>(_ => { ReceiveDeliverThenStop(Resending); });
    }

    private void WaitingForConfirmation(SequencedMessage<T> sequencedMessage)
    {
        Receive<Confirmed>(c =>
        {
            var seqNr = sequencedMessage.SeqNr;
            if (_log.IsDebugEnabled)
                _log.Debug("Received Confirmed seqNr [{0}] from consumer, stashed size [{1}].", seqNr, Stash.Count);

            long ComputeNextSeqNr()
            {
                if (sequencedMessage.First)
                {
                    // confirm the first message immediately to cancel resending of first
                    var newRequestedSeqNr = seqNr - 1 + Settings.FlowControlWindow;
                    _log.Debug("Sending Request after first with confirmedSeqNr [{0}], requestUpToSeqNr [{1}]", seqNr,
                        newRequestedSeqNr);
                    CurrentState.ProducerController.Tell(new Request(seqNr, newRequestedSeqNr, ResendLost, false));
                    return newRequestedSeqNr;
                }

                if (CurrentState.RequestedSeqNr - seqNr == Settings.FlowControlWindow / 2)
                {
                    var newRequestedSeqNr = CurrentState.RequestedSeqNr + Settings.FlowControlWindow / 2;
                    _log.Debug("Sending Request with confirmedSeqNr [{0}], requestUpToSeqNr [{1}]", seqNr,
                        newRequestedSeqNr);
                    CurrentState.ProducerController.Tell(new Request(seqNr, newRequestedSeqNr, ResendLost, false));
                    _retryTimer.Start(); // reset interval since Request was just sent
                    return newRequestedSeqNr;
                }

                if (sequencedMessage.Ack)
                {
                    if (_log.IsDebugEnabled)
                        _log.Debug("Sending Ack seqNr [{0}]", seqNr);
                    CurrentState.ProducerController.Tell(new Ack(seqNr));
                }

                return CurrentState.RequestedSeqNr;
            }

            var requestedSeqNr = ComputeNextSeqNr();
            if (CurrentState.Stopping && Stash.IsEmpty)
            {
                _log.Debug("Stopped at seqNr [{0}], after delivery of buffered messages.", seqNr);
                // best effort to Ack latest confirmed when stopping

                /*
                 * NOTE: this is not the same as Akka.Typed Behaviors.Stopped, which executes after actor PostStop
                 * seqNr returned here is a higher seqNr than what's returned in PostStop (the highest confirmed seqNr),
                 * therefore we're going to attempt to recreate those semantics using `GracefulStop` and a detached task
                 */

                async Task ShutDownAndStop()
                {
                    var producerController = CurrentState.ProducerController;
                    var ack = new Ack(seqNr);

                    try
                    {
                        Context.Stop(Self);
                        await Self.WatchAsync();
                    }
                    finally
                    {
                        producerController.Tell(ack);
                    }
                }

#pragma warning disable CS4014
                ShutDownAndStop(); // needs to run as a detached task
#pragma warning restore CS4014
            }
            else
            {
                CurrentState = CurrentState.Copy(confirmedSeqNr: seqNr, requestedSeqNr: requestedSeqNr);
                Stash.Unstash();
                Become(Active);
            }
        });

        Receive<SequencedMessage<T>>(msg =>
        {
            var expectedSeqNr = sequencedMessage.SeqNr + Stash.Count + 1; // need to compute stash buffer size
            if (msg.SeqNr < expectedSeqNr && msg.ProducerController.Equals(sequencedMessage.ProducerController))
            {
                _log.Debug("Received duplicate SequencedMessage seqNr [{0}]", msg.SeqNr);
            }
            else if (Stash.IsFull)
            {
                // possible that the stash is full if ProducerController resends unconfirmed (duplicates)
                // dropping them since they can be resent
                _log.Debug("Received SequencedMessage seqNr [{0}] but stash is full, dropping.", msg.SeqNr);
            }
            else
            {
                if (_log.IsDebugEnabled)
                    _log.Debug(
                        "Received SequencedMessage seqNr [{0}], stashing while waiting for consumer to confirm [{1}]. Stashed size [{2}].",
                        msg.SeqNr,
                        sequencedMessage.SeqNr, Stash.Count + 1);
                Stash.Stash();
            }
        });

        Receive<Retry>(_ =>
        {
            // no retries when WaitingForConfirmation, will be performed from (idle) active
        });

        Receive<ConsumerController.Start<T>>(start =>
        {
            start.DeliverTo.Tell(new Delivery<T>(sequencedMessage.Message.Message!, Self, sequencedMessage.ProducerId,
                sequencedMessage.SeqNr));
            ReceiveStart(start, () => WaitingForConfirmation(sequencedMessage));
        });

        Receive<ConsumerTerminated>(terminated => { ReceiveConsumerTerminated(terminated.Consumer); });

        Receive<RegisterToProducerController<T>>(controller =>
        {
            ReceiveRegisterToProducerController(controller, () => WaitingForConfirmation(sequencedMessage));
        });

        Receive<DeliverThenStop<T>>(stop =>
        {
            ReceiveDeliverThenStop(() => WaitingForConfirmation(sequencedMessage));
        });
    }

    protected override void PostStop()
    {
        // best effort to Ack latest confirmed when stopping
        CurrentState.ProducerController?.Tell(new Ack(CurrentState.ConfirmedSeqNr));
    }

    #region Internal Methods

    private void ReceiveUnexpectedConfirmed(Confirmed c)
    {
        _log.Warning("Received unexpected Confirmed for seqNr from consumer.");
        Unhandled(c);
    }

    private void ReceiveRetry(Action nextBehavior)
    {
        _retryTimer.ScheduleNext();
        if (_retryTimer.Interval != _retryTimer.MinBackoff)
            _log.Debug("Schedule next retry in [{0} ms]", _retryTimer.Interval.TotalMilliseconds);
        if (CurrentState.Registering.IsEmpty)
            Become(nextBehavior);
        else
            CurrentState.Registering.Value.Tell(new RegisterConsumer<T>(Context.Self));
    }

    private State RetryRequest()
    {
        if (CurrentState.ProducerController.Equals(Context.System.DeadLetters))
            return CurrentState;

        var newRequestedSeqNr = ResendLost
            ? CurrentState.RequestedSeqNr
            : CurrentState.ReceivedSeqNr + Settings.FlowControlWindow / 2;
        _log.Debug("Retry sending Request with confirmedSeqNr [{0}], requestUpToSeqNr [{1}]",
            CurrentState.ConfirmedSeqNr, newRequestedSeqNr);
        CurrentState.ProducerController.Tell(new Request(CurrentState.ConfirmedSeqNr, newRequestedSeqNr, ResendLost,
            true));
        return CurrentState.Copy(requestedSeqNr: newRequestedSeqNr);
    }

    private void ReceiveDeliverThenStop(Action nextBehavior)
    {
        if (Stash.IsEmpty && CurrentState.ReceivedSeqNr == CurrentState.ConfirmedSeqNr)
        {
            _log.Debug("Stopped at seqNr [{0}], no buffered messages.", CurrentState.ConfirmedSeqNr);
            Context.Stop(Self);
        }
        else
        {
            CurrentState = CurrentState.Copy(stopping: true);
            Become(nextBehavior);
        }
    }

    private void ReceiveRegisterToProducerController(RegisterToProducerController<T> reg, Action nextBehavior)
    {
        if (!reg.ProducerController.Equals(CurrentState.ProducerController))
        {
            _log.Debug("Registering to new ProducerController [{0}], previous was [{1}].", reg.ProducerController,
                CurrentState.ProducerController);
            _retryTimer.Start();
            reg.ProducerController.Tell(new RegisterConsumer<T>(Self));
            CurrentState = CurrentState.Copy(registering: reg.ProducerController.AsOption());
            Become(nextBehavior);
        }
    }

    private void ReceiveConsumerTerminated(IActorRef consumer)
    {
        _log.Debug("Consumer [{0}] terminated.", consumer);
        Context.Stop(Self);
    }

    private void ReceiveStart(ConsumerController.Start<T> start, Action nextBehavior)
    {
        AssertLocalConsumer(start.DeliverTo);
        if (start.DeliverTo.Equals(CurrentState.Consumer))
        {
            Become(nextBehavior);
        }
        else
        {
            // if the Consumer is restarted, it may send Start again
            Context.Unwatch(CurrentState.Consumer);
            Context.WatchWith(start.DeliverTo, new ConsumerTerminated(start.DeliverTo));
            CurrentState = CurrentState.Copy(consumer: start.DeliverTo);
            Become(nextBehavior);
        }
    }

    private void ReceiveChangedProducer(SequencedMessage<T> seqMsg)
    {
        var seqNr = seqMsg.SeqNr;

        if (seqMsg.First || !ResendLost)
        {
            LogChangedProducer(seqMsg);
            var newRequestedSeqNr = seqMsg.SeqNr - 1 + Settings.FlowControlWindow;
            _log.Debug("Sending Request with requestUpToSeqNr [{0}] after first SequencedMessage.", newRequestedSeqNr);
            seqMsg.ProducerController.Tell(new Request(0, newRequestedSeqNr, ResendLost, false));

            CurrentState = CurrentState.Copy(seqMsg.ProducerController,
                seqMsg.ProducerId,
                receivedSeqNr: seqNr,
                requestedSeqNr: newRequestedSeqNr, confirmedSeqNr: 0L,
                registering: CurrentState.UpdatedRegistering(seqMsg));
            Deliver(seqMsg);
        }
        else if (CurrentState.ReceivedSeqNr == 0)
        {
            // needed for sharding
            _log.Debug("Received SequencedMessage seqNr [{0}], from new producer [{1}] but it wasn't first. Resending.",
                seqNr, seqMsg.ProducerController);
            // request to resend all of unconfirmed, and mark first
            seqMsg.ProducerController.Tell(new Resend(0L));
            Stash.ClearStash();
            _retryTimer.Start();
            Become(Resending);
        }
        else
        {
            _log.Warning(
                "Received SequencedMessage seqNr [{0}], " +
                "discarding message because it was from unexpected producer [{1}] when expecting [{2}].",
                seqNr, seqMsg.ProducerController, CurrentState.ProducerController);
            Stash.Unstash();
        }
    }

    private void LogChangedProducer(SequencedMessage<T> seqMsg)
    {
        if (CurrentState.ProducerController.Equals(Context.System.DeadLetters))
            _log.Debug("Associated with new ProducerController [{0}], seqNr [{1}].", seqMsg.ProducerController,
                seqMsg.SeqNr);
        else
            _log.Debug("Changing ProducerController from [{0}] to [{1}], seqNr [{2}]", CurrentState.ProducerController,
                seqMsg.ProducerController, seqMsg.SeqNr);
    }

    private void Deliver(SequencedMessage<T> seqMsg)
    {
        var previouslyCollectedChunks =
            seqMsg.IsFirstChunk ? ImmutableList<SequencedMessage<T>>.Empty : CurrentState.CollectedChunks;
        if (seqMsg.IsLastChunk)
        {
            var assembledSeqMsg = !seqMsg.Message.IsMessage
                ? AssembleChunks(previouslyCollectedChunks.Add(seqMsg))
                : seqMsg;
            CurrentState.Consumer.Tell(new Delivery<T>(assembledSeqMsg.Message.Message!, Context.Self,
                seqMsg.ProducerId, seqMsg.SeqNr));
            CurrentState = CurrentState.ClearCollectedChunks();
            Become(() => WaitingForConfirmation(assembledSeqMsg));
        }
        else
        {
            // collecting chunks
            long GetNewRequestedSeqNr()
            {
                if (CurrentState.RequestedSeqNr - seqMsg.SeqNr == Settings.FlowControlWindow / 2)
                {
                    var newRequestedSeqNr = CurrentState.RequestedSeqNr + Settings.FlowControlWindow / 2;
                    _log.Debug(
                        "Sending Request when collecting chunks seqNr [{0}], confirmedSeqNr [{1}], requestUpToSeqNr [{2}]",
                        seqMsg.SeqNr,
                        CurrentState.ConfirmedSeqNr, newRequestedSeqNr);
                    CurrentState.ProducerController.Tell(new Request(CurrentState.ConfirmedSeqNr, newRequestedSeqNr,
                        ResendLost, false));
                    _retryTimer.Start();
                    return newRequestedSeqNr;
                }

                return CurrentState.RequestedSeqNr;
            }

            var newRequestedSeqNr = GetNewRequestedSeqNr();
            Stash.Unstash();
            CurrentState = CurrentState.Copy(requestedSeqNr: newRequestedSeqNr,
                collectedChunks: previouslyCollectedChunks.Add(seqMsg));
            Become(Active);
        }
    }

    private SequencedMessage<T> AssembleChunks(ImmutableList<SequencedMessage<T>> collectedChunks)
    {
        var bufferSize = collectedChunks.Sum(chunk => chunk.Message.Chunk!.Value.SerializedMessage.Count);
        byte[] bytes;
        using (var mem = MemoryPool<byte>.Shared.Rent(bufferSize))
        {
            var curIndex = 0;
            var memory = mem.Memory;
            foreach (var b in collectedChunks.Select(c => c.Message.Chunk!.Value.SerializedMessage))
            {
                b.CopyTo(ref memory, curIndex, b.Count);
                curIndex += b.Count;
            }

            // have to slice the buffer here since the memory pool may have allocated more than we needed
            bytes = memory.Slice(0, curIndex).ToArray();
        }

        var headMessage = collectedChunks.Last(); // this is the last chunk
        var headChunk = headMessage.Message.Chunk!.Value;

        // serialization exceptions are thrown, because it will anyway be stuck with same error if retried and
        // we can't just ignore the message

        var message = (T)_serialization.Deserialize(bytes, headChunk.SerializerId, headChunk.Manifest);
        return new SequencedMessage<T>(headMessage.ProducerId, headMessage.SeqNr, message,
            collectedChunks.First().First,
            headMessage.Ack, headMessage.ProducerController);
    }

    private static State InitialState(ConsumerController.Start<T> start, Option<IActorRef> registering, bool stopping)
    {
        return new State(Context.System.DeadLetters, "n/a", start.DeliverTo, 0, 0, 0,
            ImmutableList<SequencedMessage<T>>.Empty, registering, stopping);
    }

    #endregion

    #region Internal Types

    private sealed class RetryTimer
    {
        public RetryTimer(TimeSpan minBackoff, TimeSpan maxBackoff, ITimerScheduler timers)
        {
            MinBackoff = minBackoff;
            MaxBackoff = maxBackoff;
            Interval = minBackoff;
            Timers = timers;
        }

        public ITimerScheduler Timers { get; }

        public TimeSpan MinBackoff { get; }

        public TimeSpan MaxBackoff { get; }

        public TimeSpan Interval { get; private set; }

        public void Start()
        {
            Interval = MinBackoff;
            // todo: when we have timers with fixed delays, call it here
            Timers.StartPeriodicTimer(Retry.Instance, Retry.Instance, Interval);
        }

        public void ScheduleNext()
        {
            var newInterval = Interval == MaxBackoff
                ? MaxBackoff
                : MaxBackoff.Min(TimeSpan.FromSeconds(Interval.TotalSeconds * 1.5));
            if (newInterval != Interval) Timers.StartPeriodicTimer(Retry.Instance, Retry.Instance, newInterval);
        }

        public void Reset()
        {
            if (Interval != MinBackoff)
                Start();
        }
    }

    private sealed class Retry
    {
        public static readonly Retry Instance = new();

        private Retry()
        {
        }
    }

    private sealed class ConsumerTerminated
    {
        public ConsumerTerminated(IActorRef consumer)
        {
            Consumer = consumer;
        }

        public IActorRef Consumer { get; }
    }

    internal readonly struct State
    {
        public State(
            IActorRef producerController,
            string producerId,
            IActorRef consumer,
            long receivedSeqNr,
            long confirmedSeqNr,
            long requestedSeqNr,
            ImmutableList<SequencedMessage<T>> collectedChunks,
            Option<IActorRef> registering,
            bool stopping)
        {
            ProducerController = producerController;
            ProducerId = producerId;
            Consumer = consumer;
            ReceivedSeqNr = receivedSeqNr;
            ConfirmedSeqNr = confirmedSeqNr;
            RequestedSeqNr = requestedSeqNr;
            CollectedChunks = collectedChunks;
            Registering = registering;
            Stopping = stopping;
        }

        public IActorRef ProducerController { get; }

        public string ProducerId { get; }

        public IActorRef Consumer { get; }

        public long ReceivedSeqNr { get; }

        public long ConfirmedSeqNr { get; }

        public long RequestedSeqNr { get; }

        public ImmutableList<SequencedMessage<T>> CollectedChunks { get; }

        public Option<IActorRef> Registering { get; }

        public bool Stopping { get; }

        public bool IsNextExpected(SequencedMessage<T> seqMsg)
        {
            return seqMsg.SeqNr == ReceivedSeqNr + 1;
        }

        public bool IsProducerChanged(SequencedMessage<T> seqMsg)
        {
            return !seqMsg.ProducerController.Equals(ProducerController) || ReceivedSeqNr == 0;
        }

        public Option<IActorRef> UpdatedRegistering(SequencedMessage<T> seqMsg)
        {
            // clear out registration if this is a dupe, otherwise keep our previous registration value
            if (Registering.IsEmpty) return Option<IActorRef>.None;
            if (seqMsg.ProducerController.Equals(Registering.Value))
                return Option<IActorRef>.None;
            return Registering;
        }

        public State ClearCollectedChunks()
        {
            return CollectedChunks.IsEmpty ? this : Copy(collectedChunks: ImmutableList<SequencedMessage<T>>.Empty);
        }

        // add a copy method that can optionally overload every property
        public State Copy(
            IActorRef? producerController = null,
            string? producerId = null,
            IActorRef? consumer = null,
            long? receivedSeqNr = null,
            long? confirmedSeqNr = null,
            long? requestedSeqNr = null,
            ImmutableList<SequencedMessage<T>>? collectedChunks = null,
            Option<IActorRef>? registering = null,
            bool? stopping = null)
        {
            return new State(
                producerController ?? ProducerController,
                producerId ?? ProducerId,
                consumer ?? Consumer,
                receivedSeqNr ?? ReceivedSeqNr,
                confirmedSeqNr ?? ConfirmedSeqNr,
                requestedSeqNr ?? RequestedSeqNr,
                collectedChunks ?? CollectedChunks,
                registering ?? Registering,
                stopping ?? Stopping);
        }
    }

    #endregion
}