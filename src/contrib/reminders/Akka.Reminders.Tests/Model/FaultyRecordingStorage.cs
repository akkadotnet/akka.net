using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Model;

/// <summary>Storage calls a fault can target.</summary>
public enum StorageCall
{
    Schedule,
    Commit,
    Cancel,
    CancelAll,
    List,
    Overview,
    Fetch,
    Expire,
    TimedOutAcks,
    GetAwaitingAck,
    Status,
    NextAckDeadline,
    Ack,
    Cleanup,
}

public enum FaultKind
{
    /// <summary>The call fails before touching storage (a commit returns false, other calls throw).</summary>
    Fail,

    /// <summary>The call takes some virtual time, then succeeds.</summary>
    Slow,

    /// <summary>The call takes <c>StorageTimeout</c> of virtual time, then throws without touching storage.</summary>
    Timeout,

    /// <summary>The write reaches storage, then the call reports failure (the caller cannot tell it worked).</summary>
    AppliedThenFail,
}

/// <summary>
/// Wraps a real <see cref="IReminderStorage"/> and injects the faults a scenario asks for. Each fault
/// that fires is written to the <see cref="Journal"/> as <see cref="Trouble"/>. Slow calls move the
/// virtual clock, so "slow storage" is deterministic.
/// </summary>
public sealed class FaultyRecordingStorage : IConditionalReminderMutationStorage
{
    /// <summary>Entity used by the harness's own probe queries; never counted or faulted.</summary>
    public static readonly ReminderEntity ProbeEntity = new("__probe", "__probe");

    private readonly IReminderStorage _inner;
    private readonly VirtualClock _clock;
    private readonly Journal _journal;
    private readonly HarnessSignals _signals;
    private readonly TimeSpan _recovery;
    private readonly List<(StorageCall Call, FaultKind Kind, int Remaining, TimeSpan Delay, bool Fire)> _faults = [];

    public FaultyRecordingStorage(IReminderStorage inner, VirtualClock clock, Journal journal, HarnessSignals signals, TimeSpan recovery)
    {
        // A wrapper must not advertise conditional mutations unless its provider actually supports them.
        _inner = inner as IConditionalReminderMutationStorage ??
                 throw new ArgumentException("The model requires conditional recurring mutations from its inner storage.", nameof(inner));
        _clock = clock;
        _journal = journal;
        _signals = signals;
        _recovery = recovery;
    }

    public void AddFault(StorageCall call, FaultKind kind, int count, TimeSpan delay, bool fireTimersWhileSlow)
    {
        lock (_faults)
            _faults.Add((call, kind, count, delay, fireTimersWhileSlow));
    }

    public void ClearFaults()
    {
        lock (_faults)
            _faults.Clear();
    }

    private (FaultKind Kind, TimeSpan Delay, bool Fire)? TakeFault(StorageCall call)
    {
        lock (_faults)
        {
            var index = _faults.FindIndex(f => f.Call == call);
            if (index < 0)
                return null;
            var f = _faults[index];
            if (f.Remaining <= 1)
                _faults.RemoveAt(index);
            else
                _faults[index] = f with { Remaining = f.Remaining - 1 };
            return (f.Kind, f.Delay, f.Fire);
        }
    }

    private void Delay(TimeSpan by, bool fireTimers)
    {
        _clock.Advance(by);
        if (fireTimers)
            _clock.FireDue();
    }

    private void Failed()
    {
        lock (_signals.Lock)
            _signals.FailedCalls++;
    }

    /// <summary>
    /// Runs a storage call with the fault (if any) queued for it. <paramref name="failResult"/> is what a
    /// failed call returns instead of throwing (a commit reports failure by returning false).
    /// </summary>
    private async Task<T> Run<T>(StorageCall call, Func<Task<T>> body, Func<T>? failResult = null,
        IReadOnlyList<ReminderAcknowledgement>? ackTargets = null)
    {
        _signals.Touch();
        var fault = TakeFault(call);
        if (fault is { } f)
        {
            // A slow call is trouble while it lasts. A failed call is trouble until the scheduler has had
            // its recovery time.
            var until = f.Kind switch
            {
                FaultKind.Slow => _clock.Now + f.Delay,
                FaultKind.Timeout => _clock.Now + ModelSettings.StorageTimeout + _recovery,
                _ => _clock.Now + _recovery,
            };
            if (f.Kind == FaultKind.Slow)
                _journal.Stalled(f.Delay + _recovery);
            _journal.Add(new Trouble(until, null, call, f.Kind) { AckTargets = ackTargets ?? [] });

            switch (f.Kind)
            {
                case FaultKind.Fail:
                    Failed();
                    if (failResult is not null)
                        return failResult();
                    throw new TimeoutException($"Injected {call} failure");
                case FaultKind.Timeout:
                    Failed();
                    Delay(ModelSettings.StorageTimeout, f.Fire);
                    throw new OperationCanceledException($"Injected {call} timeout");
            }
        }

        var result = await body();

        switch (fault?.Kind)
        {
            // The call reached storage at once but returns late: reads hand back a stale snapshot,
            // writes report success after the clock has moved on.
            case FaultKind.Slow:
                Delay(fault.Value.Delay, fault.Value.Fire);
                break;
            case FaultKind.AppliedThenFail:
                Failed();
                if (failResult is not null)
                    return failResult();
                throw new TimeoutException($"Injected {call} failure after the call reached storage");
        }

        return result;
    }

    private async Task<T> RunInit<T>(StorageCall call, Func<Task<T>> body, bool endsInit)
    {
        try
        {
            var result = await Run(call, body);
            if (endsInit)
                lock (_signals.Lock)
                    _signals.InitPhase = false;
            return result;
        }
        catch
        {
            lock (_signals.Lock)
                if (_signals.InitPhase)
                    _signals.InitFailed = true;
            throw;
        }
    }

    public Task<ReminderProtocol.ReminderScheduled> ScheduleReminderAsync(ScheduledReminder reminder, CancellationToken ct = default)
        => Run(StorageCall.Schedule, () => _inner.ScheduleReminderAsync(reminder, ct));

    public Task<bool> UpsertReminderOccurrencesAsync(IEnumerable<ScheduledReminder> reminders, CancellationToken ct = default)
        => throw new NotSupportedException("The scheduler does not call UpsertReminderOccurrencesAsync.");

    public Task<bool> CommitReminderMutationsAsync(ReminderMutationBatch mutationBatch, CancellationToken ct = default)
        => Run(StorageCall.Commit, () => _inner.CommitReminderMutationsAsync(mutationBatch, ct), failResult: () => false);

    public Task<ReminderProtocol.RemindersCancelled> CancelReminderAsync(ReminderEntity entity, ReminderKey key, CancellationToken ct = default)
        => Run(StorageCall.Cancel, () => _inner.CancelReminderAsync(entity, key, ct));

    public Task<IReadOnlyList<ScheduledReminder>> GetRemindersForEntityAsync(ReminderEntity entity, int take = 10, int skip = 0,
        CancellationToken ct = default)
        => Run(StorageCall.List, () => _inner.GetRemindersForEntityAsync(entity, take, skip, ct));

    public Task<ReminderProtocol.RemindersCancelled> CancelAllRemindersForEntityAsync(ReminderEntity entity, CancellationToken ct = default)
        => Run(StorageCall.CancelAll, () => _inner.CancelAllRemindersForEntityAsync(entity, ct));

    public Task<ReminderOverview> GetRemindersOverviewAsync(DateTimeOffset now, CancellationToken ct = default)
        => RunInit(StorageCall.Overview, () => _inner.GetRemindersOverviewAsync(now, ct), endsInit: false);

    public Task<PendingRemindersWithSummary> GetNextRemindersAsync(DateTimeOffset untilDeadline, DateTimeOffset now,
        ReminderBatchSize maxCount, CancellationToken ct = default)
        => Run(StorageCall.Fetch, () => _inner.GetNextRemindersAsync(untilDeadline, now, maxCount, ct));

    public Task<bool> MarkRemindersAsCompletedAsync(IEnumerable<CompletedReminder> keys, CancellationToken ct = default)
        => throw new NotSupportedException("The scheduler does not call MarkRemindersAsCompletedAsync.");

    public Task<bool> CleanUpCompletedRemindersAsync(DateTimeOffset olderThan, CancellationToken ct = default)
        => Run(StorageCall.Cleanup, () => _inner.CleanUpCompletedRemindersAsync(olderThan, ct));

    public Task<int> ExpireRemindersAsync(DateTimeOffset now, CancellationToken ct = default)
        => Run(StorageCall.Expire, () => _inner.ExpireRemindersAsync(now, ct));

    public Task<bool> MarkRemindersAsAwaitingAckAsync(IEnumerable<AwaitingAckReminder> reminders, CancellationToken ct = default)
        => throw new NotSupportedException("The scheduler does not call MarkRemindersAsAwaitingAckAsync.");

    public Task<IReadOnlyList<ScheduledReminder>> GetTimedOutAckRemindersAsync(DateTimeOffset now, ReminderBatchSize maxCount,
        CancellationToken ct = default)
        => Run(StorageCall.TimedOutAcks, () => _inner.GetTimedOutAckRemindersAsync(now, maxCount, ct));

    public Task<ScheduledReminder?> GetAwaitingAckReminderAsync(ReminderEntity entity, ReminderKey key, DateTimeOffset dueTimeUtc,
        CancellationToken ct = default)
        => Run(StorageCall.GetAwaitingAck, () => _inner.GetAwaitingAckReminderAsync(entity, key, dueTimeUtc, ct));

    public Task<ReminderOccurrenceStatus?> GetReminderOccurrenceStatusAsync(ReminderEntity entity, ReminderKey key, DateTimeOffset dueTimeUtc,
        CancellationToken ct = default)
        => entity == ProbeEntity
            ? Task.FromResult<ReminderOccurrenceStatus?>(null)
            : Run(StorageCall.Status, () => _inner.GetReminderOccurrenceStatusAsync(entity, key, dueTimeUtc, ct));

    public Task<DateTimeOffset?> GetNextAwaitingAckDeadlineAsync(CancellationToken ct = default)
        => RunInit(StorageCall.NextAckDeadline, () => _inner.GetNextAwaitingAckDeadlineAsync(ct), endsInit: true);

    public Task<AckResult> AcknowledgeReminderAsync(ReminderEntity entity, ReminderKey key, DateTimeOffset dueTimeUtc,
        DateTimeOffset ackedAt, CancellationToken ct = default)
        => throw new NotSupportedException("The scheduler does not call AcknowledgeReminderAsync.");

    public Task<IReadOnlyList<AckResult>> AcknowledgeRemindersAsync(IEnumerable<ReminderAcknowledgement> acknowledgements,
        CancellationToken ct = default)
    {
        var list = acknowledgements.ToList();
        return Run(StorageCall.Ack, () => _inner.AcknowledgeRemindersAsync(list, ct), ackTargets: list);
    }
}
