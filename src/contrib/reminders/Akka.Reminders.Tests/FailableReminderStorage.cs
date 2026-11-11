using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests;

/// <summary>
/// Wraps an <see cref="IReminderStorage"/> and allows selectively failing write operations
/// to test circuit breaker and failure recovery behavior.
/// </summary>
internal sealed class FailableReminderStorage : IConditionalReminderMutationStorage
{
    private readonly IReminderStorage _inner;
    private readonly TaskCompletionSource _firstCommitMutationFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _commitMutationAttempts;
    private int _fetches;
    private int _occurrenceStatusReads;

    public int CommitMutationAttempts => Volatile.Read(ref _commitMutationAttempts);

    public Task FirstCommitMutationFailure => _firstCommitMutationFailure.Task;

    public int Fetches => Volatile.Read(ref _fetches);

    public int OccurrenceStatusReads => Volatile.Read(ref _occurrenceStatusReads);

    /// <summary>
    /// Runs before each mutation commit is forwarded, e.g. to inspect it or to let time pass.
    /// </summary>
    public Action<ReminderMutationBatch>? OnCommit { get; set; }

    /// <summary>
    /// When true, all write operations (MarkRemindersAsCompleted, ScheduleReminder) throw.
    /// Read operations (GetNextReminders, GetRemindersOverview, GetRemindersForEntity) continue to work.
    /// This simulates the "reads work, writes fail" scenario from issue #73.
    /// </summary>
    public bool FailWrites { get; set; }

    /// <summary>
    /// When true, the next mutation commit is applied to the inner storage and then reports failure
    /// (once), like a commit that reaches the server while the client sees a timeout or a dropped connection.
    /// </summary>
    public bool ApplyNextCommitThenReportFailure { get; set; }

    /// <summary>
    /// Like <see cref="ApplyNextCommitThenReportFailure"/>, but the failure is a thrown <see cref="TimeoutException"/>.
    /// </summary>
    public bool ApplyNextCommitThenThrow { get; set; }

    /// <summary>
    /// The next scheduling write lands, but its result is Error rather than Success.
    /// </summary>
    public bool ApplyNextScheduleThenReportFailure { get; set; }

    /// <summary>
    /// The next scheduling write lands and then throws a timeout.
    /// </summary>
    public bool ApplyNextScheduleThenThrow { get; set; }

    /// <summary>
    /// When true, the next overview read throws (once).
    /// </summary>
    public bool FailNextOverviewRead { get; set; }

    /// <summary>
    /// Fails the fetch and overview reads while leaving occurrence queries available as mailbox barriers.
    /// </summary>
    public bool FailFetchAndOverview { get; set; }

    public bool FailFetchReads { get; set; }

    /// <summary>
    /// When true, all read operations throw.
    /// </summary>
    public bool FailReads { get; set; }

    /// <summary>
    /// When true, MarkRemindersAsCompletedAsync throws.
    /// </summary>
    public bool FailMarkCompletedWrites { get; set; }

    /// <summary>
    /// When true, ScheduleReminderAsync throws.
    /// </summary>
    public bool FailScheduleWrites { get; set; }

    public FailableReminderStorage(IReminderStorage inner)
    {
        _inner = inner;
    }

    // --- Write operations: fail when FailWrites is true ---

    public Task<bool> MarkRemindersAsCompletedAsync(IEnumerable<CompletedReminder> keys, CancellationToken ct = default)
    {
        if (FailWrites || FailMarkCompletedWrites)
            throw new TimeoutException("Simulated database write timeout");
        return _inner.MarkRemindersAsCompletedAsync(keys, ct);
    }

    public Task<ReminderProtocol.ReminderScheduled> ScheduleReminderAsync(ScheduledReminder reminder, CancellationToken ct = default)
    {
        if (FailWrites || FailScheduleWrites)
            throw new TimeoutException("Simulated database write timeout");
        if (ApplyNextScheduleThenReportFailure || ApplyNextScheduleThenThrow)
        {
            var throws = ApplyNextScheduleThenThrow;
            ApplyNextScheduleThenReportFailure = ApplyNextScheduleThenThrow = false;
            return ApplyScheduleThenFailAsync(reminder, throws, ct);
        }
        return _inner.ScheduleReminderAsync(reminder, ct);
    }

    private async Task<ReminderProtocol.ReminderScheduled> ApplyScheduleThenFailAsync(
        ScheduledReminder reminder, bool throws, CancellationToken ct)
    {
        await _inner.ScheduleReminderAsync(reminder, ct);
        if (throws)
            throw new TimeoutException("Simulated scheduling timeout after the write landed");
        return new ReminderProtocol.ReminderScheduled(reminder.ToScheduleReminder(),
            ReminderScheduleResponseCode.Error, "Simulated scheduling error after the write landed");
    }

    public Task<bool> UpsertReminderOccurrencesAsync(IEnumerable<ScheduledReminder> reminders, CancellationToken ct = default)
    {
        if (FailWrites || FailScheduleWrites)
            throw new TimeoutException("Simulated database write timeout");
        return _inner.UpsertReminderOccurrencesAsync(reminders, ct);
    }

    public Task<bool> CommitReminderMutationsAsync(ReminderMutationBatch mutationBatch, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _commitMutationAttempts);
        OnCommit?.Invoke(mutationBatch);
        if (FailWrites || FailScheduleWrites || FailMarkCompletedWrites)
        {
            _firstCommitMutationFailure.TrySetResult();
            throw new TimeoutException("Simulated database write timeout");
        }
        if (ApplyNextCommitThenReportFailure || ApplyNextCommitThenThrow)
        {
            var throws = ApplyNextCommitThenThrow;
            ApplyNextCommitThenReportFailure = ApplyNextCommitThenThrow = false;
            return ApplyThenFailAsync(mutationBatch, throws, ct);
        }
        return _inner.CommitReminderMutationsAsync(mutationBatch, ct);
    }

    private async Task<bool> ApplyThenFailAsync(ReminderMutationBatch mutationBatch, bool throws, CancellationToken ct)
    {
        await _inner.CommitReminderMutationsAsync(mutationBatch, ct);
        if (throws)
            throw new TimeoutException("Simulated database commit timeout after the commit landed");
        return false;
    }

    public Task<ReminderProtocol.RemindersCancelled> CancelReminderAsync(ReminderEntity entity, ReminderKey key, CancellationToken ct = default)
    {
        if (FailWrites)
            throw new TimeoutException("Simulated database write timeout");
        return _inner.CancelReminderAsync(entity, key, ct);
    }

    public Task<ReminderProtocol.RemindersCancelled> CancelAllRemindersForEntityAsync(ReminderEntity entity, CancellationToken ct = default)
    {
        if (FailWrites)
            throw new TimeoutException("Simulated database write timeout");
        return _inner.CancelAllRemindersForEntityAsync(entity, ct);
    }

    public Task<bool> CleanUpCompletedRemindersAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        if (FailWrites)
            throw new TimeoutException("Simulated database write timeout");
        return _inner.CleanUpCompletedRemindersAsync(olderThan, ct);
    }

    public Task<int> ExpireRemindersAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (FailWrites)
            throw new TimeoutException("Simulated database write timeout");
        return _inner.ExpireRemindersAsync(now, ct);
    }

    // --- Read operations: always work ---

    public Task<PendingRemindersWithSummary> GetNextRemindersAsync(DateTimeOffset untilDeadline, DateTimeOffset now,
        ReminderBatchSize maxCount, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _fetches);
        if (FailFetchAndOverview || FailFetchReads)
            throw new TimeoutException("Simulated fetch read timeout");
        return _inner.GetNextRemindersAsync(untilDeadline, now, maxCount, ct);
    }

    public Task<ReminderOverview> GetRemindersOverviewAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (FailFetchAndOverview)
            throw new TimeoutException("Simulated overview read timeout");
        if (FailNextOverviewRead)
        {
            FailNextOverviewRead = false;
            throw new TimeoutException("Simulated database read timeout");
        }
        return _inner.GetRemindersOverviewAsync(now, ct);
    }

    public Task<IReadOnlyList<ScheduledReminder>> GetRemindersForEntityAsync(ReminderEntity entity, int take = 10, int skip = 0, CancellationToken ct = default)
    {
        return _inner.GetRemindersForEntityAsync(entity, take, skip, ct);
    }

    public Task<bool> MarkRemindersAsAwaitingAckAsync(IEnumerable<AwaitingAckReminder> reminders, CancellationToken ct = default)
    {
        if (FailWrites)
            throw new TimeoutException("Simulated database write timeout");
        return _inner.MarkRemindersAsAwaitingAckAsync(reminders, ct);
    }

    public Task<IReadOnlyList<ScheduledReminder>> GetTimedOutAckRemindersAsync(DateTimeOffset now, ReminderBatchSize maxCount, CancellationToken ct = default)
    {
        if (FailReads)
            throw new TimeoutException("Simulated database read timeout");
        return _inner.GetTimedOutAckRemindersAsync(now, maxCount, ct);
    }

    public Task<ScheduledReminder?> GetAwaitingAckReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        CancellationToken ct = default)
    {
        if (FailReads)
            throw new TimeoutException("Simulated database read timeout");
        return _inner.GetAwaitingAckReminderAsync(entity, key, dueTimeUtc, ct);
    }

    public Task<DateTimeOffset?> GetNextAwaitingAckDeadlineAsync(CancellationToken ct = default)
    {
        if (FailReads)
            throw new TimeoutException("Simulated database read timeout");
        return _inner.GetNextAwaitingAckDeadlineAsync(ct);
    }

    public Task<ReminderOccurrenceStatus?> GetReminderOccurrenceStatusAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        CancellationToken ct = default)
    {
        if (FailReads)
            throw new TimeoutException("Simulated database read timeout");
        Interlocked.Increment(ref _occurrenceStatusReads);
        return _inner.GetReminderOccurrenceStatusAsync(entity, key, dueTimeUtc, ct);
    }

    public Task<AckResult> AcknowledgeReminderAsync(ReminderEntity entity, ReminderKey key, DateTimeOffset dueTimeUtc, DateTimeOffset ackedAt, CancellationToken ct = default)
    {
        if (FailWrites)
            throw new TimeoutException("Simulated database write timeout");
        return _inner.AcknowledgeReminderAsync(entity, key, dueTimeUtc, ackedAt, ct);
    }

    public Task<IReadOnlyList<AckResult>> AcknowledgeRemindersAsync(IEnumerable<ReminderAcknowledgement> acknowledgements, CancellationToken ct = default)
    {
        if (FailWrites)
            throw new TimeoutException("Simulated database write timeout");
        return _inner.AcknowledgeRemindersAsync(acknowledgements, ct);
    }
}
