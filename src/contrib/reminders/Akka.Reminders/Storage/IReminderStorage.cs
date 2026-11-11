using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Reminders.Storage;

public sealed record ReminderOverview
{
    /// <summary>
    /// Determined from our actual data set - how soon is the next reminder?
    /// </summary>
    public TimeSpan TimeUntilNext { get; init; } = TimeSpan.MaxValue;
    
    /// <summary>
    /// How many reminders are pending?
    /// </summary>
    public long TotalPendingReminders { get; init; } = 0;

    public (ReminderOverview newOverview, bool hasNewerDate) Apply(ScheduledReminder newReminder, DateTimeOffset now)
    {
        var newTimespan = newReminder.When - now;
        // An empty overview has TimeUntilNext = MaxValue, so any reminder is sooner. Zero is a real
        // value ("due right now") and must not be replaced by a later reminder.
        var hasNewerDate = newTimespan <= TimeUntilNext;

        return (new ReminderOverview
        {
            TimeUntilNext = hasNewerDate ? newTimespan : TimeUntilNext,
            TotalPendingReminders = TotalPendingReminders + 1
        }, hasNewerDate);
    }
    
    public static ReminderOverview Empty => new();
}

/// <summary>
/// Validated fetch batch size for reminder retrieval operations.
/// </summary>
public readonly record struct ReminderBatchSize
{
    public ReminderBatchSize(int value)
    {
        if (value < 1)
            throw new ArgumentOutOfRangeException(nameof(value), "ReminderBatchSize must be greater than or equal to 1.");

        Value = value;
    }

    public int Value { get; }
}

/// <summary>
/// Gets the next N reminders, along with a summary of the pending reminders.
/// </summary>
/// <param name="Reminders">The next reminders that need to be delivered right now.</param>
/// <param name="NextOverview">The next reminders overview.</param>
public sealed record PendingRemindersWithSummary(IReadOnlyList<ScheduledReminder> Reminders, ReminderOverview NextOverview);

/// <summary>
/// The completion status of a reminder.
/// </summary>
public enum ReminderCompletionStatus
{
    /// <summary>
    /// Reminder is still pending delivery.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Reminder was successfully delivered to the target entity.
    /// </summary>
    Delivered = 1,

    /// <summary>
    /// Reminder failed to deliver after exhausting all retry attempts.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// Reminder was explicitly cancelled by the user.
    /// </summary>
    Cancelled = 3,

    /// <summary>
    /// Reminder has been delivered but the recipient has not yet acknowledged it.
    /// The reminder will be redelivered if the ack deadline passes without acknowledgement.
    /// </summary>
    AwaitingAck = 4,

    /// <summary>
    /// Reminder exceeded its delivery deadline and is no longer actionable.
    /// </summary>
    Expired = 5
}

/// <summary>
/// Used to mark a reminder as completed.
/// </summary>
public sealed record CompletedReminder(
    ReminderEntity Entity,
    ReminderKey Key,
    DateTimeOffset DueTimeUtc,
    DateTimeOffset CompletedAt,
    ReminderCompletionStatus Status = ReminderCompletionStatus.Delivered);

/// <summary>
/// Tracks a reminder that has been delivered but is awaiting acknowledgement from the recipient.
/// </summary>
/// <param name="Entity">The entity that owns this reminder.</param>
/// <param name="Key">The unique reminder key.</param>
/// <param name="DueTimeUtc">The original due time that identifies this reminder occurrence.</param>
/// <param name="DeliveredAt">When the reminder was delivered to the recipient.</param>
/// <param name="AckDeadline">The deadline by which the recipient must acknowledge the reminder.
/// If this deadline passes without an ack, the reminder will be redelivered.</param>
public sealed record AwaitingAckReminder(
    ReminderEntity Entity,
    ReminderKey Key,
    DateTimeOffset DueTimeUtc,
    DateTimeOffset DeliveredAt,
    DateTimeOffset AckDeadline);

/// <summary>
/// Batched mutation set applied by the scheduler inside a single storage commit.
/// </summary>
public sealed record ReminderMutationBatch(
    IReadOnlyList<ScheduledReminder> PendingUpserts,
    IReadOnlyList<CompletedReminder> CompletedReminders,
    IReadOnlyList<AwaitingAckReminder> AwaitingAckReminders)
{
    public static ReminderMutationBatch Empty { get; } = new([], [], []);

    /// <summary>Insert missing occurrences without changing any existing occurrence, including terminal state.</summary>
    public IReadOnlyList<ScheduledReminder> PendingInserts { get; init; } = [];

    /// <summary>Complete occurrences only while Pending or AwaitingAck; absent and terminal rows are unchanged.</summary>
    public IReadOnlyList<CompletedReminder> ActiveCompletions { get; init; } = [];

    public bool IsEmpty => PendingUpserts.Count == 0 && CompletedReminders.Count == 0 && AwaitingAckReminders.Count == 0
                           && PendingInserts.Count == 0 && ActiveCompletions.Count == 0;
}

/// <summary>
/// Buffered acknowledgement write issued by the scheduler.
/// </summary>
public sealed record ReminderAcknowledgement(
    ReminderEntity Entity,
    ReminderKey Key,
    DateTimeOffset DueTimeUtc,
    DateTimeOffset AckedAt);

/// <summary>
/// Result status for a reminder acknowledgement write.
/// </summary>
public enum ReminderAckStorageStatus
{
    Success = 0,
    NotFound = 1,
    Error = 2
}

/// <summary>
/// The result of acknowledging a reminder.
/// </summary>
/// <param name="Entity">Entity that owns the reminder occurrence.</param>
/// <param name="Key">Reminder key.</param>
/// <param name="DueTimeUtc">Occurrence identity.</param>
/// <param name="Status">Ack write outcome.</param>
/// <param name="ErrorMessage">Optional storage error details.</param>
public sealed record AckResult(
    ReminderEntity Entity,
    ReminderKey Key,
    DateTimeOffset DueTimeUtc,
    ReminderAckStorageStatus Status,
    string? ErrorMessage = null)
{
    public bool Success => Status == ReminderAckStorageStatus.Success;
}

/// <summary>
/// Declares support for atomic <see cref="ReminderMutationBatch.PendingInserts"/> and
/// <see cref="ReminderMutationBatch.ActiveCompletions"/>. Required for recurring reminder processing.
/// </summary>
public interface IConditionalReminderMutationStorage : IReminderStorage;

/// <summary>Storage implementation for reminders.</summary>
public interface IReminderStorage
{
    Task<ReminderProtocol.ReminderScheduled> ScheduleReminderAsync(ScheduledReminder reminder, CancellationToken ct = default);
    Task<bool> UpsertReminderOccurrencesAsync(IEnumerable<ScheduledReminder> reminders, CancellationToken ct = default);
    Task<bool> CommitReminderMutationsAsync(ReminderMutationBatch mutationBatch, CancellationToken ct = default);
    Task<ReminderProtocol.RemindersCancelled> CancelReminderAsync(ReminderEntity entity, ReminderKey key, CancellationToken ct = default);
    
    Task<IReadOnlyList<ScheduledReminder>> GetRemindersForEntityAsync(ReminderEntity entity, int take = 10, int skip = 0, CancellationToken ct = default);
    Task<ReminderProtocol.RemindersCancelled> CancelAllRemindersForEntityAsync(ReminderEntity entity, CancellationToken ct = default);
    
    /// <summary>
    /// Fetches a summary of pending reminders for diagnostics, testing, and ad-hoc queries.
    /// </summary>
    /// <remarks>
    /// This method is intended for external use cases: diagnostic tooling, integration tests,
    /// health check endpoints, and monitoring dashboards.
    ///
    /// It MUST NOT be called from the scheduling hot path (e.g., inside <see cref="GetNextRemindersAsync"/>).
    /// The scheduling actor computes its own overview from efficient aggregate queries as part of
    /// <see cref="GetNextRemindersAsync"/> — calling this method from there would introduce
    /// redundant database round-trips.
    /// </remarks>
    /// <param name="now">Current time from scheduler</param>
    /// <param name="ct">Cancellation token</param>
    Task<ReminderOverview> GetRemindersOverviewAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Gets the next reminders that are due before the specified deadline.
    /// </summary>
    /// <param name="untilDeadline">Deadline for fetching reminders</param>
    /// <param name="now">Current time from scheduler</param>
    /// <param name="maxCount">Maximum number of reminders to return.</param>
    /// <param name="ct">Cancellation token</param>
    Task<PendingRemindersWithSummary> GetNextRemindersAsync(DateTimeOffset untilDeadline, DateTimeOffset now,
        ReminderBatchSize maxCount, CancellationToken ct = default);
    
    /// <summary>
    /// Used to soft-delete reminders from the storage.
    /// </summary>
    Task<bool> MarkRemindersAsCompletedAsync(IEnumerable<CompletedReminder> keys, CancellationToken ct = default);
    
    /// <summary>
    /// Clean up all completed reminders older than the specified time.
    /// </summary>
    /// <param name="olderThan">All completed reminders older than this </param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<bool> CleanUpCompletedRemindersAsync(DateTimeOffset olderThan, CancellationToken ct = default);

    /// <summary>
    /// Marks all active reminders whose delivery deadline has passed as expired.
    /// </summary>
    /// <remarks>
    /// Must not expire a <see cref="ReminderCompletionStatus.Pending"/> occurrence that has a repeat interval.
    /// The scheduler ends that occurrence itself and writes the next one in the same commit; otherwise the
    /// recurring series would be lost. For the same reason <see cref="GetNextRemindersAsync"/>,
    /// <see cref="GetRemindersOverviewAsync"/> and <see cref="GetRemindersForEntityAsync"/> must keep returning
    /// such occurrences after their deadline has passed.
    /// </remarks>
    /// <param name="now">Current time used as the expiration cutoff.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of reminders transitioned to <see cref="ReminderCompletionStatus.Expired"/>.</returns>
    Task<int> ExpireRemindersAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Marks reminders as delivered but awaiting acknowledgement from the recipient.
    /// Sets <c>completion_status = 'AwaitingAck'</c> and records the delivery and ack-deadline timestamps.
    /// The reminder remains visible to due-reminder queries until it is either acknowledged or its
    /// ack deadline expires and it is requeued.
    /// </summary>
    /// <param name="reminders">The reminders to transition into the awaiting-ack state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the update succeeded; <c>false</c> on storage error.</returns>
    Task<bool> MarkRemindersAsAwaitingAckAsync(
        IEnumerable<AwaitingAckReminder> reminders,
        CancellationToken ct = default);

    /// <summary>
    /// Gets reminders whose ack deadline has passed without acknowledgement.
    /// These reminders should be redelivered to their target entities.
    /// </summary>
    /// <param name="now">The current time; reminders with <c>ack_deadline_utc &lt;= now</c> are returned.</param>
    /// <param name="maxCount">Maximum number of timed-out reminders to return per call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of reminders that must be redelivered.</returns>
    Task<IReadOnlyList<ScheduledReminder>> GetTimedOutAckRemindersAsync(
        DateTimeOffset now,
        ReminderBatchSize maxCount,
        CancellationToken ct = default);

    /// <summary>
    /// Gets one reminder occurrence only when it awaits an acknowledgement.
    /// </summary>
    Task<ScheduledReminder?> GetAwaitingAckReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Gets one active or terminal reminder occurrence by its durable identity.
    /// </summary>
    Task<ReminderOccurrenceStatus?> GetReminderOccurrenceStatusAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the earliest acknowledgement deadline currently persisted in storage.
    /// Returns <c>null</c> when there are no reminders awaiting acknowledgement.
    /// </summary>
    Task<DateTimeOffset?> GetNextAwaitingAckDeadlineAsync(CancellationToken ct = default);

    /// <summary>
    /// Acknowledges a reminder, marking it as fully delivered.
    /// </summary>
    /// <param name="entity">The entity that owns the reminder.</param>
    /// <param name="key">The unique reminder key.</param>
    /// <param name="dueTimeUtc">The due time that identifies the delivered reminder occurrence.</param>
    /// <param name="ackedAt">The time at which the ack was received.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="AckResult"/> with <c>Success = true</c> when the reminder was found and acknowledged;
    /// <c>Success = false</c> when no matching awaiting-ack reminder was found.
    /// </returns>
    Task<AckResult> AcknowledgeReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        DateTimeOffset ackedAt,
        CancellationToken ct = default);

    Task<IReadOnlyList<AckResult>> AcknowledgeRemindersAsync(
        IEnumerable<ReminderAcknowledgement> acknowledgements,
        CancellationToken ct = default);
}
