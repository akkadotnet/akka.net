#nullable enable

namespace Akka.Reminders;

/// <summary>Controls whether repeated delivery failures exhaust a one-off reminder.</summary>
public enum ReminderRetryPolicy
{
    /// <summary>Stop after the scheduler's configured maximum number of attempts.</summary>
    UseConfiguredAttemptLimit = 0,

    /// <summary>Retry until acknowledged, cancelled, or the configured delivery deadline expires.</summary>
    UntilAcknowledged = 1
}

/// <summary>Optional client capability for scheduling one-off reminders with a retry policy.</summary>
public interface IReminderRetryPolicyClient : IReminderClient
{
    /// <summary>Schedules a one-off reminder using the specified retry policy.</summary>
    /// <param name="key">Reminder key, replacing any existing registration with this key.</param>
    /// <param name="when">The first delivery time.</param>
    /// <param name="message">The payload to deliver.</param>
    /// <param name="retryPolicy">Whether delivery attempts can be exhausted.</param>
    /// <param name="maxDeliveryWindow">Delivery window, or null to inherit the scheduler default.</param>
    /// <param name="ct">Cancellation token for the scheduling request.</param>
    Task<ReminderProtocol.ReminderScheduled> ScheduleSingleReminderAsync(
        ReminderKey key, DateTimeOffset when, object message, ReminderRetryPolicy retryPolicy,
        TimeSpan? maxDeliveryWindow = null, CancellationToken ct = default);
}

/// <summary>Retry-policy overloads that preserve compatibility with existing reminder clients.</summary>
public static class ReminderRetryPolicyClientExtensions
{
    /// <summary>Schedules a one-off reminder using the specified retry policy.</summary>
    /// <param name="client">Client for the target entity.</param>
    /// <param name="key">Reminder key.</param>
    /// <param name="when">The first delivery time.</param>
    /// <param name="message">The payload to deliver.</param>
    /// <param name="retryPolicy">Whether delivery attempts can be exhausted.</param>
    /// <param name="maxDeliveryWindow">Delivery window, or null to inherit the scheduler default.</param>
    /// <param name="ct">Cancellation token for the scheduling request.</param>
    /// <exception cref="NotSupportedException">The client does not support the requested policy.</exception>
    public static Task<ReminderProtocol.ReminderScheduled> ScheduleSingleReminderAsync(
        this IReminderClient client, ReminderKey key, DateTimeOffset when, object message,
        ReminderRetryPolicy retryPolicy, TimeSpan? maxDeliveryWindow = null, CancellationToken ct = default)
    {
        if (retryPolicy == ReminderRetryPolicy.UseConfiguredAttemptLimit)
            return client.ScheduleSingleReminderAsync(key, when, message, maxDeliveryWindow, ct);
        if (retryPolicy != ReminderRetryPolicy.UntilAcknowledged)
            throw new ArgumentOutOfRangeException(nameof(retryPolicy));
        if (client is not IReminderRetryPolicyClient capable)
            throw new NotSupportedException("This reminder client does not support retry policies.");
        return capable.ScheduleSingleReminderAsync(key, when, message, retryPolicy, maxDeliveryWindow, ct);
    }
}
