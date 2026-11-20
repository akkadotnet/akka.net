using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Util;

namespace Akka.Reminders;

/// <summary>
/// <see cref="IExtension"/> for creating <see cref="IReminderClient"/> instances
/// that communicate with the reminder scheduler singleton.
/// </summary>
public sealed class ReminderClientExtension : IExtension
{
    private readonly ExtendedActorSystem _system;
    private readonly Lazy<IActorRef> _schedulerProxy;

    public ReminderClientExtension(ExtendedActorSystem system)
    {
        _system = system;

        // Lazy initialization of the scheduler proxy
        // This allows the extension to be created before WithReminders() completes
        // and provides a clear error message if WithReminders() was never called
        _schedulerProxy = new Lazy<IActorRef>(() =>
        {
            var registry = ActorRegistry.For(_system);
            if (!registry.TryGet<ReminderSchedulerProxy>(out var proxy))
            {
                throw new ConfigurationException(
                    "ReminderClientExtension requires WithReminders() to be called during ActorSystem configuration. " +
                    "The ReminderSchedulerProxy was not found in the ActorRegistry. " +
                    "Ensure you call builder.WithReminders() before using the reminder client.");
            }
            return proxy;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Creates a new <see cref="IReminderClient"/> for the specified entity.
    /// </summary>
    /// <param name="entity">The entity that will be scheduling reminders.</param>
    /// <returns>A client instance bound to the specified entity.</returns>
    public IReminderClient CreateClient(ReminderEntity entity)
    {
        // Proxy access is lazy - only fails here if WithReminders() wasn't called
        return new ReminderClient(_schedulerProxy.Value, entity);
    }

    /// <summary>
    /// Creates a new <see cref="IReminderClient"/> for the specified shard region and entity ID.
    /// </summary>
    /// <param name="shardRegionName">The name of the shard region.</param>
    /// <param name="entityId">The entity identifier.</param>
    /// <returns>A client instance bound to the specified entity.</returns>
    public IReminderClient CreateClient(string shardRegionName, string entityId)
    {
        return CreateClient(new ReminderEntity(shardRegionName, entityId));
    }

    /// <summary>
    /// Longer timeout applied specifically to ack operations. The scheduler's ack handler performs
    /// a storage write that may take up to <c>StorageTimeout</c> (default 5 s) to complete.
    /// Using the same 5-second default would race against that write and cause the caller to
    /// misreport a successful ack as a timeout failure.
    /// </summary>
    private static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Helper method to send a command to the scheduler proxy with error handling.
    /// Uses <paramref name="timeout"/> for the Ask call; defaults to 5 seconds when not specified.
    /// </summary>
    private async Task<TResponse> SendToSchedulerAsync<TCommand, TResponse>(
        TCommand command,
        CancellationToken ct,
        Func<string, TResponse> errorFactory,
        TimeSpan? timeout = null)
    {
        try
        {
            var response = await _schedulerProxy.Value.Ask<TResponse>(
                command,
                timeout ?? TimeSpan.FromSeconds(5),
                ct);

            return response;
        }
        catch (AskTimeoutException)
        {
            return errorFactory("Request timed out while communicating with reminder scheduler");
        }
        catch (Exception ex)
        {
            return errorFactory($"Error communicating with reminder scheduler: {ex.Message}");
        }
    }

    /// <summary>
    /// Schedules a single reminder for the specified entity without creating a client.
    /// </summary>
    /// <param name="entity">The entity that will receive the reminder.</param>
    /// <param name="key">The unique key for this reminder.</param>
    /// <param name="when">When the reminder should fire.</param>
    /// <param name="message">The message to deliver when the reminder fires.</param>
    /// <param name="maxDeliveryWindow">Optional maximum amount of time the reminder occurrence remains actionable after its due time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the reminder is scheduled.</returns>
    /// <remarks>
    /// This is a convenience method for scheduling reminders without creating a client first.
    /// Useful for bulk scheduling operations where you don't need to retain a client instance.
    /// </remarks>
    public Task<ReminderProtocol.ReminderScheduled> ScheduleSingleReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset when,
        object message,
        TimeSpan? maxDeliveryWindow = null,
        CancellationToken ct = default)
    {
        var command = new ReminderProtocol.ScheduleReminder(
            entity,
            key,
            when,
            message,
            RepeatInterval: null,
            MaxDeliveryWindow: maxDeliveryWindow);
        return SendToSchedulerAsync<ReminderProtocol.ScheduleReminder, ReminderProtocol.ReminderScheduled>(
            command,
            ct,
            errorMessage => new ReminderProtocol.ReminderScheduled(
                command,
                ReminderScheduleResponseCode.Error,
                errorMessage));
    }

    /// <summary>
    /// Schedules a single reminder for the specified shard region and entity ID without creating a client.
    /// </summary>
    /// <param name="shardRegionName">The name of the shard region.</param>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="key">The unique key for this reminder.</param>
    /// <param name="when">When the reminder should fire.</param>
    /// <param name="message">The message to deliver when the reminder fires.</param>
    /// <param name="maxDeliveryWindow">Optional maximum amount of time the reminder occurrence remains actionable after its due time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the reminder is scheduled.</returns>
    /// <remarks>
    /// This is a convenience method for scheduling reminders without creating a client first.
    /// Useful for bulk scheduling operations where you don't need to retain a client instance.
    /// </remarks>
    public Task<ReminderProtocol.ReminderScheduled> ScheduleSingleReminderAsync(
        string shardRegionName,
        string entityId,
        ReminderKey key,
        DateTimeOffset when,
        object message,
        TimeSpan? maxDeliveryWindow = null,
        CancellationToken ct = default)
    {
        return ScheduleSingleReminderAsync(
            new ReminderEntity(shardRegionName, entityId),
            key,
            when,
            message,
            maxDeliveryWindow,
            ct);
    }

    /// <summary>
    /// Schedules a recurring reminder for the specified entity without creating a client.
    /// </summary>
    /// <param name="entity">The entity that will receive the reminder.</param>
    /// <param name="key">The unique key for this reminder.</param>
    /// <param name="firstOccurrence">When the reminder should fire for the first time.</param>
    /// <param name="interval">The interval between recurring reminders.</param>
    /// <param name="message">The message to deliver when the reminder fires.</param>
    /// <param name="maxDeliveryWindow">Optional maximum amount of time each recurring occurrence remains actionable after its due time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the reminder is scheduled.</returns>
    /// <remarks>
    /// This is a convenience method for scheduling recurring reminders without creating a client first.
    /// Useful for bulk scheduling operations where you don't need to retain a client instance.
    /// </remarks>
    public Task<ReminderProtocol.ReminderScheduled> ScheduleRecurringReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset firstOccurrence,
        TimeSpan interval,
        object message,
        TimeSpan? maxDeliveryWindow = null,
        CancellationToken ct = default)
    {
        var command = new ReminderProtocol.ScheduleReminder(
            entity,
            key,
            firstOccurrence,
            message,
            RepeatInterval: interval,
            MaxDeliveryWindow: maxDeliveryWindow);
        return SendToSchedulerAsync<ReminderProtocol.ScheduleReminder, ReminderProtocol.ReminderScheduled>(
            command,
            ct,
            errorMessage => new ReminderProtocol.ReminderScheduled(
                command,
                ReminderScheduleResponseCode.Error,
                errorMessage));
    }

    /// <summary>
    /// Schedules a recurring reminder for the specified shard region and entity ID without creating a client.
    /// </summary>
    /// <param name="shardRegionName">The name of the shard region.</param>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="key">The unique key for this reminder.</param>
    /// <param name="firstOccurrence">When the reminder should fire for the first time.</param>
    /// <param name="interval">The interval between recurring reminders.</param>
    /// <param name="message">The message to deliver when the reminder fires.</param>
    /// <param name="maxDeliveryWindow">Optional maximum amount of time each recurring occurrence remains actionable after its due time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the reminder is scheduled.</returns>
    /// <remarks>
    /// This is a convenience method for scheduling recurring reminders without creating a client first.
    /// Useful for bulk scheduling operations where you don't need to retain a client instance.
    /// </remarks>
    public Task<ReminderProtocol.ReminderScheduled> ScheduleRecurringReminderAsync(
        string shardRegionName,
        string entityId,
        ReminderKey key,
        DateTimeOffset firstOccurrence,
        TimeSpan interval,
        object message,
        TimeSpan? maxDeliveryWindow = null,
        CancellationToken ct = default)
    {
        return ScheduleRecurringReminderAsync(
            new ReminderEntity(shardRegionName, entityId),
            key,
            firstOccurrence,
            interval,
            message,
            maxDeliveryWindow,
            ct);
    }

    /// <summary>
    /// Acknowledges receipt of a reminder without creating a client.
    /// </summary>
    /// <param name="envelope">The envelope received when the reminder fired.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A task containing the scheduler's acknowledgement response.
    /// Check <see cref="ReminderProtocol.ReminderAckResponse.ResponseCode"/> to determine success.
    /// If this Task faults or times out, a duplicate delivery may occur.
    /// </returns>
    public Task<ReminderProtocol.ReminderAckResponse> AckAsync(
        ReminderEnvelope envelope,
        CancellationToken ct = default)
    {
        var command = new ReminderProtocol.ReminderAck(envelope.Entity, envelope.Key, envelope.DueTimeUtc);
        return SendToSchedulerAsync<ReminderProtocol.ReminderAck, ReminderProtocol.ReminderAckResponse>(
            command,
            ct,
            errorMessage => new ReminderProtocol.ReminderAckResponse(
                envelope.Entity,
                envelope.Key,
                envelope.DueTimeUtc,
                ReminderAckResponseCode.Error,
                errorMessage),
            AckTimeout);
    }

    /// <summary>
    /// Gets the <see cref="ReminderClientExtension"/> for the specified actor system.
    /// This is the standard pattern for accessing Akka.NET extensions.
    /// </summary>
    /// <param name="system">The actor system.</param>
    /// <returns>The reminder client extension instance.</returns>
    public static ReminderClientExtension Get(ActorSystem system)
    {
        return system.WithExtension<ReminderClientExtension, ReminderClientProvider>();
    }
}

/// <summary>
/// <see cref="ExtensionIdProvider{T}"/> for <see cref="ReminderClientExtension"/>.
/// </summary>
public sealed class ReminderClientProvider : ExtensionIdProvider<ReminderClientExtension>
{
    public override ReminderClientExtension CreateExtension(ExtendedActorSystem system)
    {
        return new ReminderClientExtension(system);
    }
}

/// <summary>
/// Convenient extension methods for accessing <see cref="ReminderClientExtension"/>.
/// These provide alternative syntax to the standard Get() method.
/// </summary>
public static class ReminderClientExtensions
{
    /// <summary>
    /// Gets the <see cref="ReminderClientExtension"/> for the specified actor system.
    /// Alternative to <see cref="ReminderClientExtension.Get"/>.
    /// </summary>
    public static ReminderClientExtension ReminderClient(this ActorSystem system)
    {
        return ReminderClientExtension.Get(system);
    }

    /// <summary>
    /// Gets the <see cref="ReminderClientExtension"/> for the specified actor context.
    /// </summary>
    public static ReminderClientExtension ReminderClient(this IActorContext context)
    {
        return ReminderClientExtension.Get(context.System);
    }
}
