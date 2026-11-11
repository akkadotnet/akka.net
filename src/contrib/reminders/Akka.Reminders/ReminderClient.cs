using Akka.Actor;
using Akka.Util;

namespace Akka.Reminders;

/// <summary>
/// Concrete implementation of <see cref="IReminderClient"/> that communicates with
/// the reminder scheduler singleton via a ClusterSingletonProxy.
/// </summary>
internal sealed class ReminderClient : IReminderClient
{
    private readonly IActorRef _schedulerProxy;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Longer timeout for ack operations. The scheduler's ack handler performs a storage write
    /// that may take up to <c>StorageTimeout</c> (default 5 s) to complete. Using the same
    /// 5-second default as <see cref="_defaultTimeout"/> would race against that write and cause
    /// the caller to misreport a successful ack as a timeout failure.
    /// </summary>
    private readonly TimeSpan _ackTimeout = TimeSpan.FromSeconds(15);

    public ReminderClient(IActorRef schedulerProxy, ReminderEntity entity)
    {
        _schedulerProxy = schedulerProxy;
        Entity = entity;
    }

    /// <inheritdoc />
    public ReminderEntity Entity { get; }

    /// <inheritdoc />
    public async Task<ReminderProtocol.ReminderScheduled> ScheduleSingleReminderAsync(
        ReminderKey key,
        DateTimeOffset when,
        object message,
        TimeSpan? maxDeliveryWindow = null,
        CancellationToken ct = default)
    {
        var command = new ReminderProtocol.ScheduleReminder(
            Entity,
            key,
            when,
            message,
            RepeatInterval: null,
            MaxDeliveryWindow: maxDeliveryWindow);

        try
        {
            var response = await _schedulerProxy.Ask<ReminderProtocol.ReminderScheduled>(
                command,
                _defaultTimeout,
                ct);

            return response;
        }
        catch (AskTimeoutException)
        {
            // Return a timeout error response
            return new ReminderProtocol.ReminderScheduled(
                command,
                ReminderScheduleResponseCode.Error,
                "Request timed out while communicating with reminder scheduler");
        }
        catch (Exception ex)
        {
            // Return a generic error response
            return new ReminderProtocol.ReminderScheduled(
                command,
                ReminderScheduleResponseCode.Error,
                $"Error scheduling reminder: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ReminderProtocol.ReminderScheduled> ScheduleRecurringReminderAsync(
        ReminderKey key,
        DateTimeOffset firstOccurrence,
        TimeSpan interval,
        object message,
        TimeSpan? maxDeliveryWindow = null,
        CancellationToken ct = default)
    {
        var command = new ReminderProtocol.ScheduleReminder(
            Entity,
            key,
            firstOccurrence,
            message,
            RepeatInterval: interval,
            MaxDeliveryWindow: maxDeliveryWindow);

        try
        {
            var response = await _schedulerProxy.Ask<ReminderProtocol.ReminderScheduled>(
                command,
                _defaultTimeout,
                ct);

            return response;
        }
        catch (AskTimeoutException)
        {
            return new ReminderProtocol.ReminderScheduled(
                command,
                ReminderScheduleResponseCode.Error,
                "Request timed out while communicating with reminder scheduler");
        }
        catch (Exception ex)
        {
            return new ReminderProtocol.ReminderScheduled(
                command,
                ReminderScheduleResponseCode.Error,
                $"Error scheduling recurring reminder: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ReminderProtocol.RemindersCancelled> CancelReminderAsync(
        ReminderKey key,
        CancellationToken ct = default)
    {
        var command = new ReminderProtocol.CancelReminder(Entity, key);

        try
        {
            var response = await _schedulerProxy.Ask<ReminderProtocol.RemindersCancelled>(
                command,
                _defaultTimeout,
                ct);

            return response;
        }
        catch (AskTimeoutException)
        {
            return new ReminderProtocol.RemindersCancelled(
                Entity,
                ReminderCancelResponseCode.Error,
                [],
                "Request timed out while communicating with reminder scheduler");
        }
        catch (Exception ex)
        {
            return new ReminderProtocol.RemindersCancelled(
                Entity,
                ReminderCancelResponseCode.Error,
                [],
                $"Error canceling reminder: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ReminderProtocol.RemindersCancelled> CancelAllRemindersAsync(
        CancellationToken ct = default)
    {
        var command = new ReminderProtocol.CancelAllReminders(Entity);

        try
        {
            var response = await _schedulerProxy.Ask<ReminderProtocol.RemindersCancelled>(
                command,
                _defaultTimeout,
                ct);

            return response;
        }
        catch (AskTimeoutException)
        {
            return new ReminderProtocol.RemindersCancelled(
                Entity,
                ReminderCancelResponseCode.Error,
                [],
                "Request timed out while communicating with reminder scheduler");
        }
        catch (Exception ex)
        {
            return new ReminderProtocol.RemindersCancelled(
                Entity,
                ReminderCancelResponseCode.Error,
                [],
                $"Error canceling all reminders: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ReminderProtocol.RemindersForEntity> ListRemindersAsync(
        CancellationToken ct = default)
    {
        var query = new ReminderProtocol.GetReminders(Entity);

        try
        {
            var response = await _schedulerProxy.Ask<ReminderProtocol.RemindersForEntity>(
                query,
                _defaultTimeout,
                ct);

            return response;
        }
        catch (AskTimeoutException)
        {
            return new ReminderProtocol.RemindersForEntity(
                Entity,
                FetchRemindersResponseCode.Error,
                [],
                "Request timed out while communicating with reminder scheduler");
        }
        catch (Exception ex)
        {
            return new ReminderProtocol.RemindersForEntity(
                Entity,
                FetchRemindersResponseCode.Error,
                [],
                $"Error fetching reminders: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ReminderProtocol.ReminderAckResponse> AckAsync(
        ReminderEnvelope envelope,
        CancellationToken ct = default)
    {
        EnsureEnvelopeOwnership(envelope);
        var command = new ReminderProtocol.ReminderAck(envelope.Entity, envelope.Key, envelope.DueTimeUtc);

        try
        {
            var response = await _schedulerProxy.Ask<ReminderProtocol.ReminderAckResponse>(
                command, _ackTimeout, ct);

            return response;
        }
        catch (AskTimeoutException)
        {
            return new ReminderProtocol.ReminderAckResponse(
                envelope.Entity,
                envelope.Key,
                envelope.DueTimeUtc,
                ReminderAckResponseCode.Error,
                "Request timed out while acknowledging reminder");
        }
        catch (Exception ex)
        {
            return new ReminderProtocol.ReminderAckResponse(
                envelope.Entity,
                envelope.Key,
                envelope.DueTimeUtc,
                ReminderAckResponseCode.Error,
                $"Error acknowledging reminder: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ReminderProtocol.ReminderNackResponse> NackAsync(
        ReminderEnvelope envelope,
        string reason,
        CancellationToken ct = default)
    {
        EnsureEnvelopeOwnership(envelope);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A negative acknowledgement requires a failure reason.", nameof(reason));

        var command = new ReminderProtocol.ReminderNack(
            envelope.Entity,
            envelope.Key,
            envelope.DueTimeUtc,
            reason);

        try
        {
            return await _schedulerProxy.Ask<ReminderProtocol.ReminderNackResponse>(
                command, _ackTimeout, ct);
        }
        catch (AskTimeoutException)
        {
            return new ReminderProtocol.ReminderNackResponse(
                envelope.Entity,
                envelope.Key,
                envelope.DueTimeUtc,
                ReminderNackResponseCode.Error,
                AttemptCount: 0,
                Message: "Request timed out while rejecting reminder");
        }
        catch (Exception ex)
        {
            return new ReminderProtocol.ReminderNackResponse(
                envelope.Entity,
                envelope.Key,
                envelope.DueTimeUtc,
                ReminderNackResponseCode.Error,
                AttemptCount: 0,
                Message: $"Error rejecting reminder: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ReminderProtocol.ReminderOccurrenceStatusResponse> GetOccurrenceStatusAsync(
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        CancellationToken ct = default)
    {
        var query = new ReminderProtocol.GetReminderOccurrenceStatus(Entity, key, dueTimeUtc);

        try
        {
            return await _schedulerProxy.Ask<ReminderProtocol.ReminderOccurrenceStatusResponse>(
                query, _defaultTimeout, ct);
        }
        catch (AskTimeoutException)
        {
            return new ReminderProtocol.ReminderOccurrenceStatusResponse(
                Entity,
                key,
                dueTimeUtc,
                ReminderOccurrenceStatusResponseCode.Error,
                Message: "Request timed out while fetching reminder occurrence status");
        }
        catch (Exception ex)
        {
            return new ReminderProtocol.ReminderOccurrenceStatusResponse(
                Entity,
                key,
                dueTimeUtc,
                ReminderOccurrenceStatusResponseCode.Error,
                Message: $"Error fetching reminder occurrence status: {ex.Message}");
        }
    }

    private void EnsureEnvelopeOwnership(ReminderEnvelope envelope)
    {
        if (envelope.Entity != Entity)
        {
            throw new ArgumentException(
                $"The reminder envelope belongs to [{envelope.Entity}], but this client belongs to [{Entity}].",
                nameof(envelope));
        }
    }
}
