// -----------------------------------------------------------------------
//  <copyright file="ProducerControllerImpl.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.IO;
using Akka.Pattern;
using Akka.Util;
using Akka.Util.Extensions;
using static Akka.Delivery.ProducerController;
using static Akka.Delivery.ConsumerController;

namespace Akka.Delivery.Internal;

/// <summary>
///     INTERNAL API
/// </summary>
/// <typeparam name="T">The type of message handled by this producer</typeparam>
internal sealed class ProducerController<T> : ReceiveActor, IWithTimers
{
    /// <summary>
    ///     Default send function for when none are specified.
    /// </summary>
    private static readonly Func<SequencedMessage<T>, object> DefaultSend = message => message;

    private readonly Option<Props> _durableProducerQueueProps;

    /// <summary>
    ///     Used only for testing to simulate network failures.
    /// </summary>
    private readonly Func<object, double>? _fuzzingControl;

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly Lazy<Serialization.Serialization> _serialization = new(() => Context.System.Serialization);
    private readonly ITimeProvider _timeProvider;

    /// <summary>
    ///     Uses a custom send function. Used with the ShardingProducerController so the messages can be wrapped inside
    ///     a ShardEnvelope before being delivered to the shard region.
    /// </summary>
    public ProducerController(string producerId,
        Option<Props> durableProducerQueue, Action<SequencedMessage<T>> sendAdapter,
        ProducerController.Settings? settings = null,
        ITimeProvider? timeProvider = null,
        Func<object, double>? fuzzingControl = null)
    {
        ProducerId = producerId;
        Settings = settings ?? ProducerController.Settings.Create(Context.System);
        _durableProducerQueueProps = durableProducerQueue;
        _timeProvider = timeProvider ?? DateTimeOffsetNowTimeProvider.Instance;
        _fuzzingControl = fuzzingControl;

        // this state gets overridden during the loading sequence, so it's not used at all really
        CurrentState = new State(false, 0, 0, 0, true, 0, ImmutableList<SequencedMessage<T>>.Empty,
            ActorRefs.NoSender, ImmutableList<SequencedMessage<T>>.Empty,
            ImmutableDictionary<long, IActorRef>.Empty, _ => { }, 0);

        // not going to use the ConsumerController here due to custom send adapter
        WaitingForActivation(Context.System.DeadLetters.AsOption(), state =>
        {
            state = state with
            {
                // use the custom send adapter
                Send = sendAdapter
            };

            BecomeActive(state);
        });
    }

    public ProducerController(string producerId,
        Option<Props> durableProducerQueue, ProducerController.Settings? settings = null,
        ITimeProvider? timeProvider = null,
        Func<object, double>? fuzzingControl = null)
    {
        ProducerId = producerId;
        Settings = settings ?? ProducerController.Settings.Create(Context.System);
        _durableProducerQueueProps = durableProducerQueue;
        _timeProvider = timeProvider ?? DateTimeOffsetNowTimeProvider.Instance;
        _fuzzingControl = fuzzingControl;

        // this state gets overridden during the loading sequence, so it's not used at all really
        CurrentState = new State(false, 0, 0, 0, true, 0, ImmutableList<SequencedMessage<T>>.Empty,
            ActorRefs.NoSender, ImmutableList<SequencedMessage<T>>.Empty,
            ImmutableDictionary<long, IActorRef>.Empty, _ => { }, 0);

        WaitingForActivation(Option<IActorRef>.None, BecomeActive);
    }

    public string ProducerId { get; }

    public State CurrentState { get; private set; }

    public Option<IActorRef> DurableProducerQueueRef { get; private set; }

    public ProducerController.Settings Settings { get; }

    public ITimerScheduler Timers { get; set; } = null!;

    protected internal override bool AroundReceive(Receive receive, object message)
    {
        // TESTING PURPOSES ONLY - used to simulate network failures.
        if (_fuzzingControl != null && ThreadLocalRandom.Current.NextDouble() < _fuzzingControl(message))
            return true;
        return base.AroundReceive(receive, message);
    }

    #region Internal Message and State Types

    /// <summary>
    ///     The delivery state of the producer.
    /// </summary>
    public record struct State
    {
        public State(bool requested, long currentSeqNr, long confirmedSeqNr, long requestedSeqNr, bool supportResend,
            long firstSeqNr,
            ImmutableList<SequencedMessage<T>> unconfirmed, IActorRef? producer,
            ImmutableList<SequencedMessage<T>> remainingChunks,
            ImmutableDictionary<long, IActorRef> replyAfterStore, Action<SequencedMessage<T>> send,
            long storeMessageSentInProgress)
        {
            Requested = requested;
            CurrentSeqNr = currentSeqNr;
            ConfirmedSeqNr = confirmedSeqNr;
            RequestedSeqNr = requestedSeqNr;
            FirstSeqNr = firstSeqNr;
            Unconfirmed = unconfirmed;
            Producer = producer;
            RemainingChunks = remainingChunks;
            ReplyAfterStore = replyAfterStore;
            Send = send;
            StoreMessageSentInProgress = storeMessageSentInProgress;
            SupportResend = supportResend;
        }

        /// <summary>
        ///     Has the consumer sent us their first request yet?
        /// </summary>
        public bool Requested { get; init; }

        /// <summary>
        ///     Highest produced sequence number. Should always be less than or equal to <see cref="ConfirmedSeqNr" />.
        /// </summary>
        public long CurrentSeqNr { get; init; }

        /// <summary>
        ///     Highest confirmed sequence number
        /// </summary>
        public long ConfirmedSeqNr { get; init; }

        /// <summary>
        ///     The current sequence number being requested by the consumer.
        /// </summary>
        public long RequestedSeqNr { get; init; }

        public bool SupportResend { get; init; }

        public ImmutableDictionary<long, IActorRef> ReplyAfterStore { get; init; }

        /// <summary>
        ///     The first sequence number in this state.
        /// </summary>
        public long FirstSeqNr { get; init; }

        /// <summary>
        ///     The unconfirmed messages that have been sent to the consumer.
        /// </summary>
        public ImmutableList<SequencedMessage<T>> Unconfirmed { get; init; }

        /// <summary>
        ///     When chunked delivery is enabled, this is where the not-yet-transmitted chunks are stored.
        /// </summary>
        public ImmutableList<SequencedMessage<T>> RemainingChunks { get; init; }

        /// <summary>
        ///     The sequence number of a message that is currently being stored.
        /// </summary>
        public long StoreMessageSentInProgress { get; init; }

        /// <summary>
        ///     A reference to the producer actor.
        /// </summary>
        public IActorRef? Producer { get; init; }

        /// <summary>
        ///     Monad for sending a message to the ConsumerController.
        /// </summary>
        public Action<SequencedMessage<T>> Send { get; init; }
    }

    #endregion

    #region Behaviors

    private void WaitingForActivation(Option<IActorRef> consumerController, Action<State> becomeActive)
    {
        var initialState = CreateInitialState(_durableProducerQueueProps.HasValue);

        bool IsReadyForActivation()
        {
            return !CurrentState.Producer.IsNobody() && consumerController.HasValue && initialState.HasValue;
        }

        Receive<ProducerController.Start<T>>(start =>
        {
            AssertLocalProducer(start.Producer);
            CurrentState = CurrentState with { Producer = start.Producer };

            if (IsReadyForActivation())
                becomeActive(CreateState(start.Producer, consumerController, initialState.Value));
        });

        Receive<RegisterConsumer<T>>(consumer =>
        {
            consumerController = consumer.ConsumerController.AsOption();

            if (IsReadyForActivation())
                becomeActive(CreateState(CurrentState.Producer!, consumerController, initialState.Value));
        });

        Receive<LoadStateReply<T>>(reply =>
        {
            initialState = reply.State;
            if (IsReadyForActivation())
                becomeActive(CreateState(CurrentState.Producer!, consumerController, initialState.Value));
        });

        Receive<LoadStateFailed>(failed =>
        {
            if (failed.Attempts > Settings.DurableQueueRetryAttempts)
            {
                var errorMessage = $"LoadState failed after [{failed.Attempts}] attempts. Giving up.";
                _log.Error(errorMessage);
                throw new TimeoutException(errorMessage);
            }

            _log.Warning("LoadState failed, attempt [{0}] of [{1}], retrying", failed.Attempts,
                Settings.DurableQueueRetryAttempts);
            // retry
            AskLoadState(DurableProducerQueueRef, failed.Attempts + 1);
        });

        Receive<DurableQueueTerminated>(terminated =>
        {
            throw new IllegalStateException("DurableQueue was unexpectedly terminated.");
        });
    }

    private void BecomeActive(State state)
    {
        CurrentState = state;
        var requested = false;
        if (CurrentState.Unconfirmed.IsEmpty)
        {
            requested = true;

            CurrentState.Producer.Tell(new RequestNext<T>(ProducerId, 1L, 0, Self));
        }
        else // will only be true if we've loaded our state from persistence
        {
            _log.Debug("Starting with [{0}] unconfirmed", CurrentState.Unconfirmed.Count);
            Self.Tell(ResendFirst.Instance);
            requested = false;
        }

        CurrentState = CurrentState with { Requested = requested };

        Become(Active);
    }

    private void Active()
    {
        // receiving a live message with an explicit ACK target
        Receive<MessageWithConfirmation<T>>(sendNext =>
        {
            CheckReceiveMessageRemainingChunkState();
            var chunks = Chunk(sendNext.Message, true, _serialization.Value);
            var newReplyAfterStore =
                CurrentState.ReplyAfterStore.SetItem(chunks.Last().SeqNr, sendNext.ReplyTo);

            if (DurableProducerQueueRef.IsEmpty)
            {
                // TODO: optimize this with spans and slices
                OnMsg(chunks.First(), newReplyAfterStore, chunks.Skip(1).ToImmutableList());
            }
            else
            {
                var seqMsg = chunks.First();
                StoreMessageSent(
                    DurableProducerQueue.MessageSent<T>.FromMessageOrChunked(seqMsg.SeqNr, seqMsg.Message, seqMsg.Ack,
                        DurableProducerQueue.NoQualifier, _timeProvider.Now.Ticks), 1);

                CurrentState = CurrentState with
                {
                    RemainingChunks = chunks,
                    ReplyAfterStore = newReplyAfterStore,
                    StoreMessageSentInProgress = seqMsg.SeqNr
                };
            }
        });

        // receiving a live message without an explicit ACK target
        Receive<T>(sendNext =>
        {
            CheckReceiveMessageRemainingChunkState();
            var chunks = Chunk(sendNext, false, _serialization.Value);

            if (DurableProducerQueueRef.IsEmpty)
            {
                // TODO: optimize this with spans and slices
                OnMsg(chunks.First(), CurrentState.ReplyAfterStore, chunks.Skip(1).ToImmutableList());
            }
            else
            {
                var seqMsg = chunks.First();
                StoreMessageSent(
                    DurableProducerQueue.MessageSent<T>.FromMessageOrChunked(seqMsg.SeqNr, seqMsg.Message, seqMsg.Ack,
                        DurableProducerQueue.NoQualifier, _timeProvider.Now.Ticks), 1);

                CurrentState = CurrentState with
                {
                    RemainingChunks = chunks, StoreMessageSentInProgress = seqMsg.SeqNr
                };
            }
        });

        Receive<StoreMessageSentCompleted<T>>(completed =>
        {
            ReceiveStoreMessageSentCompleted(completed.MessageSent.SeqNr);
        });

        Receive<StoreMessageSentFailed<T>>(ReceiveStoreMessageSentFailed);

        Receive<Request>(r => { ReceiveRequest(r.ConfirmedSeqNr, r.RequestUpToSeqNr, r.SupportResend, r.ViaTimeout); });

        Receive<Ack>(ack => ReceiveAck(ack.ConfirmedSeqNr));

        Receive<SendChunk>(_ => ReceiveSendChunk());

        Receive<Resend>(resend => ReceiveResend(resend.FromSeqNr));

        Receive<ResendFirst>(_ => ReceiveResendFirst());

        Receive<ResendFirstUnconfirmed>(_ => ReceiveResendFirstUnconfirmed());

        Receive<ProducerController.Start<T>>(ReceiveStart);

        Receive<RegisterConsumer<T>>(c => ReceiveRegisterConsumer(c.ConsumerController));

        Receive<DurableQueueTerminated>(terminated =>
            throw new IllegalStateException("DurableQueue was unexpectedly terminated."));

        ReceiveAny(_ => throw new InvalidOperationException($"Unexpected message: {_.GetType()}"));
    }

    protected override void PreStart()
    {
        DurableProducerQueueRef = AskLoadState();
    }

    #endregion

    #region Internal Methods and Properties

    private void CheckOnMsgRequestedState()
    {
        if (!CurrentState.Requested || CurrentState.CurrentSeqNr > CurrentState.RequestedSeqNr)
            throw new IllegalStateException(
                $"Unexpected Msg when no demand, requested: {CurrentState.Requested}, requestedSeqNr: {CurrentState.RequestedSeqNr}, currentSeqNr:{CurrentState.CurrentSeqNr}");
    }

    private void CheckReceiveMessageRemainingChunkState()
    {
        if (CurrentState.RemainingChunks.Any())
            throw new IllegalStateException(
                $"Received unexpected message before sending remaining {CurrentState.RemainingChunks.Count} chunks");
    }

    private void OnMsg(SequencedMessage<T> seqMsg,
        ImmutableDictionary<long, IActorRef> newReplyAfterStore,
        ImmutableList<SequencedMessage<T>> remainingChunks)
    {
        CheckOnMsgRequestedState();
        if (seqMsg.IsLastChunk && remainingChunks.Any())
            throw new IllegalStateException(
                $"SeqNsg [{seqMsg.SeqNr}] was last chunk but remaining {remainingChunks.Count} chunks");

        if (_log.IsDebugEnabled) _log.Debug("Sending [{0}] with seqNo [{1}] to consumer", seqMsg.Message, seqMsg.SeqNr);

        var newUnconfirmed = CurrentState.SupportResend
            ? CurrentState.Unconfirmed.Add(seqMsg)
            : ImmutableList<SequencedMessage<T>>.Empty; // no need to keep unconfirmed if no resend

        if (CurrentState.CurrentSeqNr == CurrentState.FirstSeqNr)
            Timers.StartSingleTimer(ResendFirst.Instance, ResendFirst.Instance,
                Settings.DurableQueueResendFirstInterval);

        CurrentState.Send(seqMsg);
        var newRequested = false;
        if (CurrentState.CurrentSeqNr == CurrentState.RequestedSeqNr)
        {
            newRequested = remainingChunks.Any();
        }
        else if (seqMsg.IsLastChunk)
        {
            // kick off read task
            CurrentState.Producer.Tell(new RequestNext<T>(ProducerId, CurrentState.CurrentSeqNr + 1,
                CurrentState.ConfirmedSeqNr, Self));
            newRequested = true;
        }
        else
        {
            Self.Tell(SendChunk.Instance);
            newRequested = true;
        }

        CurrentState = CurrentState with
        {
            Unconfirmed = newUnconfirmed,
            Requested = newRequested,
            CurrentSeqNr = CurrentState.CurrentSeqNr + 1,
            ReplyAfterStore = newReplyAfterStore,
            RemainingChunks = remainingChunks,
            StoreMessageSentInProgress = 0
        };
    }

    private State OnAck(long newConfirmedSeqNr)
    {
        var replies = CurrentState.ReplyAfterStore.Where(c => c.Key <= newConfirmedSeqNr).Select(c => (c.Key, c.Value))
            .ToArray();
        var newReplyAfterStore =
            CurrentState.ReplyAfterStore.Where(c => c.Key > newConfirmedSeqNr).ToImmutableDictionary();

        if (_log.IsDebugEnabled && replies.Any())
            _log.Debug("Sending confirmation replies from [{0}] to [{1}]", replies.First().Key, replies.Last().Key);

        foreach (var (seqNr, replyTo) in replies) replyTo.Tell(seqNr);

        var newUnconfirmed = CurrentState.SupportResend
            ? CurrentState.Unconfirmed.Where(c => c.SeqNr > newConfirmedSeqNr).ToImmutableList()
            : ImmutableList<SequencedMessage<T>>.Empty; // no need to keep unconfirmed if no resend

        if (newConfirmedSeqNr == CurrentState.FirstSeqNr)
            Timers.Cancel(ResendFirst.Instance);

        var newMaxConfirmedSeqNr = Math.Max(CurrentState.ConfirmedSeqNr, newConfirmedSeqNr);

        DurableProducerQueueRef.OnSuccess(d =>
        {
            // Storing the confirmedSeqNr can be "write behind", at-least-once delivery
            // TODO to reduce number of writes, consider to only StoreMessageConfirmed for the Request messages and not for each Ack
            if (newMaxConfirmedSeqNr != CurrentState.ConfirmedSeqNr)
                d.Tell(new DurableProducerQueue.StoreMessageConfirmed(newMaxConfirmedSeqNr,
                    DurableProducerQueue.NoQualifier, _timeProvider.Now.Ticks));
        });

        return CurrentState = CurrentState with
        {
            ConfirmedSeqNr = newConfirmedSeqNr, ReplyAfterStore = newReplyAfterStore, Unconfirmed = newUnconfirmed
        };
    }

    private void ReceiveRequest(long newConfirmedSeqNr, long newRequestedSeqNr, bool supportResend, bool viaTimeout)
    {
        _log.Debug("Received request, confirmed [{0}], requested [{1}], current [{2}]", newConfirmedSeqNr,
            newRequestedSeqNr, CurrentState.CurrentSeqNr);

        var stateAfterAck = OnAck(newConfirmedSeqNr);

        var newUnconfirmed = supportResend ? stateAfterAck.Unconfirmed : ImmutableList<SequencedMessage<T>>.Empty;

        if ((viaTimeout || newConfirmedSeqNr == CurrentState.FirstSeqNr) && supportResend)
            // the last message was lost and no more message was sent that would trigger Resend
            ResendUnconfirmed(newUnconfirmed);

        // when supportResend=false the requestedSeqNr window must be expanded if all sent messages were lost
        var newRequestedSeqNr2 = !supportResend && newRequestedSeqNr <= stateAfterAck.CurrentSeqNr
            ? stateAfterAck.CurrentSeqNr + (newRequestedSeqNr - newConfirmedSeqNr)
            : newRequestedSeqNr;

        if (newRequestedSeqNr2 != newRequestedSeqNr)
            _log.Debug("Expanded requestedSeqNr from [{0}] to [{1}], because current [{3}] and all were probably lost.",
                newRequestedSeqNr, newRequestedSeqNr2, stateAfterAck.CurrentSeqNr);

        if (newRequestedSeqNr > CurrentState.RequestedSeqNr)
        {
            bool newRequested;
            if (CurrentState.StoreMessageSentInProgress != 0)
            {
                newRequested = CurrentState.Requested;
            }
            else if (!CurrentState.RemainingChunks.IsEmpty)
            {
                Self.Tell(SendChunk.Instance);
                newRequested = CurrentState.Requested;
            }
            else if (!CurrentState.Requested && newRequestedSeqNr2 - CurrentState.RequestedSeqNr > 0)
            {
                CurrentState.Producer.Tell(new RequestNext<T>(ProducerId, CurrentState.CurrentSeqNr, newConfirmedSeqNr,
                    Self));
                newRequested = true;
            }
            else
            {
                newRequested = CurrentState.Requested;
            }

            CurrentState = CurrentState with
            {
                Requested = newRequested,
                SupportResend = supportResend,
                RequestedSeqNr = newRequestedSeqNr2,
                Unconfirmed = newUnconfirmed
            };
        }
        else
        {
            CurrentState = CurrentState with { SupportResend = supportResend, Unconfirmed = newUnconfirmed };
        }
    }

    private void ReceiveAck(long newConfirmedSeqNr)
    {
        if (_log.IsDebugEnabled)
            _log.Debug("Received ack, confirmed [{0}], current [{1}]", newConfirmedSeqNr, CurrentState.CurrentSeqNr);

        var stateAfterAck = OnAck(newConfirmedSeqNr);
        if (newConfirmedSeqNr == stateAfterAck.FirstSeqNr && !stateAfterAck.Unconfirmed.IsEmpty)
            ResendUnconfirmed(stateAfterAck.Unconfirmed);

        CurrentState = stateAfterAck;
    }

    private Option<IActorRef> AskLoadState()
    {
        return _durableProducerQueueProps.Select(p =>
        {
            var actor = Context.ActorOf(p.WithDispatcher(Context.Dispatcher.Id), "durable");
            Context.WatchWith(actor, DurableQueueTerminated.Instance);
            AskLoadState(Option<IActorRef>.Create(actor), 1);
            return actor;
        });
    }

    private void AskLoadState(Option<IActorRef> durableProducerQueue, int attempt)
    {
        var timeout = Settings.DurableQueueRequestTimeout;
        durableProducerQueue.OnSuccess(@ref =>
        {
            DurableProducerQueue.LoadState Mapper(IActorRef r)
            {
                return new DurableProducerQueue.LoadState(r);
            }

            var self = Self;
            @ref.Ask<DurableProducerQueue.State<T>>(Mapper, timeout, default)
                .PipeTo(self, success: state => new LoadStateReply<T>(state),
                    failure: ex => new LoadStateFailed(attempt)); // timeout
        });
    }

    private static Option<DurableProducerQueue.State<T>> CreateInitialState(bool hasDurableQueue)
    {
        if (hasDurableQueue) return Option<DurableProducerQueue.State<T>>.None;
        return Option<DurableProducerQueue.State<T>>.Create(DurableProducerQueue.State<T>.Empty);
    }

    private State CreateState(IActorRef producer, Option<IActorRef> consumerController,
        DurableProducerQueue.State<T> loadedState)
    {
        var unconfirmedBuilder = ImmutableList.CreateBuilder<SequencedMessage<T>>();
        var i = 0;
        foreach (var u in loadedState.Unconfirmed)
        {
            unconfirmedBuilder.Add(
                new SequencedMessage<T>(ProducerId, u.SeqNr, u.Message, i == 0, u.Ack, Self));
            i++;
        }

        void Send(SequencedMessage<T> msg)
        {
            consumerController.Value.Tell(msg);
        }

        return new State(false, loadedState.CurrentSeqNr, loadedState.HighestConfirmedSeqNr, 1L, true,
            loadedState.HighestConfirmedSeqNr + 1, unconfirmedBuilder.ToImmutable(), producer,
            ImmutableList<SequencedMessage<T>>.Empty, ImmutableDictionary<long, IActorRef>.Empty, Send, 0);
    }

    private void ResendUnconfirmed(ImmutableList<SequencedMessage<T>> newUnconfirmed)
    {
        if (!newUnconfirmed.IsEmpty)
        {
            var fromSeqNr = newUnconfirmed.First().SeqNr;
            var toSeqNr = newUnconfirmed.Last().SeqNr;
            _log.Debug("Resending unconfirmed messages [{0} - {1}]", fromSeqNr, toSeqNr);
            foreach (var n in newUnconfirmed)
                CurrentState.Send(n);
        }
    }

    private void ReceiveResendFirstUnconfirmed()
    {
        if (CurrentState.Unconfirmed.Any())
        {
            _log.Debug("Resending first unconfirmed [{0}]", CurrentState.Unconfirmed.First().SeqNr);
            CurrentState.Send(CurrentState.Unconfirmed.First());
        }
    }

    private void ReceiveResendFirst()
    {
        if (!CurrentState.Unconfirmed.IsEmpty && CurrentState.Unconfirmed[0].SeqNr == CurrentState.FirstSeqNr)
        {
            _log.Debug("Resending first message [{0}]", CurrentState.Unconfirmed[0].SeqNr);
            CurrentState.Send(CurrentState.Unconfirmed[0].AsFirst());
        }
        else
        {
            if (CurrentState.CurrentSeqNr > CurrentState.FirstSeqNr)
                Timers.Cancel(ResendFirst.Instance);
        }
    }

    private void ReceiveStart(ProducerController.Start<T> start)
    {
        AssertLocalProducer(start.Producer);
        _log.Debug("Registering new producer [{0}], currentSeqNr [{1}]", start.Producer, CurrentState.CurrentSeqNr);
        if (CurrentState is { Requested: true, RemainingChunks.IsEmpty: true })
            start.Producer.Tell(new RequestNext<T>(ProducerId, CurrentState.CurrentSeqNr, CurrentState.ConfirmedSeqNr,
                Self));

        CurrentState = CurrentState with { Producer = start.Producer };
    }

    private void ReceiveRegisterConsumer(IActorRef consumerController)
    {
        var newFirstSeqNr = CurrentState.Unconfirmed.IsEmpty
            ? CurrentState.CurrentSeqNr
            : CurrentState.Unconfirmed.First().SeqNr;

        _log.Debug("Register new ConsumerController [{0}], starting with seqNr [{1}]", consumerController,
            newFirstSeqNr);

        if (!CurrentState.Unconfirmed.IsEmpty)
        {
            Timers.StartSingleTimer(ResendFirst.Instance, ResendFirst.Instance,
                Settings.DurableQueueResendFirstInterval);
            Self.Tell(ResendFirst.Instance);
        }

        // update the send function
        void Send(SequencedMessage<T> msg)
        {
            consumerController.Tell(msg);
        }

        CurrentState = CurrentState with { Send = Send, FirstSeqNr = newFirstSeqNr };
    }

    private void ReceiveSendChunk()
    {
        if (!CurrentState.RemainingChunks.IsEmpty &&
            CurrentState.RemainingChunks.First().SeqNr <= CurrentState.RequestedSeqNr &&
            CurrentState.StoreMessageSentInProgress == 0)
        {
            if (_log.IsDebugEnabled)
                _log.Debug("Send next chunk seqNr [{0}].", CurrentState.RemainingChunks.First().SeqNr);

            if (DurableProducerQueueRef.IsEmpty)
            {
                // TODO: optimize this using spans and slices
                OnMsg(CurrentState.RemainingChunks.First(), CurrentState.ReplyAfterStore,
                    CurrentState.RemainingChunks.Skip(1).ToImmutableList());
            }
            else
            {
                var seqMsg = CurrentState.RemainingChunks.First();
                StoreMessageSent(
                    DurableProducerQueue.MessageSent<T>.FromMessageOrChunked(seqMsg.SeqNr, seqMsg.Message, seqMsg.Ack,
                        DurableProducerQueue.NoQualifier, _timeProvider.Now.Ticks), 1);
                CurrentState = CurrentState with { StoreMessageSentInProgress = seqMsg.SeqNr };
            }
        }
    }

    private ImmutableList<SequencedMessage<T>> Chunk(T msg, bool ack, Serialization.Serialization serialization)
    {
        var chunkSize = Settings.ChunkLargeMessagesBytes ?? 0;
        if (chunkSize == 0) // chunking not enabled
        {
            var sequencedMessage = new SequencedMessage<T>(ProducerId, CurrentState.CurrentSeqNr,
                msg, CurrentState.CurrentSeqNr == CurrentState.FirstSeqNr, ack, Self);
            return ImmutableList<SequencedMessage<T>>.Empty.Add(sequencedMessage);
        }

        // chunking is enabled
        var chunkedMessages = CreateChunks(msg, chunkSize, serialization).ToList();
        if (_log.IsDebugEnabled)
        {
            if (chunkedMessages.Count == 1)
                _log.Debug("No chunking of SeqNo [{0}], size [{1}] bytes", CurrentState.CurrentSeqNr,
                    chunkedMessages.First().SerializedMessage.Count);
            else
                _log.Debug("Chunking SeqNo [{0}] into [{1}] chunks, total size [{2}] bytes",
                    CurrentState.CurrentSeqNr, chunkedMessages.Count,
                    chunkedMessages.Sum(x => x.SerializedMessage.Count));
        }

        var i = 0;
        var chunks = chunkedMessages.Select(chunkedMessage =>
        {
            var seqNr = CurrentState.CurrentSeqNr + i;
            i += 1;
            var sequencedMessage = SequencedMessage<T>.FromChunkedMessage(ProducerId, seqNr,
                chunkedMessage,
                seqNr == CurrentState.FirstSeqNr, ack, Context.Self);
            return sequencedMessage;
        }).ToImmutableList();

        return chunks;
    }

    /// <summary>
    ///     INTERNAL API
    /// </summary>
    /// <remarks>
    ///     Made internal for testing purposes.
    /// </remarks>
    internal static IEnumerable<ChunkedMessage> CreateChunks(T msg, int chunkSize,
        Serialization.Serialization serialization)
    {
        var serializer = serialization.FindSerializerFor(msg);
        var manifest = Serialization.Serialization.ManifestFor(serializer, msg);
        var serializerId = serializer.Identifier;
        var bytes = serialization.Serialize(msg);
        if (bytes.Length <= chunkSize)
        {
            var chunkedMessage = new ChunkedMessage(ByteString.CopyFrom(bytes), true, true, serializerId, manifest);
            yield return chunkedMessage;
        }
        else
        {
            var chunkCount = (int)Math.Ceiling(bytes.Length / (double)chunkSize);
            var first = true;
            for (var i = 0; i < chunkCount; i++)
            {
                var isLast = i == chunkCount - 1;
                var nextChunk = Math.Min(chunkSize, bytes.Length - i * chunkSize); // needs to be the next chunkSize or remaining bytes, whichever is smaller.
                var chunkedMessage = new ChunkedMessage(ByteString.FromBytes(bytes, i * chunkSize, nextChunk), first,
                    isLast, serializerId, manifest);

                first = false;
                yield return chunkedMessage;
            }
        }
    }

    private void ReceiveStoreMessageSentCompleted(long seqNr)
    {
        if (CurrentState.StoreMessageSentInProgress == seqNr)
        {
            if (seqNr != CurrentState.CurrentSeqNr)
                throw new IllegalStateException(
                    $"currentSeqNr is [{CurrentState.CurrentSeqNr}] not matching stored seqNr [{seqNr}]");

            var seqMsg = CurrentState.RemainingChunks.First();
            if (seqNr != seqMsg.SeqNr)
                throw new IllegalStateException($"seqNr is [{seqNr}] not matching stored seqNr [{seqMsg.SeqNr}]");

            if (CurrentState.ReplyAfterStore.TryGetValue(seqNr, out var replyAfterStore))
            {
                if (_log.IsDebugEnabled) _log.Debug("Sending confirmation reply to [{0}] after storage", seqNr);

                replyAfterStore.Tell(seqNr);
            }

            var newReplyAfterStore = CurrentState.ReplyAfterStore.Remove(seqNr);

            OnMsg(seqMsg, newReplyAfterStore, CurrentState.RemainingChunks.Skip(1).ToImmutableList());
        }
        else
        {
            _log.Debug("Received StoreMessageSentCompleted for seqNr [{0}], but waiting for [{1}].",
                seqNr, CurrentState.StoreMessageSentInProgress);
        }
    }

    private void ReceiveStoreMessageSentFailed(StoreMessageSentFailed<T> f)
    {
        if (f.MessageSent.SeqNr == CurrentState.StoreMessageSentInProgress)
        {
            if (f.Attempt >= Settings.DurableQueueRetryAttempts)
            {
                var errorMessage =
                    $"StoreMessageSentFailed for seqNr [{f.MessageSent.SeqNr}] after [{f.Attempt}] attempts, giving up.";
                _log.Error(errorMessage);
                throw new TimeoutException(errorMessage);
            }

            _log.Warning("StoreMessageSentFailed for seqNr [{0}], attempt [{1}] of [{2}], retrying.",
                f.MessageSent.SeqNr, f.Attempt, Settings.DurableQueueRetryAttempts);

            // retry
            if (f.MessageSent.IsFirstChunk)
            {
                StoreMessageSent(f.MessageSent, f.Attempt + 1);
            }
            else
            {
                /* store all chunks again, because partially stored chunks are discarded by the DurableQueue
                     * when it's restarted.
                     */
                var unconfirmedReverse = CurrentState.Unconfirmed.Reverse().ToImmutableList();
                var xs = unconfirmedReverse.TakeWhile(x => !x.IsFirstChunk).ToImmutableList();
                if (unconfirmedReverse.Count == xs.Count)
                    throw new IllegalStateException(
                        $"First chunk not found in unconfirmed[{string.Join(",", CurrentState.Unconfirmed)}]");

                var firstChunk = unconfirmedReverse.Skip(xs.Count).First();
                var newRemainingChunksBuilder = ImmutableList.CreateBuilder<SequencedMessage<T>>();
                newRemainingChunksBuilder.Add(firstChunk);
                newRemainingChunksBuilder.AddRange(xs.Reverse());
                newRemainingChunksBuilder.AddRange(CurrentState.RemainingChunks);
                var newRemainingChunks = newRemainingChunksBuilder.ToImmutable();
                var i = xs.Count + 1;
                var newUnconfirmed = CurrentState.Unconfirmed.Take(CurrentState.Unconfirmed.Count - i)
                    .ToImmutableList();

                _log.Debug("Store all [{0}] chunks again, starting at seqNr [{1}]", newRemainingChunks.Count,
                    firstChunk.SeqNr);
                if (!newRemainingChunks.First().IsFirstChunk || !newRemainingChunks.Last().IsLastChunk)
                    throw new IllegalStateException(
                        $"Wrong remainingChunks[{string.Join(",", newRemainingChunks)}]");

                StoreMessageSent(
                    DurableProducerQueue.MessageSent<T>.FromMessageOrChunked(firstChunk.SeqNr, firstChunk.Message,
                        firstChunk.Ack, DurableProducerQueue.NoQualifier, _timeProvider.Now.Ticks),
                    f.Attempt + 1);

                CurrentState = CurrentState with
                {
                    StoreMessageSentInProgress = firstChunk.SeqNr,
                    RemainingChunks = newRemainingChunks,
                    Unconfirmed = newUnconfirmed,
                    CurrentSeqNr = firstChunk.SeqNr
                };
            }
        }
    }

    private void ReceiveResend(long fromSeqNr)
    {
        ResendUnconfirmed(CurrentState.Unconfirmed.Where(c => c.SeqNr >= fromSeqNr).ToImmutableList());
        if (fromSeqNr == 0 && !CurrentState.Unconfirmed.IsEmpty)
        {
            // need to mark the first unconfirmed message as "first" again, so the delivery-state inside the ConsumerController is correct
            var newUnconfirmed = ImmutableList.Create(CurrentState.Unconfirmed.First().AsFirst())
                .AddRange(CurrentState.Unconfirmed.Skip(1));

            CurrentState = CurrentState with { Unconfirmed = newUnconfirmed };
        }
    }


    private void StoreMessageSent(DurableProducerQueue.MessageSent<T> messageSent, int attempt)
    {
        DurableProducerQueue.StoreMessageSent<T> Mapper(IActorRef r)
        {
            return new DurableProducerQueue.StoreMessageSent<T>(messageSent, r);
        }

        var self = Self;
        DurableProducerQueueRef.Value.Ask<DurableProducerQueue.StoreMessageSentAck>(Mapper,
                Settings.DurableQueueRequestTimeout, default)
            .PipeTo(self, success: ack => new StoreMessageSentCompleted<T>(messageSent),
                failure: ex => new StoreMessageSentFailed<T>(messageSent, attempt));
    }

    #endregion
}