#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Delivery;
using Akka.Delivery.Internal;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;
using Akka.Util.Extensions;
using static Akka.Cluster.Sharding.Delivery.ShardingProducerController;

namespace Akka.Cluster.Sharding.Delivery.Internal;

using OutKey = String;
using EntityId = String;
using TotalSeqNr = Int64;
using OutSeqNr = Int64;

internal sealed class ShardingProducerController<T> : ReceiveActor, IWithStash, IWithTimers
{
    public string ProducerId { get; }

    public IActorRef ShardRegion { get; }

    public ShardingProducerController.Settings Settings { get; }

    public Option<IActorRef> DurableQueueRef { get; private set; } = Option<IActorRef>.None;
    private readonly Option<Props> _durableQueueProps;
    private readonly ITimeProvider _timeProvider;

    public IActorRef MsgAdapter { get; private set; } = ActorRefs.Nobody;

    public IActorRef RequestNextAdapter { get; private set; } = ActorRefs.Nobody;

    public State<T> CurrentState { get; private set; } = State<T>.Empty;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    public IStash Stash { get; set; } = null!;

    public ITimerScheduler Timers { get; set; } = null!;

    public ShardingProducerController(string producerId, IActorRef shardRegion, Option<Props> durableQueueProps,
        ShardingProducerController.Settings settings, ITimeProvider? timeProvider = null)
    {
        ProducerId = producerId;
        ShardRegion = shardRegion;
        _durableQueueProps = durableQueueProps;
        Settings = settings;
        _timeProvider = timeProvider ?? DateTimeOffsetNowTimeProvider.Instance;

        WaitingForStart(Option<IActorRef>.None, CreateInitialState(_durableQueueProps.HasValue));
    }

    protected override void PreStart()
    {
        DurableQueueRef = AskLoadState();

        // TODO: replace Akka.Actor.Dsl calls here with function refs, once we can find a better way to expose those
        var self = Self;
        RequestNextAdapter =
            Context.ActorOf(
                act =>
                {
                    act.Receive<ProducerController.RequestNext<T>>((msg, ctx) =>
                    {
                        self.Forward(new WrappedRequestNext<T>(msg));
                    });
                }, "requestNextAdapter");
    }

    #region Behaviors

    private void WaitingForStart(Option<IActorRef> producer, Option<DurableProducerQueue.State<T>> initialState)
    {
        Receive<Start<T>>(start =>
        {
            ProducerController.AssertLocalProducer(start.Producer);
            if (initialState.HasValue)
            {
                BecomeActive(start.Producer, initialState);
            }
            else
            {
                // waiting for load state reply
                producer = start.Producer.AsOption();
            }
        });

        Receive<LoadStateReply<T>>(reply =>
        {
            if (producer.HasValue)
            {
                BecomeActive(producer.Value, reply.State);
            }
            else
            {
                // waiting for Start
                initialState = reply.State;
            }
        });

        Receive<LoadStateFailed>(failed =>
        {
            if (failed.Attempt >= Settings.ProducerControllerSettings.DurableQueueRetryAttempts)
            {
                var errorMsg = $"Failed to load state from durable queue after {failed.Attempt} attempts, giving up.";
                _log.Error(errorMsg);
                throw new TimeoutException(errorMsg);
            }
            else
            {
                _log.Warning("LoadState failed, attempt [{0}] of [{1}], retrying.", failed.Attempt,
                    Settings.ProducerControllerSettings.DurableQueueRetryAttempts);
                // retry
                AskLoadState(DurableQueueRef, failed.Attempt + 1);
            }
        });

        Receive<DurableQueueTerminated>(_ =>
        {
            throw new IllegalStateException("DurableQueue was unexpectedly terminated.");
        });

        ReceiveAny(_ =>
        {
            CheckIfStashIsFull();
            Stash.Stash();
        });
    }

    private void BecomeActive(IActorRef producer, Option<DurableProducerQueue.State<T>> initialState)
    {
        Timers.StartPeriodicTimer(CleanupUnused.Instance, CleanupUnused.Instance,
            TimeSpan.FromMilliseconds(Settings.CleanupUnusedAfter.TotalMilliseconds / 2));
        Timers.StartPeriodicTimer(ShardingProducerController.ResendFirstUnconfirmed.Instance, ShardingProducerController.ResendFirstUnconfirmed.Instance,
            TimeSpan.FromMilliseconds(Settings.ResendFirstUnconfirmedIdleTimeout.TotalMilliseconds / 2));

        // resend unconfirmed before other stashed messages
        initialState.OnSuccess(s =>
        {
            var unconfirmedDeliveries = s.Unconfirmed.Select(c => new Envelope(c, Self));
            Stash.Prepend(unconfirmedDeliveries);
        });

        var self = Self;
        MsgAdapter = Context.ActorOf(act =>
        {
            act.Receive<ShardingEnvelope>((msg, ctx) => { self.Forward(new Msg(msg, 0)); });

            act.ReceiveAny((_, ctx) =>
            {
                var errorMessage =
                    $"Message sent to ShardingProducerController must be ShardingEnvelope, was [{_.GetType()
                        .Name}]";
                ctx.GetLogger().Error(errorMessage);
                ctx.Sender.Tell(new Status.Failure(new InvalidOperationException(errorMessage)));
            });
        }, "msg-adapter");

        if (initialState.IsEmpty || initialState.Value.Unconfirmed.IsEmpty)
            producer.Tell(new RequestNext<T>(MsgAdapter, Self, ImmutableHashSet<string>.Empty,
                ImmutableDictionary<string, int>.Empty));

        CurrentState = CurrentState with
        {
            Producer = producer,
            CurrentSeqNr = initialState.HasValue ? initialState.Value.CurrentSeqNr : 0,
        };

        Become(Active);
        Stash.UnstashAll();
    }

    private void Active()
    {
        Receive<Msg>(msg =>
        {
            if (DurableQueueRef.IsEmpty)
            {
                // currentSeqNr is only updated when DurableQueue is available
                OnMsg(msg.Envelope.EntityId, (T)msg.Envelope.Message, Option<IActorRef>.None, CurrentState.CurrentSeqNr,
                    CurrentState.ReplyAfterStore);
            }
            else if (msg.IsAlreadyStored)
            {
                // loaded from durable queue, currentSeqNr has already been stored previously
                OnMsg(msg.Envelope.EntityId, (T)msg.Envelope.Message, Option<IActorRef>.None, msg.AlreadyStored,
                    CurrentState.ReplyAfterStore);
            }
            else
            {
                StoreMessageSent(
                    new DurableProducerQueue.MessageSent<T>(CurrentState.CurrentSeqNr, (T)msg.Envelope.Message, false,
                        msg.Envelope.EntityId, _timeProvider.Now.Ticks), attempt: 1);
                
                CurrentState = CurrentState with { CurrentSeqNr = CurrentState.CurrentSeqNr + 1 };
            }
        });

        Receive<MessageWithConfirmation<T>>(messageWithConfirmation =>
        {
            var (entityId, message, replyTo) = messageWithConfirmation;
            
            if (DurableQueueRef.IsEmpty)
            {
                OnMsg(entityId, message, replyTo.AsOption(), CurrentState.CurrentSeqNr, CurrentState.ReplyAfterStore);
            }
            else
            {
                StoreMessageSent(new DurableProducerQueue.MessageSent<T>(CurrentState.CurrentSeqNr, message, true,
                    entityId, _timeProvider.Now.Ticks), attempt: 1);
                var newReplyAfterStore = CurrentState.ReplyAfterStore.SetItem(CurrentState.CurrentSeqNr, replyTo);
                CurrentState = CurrentState with
                {
                    CurrentSeqNr = CurrentState.CurrentSeqNr + 1,
                    ReplyAfterStore = newReplyAfterStore
                };
            }
        });

        Receive<StoreMessageSentCompleted<T>>(completed =>
        {
            var (seqNr, msg, _, entityId, _) = completed.MessageSent;
            ReceiveStoreMessageSentCompleted(seqNr, msg, entityId);
        });

        Receive<StoreMessageSentFailed<T>>(ReceiveStoreMessageSentFailed);

        Receive<Ack>(ReceiveAck);

        Receive<WrappedRequestNext<T>>(ReceiveWrappedRequestNext);
        
        Receive<ShardingProducerController.ResendFirstUnconfirmed>(_ => ResendFirstUnconfirmed());
        
        Receive<CleanupUnused>(_ => ReceiveCleanupUnused());

        Receive<Start<T>>(ReceiveStart);

        Receive<AskTimeout>(t =>
        {
            var (outKey, outSeqNr) = t;
            _log.Debug("Message seqNr [{0}] sent to entity [{1}] timed out. It will be redelivered.", outSeqNr, outKey);
        });

        Receive<DurableQueueTerminated>(_ =>
        {
            throw new IllegalStateException("DurableQueue was unexpectedly terminated.");
        });
        
        ReceiveAny(_ =>
        {
            throw new InvalidOperationException($"Unexpected message [{_}] in Active state.");
        });
    }

    #endregion

    #region Internal Methods

    private void OnMsg(EntityId entityId, T msg, Option<IActorRef> replyTo, TotalSeqNr totalSeqNr,
        ImmutableDictionary<TotalSeqNr, IActorRef> newReplyAfterStore)
    {
        var outKey = $"{ProducerId}-{entityId}";
        if (CurrentState.OutStates.TryGetValue(outKey, out var outState))
        {
            // there is demand, send immediately
            if (outState.NextTo.HasValue)
            {
                Send(msg, outKey, outState.SeqNr, outState.NextTo.Value);
                var newUnconfirmed = outState.Unconfirmed.Add(new Unconfirmed<T>(totalSeqNr, outState.SeqNr, replyTo));

                CurrentState = CurrentState with
                {
                    OutStates = CurrentState.OutStates.SetItem(outKey, outState with
                    {
                        SeqNr = outState.SeqNr + 1,
                        Unconfirmed = newUnconfirmed,
                        NextTo = Option<IActorRef>.None,
                        LastUsed = _timeProvider.Now.Ticks
                    }),
                    ReplyAfterStore = newReplyAfterStore
                };
            }
            else
            {
                var buffered = outState.Buffered;

                // no demand, buffer
                if (CurrentState.BufferSize >= Settings.BufferSize)
                    throw new IllegalStateException(
                        $"Buffer overflow, current size [{CurrentState.BufferSize}] >= max [{Settings.BufferSize}]");
                _log.Debug("Buffering message to entityId [{0}], buffer size for entityId [{1}]", entityId,
                    buffered.Count + 1);

                var newBuffered = buffered.Add(new Buffered<T>(totalSeqNr, msg, replyTo));
                var newS = CurrentState with
                {
                    OutStates = CurrentState.OutStates.SetItem(outKey, outState with
                    {
                        Buffered = newBuffered
                    }),
                    ReplyAfterStore = newReplyAfterStore
                };
                // send an updated RequestNext to indicate buffer usage
                CurrentState.Producer.Tell(CreateRequestNext(newS));
                CurrentState = newS;
            }
        }
        else
        {
            _log.Debug("Creating ProducerController for entityId [{0}]", entityId);
            Action<ConsumerController.SequencedMessage<T>> customSend = s =>
            {
                ShardRegion.Tell(new ShardingEnvelope(entityId, s));
            };

            var producer =
                Context.ActorOf(
                    Props.Create(() => new ProducerController<T>(outKey, Option<Props>.None, customSend,
                        Settings.ProducerControllerSettings, _timeProvider, null)), entityId);
            producer.Tell(new ProducerController.Start<T>(RequestNextAdapter));
            CurrentState = CurrentState with
            {
                OutStates = CurrentState.OutStates.SetItem(outKey,
                    new OutState<T>(entityId, producer, Option<IActorRef>.None,
                        ImmutableList<Buffered<T>>.Empty.Add(new Buffered<T>(totalSeqNr, msg, replyTo)), 1L,
                        ImmutableList<Unconfirmed<T>>.Empty, _timeProvider.Now.Ticks)),
                ReplyAfterStore = newReplyAfterStore
            };
        }
    }

    private ImmutableList<Unconfirmed<T>> OnAck(OutState<T> outState, long confirmedSeqNr)
    {
        var grouped = outState.Unconfirmed.GroupBy(c => c.OutSeqNr <= confirmedSeqNr).ToList();
        var confirmed = grouped.Where(c => c.Key).SelectMany(c => c).ToImmutableList();
        var newUnconfirmed = grouped.Where(c => !c.Key).SelectMany(c => c).ToImmutableList();

        if (confirmed.Any())
        {
            foreach (var c in confirmed)
            {
                switch (c)
                {
                    case (_, _, { IsEmpty: true }): // no reply
                        break;
                    case (_, _, { IsEmpty: false } replyTo):
                        replyTo.Value.Tell(Done.Instance);
                        break;
                }
            }

            DurableQueueRef.OnSuccess(d =>
            {
                // Storing the confirmedSeqNr can be "write behind", at-least-once delivery
                d.Tell(new DurableProducerQueue.StoreMessageConfirmed<T>(confirmed.Last().TotalSeqNr, outState.EntityId,
                    _timeProvider.Now.Ticks));
            });
        }

        return newUnconfirmed;
    }


    private void ReceiveStoreMessageSentCompleted(long seqNr, T msg, string entityId)
    {
        if (CurrentState.ReplyAfterStore.TryGetValue(seqNr, out var replyTo))
        {
            _log.Info("Confirmation reply to [{0}] after storage", seqNr);
            replyTo.Tell(Done.Instance);
        }

        var newReplyAfterStore = CurrentState.ReplyAfterStore.Remove(seqNr);

        OnMsg(entityId, msg, Option<IActorRef>.None, seqNr, newReplyAfterStore);
    }

    private void ReceiveStoreMessageSentFailed(StoreMessageSentFailed<T> f)
    {
        if (f.Attempt >= Settings.ProducerControllerSettings.DurableQueueRetryAttempts)
        {
            var errorMessage =
                $"StoreMessageSentFailed seqNr [{f.MessageSent.SeqNr}] failed after [{f.Attempt}] attempts, giving up.";
            _log.Error(errorMessage);
            throw new TimeoutException(errorMessage);
        }
        else
        {
            _log.Info("StoreMessageSent seqNr [{0}] failed, attempt [{1}], retrying", f.MessageSent.SeqNr, f.Attempt);
            // retry
            StoreMessageSent(f.MessageSent, f.Attempt + 1);
        }
    }

    private void ReceiveAck(Ack ack)
    {
        if (CurrentState.OutStates.TryGetValue(ack.OutKey, out var outState))
        {
            // TODO: support tracing loglevel
            if (_log.IsDebugEnabled)
                _log.Debug("Received Ack, confirmed [{0}], current [{1}]", ack.ConfirmedSeqNr,
                    CurrentState.CurrentSeqNr);
            var newUnconfirmed = OnAck(outState, ack.ConfirmedSeqNr);
            var newUsedTime = newUnconfirmed.Count != outState.Unconfirmed.Count
                ? _timeProvider.Now.Ticks
                : outState.LastUsed;

            var newOutState = outState with
            {
                Unconfirmed = newUnconfirmed,
                LastUsed = newUsedTime
            };

            CurrentState = CurrentState with
            {
                OutStates = CurrentState.OutStates.SetItem(ack.OutKey, newOutState)
            };
        }
        else
        {
            // obsolete Ack, ConsumerController already deregistered
            Unhandled(ack);
        }
    }

    private void ReceiveWrappedRequestNext(WrappedRequestNext<T> w)
    {
        var next = w.RequestNext;
        var outKey = next.ProducerId;
        if (CurrentState.OutStates.TryGetValue(outKey, out var outState))
        {
            if (outState.NextTo.HasValue)
                throw new IllegalStateException($"Received RequestNext but already has demand for [{outKey}]");

            var confirmedSeqNr = w.RequestNext.ConfirmedSeqNr;
            // TODO: support tracing loglevel
            if (_log.IsDebugEnabled)
                _log.Debug("Received RequestNext from [{0}], confirmed seqNr [{1}]", outState.EntityId, confirmedSeqNr);

            var newUnconfirmed = OnAck(outState, confirmedSeqNr);
            if (outState.Buffered.Any())
            {
                var buf = outState.Buffered.First();
                Send(buf.Msg, outKey, outState.SeqNr, next.SendNextTo);
                var newUnconfirmed2 =
                    newUnconfirmed.Add(new Unconfirmed<T>(buf.TotalSeqNr, outState.SeqNr, buf.ReplyTo));
                var newProducers = CurrentState.OutStates.SetItem(outKey,
                    outState with
                    {
                        Buffered = outState.Buffered.RemoveAt(0),
                        SeqNr = outState.SeqNr + 1,
                        Unconfirmed = newUnconfirmed2,
                        LastUsed = _timeProvider.Now.Ticks,
                        NextTo = Option<IActorRef>.None
                    });

                CurrentState = CurrentState with { OutStates = newProducers };
            }
            else
            {
                var newProducers = CurrentState.OutStates.SetItem(outKey,
                    outState with
                    {
                        Unconfirmed = newUnconfirmed,
                        LastUsed = _timeProvider.Now.Ticks,
                        NextTo = next.SendNextTo.AsOption()
                    });
                var newState = CurrentState with { OutStates = newProducers };

                // send an updated RequestNext
                CurrentState.Producer.Tell(CreateRequestNext(newState));
                CurrentState = newState;
            }
        }
        else
        {
            // if ProducerController was stopped and there was a RequestNext in flight, but will not happen in practice
            _log.Warning("Received RequestNext for unknown [{0}]", outKey);
        }
    }

    private void ReceiveStart(Start<T> start)
    {
        ProducerController.AssertLocalProducer(start.Producer);
        _log.Debug("Register new Producer [{0}], currentSeqNr [{1}].", start.Producer, CurrentState.CurrentSeqNr);
        start.Producer.Tell(CreateRequestNext(CurrentState));
        CurrentState = CurrentState with
        {
            Producer = start.Producer
        };
    }

    private void ResendFirstUnconfirmed()
    {
        var now = _timeProvider.Now.Ticks;
        foreach (var (outKey, outState) in CurrentState.OutStates)
        {
            var idleDurationMs = (now - outState.LastUsed) / TimeSpan.TicksPerMillisecond;
            if (outState.Unconfirmed.Any() &&
                idleDurationMs >= Settings.ResendFirstUnconfirmedIdleTimeout.TotalMilliseconds)
            {
                _log.Debug("Resend first unconfirmed for [{0}], because it was idle for [{1}] ms", outKey,
                    idleDurationMs);
                outState.ProducerController.Tell(ProducerController.ResendFirstUnconfirmed.Instance);
            }
        }
    }

    private void ReceiveCleanupUnused()
    {
        var now = _timeProvider.Now.Ticks;
        var removeOutKeys = CurrentState.OutStates.Select(c =>
        {
            var outKey = c.Key;
            var outState = c.Value;
            var idleDurationMs = (now - outState.LastUsed) / TimeSpan.TicksPerMillisecond;
            if (outState.Unconfirmed.IsEmpty && outState.Buffered.IsEmpty &&
                idleDurationMs >= Settings.CleanupUnusedAfter.TotalMilliseconds)
            {
                _log.Debug("Cleanup unused [{0}], because it was idle for [{1}] ms", outKey, idleDurationMs);
                Context.Stop(outState.ProducerController);
                return outKey.AsOption();
            }
            else
            {
                return Option<string>.None;
            }
        }).Where(c => c.HasValue).Select(c => c.Value).ToImmutableList();

        if (removeOutKeys.Any())
        {
            CurrentState = CurrentState with
            {
                OutStates = CurrentState.OutStates.RemoveRange(removeOutKeys)
            };
        }
    }

    private void StoreMessageSent(DurableProducerQueue.MessageSent<T> messageSent, int attempt)
    {
        var askTimeout = Settings.ProducerControllerSettings.DurableQueueRequestTimeout;
        DurableProducerQueue.StoreMessageSent<T> Mapper(IActorRef r) => new(messageSent, r);

        var self = Self;
        
        DurableQueueRef.Value.Ask<DurableProducerQueue.StoreMessageSentAck>(Mapper,
                askTimeout, cancellationToken: default)
            .PipeTo(self, success: ack => new StoreMessageSentCompleted<T>(messageSent),
                failure: ex => new StoreMessageSentFailed<T>(messageSent, attempt));
    }

    private RequestNext<T> CreateRequestNext(State<T> state)
    {
        var entitiesWithDemand = state.OutStates.Values.Where(c => c.NextTo.HasValue).Select(c => c.EntityId)
            .ToImmutableHashSet();
        var bufferedForEntitiesWithoutDemand = state.OutStates.Values.Where(c => c.NextTo.IsEmpty)
            .ToImmutableDictionary(c => c.EntityId, c => c.Buffered.Count);

        return new RequestNext<T>(MsgAdapter, Self, entitiesWithDemand, bufferedForEntitiesWithoutDemand);
    }

    private void Send(T msg, OutKey outKey, OutSeqNr outSeqNr, IActorRef nextTo)
    {
        if (_log.IsDebugEnabled) // TODO: add trace support
            _log.Debug("Sending [{0}] to [{1}] with outSeqNr [{2}]", msg?.GetType().Name, nextTo, outSeqNr);

        ProducerController.MessageWithConfirmation<T> Transform(IActorRef askTarget)
        {
            return new ProducerController.MessageWithConfirmation<T>(msg, askTarget);
        }

        var self = Self;
        nextTo.Ask<long>(Transform, Settings.InternalAskTimeout, CancellationToken.None)
            .PipeTo(self, success: seqNr =>
                {
                    if (seqNr != outSeqNr)
                        _log.Error("Inconsistent Ack seqNr [{0}] != [{1}]", seqNr, outSeqNr);
                    return new Ack(outKey, seqNr);
                },
                failure: _ => new AskTimeout(outKey, outSeqNr));
    }

    private static Option<DurableProducerQueue.State<T>> CreateInitialState(bool hasDurableQueue)
    {
        return hasDurableQueue ? Option<DurableProducerQueue.State<T>>.None : DurableProducerQueue.State<T>.Empty;
    }

    private void CheckIfStashIsFull()
    {
        if (Stash.IsFull)
            throw new ArgumentException($"Buffer is full, size [{Stash.Count}]");
    }

    private Option<IActorRef> AskLoadState()
    {
        return _durableQueueProps.Select(p =>
        {
            var durableQueue = Context.ActorOf(p, "durable");
            Context.WatchWith(durableQueue, DurableQueueTerminated.Instance);
            AskLoadState(durableQueue.AsOption(), 1);
            return durableQueue;
        });
    }

    private void AskLoadState(Option<IActorRef> durableProducerQueue, int attempt)
    {
        var loadTimeout = Settings.ProducerControllerSettings.DurableQueueRequestTimeout;
        durableProducerQueue.OnSuccess(@ref =>
        {
            DurableProducerQueue.LoadState<T> Mapper(IActorRef r) => new(r);

            var self = Self;
            @ref.Ask<DurableProducerQueue.State<T>>(Mapper, timeout: loadTimeout, cancellationToken: default)
                .PipeTo(self, success: state => new LoadStateReply<T>(state),
                    failure: ex => new LoadStateFailed(attempt)); // timeout
        });
    }

    #endregion
}