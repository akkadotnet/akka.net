// //-----------------------------------------------------------------------
// // <copyright file="EventSourcedProducerQueue.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;
using Akka.Util.Internal;
using static Akka.Delivery.DurableProducerQueue;

namespace Akka.Persistence.Delivery;

/// <summary>
/// A <see cref="DurableProducerQueue"/> implementation that can be used with <see cref="ProducerController"/>
/// for reliable delivery of messages. It is implemented with event-sourcing and stores one event before
/// sending the message to the destination and one event for the confirmation that the message has been
/// delivered and processed.
/// </summary>
public static class EventSourcedProducerQueue
{
    /// <summary>
    /// <see cref="EventSourcedProducerQueue"/> settings.
    /// </summary>
    public record Settings
    {
        /// <summary>
        /// Creates a new settings instance from the `akka.reliable-delivery.producer-controller.event-sourced-durable-queue`
        /// of the <see cref="ActorSystem"/>.
        /// </summary>
        public static Settings Create(ActorSystem sys)
        {
            var config =
                sys.Settings.Config.GetConfig("akka.reliable-delivery.producer-controller.event-sourced-durable-queue");
            return Create(config);
        }

        /// <summary>
        /// Creates a new settings instance from Config corresponding to `akka.reliable-delivery.producer-controller.event-sourced-durable-queue`.
        /// </summary>
        public static Settings Create(Config config)
        {
            return new Settings(config.GetTimeSpan("restart-max-backoff"), config.GetInt("snapshot-every"),
                config.GetInt("keep-n-snapshots"), config.GetBoolean("delete-events"),
                config.GetTimeSpan("cleanup-unused-after"),
                config.GetString("journal-plugin-id"), config.GetString("snapshot-plugin-id"));
        }

        private Settings(TimeSpan restartMaxBackoff, int snapshotEvery, int keepNSnapshots, bool deleteEvents,
            TimeSpan cleanupUnusedAfter, string journalPluginId, string snapshotPluginId)
        {
            RestartMaxBackoff = restartMaxBackoff;
            SnapshotEvery = snapshotEvery;
            KeepNSnapshots = keepNSnapshots;
            DeleteEvents = deleteEvents;
            CleanupUnusedAfter = cleanupUnusedAfter;
            JournalPluginId = journalPluginId;
            SnapshotPluginId = snapshotPluginId;
        }

        public TimeSpan RestartMaxBackoff { get; init; }
        public int SnapshotEvery { get; init; }
        public int KeepNSnapshots { get; init; }
        public bool DeleteEvents { get; init; }
        public TimeSpan CleanupUnusedAfter { get; init; }
        public string JournalPluginId { get; init; }
        public string SnapshotPluginId { get; init; }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class CleanupTick
    {
        public static readonly CleanupTick Instance = new CleanupTick();

        private CleanupTick()
        {
        }
    }

    /// <summary>
    /// Creates <see cref="Props"/> for an <see cref="EventSourcedProducerQueue{T}"/> that can be passed
    /// to a <see cref="ProducerController"/> for reliable message delivery.
    /// </summary>
    /// <param name="persistentId">The Akka.Persistence id used by this actor - must be globally unique.</param>
    /// <param name="settings">The settings for this producer queue.</param>
    /// <typeparam name="T">The type of message that is supported by the <see cref="ProducerController"/>.</typeparam>
    public static Props Create<T>(string persistentId, Settings settings)
    {
        var childProps = Props.Create(() => new EventSourcedProducerQueue<T>(persistentId, settings, null));
        var backoffSupervisorProps = Backoff.OnStop(childProps, "producer-queue",
            TimeSpan.FromSeconds(1).Min(settings.RestartMaxBackoff), settings.RestartMaxBackoff, 0.1, 100);
        return backoffSupervisorProps.Props;
    }

    /// <summary>
    /// Creates <see cref="Props"/> for an <see cref="EventSourcedProducerQueue{T}"/> that can be passed
    /// to a <see cref="ProducerController"/> for reliable message delivery.
    /// </summary>
    /// <param name="persistentId">The Akka.Persistence id used by this actor - must be globally unique.</param>
    /// <param name="system">The <see cref="ActorSystem"/> or <see cref="IActorContext"/> this actor might be created under.
    /// This value is only used to load the <see cref="Settings"/>.
    /// </param>
    /// <typeparam name="T">The type of message that is supported by the <see cref="ProducerController"/>.</typeparam>
    public static Props Create<T>(string persistentId, IActorRefFactory system)
    {
        return system switch
        {
            IActorContext context => Create<T>(persistentId, Settings.Create(context.System)),
            ActorSystem actorSystem => Create<T>(persistentId, Settings.Create(actorSystem)),
            _ => throw new ArgumentOutOfRangeException(nameof(system), $"Unknown IActorRefFactory {system}")
        };
    }
}

/// <summary>
/// INTERNAL API
/// </summary>
/// <typeparam name="T">The types of messages that can be handled by the <see cref="ProducerController"/>.</typeparam>
internal sealed class EventSourcedProducerQueue<T> : UntypedPersistentActor, IWithTimers, IWithStash
{
    public EventSourcedProducerQueue(string persistenceId, EventSourcedProducerQueue.Settings? settings = null,
        ITimeProvider? timeProvider = null)
    {
        PersistenceId = persistenceId;
        Settings = settings ?? EventSourcedProducerQueue.Settings.Create(Context.System);
        _timeProvider = timeProvider ?? DateTimeOffsetNowTimeProvider.Instance;
        JournalPluginId = Settings.JournalPluginId;
        SnapshotPluginId = Settings.SnapshotPluginId;
        Self.Tell(EventSourcedProducerQueue.CleanupTick.Instance);
        Timers.StartPeriodicTimer(EventSourcedProducerQueue.CleanupTick.Instance,
            EventSourcedProducerQueue.CleanupTick.Instance,
            TimeSpan.FromMilliseconds(Settings.CleanupUnusedAfter.TotalMilliseconds / 2));
    }

    public EventSourcedProducerQueue.Settings Settings { get; }

    public override string PersistenceId { get; }
    public ITimerScheduler Timers { get; set; } = null!;
    private readonly ITimeProvider _timeProvider;
    private readonly ILoggingAdapter _log = Context.GetLogger();

    // transient
    private bool _initialCleanupDone = false;

    public State<T> State { get; private set; } = State<T>.Empty;

    protected override void OnCommand(object message)
    {
        if (!_initialCleanupDone)
            OnCommandBeforeInitialCleanup(message);

        switch (message)
        {
            case StoreMessageSent<T>(var sent, var replyTo):
            {
                var currentSeqNr = State.CurrentSeqNr;
                if (sent.SeqNr == currentSeqNr)
                {
                    // TODO: add trace support
                    if (_log.IsDebugEnabled)
                        _log.Debug("StoreMessageSent seqNr [{0}], confirmationQualifier [{1}]", sent.SeqNr,
                            sent.ConfirmationQualifier);
                    Persist(sent, e =>
                    {
                        State = State.AddMessageSent(e);
                        replyTo.Tell(new StoreMessageSentAck(e.SeqNr));
                    });
                }
                else if (sent.SeqNr == currentSeqNr - 1)
                {
                    // already stored - could be a retry after timeout
                    _log.Debug("Duplicate seqNr [{0}], currentSeqNr [{1}]", sent.SeqNr, currentSeqNr);
                    Sender.Tell(new StoreMessageSentAck(sent.SeqNr));
                }
                else
                {
                    // may happen after failure
                    _log.Debug("Ignoring unexpected seqNr [{0}], currentSeqNr [{1}]", sent.SeqNr, currentSeqNr);
                    Unhandled(message); // no reply, request will time out
                }

                break;
            }
            case StoreMessageConfirmed(var seqNr, var confirmationQualifier, var timestamp):
            {
                // TODO: add trace support
                if (_log.IsDebugEnabled)
                    _log.Debug("StoreMessageConfirmed seqNr [{0}], confirmationQualifier [{1}]",
                        seqNr, confirmationQualifier);

                var previousConfirmedSeqNr = 0L;
                if (State.ConfirmedSeqNr.TryGetValue(confirmationQualifier, out var confirmedNr))
                {
                    previousConfirmedSeqNr = confirmedNr.Item1;
                }

                if (seqNr > previousConfirmedSeqNr)
                {
                    Persist(new Confirmed(seqNr, confirmationQualifier, timestamp),
                        e => { State = State.AddConfirmed(e.SeqNr, e.Qualifier, e.Timestamp); });
                }

                break;
            }
            case LoadState(var replyTo):
            {
                replyTo.Tell(State);
                break;
            }
            case EventSourcedProducerQueue.CleanupTick _:
            {
                OnCleanupTick();
                break;
            }
            case SaveSnapshotSuccess saveSnapshotSuccess:
            {
                if (Settings.DeleteEvents)
                    DeleteMessages(saveSnapshotSuccess.Metadata.SequenceNr);
                DeleteSnapshots(new SnapshotSelectionCriteria((saveSnapshotSuccess.Metadata.SequenceNr - 1) -
                                                              Settings.KeepNSnapshots * Settings.SnapshotEvery));
                break;
            }
            case SaveSnapshotFailure failure:
            {
                _log.Warning(failure.Cause, "Failed to save snapshot, sequence number [{0}]",
                    failure.Metadata.SequenceNr);
                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }

    private void TakeSnapshotWhenReady()
    {
        if (LastSequenceNr % Settings.SnapshotEvery == 0 && LastSequenceNr != 0)
            SaveSnapshot(State);
    }

    private void OnCommandBeforeInitialCleanup(object message)
    {
        switch (message)
        {
            case EventSourcedProducerQueue.CleanupTick _:
            {
                var old = OldUnconfirmedToCleanup(State);
                var stateWithoutPartialChunkedMessages = State.CleanUpPartialChunkedMessages();
                _initialCleanupDone = true;
                if (old.IsEmpty && State.Equals(stateWithoutPartialChunkedMessages))
                {
                    Stash.UnstashAll();
                }
                else
                {
                    if (_log.IsDebugEnabled)
                        _log.Debug("Initial cleanup [{0}]", string.Join(", ", old));
                    var e = new Cleanup(old);

                    Persist(e, cleanup =>
                    {
                        State = State.CleanUp(cleanup.ConfirmationQualifiers);
                        Stash.UnstashAll();
                    });
                }

                break;
            }
            default:
                Stash.Stash();
                break;
        }
    }

    protected override void OnRecover(object message)
    {
        switch (message)
        {
            case SnapshotOffer { Snapshot: State<T> state }:
            {
                State = state;
                break;
            }
            case MessageSent<T> sent:
            {
                State = State.AddMessageSent(sent);
                break;
            }
            case Confirmed confirmed:
            {
                State = State.AddConfirmed(confirmed.SeqNr, confirmed.Qualifier, confirmed.Timestamp);
                break;
            }
            case Cleanup cleanup:
            {
                State = State.CleanUp(cleanup.ConfirmationQualifiers);
                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }

    private void OnCleanupTick()
    {
        var oldUnconfirmed = OldUnconfirmedToCleanup(State);
        if (oldUnconfirmed.IsEmpty)
            return; // early exit

        if (_log.IsDebugEnabled)
            _log.Debug("Periodic cleanup [{0}]", string.Join(",", oldUnconfirmed));

        var e = new Cleanup(oldUnconfirmed);
        Persist(e, cleanup => { State = State.CleanUp(cleanup.ConfirmationQualifiers); });
    }

    private ImmutableHashSet<string> OldUnconfirmedToCleanup(State<T> state)
    {
        var now = _timeProvider.Now.Ticks;
        var oldUnconfirmed = state.ConfirmedSeqNr.Where(c => now - c.Value.Item2 >= Settings.CleanupUnusedAfter.Ticks
                                                             && !state.Unconfirmed.Any(m =>
                                                                 m.ConfirmationQualifier.Equals(c.Key)))
            .Select(c => c.Key).ToImmutableHashSet();
        return oldUnconfirmed;
    }
}