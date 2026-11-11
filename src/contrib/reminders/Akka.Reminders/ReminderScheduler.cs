using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Reminders.Sharding;
using Akka.Reminders.Storage;

namespace Akka.Reminders;

/// <summary>
/// Settings needed by the <see cref="ReminderScheduler"/> to schedule reminders.
/// </summary>
public sealed record ReminderSettings
{
    /// <summary>
    /// If we're grabbing reminders that are due before or upuntil DateTime.UtcNow,
    /// we also grab reminders that are due Now plus MaxSlippage?
    /// </summary>
    public TimeSpan MaxSlippage { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Default timeout for the reminder storage.
    /// </summary>
    public TimeSpan StorageTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How frequently we need to prune.
    /// </summary>
    public TimeSpan PruneInterval { get; init; } = TimeSpan.FromHours(12);

    /// <summary>
    /// How long to keep completed / canceled / failed reminders around.
    /// </summary>
    public TimeSpan PruneOlderThan { get; init; } = TimeSpan.FromDays(12);

    /// <summary>
    /// Maximum number of reminders to fetch from storage in a single batch.
    /// When more reminders are due, multiple batches will be processed in a loop.
    /// </summary>
    public int MaxBatchSize { get; init; } = 1000;

    /// <summary>
    /// Number of reminders to process per deliver/persist chunk within each fetched batch.
    /// Lower values reduce duplicate-delivery blast radius when writes fail mid-run.
    /// </summary>
    public int DeliveryCommitChunkSize { get; init; } = 100;

    /// <summary>
    /// Maximum number of delivery attempts before a reminder is marked as permanently failed.
    /// Applies to both infrastructure failures (shard region not found) and ack timeouts.
    /// </summary>
    public int MaxDeliveryAttempts { get; init; } = 10;

    /// <summary>
    /// Default delivery window applied to every scheduled occurrence when a schedule call does
    /// not pass its own <c>maxDeliveryWindow</c>. An occurrence whose due time plus this window
    /// has passed is expired rather than delivered. When null (the default), occurrences carry no
    /// deadline and are retried until <see cref="MaxDeliveryAttempts"/> is exhausted.
    /// </summary>
    public TimeSpan? DefaultMaxDeliveryWindow { get; init; }

    /// <summary>
    /// Base delay for exponential backoff when retrying failed reminders.
    /// Actual delay = min(RetryBackoffBase * (2 ^ attemptCount), MaxRetryBackoff)
    /// Applies to both infrastructure failures (shard region not found) and ack timeouts.
    /// </summary>
    public TimeSpan RetryBackoffBase { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum backoff delay between retry attempts. Prevents exponential backoff from
    /// growing to absurdly long intervals at high attempt counts.
    /// </summary>
    public TimeSpan MaxRetryBackoff { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long to wait for an acknowledgement after delivering a reminder before retrying.
    /// </summary>
    /// <summary>
    /// Default ack timeout: 10 seconds.
    /// </summary>
    public static readonly TimeSpan DefaultAckTimeout = TimeSpan.FromSeconds(10);

    public TimeSpan AckTimeout { get; init; } = DefaultAckTimeout;

    /// <summary>
    /// Maximum number of acknowledgements to flush in a single storage batch.
    /// </summary>
    public int AckFlushBatchSize { get; init; } = 256;

    /// <summary>
    /// Validates reminder settings.
    /// </summary>
    public void Validate()
    {
        if (StorageTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(StorageTimeout), "StorageTimeout must be greater than zero.");
        if (MaxBatchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxBatchSize), "MaxBatchSize must be greater than or equal to 1.");
        if (DeliveryCommitChunkSize < 1)
            throw new ArgumentOutOfRangeException(nameof(DeliveryCommitChunkSize),
                "DeliveryCommitChunkSize must be greater than or equal to 1.");
        if (AckFlushBatchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(AckFlushBatchSize),
                "AckFlushBatchSize must be greater than or equal to 1.");
        if (DefaultMaxDeliveryWindow is { } defaultWindow && defaultWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DefaultMaxDeliveryWindow),
                "DefaultMaxDeliveryWindow must be greater than zero, or null to leave reminders without a delivery deadline.");
    }
}

/// <summary>
/// INTERNAL API
///
/// Performs the scheduling functionality for reminders. Meant to be run as a singleton actor.
/// </summary>
internal sealed class ReminderScheduler : UntypedActor, IWithTimers, IWithStash
{
    public ReminderScheduler(ReminderSettings settings, IShardRegionResolver shardRegionResolver,
        IReminderStorage storage, ITimeProvider timeProvider)
    {
        settings.Validate();
        Settings = settings;
        ShardRegionResolver = shardRegionResolver;
        Storage = storage;
        TimeProvider = timeProvider;
    }

    public ReminderSettings Settings { get; }

    public IShardRegionResolver ShardRegionResolver { get; }

    public IReminderStorage Storage { get; }

    public ITimeProvider TimeProvider { get; }

    /// <summary>
    /// State of pending reminders - gets loaded upon startup and updated as reminders are scheduled.
    /// </summary>
    public ReminderOverview PendingReminders { get; set; } = ReminderOverview.Empty;

    /// <summary>
    /// The clock reading <see cref="PendingReminders"/> was computed against. Its
    /// <see cref="ReminderOverview.TimeUntilNext"/> counts from here, not from when a timer is armed.
    /// </summary>
    private DateTimeOffset _overviewAsOf;

    /// <summary>
    /// Write circuit breaker. When database writes fail (mark-complete, schedule), this flag
    /// is set to prevent fetching and delivering full batches against a database that can't
    /// persist completions. While open, ProcessReminders probes with a single reminder to
    /// detect write recovery before resuming full-batch processing.
    /// </summary>
    private bool _writeCircuitOpen;

    /// <summary>
    /// Debounce flag for ack flush scheduling. Multiple ReminderAck messages can arrive
    /// while the actor is processing other work; this flag ensures only one
    /// FlushBufferedAcks self-message is queued at a time.
    /// </summary>
    private bool _ackFlushScheduled;

    /// <summary>
    /// Tracks the currently scheduled ack-timeout deadline so we can avoid rescheduling
    /// the timer when a later deadline arrives. Only replaced when an earlier deadline
    /// is found.
    /// </summary>
    private DateTimeOffset? _nextAckTimeoutAt;

    // Storage uncertainty needs a fetch even when the cached overview is empty. Keep this
    // separate from ack deadlines: acknowledging the last delivered row cannot cancel recovery.
    private DateTimeOffset? _storageRecoveryAt;
    private DateTimeOffset? _fetchRetryNotBefore;

    /// <summary>
    /// In-memory buffer for incoming acks. Acks are NOT written to storage immediately —
    /// they accumulate here and are flushed in batches via FlushBufferedAcksAsync.
    /// Keyed by occurrence identity so duplicate acks for the same occurrence are
    /// coalesced (additional senders are appended to the existing entry's ReplyTo list).
    /// </summary>
    private readonly Dictionary<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc), BufferedAckWrite>
        _bufferedAcknowledgements = new();

    private readonly ILoggingAdapter _log = Context.GetLogger();

    public IStash Stash { get; set; } = null!;

    private sealed class RestartBackoffTimer : INoSerializationVerificationNeeded
    {
        public static readonly RestartBackoffTimer Instance = new();

        private RestartBackoffTimer()
        {
        }
    }

    /// <summary>
    /// Time to fetch reminders
    /// </summary>
    private sealed class FetchReminders : INoSerializationVerificationNeeded
    {
        public static readonly FetchReminders Instance = new();

        private FetchReminders()
        {
        }
    }

    /// <summary>
    /// Time to prune completed reminders
    /// </summary>
    private sealed class PruneCompletedReminders : INoSerializationVerificationNeeded
    {
        public static readonly PruneCompletedReminders Instance = new();

        private PruneCompletedReminders()
        {
        }
    }

    /// <summary>
    /// Periodic timer message that triggers a storage-backed scan for reminders whose ack deadline
    /// has elapsed and either retries or permanently completes them.
    /// </summary>
    private sealed class CheckAckTimeouts : INoSerializationVerificationNeeded
    {
        public static readonly CheckAckTimeouts Instance = new();

        private CheckAckTimeouts()
        {
        }
    }

    private sealed class FlushBufferedAcks : INoSerializationVerificationNeeded
    {
        public static readonly FlushBufferedAcks Instance = new();

        private FlushBufferedAcks()
        {
        }
    }

    private sealed class BufferedAckWrite
    {
        public BufferedAckWrite(ReminderAcknowledgement acknowledgement, IActorRef replyTo)
        {
            Acknowledgement = acknowledgement;
            ReplyTo = [replyTo];
        }

        public ReminderAcknowledgement Acknowledgement { get; }

        public List<IActorRef> ReplyTo { get; }

        public void AddReplyTo(IActorRef replyTo)
        {
            ReplyTo.Add(replyTo);
        }
    }

    private void TryScheduleFetchReminders()
    {
        var now = TimeProvider.Now;
        var delay = TimeSpan.MaxValue;
        if (PendingReminders.TotalPendingReminders > 0)
        {
            // Always use a timer — never Self.Tell(FetchReminders.Instance) directly.
            // Self.Tell creates a tight delivery loop when an actor reschedules the same
            // reminder key from within its delivery handler (the UPSERT resets is_completed,
            // and the immediate fetch re-delivers it ~12 times/second).
            // StartSingleTimer with the same key cancels any prior pending timer,
            // naturally debouncing rapid TryScheduleFetchReminders calls.
            // Storage calls since the overview was read took time; count the delay from now.
            // MaxValue means "no deadline"; subtracting from it would overflow if the clock stepped back.
            delay = PendingReminders.TimeUntilNext;
            if (delay != TimeSpan.MaxValue)
                delay -= now - _overviewAsOf;
        }

        if (_storageRecoveryAt is { } recoveryAt && recoveryAt - now < delay)
            delay = recoveryAt - now;
        if (delay == TimeSpan.MaxValue)
            return;
        if (_fetchRetryNotBefore is { } retryAt && retryAt - now > delay)
            delay = retryAt - now;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        Timers.StartSingleTimer(FetchReminders.Instance, FetchReminders.Instance, delay);
    }

    private void ScheduleStorageRecovery()
    {
        _storageRecoveryAt ??= TimeProvider.Now.Add(Settings.StorageTimeout * 2);
        TryScheduleFetchReminders();
    }

    private void ClearStorageRecoveryOnRead() => _storageRecoveryAt = null;

    private static (ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc) ToOccurrenceKey(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc)
        => (entity, key, dueTimeUtc);

    /// <summary>
    /// Schedules a FlushBufferedAcks message to self if one isn't already pending.
    /// Uses Self.Tell (not a timer) so the flush happens on the next mailbox dispatch —
    /// essentially "as soon as possible" without blocking the current message handler.
    /// </summary>
    private void ScheduleBufferedAckFlush()
    {
        if (_ackFlushScheduled || _bufferedAcknowledgements.Count == 0)
            return;

        _ackFlushScheduled = true;
        Self.Tell(FlushBufferedAcks.Instance);
    }

    private void CancelAckTimeoutCheck()
    {
        Timers.Cancel(CheckAckTimeouts.Instance);
        _nextAckTimeoutAt = null;
    }

    /// <summary>
    /// Schedules a CheckAckTimeouts timer at the given deadline, but only if it's
    /// earlier than any currently scheduled check. This ensures the ack-timeout checker
    /// fires at the earliest possible deadline across all AwaitingAck reminders.
    /// StartSingleTimer with the same key automatically cancels any prior timer.
    /// </summary>
    private void ScheduleAckTimeoutCheck(DateTimeOffset ackDeadlineUtc)
    {
        // Already have an earlier or equal deadline scheduled — no need to reschedule.
        if (_nextAckTimeoutAt.HasValue && _nextAckTimeoutAt.Value <= ackDeadlineUtc)
            return;

        _nextAckTimeoutAt = ackDeadlineUtc;

        var delay = ackDeadlineUtc - TimeProvider.Now;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        Timers.StartSingleTimer(CheckAckTimeouts.Instance, CheckAckTimeouts.Instance, delay);
    }

    private void TrackAckDeadlines(IEnumerable<AwaitingAckReminder> reminders)
    {
        var nextDeadline = reminders
            .Select(r => r.AckDeadline)
            .DefaultIfEmpty(DateTimeOffset.MaxValue)
            .Min();

        if (nextDeadline != DateTimeOffset.MaxValue)
            ScheduleAckTimeoutCheck(nextDeadline);
    }

    private async Task RefreshAckTimeoutScheduleFromStorageAsync()
    {
        using var cts = new CancellationTokenSource(Settings.StorageTimeout);
        var nextAckDeadline = await Storage.GetNextAwaitingAckDeadlineAsync(cts.Token);

        if (nextAckDeadline.HasValue)
            ScheduleAckTimeoutCheck(nextAckDeadline.Value);
        else
            CancelAckTimeoutCheck();
    }

    private static ReminderAckResponseCode MapAckStatus(ReminderAckStorageStatus status)
        => status switch
        {
            ReminderAckStorageStatus.Success => ReminderAckResponseCode.Success,
            ReminderAckStorageStatus.NotFound => ReminderAckResponseCode.NotFound,
            _ => ReminderAckResponseCode.Error
        };

    private async Task<ScheduledReminder?> GetAwaitingAckOccurrenceAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc)
    {
        using var cts = new CancellationTokenSource(Settings.StorageTimeout);
        return await Storage.GetAwaitingAckReminderAsync(entity, key, dueTimeUtc, cts.Token);
    }

    private async Task HandleNackAsync(ReminderProtocol.ReminderNack nack, IActorRef replyTo)
    {
        await FlushBufferedAcksIfAnyAsync();

        var reminder = await GetAwaitingAckOccurrenceAsync(nack.Entity, nack.Key, nack.DueTimeUtc);
        if (reminder is null)
        {
            replyTo.Tell(new ReminderProtocol.ReminderNackResponse(
                nack.Entity,
                nack.Key,
                nack.DueTimeUtc,
                ReminderNackResponseCode.NotFound,
                AttemptCount: 0,
                Message: "Reminder occurrence was not awaiting acknowledgement or was already stale."), ActorRefs.NoSender);
            return;
        }

        var now = TimeProvider.Now;
        var pendingUpserts = new List<ScheduledReminder>();
        var completions = new List<CompletedReminder>();
        ReminderNackResponseCode responseCode;
        DateTimeOffset? nextAttemptAtUtc = null;
        int attemptCount;

        if (TryCreateRetryReminder(reminder, now, nack.Reason, out var retryReminder, out var terminalStatus))
        {
            pendingUpserts.Add(retryReminder);
            responseCode = ReminderNackResponseCode.RetryScheduled;
            nextAttemptAtUtc = retryReminder.When;
            attemptCount = retryReminder.AttemptCount;
        }
        else
        {
            var terminalAttempt = CreateTerminalAttempt(reminder, nack.Reason);
            pendingUpserts.Add(terminalAttempt);
            completions.Add(new CompletedReminder(
                reminder.Entity,
                reminder.Key,
                reminder.DueTimeUtc,
                now,
                terminalStatus));
            responseCode = terminalStatus == ReminderCompletionStatus.Expired
                ? ReminderNackResponseCode.Expired
                : ReminderNackResponseCode.Failed;
            attemptCount = terminalAttempt.AttemptCount;
        }

        bool committed;
        try
        {
            using var mutationCts = new CancellationTokenSource(Settings.StorageTimeout);
            committed = await Storage.CommitReminderMutationsAsync(
                new ReminderMutationBatch(pendingUpserts, completions, []),
                mutationCts.Token);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to commit negative acknowledgement for reminder occurrence [{0}] / [{1}]", nack.Entity, nack.Key);
            committed = false;
        }

        if (!committed)
        {
            // The commit may have landed even though it reported failure: pick up a retry left Pending.
            await ReloadPendingOverviewAndScheduleFetchAsync();
            replyTo.Tell(new ReminderProtocol.ReminderNackResponse(
                nack.Entity,
                nack.Key,
                nack.DueTimeUtc,
                ReminderNackResponseCode.Error,
                reminder.AttemptCount,
                Message: "Storage rejected the negative acknowledgement mutation."), ActorRefs.NoSender);
            return;
        }

        await ReloadPendingOverviewAndScheduleFetchAsync();
        await RefreshAckTimeoutScheduleFromStorageAsync();

        replyTo.Tell(new ReminderProtocol.ReminderNackResponse(
            nack.Entity,
            nack.Key,
            nack.DueTimeUtc,
            responseCode,
            attemptCount,
            nextAttemptAtUtc,
            nack.Reason), ActorRefs.NoSender);
    }

    /// <summary>
    /// Drains the ack buffer in batches, writing each batch to storage via
    /// AcknowledgeRemindersAsync. On success, marks the occurrence as Delivered
    /// and refreshes the ack-timeout schedule. On failure, replies Error to all
    /// buffered senders — the occurrence stays AwaitingAck and will be retried
    /// after the ack timeout fires.
    /// </summary>
    private async Task FlushBufferedAcksAsync()
    {
        var shouldRefreshAckTimeoutSchedule = false;

        while (_bufferedAcknowledgements.Count > 0)
        {
            var batch = _bufferedAcknowledgements
                .Take(Settings.AckFlushBatchSize)
                .ToList();

            // Remove from the buffer before writing — if the write fails, the entries
            // are gone. This is safe because the occurrence stays AwaitingAck in storage
            // and the ack-timeout checker will handle it on the next scan.
            foreach (var entry in batch)
                _bufferedAcknowledgements.Remove(entry.Key);

            IReadOnlyList<AckResult> results;
            try
            {
                using var cts = new CancellationTokenSource(Settings.StorageTimeout);
                results = await Storage.AcknowledgeRemindersAsync(
                    batch.Select(b => b.Value.Acknowledgement),
                    cts.Token);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to flush [{0}] buffered reminder acknowledgements", batch.Count);
                results = batch
                    .Select(b => new AckResult(
                        b.Value.Acknowledgement.Entity,
                        b.Value.Acknowledgement.Key,
                        b.Value.Acknowledgement.DueTimeUtc,
                        ReminderAckStorageStatus.Error,
                        ex.Message))
                    .ToList();
            }

            for (var i = 0; i < batch.Count; i++)
            {
                var buffered = batch[i].Value;
                var result = i < results.Count
                    ? results[i]
                    : new AckResult(
                        buffered.Acknowledgement.Entity,
                        buffered.Acknowledgement.Key,
                        buffered.Acknowledgement.DueTimeUtc,
                        ReminderAckStorageStatus.Error,
                        "Storage returned an incomplete acknowledgement batch result.");

                var response = new ReminderProtocol.ReminderAckResponse(
                    result.Entity,
                    result.Key,
                    result.DueTimeUtc,
                    MapAckStatus(result.Status),
                    result.ErrorMessage);

                foreach (var replyTo in buffered.ReplyTo)
                    replyTo.Tell(response, ActorRefs.NoSender);

                if (result.Success)
                    shouldRefreshAckTimeoutSchedule = true;
            }
        }

        if (shouldRefreshAckTimeoutSchedule)
            await RefreshAckTimeoutScheduleFromStorageAsync();
    }

    private async Task FlushBufferedAcksIfAnyAsync()
    {
        if (_bufferedAcknowledgements.Count == 0)
            return;

        _ackFlushScheduled = false;
        await FlushBufferedAcksAsync();
    }

    // Initial behavior: recover our schedule
    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case InitResult init:
                _log.Info("Loaded reminder overview from storage: {0}", init.Overview);
                PendingReminders = init.Overview;
                _overviewAsOf = init.AsOf;

                // Schedule ack timeout check BEFORE unstashing so the mailbox is
                // fully ready when client messages are replayed — no RunTask gap.
                if (init.NextAckDeadline.HasValue)
                    ScheduleAckTimeoutCheck(init.NextAckDeadline.Value);

                Become(Scheduling);
                Stash.UnstashAll();
                TryScheduleFetchReminders();
                Timers.Cancel(RestartBackoffTimer.Instance);
                break;
            case Status.Failure failure:
                _log.Error(failure.Cause, "Failed to load reminder overview from storage - restarting...");
                Timers.StartSingleTimer(RestartBackoffTimer.Instance, RestartBackoffTimer.Instance,
                    Settings.StorageTimeout * 2);
                break;
            case RestartBackoffTimer:
                // TODO: fail after we've tried to load the reminder overview too many times
                _log.Info("Retrying storage initialization...");
                _ = LoadReminderOverview();
                break;
            default:
                // Stash messages that arrive during initialization so they can be processed once ready
                Stash.Stash();
                break;
        }
    }

    /// <summary>
    /// Main behavior after initialization. Handles scheduling commands, delivery ticks,
    /// ack processing, and ack-timeout scanning. All I/O-bound work uses RunTask to
    /// avoid blocking the dispatcher thread — the mailbox is suspended until each
    /// RunTask completes.
    /// </summary>
    private void Scheduling(object message)
    {
        switch (message)
        {
            case FetchReminders:
            {
                // RunTask suspends the mailbox until the task ends, so two fetches cannot overlap.
                // Never drop a tick here: the tick armed at the end of a fetch can reach the mailbox
                // before that fetch's task is done, and a dropped tick is never armed again.
                // Each tick: flush any pending acks first (so the fetch sees up-to-date
                // storage state), then expire stale reminders, then process due reminders.
                // MaxSlippage causes the scheduler to fetch slightly ahead of the current
                // time, avoiding a re-schedule for reminders about to become due.
                RunTask(async () =>
                {
                    await FlushBufferedAcksIfAnyAsync();
                    await ExpireRemindersAsync(TimeProvider.Now);
                    await ProcessReminders(TimeProvider.Now + Settings.MaxSlippage);
                });
                break;
            }
            case ReminderProtocol.ScheduleReminder scheduleSingle:
            {
                _log.Debug("Scheduling reminder {0}", scheduleSingle);
                var replyTo = Sender;
                RunTask(async () =>
                {
                    using var cts = new CancellationTokenSource(Settings.StorageTimeout);
                    var reminder = CreateScheduledReminder(scheduleSingle);
                    if (reminder.RepeatInterval.HasValue && Storage is not IConditionalReminderMutationStorage)
                    {
                        replyTo.Tell(new ReminderProtocol.ReminderScheduled(scheduleSingle,
                            ReminderScheduleResponseCode.Error,
                            "Recurring reminders require storage implementing IConditionalReminderMutationStorage."), ActorRefs.NoSender);
                        return;
                    }
                    try
                    {
                        // validate that the ShardRegion exists
                        var shardRegion = ShardRegionResolver.TryResolve(reminder.Entity);
                        if (shardRegion is null)
                        {
                            replyTo.Tell(new ReminderProtocol.ReminderScheduled(scheduleSingle,
                                ReminderScheduleResponseCode.ShardRegionNotFound,
                                $"ShardRegion [{reminder.Entity.ShardRegionName}] not found"), ActorRefs.NoSender);
                            return;
                        }

                        // fail now, not at delivery time (possibly after a restart), when the
                        // envelope for this message type cannot be built (Native AOT, unregistered type)
                        var messageType = reminder.Message.GetType();
                        if (!ReminderEnvelopeFactory.CanCreate(messageType))
                        {
                            var error = ReminderEnvelopeFactory.NotRegisteredMessage(messageType);
                            _log.Error("Rejected reminder {0}: {1}", scheduleSingle, error);
                            replyTo.Tell(new ReminderProtocol.ReminderScheduled(scheduleSingle,
                                ReminderScheduleResponseCode.Error, error), ActorRefs.NoSender);
                            return;
                        }

                        // persist the reminder
                        var r = await Storage.ScheduleReminderAsync(reminder, cts.Token);

                        // bail out early if we couldn't schedule or if it was a no-op
                        if (r.ResponseCode != ReminderScheduleResponseCode.Success)
                        {
                            if (r.ResponseCode == ReminderScheduleResponseCode.Error)
                                ScheduleStorageRecovery();
                            replyTo.Tell(r, ActorRefs.NoSender);
                            return;
                        }

                        try
                        {
                            await ReloadPendingOverviewAsync();
                        }
                        catch (Exception ex)
                        {
                            // The reminder is stored. Its due time is known, so the fetch timer can be
                            // armed from it alone; the next fetch brings a fresh overview.
                            _log.Warning(ex, "Failed to reload the overview after storing {0}; arming the fetch timer from its due time", scheduleSingle);
                            // TimeUntilNext counts from _overviewAsOf, not from now.
                            PendingReminders = PendingReminders.Apply(reminder, _overviewAsOf).newOverview;
                        }

                        TryScheduleFetchReminders();

                        // Reply AFTER the fetch timer is scheduled so the caller
                        // knows the scheduler is ready to process this reminder.
                        replyTo.Tell(r, ActorRefs.NoSender);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Failed to schedule reminder {0}", scheduleSingle);
                        ScheduleStorageRecovery();
                        replyTo.Tell(new ReminderProtocol.ReminderScheduled(scheduleSingle,
                            ReminderScheduleResponseCode.Error, ex.Message), ActorRefs.NoSender);
                    }
                });
                break;
            }
            case ReminderProtocol.CancelReminder cancel:
            {
                _log.Debug("Cancelling reminder {0}", cancel);
                var replyTo = Sender;
                RunTask(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(Settings.StorageTimeout);
                        var cancellationResult =
                            await Storage.CancelReminderAsync(cancel.Entity, cancel.Key, cts.Token);

                        await ReloadPendingOverviewAsync();
                        TryScheduleFetchReminders();
                        replyTo.Tell(cancellationResult, ActorRefs.NoSender);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Failed to cancel reminder {0}", cancel);
                        replyTo.Tell(new ReminderProtocol.RemindersCancelled(cancel.Entity,
                            ReminderCancelResponseCode.Error,
                            [cancel.Key], ex.Message), ActorRefs.NoSender);
                    }
                });
                break;
            }
            case ReminderProtocol.CancelAllReminders cancelAll:
            {
                _log.Debug("Cancelling all reminders for {0}", cancelAll.Entity);
                var replyTo = Sender;
                RunTask(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(Settings.StorageTimeout);
                        var cancellationResult =
                            await Storage.CancelAllRemindersForEntityAsync(cancelAll.Entity, cts.Token);

                        await ReloadPendingOverviewAsync();
                        TryScheduleFetchReminders();
                        replyTo.Tell(cancellationResult, ActorRefs.NoSender);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Failed to cancel all reminders for {0}", cancelAll);
                        replyTo.Tell(new ReminderProtocol.RemindersCancelled(cancelAll.Entity,
                            ReminderCancelResponseCode.Error,
                            [], ex.Message), ActorRefs.NoSender);
                    }
                });
                break;
            }
            case ReminderProtocol.GetReminders getReminders:
            {
                var replyTo = Sender;
                RunTask(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(Settings.StorageTimeout);
                        // TODO: add skip / take support
                        var queryResult = Storage.GetRemindersForEntityAsync(getReminders.Entity, ct:cts.Token);
                        var reminders = await queryResult;
                        replyTo.Tell(new ReminderProtocol.RemindersForEntity(
                            getReminders.Entity,
                            FetchRemindersResponseCode.Success,
                            reminders), ActorRefs.NoSender);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Failed to get reminders for {0}", getReminders);
                        replyTo.Tell(new ReminderProtocol.RemindersForEntity(getReminders.Entity,
                            FetchRemindersResponseCode.Error,
                            [], ex.Message), ActorRefs.NoSender);
                    }
                });
                break;
            }
            case ReminderProtocol.GetReminderOccurrenceStatus query:
            {
                var replyTo = Sender;
                RunTask(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(Settings.StorageTimeout);
                        var status = await Storage.GetReminderOccurrenceStatusAsync(
                            query.Entity,
                            query.Key,
                            query.DueTimeUtc,
                            cts.Token);
                        replyTo.Tell(new ReminderProtocol.ReminderOccurrenceStatusResponse(
                            query.Entity,
                            query.Key,
                            query.DueTimeUtc,
                            status is null
                                ? ReminderOccurrenceStatusResponseCode.NotFound
                                : ReminderOccurrenceStatusResponseCode.Success,
                            status), ActorRefs.NoSender);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Failed to get reminder occurrence status for [{0}] / [{1}]", query.Entity, query.Key);
                        replyTo.Tell(new ReminderProtocol.ReminderOccurrenceStatusResponse(
                            query.Entity,
                            query.Key,
                            query.DueTimeUtc,
                            ReminderOccurrenceStatusResponseCode.Error,
                            Message: ex.Message), ActorRefs.NoSender);
                    }
                });
                break;
            }
            case PruneCompletedReminders:
            {
                _log.Debug("Pruning completed reminders older than {0}", Settings.PruneOlderThan);
                RunTask(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(Settings.StorageTimeout);
                        var cutoffDate = TimeProvider.Now.Subtract(Settings.PruneOlderThan);
                        var result = await Storage.CleanUpCompletedRemindersAsync(cutoffDate, cts.Token);
                        if (result)
                        {
                            _log.Info("Successfully pruned completed reminders older than {0}", cutoffDate);
                        }
                        else
                        {
                            _log.Warning("Failed to prune completed reminders");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error pruning completed reminders");
                    }
                });
                break;
            }
            // Acks are buffered in memory and flushed in batches, not written per-ack.
            // If multiple acks arrive for the same occurrence (duplicate delivery),
            // they're coalesced — all senders get the same response when the batch flushes.
            case ReminderProtocol.ReminderAck ack:
            {
                _log.Debug("Received ReminderAck for [{0}] / [{1}] due at [{2}]", ack.Entity, ack.Key, ack.DueTimeUtc);
                var ackSender = Sender;
                var occurrenceKey = ToOccurrenceKey(ack.Entity, ack.Key, ack.DueTimeUtc);
                if (_bufferedAcknowledgements.TryGetValue(occurrenceKey, out var bufferedAck))
                {
                    // Duplicate ack for an occurrence already in the buffer — just
                    // append the sender so they get the response when it flushes.
                    bufferedAck.AddReplyTo(ackSender);
                }
                else
                {
                    _bufferedAcknowledgements[occurrenceKey] = new BufferedAckWrite(
                        new ReminderAcknowledgement(ack.Entity, ack.Key, ack.DueTimeUtc, TimeProvider.Now),
                        ackSender);
                }

                ScheduleBufferedAckFlush();
                break;
            }
            case ReminderProtocol.ReminderNack nack:
            {
                var replyTo = Sender;
                RunTask(async () =>
                {
                    try
                    {
                        await HandleNackAsync(nack, replyTo);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Failed to reject reminder occurrence [{0}] / [{1}]", nack.Entity, nack.Key);
                        replyTo.Tell(new ReminderProtocol.ReminderNackResponse(
                            nack.Entity,
                            nack.Key,
                            nack.DueTimeUtc,
                            ReminderNackResponseCode.Error,
                            AttemptCount: 0,
                            Message: ex.Message), ActorRefs.NoSender);
                    }
                });
                break;
            }
            case FlushBufferedAcks:
            {
                _ackFlushScheduled = false;
                if (_bufferedAcknowledgements.Count == 0)
                    break;

                RunTask(FlushBufferedAcksAsync);
                break;
            }
            // Fired by a deadline-driven one-shot timer (not periodic). Scans storage
            // for AwaitingAck rows whose ack deadline has elapsed, and either retries
            // them (back to Pending with backoff) or marks them terminal (Failed/Expired).
            // Flushes buffered acks first so we don't retry an occurrence that was
            // actually acked but not yet flushed.
            case CheckAckTimeouts:
            {
                // As with FetchReminders: the mailbox is suspended while the task runs, and a
                // dropped tick would leave _nextAckTimeoutAt set with no timer behind it.
                _nextAckTimeoutAt = null;

                RunTask(async () =>
                {
                    await FlushBufferedAcksIfAnyAsync();
                    await ExpireRemindersAsync(TimeProvider.Now);
                    await ProcessAckTimeouts();
                });
                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }

    protected override void PreStart()
    {
        _log.Info("Loading reminder overview from storage...");
        _ = LoadReminderOverview();

        // Schedule periodic pruning of completed reminders
        Timers.StartPeriodicTimer(
            PruneCompletedReminders.Instance,
            PruneCompletedReminders.Instance,
            Settings.PruneInterval,
            Settings.PruneInterval);
        _log.Info("Scheduled periodic pruning every {0}", Settings.PruneInterval);
    }

    protected override void PostStop()
    {
        // IWithTimers cancels all timers automatically on stop.
        base.PostStop();
    }

    private async Task ReloadPendingOverviewAsync()
    {
        using var cts = new CancellationTokenSource(Settings.StorageTimeout);
        var now = TimeProvider.Now;
        PendingReminders = await Storage.GetRemindersOverviewAsync(now, cts.Token);
        _overviewAsOf = now;
        ClearStorageRecoveryOnRead();
    }

    /// <summary>
    /// Reconcile Pending work after a mutation, including an indeterminate commit. A failed read
    /// retains a fetch recovery deadline independently of the remaining AwaitingAck rows.
    /// </summary>
    private async Task ReloadPendingOverviewAndScheduleFetchAsync()
    {
        try
        {
            await ReloadPendingOverviewAsync();
            TryScheduleFetchReminders();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to reload reminder overview after a mutation; retrying at the next recovery fetch");
            ScheduleStorageRecovery();
        }
    }

    private async Task ExpireRemindersAsync(DateTimeOffset now)
    {
        try
        {
            using var cts = new CancellationTokenSource(Settings.StorageTimeout);
            var expired = await Storage.ExpireRemindersAsync(now, cts.Token);
            if (expired > 0)
            {
                _log.Info("Marked [{0}] expired reminder occurrence(s) as complete", expired);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to expire stale reminder occurrences; continuing with deadline-filtered reads");
        }
    }

    /// <summary>
    /// Carries both the reminder overview and the next ack-timeout deadline so that
    /// initialization completes in a single PipeTo — no stashed RunTask to block
    /// the mailbox after UnstashAll.
    /// </summary>
    private sealed record InitResult(ReminderOverview Overview, DateTimeOffset AsOf, DateTimeOffset? NextAckDeadline) : Akka.Actor.INoSerializationVerificationNeeded;

    private Task LoadReminderOverview()
    {
        async Task<InitResult> LoadAsync()
        {
            await ExpireRemindersAsync(TimeProvider.Now);
            using var cts = new CancellationTokenSource(Settings.StorageTimeout);
            var now = TimeProvider.Now;
            var overview = await Storage.GetRemindersOverviewAsync(now, cts.Token);
            var nextAckDeadline = await Storage.GetNextAwaitingAckDeadlineAsync(cts.Token);
            return new InitResult(overview, now, nextAckDeadline);
        }

        var init = LoadAsync();
        return init.PipeTo(Self, success: r => r, failure: ex => new Status.Failure(ex));
    }

    /// <summary>
    /// Computes the absolute UTC deadline for a reminder occurrence.
    ///
    /// Rules:
    /// - No window, no repeat: null (infinite — retries until MaxDeliveryAttempts).
    /// - Window set: due + window.
    /// - Recurring: clamped to the next occurrence's due time (latest-only semantics).
    /// - Both: min(due + window, next due).
    /// </summary>
    private static DateTimeOffset? ComputeDeliveryDeadlineUtc(
        DateTimeOffset dueTimeUtc,
        TimeSpan? repeatInterval,
        TimeSpan? maxDeliveryWindow)
    {
        DateTimeOffset? deadline = null;
        if (maxDeliveryWindow.HasValue)
            deadline = dueTimeUtc.Add(maxDeliveryWindow.Value);

        if (repeatInterval.HasValue)
        {
            var nextDue = dueTimeUtc.Add(repeatInterval.Value);
            deadline = deadline.HasValue && deadline.Value < nextDue ? deadline.Value : nextDue;
        }

        return deadline;
    }

    /// <summary>
    /// Computes the <see cref="ReminderDeadline"/> to attach to the delivered
    /// <see cref="ReminderEnvelope"/>. This tells the recipient how long the
    /// current delivery attempt is relevant:
    ///
    /// - If another retry is possible (attempts remain and backoff fits within
    ///   the occurrence deadline), the deadline is the ack timeout for this attempt
    ///   (<paramref name="ackDeadline"/>), because a new delivery will replace this one.
    /// - If this is the final attempt (max attempts reached, or the next backoff
    ///   would exceed the occurrence deadline), the deadline is the occurrence-level
    ///   <see cref="ScheduledReminder.DeliveryDeadlineUtc"/>, or
    ///   <see cref="ReminderDeadline.Infinite"/> if none exists.
    /// </summary>
    private ReminderDeadline ComputeEnvelopeDeadline(
        ScheduledReminder reminder,
        DateTimeOffset ackDeadline)
    {
        // Will there be another attempt after this one?
        var hasMoreAttempts = reminder.AttemptCount + 1 < Settings.MaxDeliveryAttempts;

        if (hasMoreAttempts)
        {
            // Check whether the next backoff would land before the occurrence deadline.
            // If the occurrence has no deadline, there's always room for another attempt.
            if (reminder.DeliveryDeadlineUtc.HasValue)
            {
                var nextBackoff = TimeSpan.FromSeconds(
                    Math.Min(
                        Settings.RetryBackoffBase.TotalSeconds * Math.Pow(2, reminder.AttemptCount),
                        Settings.MaxRetryBackoff.TotalSeconds));
                var nextRetryAt = ackDeadline.Add(nextBackoff);

                if (nextRetryAt >= reminder.DeliveryDeadlineUtc.Value)
                {
                    // Next retry would miss the occurrence deadline — this is effectively
                    // the last attempt. Use the occurrence deadline.
                    return new ReminderDeadline(reminder.DeliveryDeadlineUtc.Value);
                }
            }

            // Another delivery is coming — this attempt expires at the ack deadline.
            return new ReminderDeadline(ackDeadline);
        }

        // Final attempt — use the occurrence deadline or infinite.
        return reminder.Deadline;
    }

    private ScheduledReminder CreateScheduledReminder(ReminderProtocol.ScheduleReminder scheduleReminder)
    {
        var dueTimeUtc = scheduleReminder.When.ToUniversalTime();
        var maxDeliveryWindow = scheduleReminder.MaxDeliveryWindow ?? Settings.DefaultMaxDeliveryWindow;
        return new ScheduledReminder(
            scheduleReminder.Entity,
            scheduleReminder.Key,
            dueTimeUtc,
            scheduleReminder.Message,
            scheduleReminder.RepeatInterval,
            MaxDeliveryWindow: maxDeliveryWindow,
            DeliveryDeadlineUtc: ComputeDeliveryDeadlineUtc(
                dueTimeUtc,
                scheduleReminder.RepeatInterval,
                maxDeliveryWindow),
            OccurrenceDueTimeUtc: dueTimeUtc);
    }

    /// <summary>
    /// Creates the next occurrence for a recurring reminder: a NEW row with a new occurrence
    /// identity and a fresh retry budget. It is the earliest slot after the current one whose
    /// deadline is still after <paramref name="now"/>, so slots missed while the scheduler lagged
    /// are skipped. Returns null when there is no valid next slot (interval of zero or less, or
    /// out of range), so a bad row can never throw inside the processing loop.
    /// </summary>
    internal static ScheduledReminder? CreateNextRecurringOccurrence(ScheduledReminder reminder, DateTimeOffset now)
    {
        if (reminder.RepeatInterval is not { } interval || interval <= TimeSpan.Zero)
            return null;

        try
        {
            // Slot k is due at due + k * I; its deadline is that plus offset = min(window ?? I, I).
            var offset = Math.Min(reminder.MaxDeliveryWindow?.Ticks ?? interval.Ticks, interval.Ticks);
            var lag = checked(now.UtcTicks - offset - reminder.DueTimeUtc.UtcTicks);
            var k = lag < 0 ? 1 : lag / interval.Ticks + 1;
            var nextDue = reminder.DueTimeUtc.AddTicks(checked(k * interval.Ticks));
            return reminder with
            {
                When = nextDue,
                AttemptCount = 0,
                LastFailureReason = null,
                DeliveryDeadlineUtc = ComputeDeliveryDeadlineUtc(nextDue, interval, reminder.MaxDeliveryWindow),
                OccurrenceDueTimeUtc = nextDue
            };
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// The first attempt writes its successor normally, preserving explicit registration replacement.
    /// A retry only inserts a missing successor, leaving existing occurrence state and retry budgets intact.
    /// </summary>
    private void AddNextOccurrence(ScheduledReminder reminder, DateTimeOffset now,
        List<ScheduledReminder> occurrencesToUpsert, List<ScheduledReminder> pendingInserts)
    {
        var next = CreateNextRecurringOccurrence(reminder, now);
        if (next is null)
        {
            _log.Error("Recurring reminder {0} has no valid next occurrence; the series ends here.", reminder);
            return;
        }

        if (occurrencesToUpsert.Exists(r => r.Entity == next.Entity && r.Key == next.Key && r.DueTimeUtc == next.DueTimeUtc))
            return;
        var successors = reminder.AttemptCount == 0 ? occurrencesToUpsert : pendingInserts;
        if (!successors.Exists(r => r.Entity == next.Entity && r.Key == next.Key && r.DueTimeUtc == next.DueTimeUtc))
            successors.Add(next);
    }

    /// <summary>
    /// Adds a retry or terminal row to a commit, replacing any earlier row for the same occurrence.
    /// One chunk can produce two rows for one slot, e.g. a stale occurrence rolling forward onto a
    /// successor that is retried in the same chunk, and PostgreSQL and SQL Server reject an upsert
    /// that names the same key twice.
    /// </summary>
    private static void AddUpsert(List<ScheduledReminder> occurrencesToUpsert, ScheduledReminder row)
    {
        occurrencesToUpsert.RemoveAll(r => r.Entity == row.Entity && r.Key == row.Key && r.DueTimeUtc == row.DueTimeUtc);
        occurrencesToUpsert.Add(row);
    }

    /// <summary>
    /// Attempts to create a retry for a failed reminder delivery. Returns false when
    /// the reminder has exhausted its retry budget (MaxDeliveryAttempts) or its
    /// delivery deadline has passed — in those cases, the caller should mark it terminal.
    ///
    /// The retry uses exponential backoff: when_utc is pushed forward by
    /// RetryBackoffBase * 2^AttemptCount (capped by MaxRetryBackoff). The DueTimeUtc
    /// (occurrence identity) stays fixed so the ack still matches the original occurrence.
    /// </summary>
    private bool TryCreateRetryReminder(
        ScheduledReminder reminder,
        DateTimeOffset now,
        string failureReason,
        out ScheduledReminder retryReminder,
        out ReminderCompletionStatus terminalStatus)
    {
        retryReminder = reminder;

        // Deadline passed — no point retrying.
        if (reminder.DeliveryDeadlineUtc.HasValue && reminder.DeliveryDeadlineUtc.Value <= now)
        {
            terminalStatus = ReminderCompletionStatus.Expired;
            return false;
        }

        // Used all attempts.
        if (reminder.AttemptCount + 1 >= Settings.MaxDeliveryAttempts)
        {
            terminalStatus = ReminderCompletionStatus.Failed;
            return false;
        }

        var backoff = TimeSpan.FromSeconds(
            Math.Min(
                Settings.RetryBackoffBase.TotalSeconds * Math.Pow(2, reminder.AttemptCount),
                Settings.MaxRetryBackoff.TotalSeconds));
        var retryAt = now.Add(backoff);

        // Backoff would land after the deadline — expire instead of scheduling a doomed retry.
        if (reminder.DeliveryDeadlineUtc.HasValue && retryAt >= reminder.DeliveryDeadlineUtc.Value)
        {
            terminalStatus = ReminderCompletionStatus.Expired;
            return false;
        }

        // Push when_utc forward (changes the fetch time) but keep DueTimeUtc fixed
        // (preserves the occurrence identity for ack matching).
        retryReminder = reminder with
        {
            When = retryAt,
            AttemptCount = reminder.AttemptCount + 1,
            LastFailureReason = failureReason,
            OccurrenceDueTimeUtc = reminder.DueTimeUtc
        };
        terminalStatus = ReminderCompletionStatus.Pending;
        return true;
    }

    private static ScheduledReminder CreateTerminalAttempt(ScheduledReminder reminder, string failureReason)
        => reminder with
        {
            AttemptCount = reminder.AttemptCount + 1,
            LastFailureReason = failureReason
        };

    private async Task ProcessAckTimeouts()
    {
        var totalRetried = 0;
        var totalFailed = 0;
        var totalExpired = 0;
        var processingFailed = false;

        while (true)
        {
            IReadOnlyList<ScheduledReminder> timedOut;
            try
            {
                using var readCts = new CancellationTokenSource(Settings.StorageTimeout);
                timedOut = await Storage.GetTimedOutAckRemindersAsync(
                    TimeProvider.Now,
                    new ReminderBatchSize(Settings.MaxBatchSize),
                    readCts.Token);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to read timed-out awaiting-ack reminders from storage");
                processingFailed = true;
                break;
            }

            if (timedOut.Count == 0)
                break;

            var occurrencesToUpsert = new List<ScheduledReminder>();
            var terminalReminders = new List<CompletedReminder>();
            var now = TimeProvider.Now;
            var batchRetried = 0;
            var batchFailed = 0;
            var batchExpired = 0;

            foreach (var reminder in timedOut)
            {
                const string failureReason = "Ack timeout";
                if (TryCreateRetryReminder(reminder, now, failureReason, out var retryReminder, out var terminalStatus))
                {
                    AddUpsert(occurrencesToUpsert, retryReminder);
                    batchRetried += 1;
                }
                else
                {
                    AddUpsert(occurrencesToUpsert, CreateTerminalAttempt(reminder, failureReason));
                    terminalReminders.Add(new CompletedReminder(
                        reminder.Entity,
                        reminder.Key,
                        reminder.DueTimeUtc,
                        now,
                        terminalStatus));

                    if (terminalStatus == ReminderCompletionStatus.Expired)
                        batchExpired += 1;
                    else
                        batchFailed += 1;
                }
            }

            var writeFailed = false;

            var mutationBatch = new ReminderMutationBatch(
                occurrencesToUpsert,
                terminalReminders,
                []);

            if (!mutationBatch.IsEmpty)
            {
                try
                {
                    using var mutationCts = new CancellationTokenSource(Settings.StorageTimeout);
                    var mutationResult = await Storage.CommitReminderMutationsAsync(mutationBatch, mutationCts.Token);
                    if (!mutationResult)
                        writeFailed = true;
                }
                catch (Exception ex)
                {
                    _log.Error(ex,
                        "Failed to commit [{0}] ack-timeout mutation(s) with [{1}] retry upsert(s) and [{2}] terminal completion(s)",
                        occurrencesToUpsert.Count + terminalReminders.Count,
                        occurrencesToUpsert.Count,
                        terminalReminders.Count);
                    writeFailed = true;
                }
            }

            if (writeFailed)
            {
                _writeCircuitOpen = true;
                processingFailed = true;
                _log.Warning("Write circuit OPEN — ack-timeout processing encountered storage write failures.");
                break;
            }

            totalRetried += batchRetried;
            totalFailed += batchFailed;
            totalExpired += batchExpired;

            if (timedOut.Count < Settings.MaxBatchSize)
                break;
        }

        if (processingFailed)
            ScheduleAckTimeoutCheck(TimeProvider.Now.Add(Settings.StorageTimeout * 2));
        else
            await RefreshAckTimeoutScheduleFromStorageAsync();

        // Mutations may have created Pending retries even when the commit reported failure.
        // An empty timeout pass needs no pending aggregate read.
        if (processingFailed || totalRetried > 0 || totalFailed > 0 || totalExpired > 0)
            await ReloadPendingOverviewAndScheduleFetchAsync();

        if (totalRetried > 0 || totalFailed > 0 || totalExpired > 0)
        {
            _log.Info(
                "Ack-timeout processing retried [{0}] reminder occurrence(s), failed [{1}], expired [{2}]",
                totalRetried,
                totalFailed,
                totalExpired);
        }
    }

    private async Task ProcessReminders(DateTimeOffset untilDeadline)
    {
        var totalDelivered = 0;
        var totalRetried = 0;
        var totalFailed = 0;
        var totalExpired = 0;
        var latestOverview = PendingReminders;
        var fetchedAt = _overviewAsOf;
        var needsOverviewReload = false;
        var fetchFailed = false;

        // When the write circuit is open, probe with a single reminder to test
        // write availability before resuming full-batch processing. This limits
        // the blast radius to at most 1 duplicate delivery during the probe.
        var effectiveBatchSize = _writeCircuitOpen ? 1 : Settings.MaxBatchSize;

        if (_writeCircuitOpen)
        {
            _log.Warning("Write circuit is open — probing with a single reminder before resuming");
        }

        // Process batches until we've handled all due reminders
        while (true)
        {
            // Phase 1: Fetch a batch of due reminders
            PendingRemindersWithSummary batch;
            try
            {
                using var fetchCts = new CancellationTokenSource(Settings.StorageTimeout);
                fetchedAt = TimeProvider.Now;
                batch = await Storage.GetNextRemindersAsync(untilDeadline, fetchedAt,
                    new ReminderBatchSize(effectiveBatchSize), fetchCts.Token);
                _fetchRetryNotBefore = null;
                ClearStorageRecoveryOnRead();
                _log.Info("Fetched {0} due reminders (batch)", batch.Reminders.Count);
                latestOverview = batch.NextOverview;
            }
            catch (Exception ex)
            {
                // If fetch fails, no reminders were delivered and no write operations were attempted,
                // so the write circuit remains unchanged.
                _log.Error(ex, "Failed to fetch due reminders from storage");
                needsOverviewReload = true;
                fetchFailed = true;
                break;
            }

            if (batch.Reminders.Count == 0)
                break;

            var stopProcessing = false;
            var recoveredFromProbe = false;
            var hasConditionalInserts = false;
            var batchOverview = batch.NextOverview;
            var expiredAsPrevious = new HashSet<(ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc)>();

            // Process the fetched batch in smaller chunks to cap duplicate blast radius
            // if writes fail after delivery.
            for (var offset = 0; offset < batch.Reminders.Count; offset += Settings.DeliveryCommitChunkSize)
            {
                var chunk = batch.Reminders
                    .Skip(offset)
                    .Take(Settings.DeliveryCommitChunkSize)
                    .ToList();

                // One clock reading per chunk, used for the expiry check, the next-slot math, the
                // retry/terminal decision and the completion timestamp, so they cannot disagree.
                // It is read per chunk because earlier commits take time: a later chunk must not be
                // checked against a stale clock and delivered past its deadline.
                var completedAt = TimeProvider.Now;

                var occurrencesToUpsert = new List<ScheduledReminder>();
                var pendingInserts = new List<ScheduledReminder>();
                var activeCompletions = new List<CompletedReminder>();
                // Rows upserted only to record their last attempt; this commit also ends them.
                var finalAttempts = new HashSet<ScheduledReminder>(ReferenceEqualityComparer.Instance);
                var terminalReminders = new List<CompletedReminder>();
                var remindersToAwaitAck = new List<AwaitingAckReminder>();
                var deliveries = new List<(IActorRef ShardRegion, ScheduledReminder Reminder, DateTimeOffset AckDeadline)>();
                var chunkRetried = 0;
                var chunkFailed = 0;
                var chunkExpired = 0;

                foreach (var reminder in chunk)
                {
                    // Already ended in this batch because its next occurrence was sent first.
                    if (expiredAsPrevious.Contains(ToOccurrenceKey(reminder.Entity, reminder.Key, reminder.DueTimeUtc)))
                        continue;

                    if (reminder.DeliveryDeadlineUtc.HasValue && reminder.DeliveryDeadlineUtc.Value <= completedAt)
                    {
                        terminalReminders.Add(new CompletedReminder(
                            reminder.Entity,
                            reminder.Key,
                            reminder.DueTimeUtc,
                            completedAt,
                            ReminderCompletionStatus.Expired));
                        chunkExpired += 1;

                        // Never deliver a stale occurrence late, but keep a recurring series alive.
                        if (reminder.RepeatInterval.HasValue)
                            AddNextOccurrence(reminder, completedAt, occurrencesToUpsert, pendingInserts);
                        continue;
                    }

                    var shardRegion = ShardRegionResolver.TryResolve(reminder.Entity);
                    if (shardRegion is null)
                    {
                        var failureReason = $"ShardRegion [{reminder.Entity.ShardRegionName}] not found";
                        _log.Warning("Reminder {0} could not be resolved to a ShardRegion. Attempt {1} of {2}",
                            reminder, reminder.AttemptCount + 1, Settings.MaxDeliveryAttempts);

                        if (TryCreateRetryReminder(
                                reminder,
                                completedAt,
                                failureReason,
                                out var retryReminder,
                                out var terminalStatus))
                        {
                            AddUpsert(occurrencesToUpsert, retryReminder);
                            _log.Info("Scheduling retry for reminder {0} at {1}", reminder.Key, retryReminder.When);
                            chunkRetried += 1;

                            // The next occurrence is written the first time this one is processed, whatever
                            // the outcome, so later retries never need to write (and possibly reset) it.
                            if (reminder.RepeatInterval.HasValue)
                                AddNextOccurrence(reminder, completedAt, occurrencesToUpsert, pendingInserts);
                        }
                        else
                        {
                            var finalAttempt = CreateTerminalAttempt(reminder, failureReason);
                            AddUpsert(occurrencesToUpsert, finalAttempt);
                            finalAttempts.Add(finalAttempt);
                            terminalReminders.Add(new CompletedReminder(
                                reminder.Entity,
                                reminder.Key,
                                reminder.DueTimeUtc,
                                completedAt,
                                terminalStatus));

                            if (reminder.RepeatInterval.HasValue)
                                AddNextOccurrence(reminder, completedAt, occurrencesToUpsert, pendingInserts);

                            if (terminalStatus == ReminderCompletionStatus.Expired)
                                chunkExpired += 1;
                            else
                                chunkFailed += 1;
                        }
                    }
                    else
                    {
                        var ackDeadline = TimeProvider.Now.Add(Settings.AckTimeout);

                        // For recurring reminders, pre-create the next occurrence NOW
                        // (before delivery) so it's persisted in the same commit batch.
                        // This means the next occurrence exists in storage even if the
                        // scheduler crashes after delivery — no occurrences are lost.
                        if (reminder.RepeatInterval.HasValue)
                        {
                            AddNextOccurrence(reminder, completedAt, occurrencesToUpsert, pendingInserts);

                            // Latest-only: sending this occurrence ends the previous one if it is still
                            // unacked. Once this one is due the previous one is past its deadline and
                            // dead anyway, so only an early send (inside MaxSlippage) needs this.
                            var interval = reminder.RepeatInterval.Value;
                            if (interval > TimeSpan.Zero && completedAt < reminder.DueTimeUtc && reminder.DueTimeUtc.UtcTicks > interval.Ticks)
                            {
                                var previous = ToOccurrenceKey(reminder.Entity, reminder.Key, reminder.DueTimeUtc - interval);
                                bool IsPrevious(ReminderEntity e, ReminderKey k, DateTimeOffset due) => ToOccurrenceKey(e, k, due) == previous;
                                deliveries.RemoveAll(d => IsPrevious(d.Reminder.Entity, d.Reminder.Key, d.Reminder.DueTimeUtc));
                                remindersToAwaitAck.RemoveAll(a => IsPrevious(a.Entity, a.Key, a.DueTimeUtc));
                                if (!terminalReminders.Exists(t => IsPrevious(t.Entity, t.Key, t.DueTimeUtc)))
                                    activeCompletions.Add(new CompletedReminder(reminder.Entity, reminder.Key, previous.DueTimeUtc,
                                        completedAt, ReminderCompletionStatus.Expired));
                                expiredAsPrevious.Add(previous);
                            }
                        }

                        // Move the current occurrence to AwaitingAck. It stays there
                        // until the entity acks it or the ack deadline elapses.
                        remindersToAwaitAck.Add(new AwaitingAckReminder(
                            reminder.Entity,
                            reminder.Key,
                            reminder.DueTimeUtc,
                            completedAt,
                            ackDeadline));
                        deliveries.Add((shardRegion, reminder, ackDeadline));
                    }
                }

                // Phase 3: Commit all mutations in a single atomic batch BEFORE delivery.
                // This is the key correctness boundary — if the write fails, nothing is
                // delivered, so there are zero duplicate deliveries on first failure.
                var writeFailed = false;
                var mutationBatch = new ReminderMutationBatch(
                    occurrencesToUpsert,
                    terminalReminders,
                    remindersToAwaitAck)
                {
                    PendingInserts = pendingInserts,
                    ActiveCompletions = activeCompletions
                };

                if (!mutationBatch.IsEmpty)
                {
                    try
                    {
                        if (Storage is not IConditionalReminderMutationStorage && chunk.Any(r => r.RepeatInterval.HasValue))
                            throw new NotSupportedException("Recurring reminders require storage implementing IConditionalReminderMutationStorage.");
                        using var mutationCts = new CancellationTokenSource(Settings.StorageTimeout);
                        var mutationResult = await Storage.CommitReminderMutationsAsync(mutationBatch, mutationCts.Token);
                        if (!mutationResult)
                            writeFailed = true;
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex,
                            "Failed to commit reminder mutation chunk with [{0}] upsert(s), [{1}] completion(s), and [{2}] awaiting-ack transition(s)",
                            occurrencesToUpsert.Count,
                            terminalReminders.Count,
                            remindersToAwaitAck.Count);
                        writeFailed = true;
                    }
                }

                if (writeFailed)
                {
                    // The commit may have landed even though it reported failure. Arm the ack-timeout
                    // check so the normal timeout path finds and retries any rows left AwaitingAck.
                    TrackAckDeadlines(remindersToAwaitAck);

                    _writeCircuitOpen = true;
                    _log.Warning("Write circuit OPEN — database writes are failing. " +
                                 "Pausing batch processing until writes recover. " +
                                 "Delivered [{0}] reminders in this run before failure.",
                        totalDelivered);
                    needsOverviewReload = true;
                    stopProcessing = true;
                    break;
                }

                hasConditionalInserts |= pendingInserts.Count > 0;
                totalRetried += chunkRetried;
                totalFailed += chunkFailed;
                totalExpired += chunkExpired;

                // Update the in-memory overview incrementally from upserted reminders
                // (next recurring occurrences, retries). Avoids an extra storage query
                // per chunk — the overview is only reloaded from storage on failure.
                if (occurrencesToUpsert.Count > 0)
                {
                    // A row this commit also ends (Failed/Expired) is not pending work.
                    foreach (var pendingReminder in occurrencesToUpsert.Where(u => !finalAttempts.Contains(u) &&
                                 !expiredAsPrevious.Contains(ToOccurrenceKey(u.Entity, u.Key, u.DueTimeUtc))))
                    {
                        batchOverview = batchOverview.Apply(pendingReminder, fetchedAt).newOverview;
                    }
                }

                if (remindersToAwaitAck.Count > 0)
                    TrackAckDeadlines(remindersToAwaitAck);

                if (_writeCircuitOpen)
                {
                    _writeCircuitOpen = false;
                    effectiveBatchSize = Settings.MaxBatchSize;
                    recoveredFromProbe = true;
                    _log.Info("Write circuit CLOSED — database writes recovered, resuming full-batch processing");
                }

                // Phase 4: Deliver AFTER the commit succeeds. If we get here, storage
                // has the AwaitingAck state persisted, so a crash after delivery is safe —
                // the ack-timeout checker will find the unacked occurrence and retry it.
                foreach (var delivery in deliveries)
                {
                    _log.Debug("Sending reminder occurrence [{0}] / [{1}] due at [{2}] to [{3}]",
                        delivery.Reminder.Entity,
                        delivery.Reminder.Key,
                        delivery.Reminder.DueTimeUtc,
                        delivery.ShardRegion);
                    ReminderEnvelope envelope;
                    try
                    {
                        envelope = ReminderEnvelopeFactory.Create(
                            delivery.Reminder.Entity,
                            delivery.Reminder.Key,
                            delivery.Reminder.DueTimeUtc,
                            ComputeEnvelopeDeadline(delivery.Reminder, delivery.AckDeadline),
                            delivery.Reminder.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Unregistered message type under Native AOT. The occurrence is already
                        // AwaitingAck in storage, so the ack-timeout path retries it and, after
                        // MaxDeliveryAttempts, marks it failed. Keep delivering the rest of the batch.
                        _log.Error(ex, "Cannot deliver reminder occurrence [{0}] / [{1}] due at [{2}]",
                            delivery.Reminder.Entity,
                            delivery.Reminder.Key,
                            delivery.Reminder.DueTimeUtc);
                        continue;
                    }

                    ShardRegionResolver.DeliverReminder(delivery.Reminder.Entity, envelope);
                    totalDelivered += 1;
                }
            }

            latestOverview = batchOverview;

            if (stopProcessing)
                break;

            // If a single-reminder probe succeeded, immediately continue with full-batch
            // processing in this same run rather than waiting for a later timer tick.
            if (recoveredFromProbe)
                continue;

            // If we got fewer than the effective fetch batch size, there are no more due reminders
            if (batch.Reminders.Count < effectiveBatchSize)
            {
                if (!hasConditionalInserts)
                    break;
                // Reconcile insert-only successors without refetching future retries inside MaxSlippage.
                // A retry created in this batch is strictly after its fetch clock reading.
                untilDeadline = fetchedAt;
            }
        }

        if (needsOverviewReload)
        {
            try
            {
                using var overviewCts = new CancellationTokenSource(Settings.StorageTimeout);
                var now = TimeProvider.Now;
                PendingReminders = await Storage.GetRemindersOverviewAsync(now, overviewCts.Token);
                _overviewAsOf = now;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to reload reminder overview after processing");
                fetchFailed = true;
                ScheduleStorageRecovery();
            }
        }
        else
        {
            PendingReminders = latestOverview;
            _overviewAsOf = fetchedAt;
        }

        _log.Info(
            "Successfully delivered [{0}] reminder occurrence(s); retrying [{1}] infrastructure-failed occurrence(s); permanently failed [{2}] occurrence(s); expired [{3}] occurrence(s). Next reminder due: {4}",
            totalDelivered,
            totalRetried,
            totalFailed,
            totalExpired,
            PendingReminders.TimeUntilNext);

        // An old, already-due overview must not turn a failed fetch into immediate polling.
        // Count a recovery delay from the end of the failed pass, even when the overview read works.
        if (fetchFailed)
            _fetchRetryNotBefore = TimeProvider.Now.Add(Settings.StorageTimeout * 2);
        TryScheduleFetchReminders();
    }

    public ITimerScheduler Timers { get; set; } = null!;
}
