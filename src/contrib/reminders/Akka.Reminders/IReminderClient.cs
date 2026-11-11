namespace Akka.Reminders;

/// <summary>
/// Interface for working with persistent reminders.
/// </summary>
public interface IReminderClient
{
    /// <summary>
    /// Reminders will be set on behalf of this entity.
    /// </summary>
    public ReminderEntity Entity { get; }

    /// <summary>
    /// Schedule a one-off reminder.
    /// If a reminder with the same key already exists, it will be overwritten.
    /// </summary>
    Task<ReminderProtocol.ReminderScheduled> ScheduleSingleReminderAsync(
        ReminderKey key,
        DateTimeOffset when,
        object message,
        TimeSpan? maxDeliveryWindow = null,
        CancellationToken ct = default);

    /// <summary>
    /// Schedule a recurring reminder that fires at regular intervals.
    /// If a reminder with the same key already exists, it will be overwritten.
    /// </summary>
    Task<ReminderProtocol.ReminderScheduled> ScheduleRecurringReminderAsync(
        ReminderKey key,
        DateTimeOffset firstOccurrence,
        TimeSpan interval,
        object message,
        TimeSpan? maxDeliveryWindow = null,
        CancellationToken ct = default);

    Task<ReminderProtocol.RemindersCancelled> CancelReminderAsync(ReminderKey key, CancellationToken ct = default);

    Task<ReminderProtocol.RemindersCancelled> CancelAllRemindersAsync(CancellationToken ct = default);

    Task<ReminderProtocol.RemindersForEntity> ListRemindersAsync(CancellationToken ct = default);

    /// <summary>
    /// Acknowledge receipt of a reminder. Returns when the scheduler confirms.
    /// If this Task faults or times out, a duplicate delivery may occur.
    /// </summary>
    /// <param name="envelope">The envelope received for <see cref="Entity"/> when the reminder fired.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A task containing the scheduler's acknowledgement response.
    /// Check <see cref="ReminderProtocol.ReminderAckResponse.ResponseCode"/> to determine success.
    /// </returns>
    Task<ReminderProtocol.ReminderAckResponse> AckAsync(ReminderEnvelope envelope, CancellationToken ct = default);

    /// <summary>
    /// Reports a failed delivery attempt and applies the configured retry policy.
    /// The envelope must belong to <see cref="Entity"/>.
    /// </summary>
    Task<ReminderProtocol.ReminderNackResponse> NackAsync(
        ReminderEnvelope envelope,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the durable state for one reminder occurrence for <see cref="Entity"/>.
    /// </summary>
    Task<ReminderProtocol.ReminderOccurrenceStatusResponse> GetOccurrenceStatusAsync(
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        CancellationToken ct = default);
}
