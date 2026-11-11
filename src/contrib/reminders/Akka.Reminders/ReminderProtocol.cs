using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Reminders.Storage;

namespace Akka.Reminders;

/// <summary>
/// Absolute UTC deadline metadata attached to a delivered reminder occurrence.
/// </summary>
public readonly record struct ReminderDeadline
{
    public static ReminderDeadline Infinite => new(DateTimeOffset.MaxValue);

    public ReminderDeadline(DateTimeOffset utcDateTime)
    {
        UtcDateTime = utcDateTime.ToUniversalTime();
    }

    /// <summary>
    /// Absolute UTC timestamp after which the reminder occurrence is stale.
    /// </summary>
    public DateTimeOffset UtcDateTime { get; }

    /// <summary>
    /// Returns <c>true</c> when this deadline never expires.
    /// </summary>
    public bool IsInfinite => UtcDateTime == DateTimeOffset.MaxValue;

    /// <summary>
    /// Returns <c>true</c> when the supplied time is at or beyond the deadline.
    /// </summary>
    public bool IsExpired(DateTimeOffset now) => !IsInfinite && now >= UtcDateTime;

    /// <summary>
    /// Returns <c>true</c> when the current UTC time is at or beyond the deadline.
    /// </summary>
    public bool IsExpired() => IsExpired(DateTimeOffset.UtcNow);

    /// <summary>
    /// Returns the remaining time until the deadline.
    /// </summary>
    public TimeSpan TimeRemaining(DateTimeOffset now) => IsInfinite ? TimeSpan.MaxValue : UtcDateTime - now;

    /// <summary>
    /// Returns the remaining time until the deadline using the current UTC time.
    /// </summary>
    public TimeSpan TimeRemaining() => TimeRemaining(DateTimeOffset.UtcNow);
}

/// <summary>
/// Marker interface for all reminder messages that need to cross node boundaries
/// via Akka.Remote / Akka.Cluster. Used to bind the <see cref="Serialization.ReminderSerializer"/>
/// to all wire-visible types in a single registration.
/// </summary>
public interface IReminderWireMessage;

/// <summary>
/// Wraps a reminder message with its originating entity, key, and occurrence metadata,
/// allowing recipients to acknowledge delivery via <see cref="IReminderClient.AckAsync"/>.
/// </summary>
public class ReminderEnvelope : IWrappedMessage, IReminderWireMessage
{
    /// <summary>
    /// The entity that scheduled this reminder.
    /// </summary>
    public ReminderEntity Entity { get; }

    /// <summary>
    /// The unique key identifying this reminder for the entity.
    /// </summary>
    public ReminderKey Key { get; }

    /// <summary>
    /// The original due time for this reminder occurrence in UTC.
    /// </summary>
    public DateTimeOffset DueTimeUtc { get; }

    /// <summary>
    /// Deadline indicating how long this delivery attempt is relevant.
    /// When another retry is possible, this equals the ack timeout for this attempt.
    /// When this is the final attempt, this equals the occurrence-level delivery deadline.
    /// Unbounded final attempts use <see cref="ReminderDeadline.Infinite"/>.
    /// </summary>
    public ReminderDeadline Deadline { get; }

    /// <summary>
    /// The payload that was originally scheduled.
    /// </summary>
    public object Message { get; }

    /// <summary>
    /// Creates a new <see cref="ReminderEnvelope"/>.
    /// </summary>
    /// <param name="entity">The entity that scheduled this reminder.</param>
    /// <param name="key">The unique key identifying this reminder.</param>
    /// <param name="dueTimeUtc">The original due time for this reminder occurrence.</param>
    /// <param name="deadline">The absolute delivery deadline for this reminder occurrence.</param>
    /// <param name="message">The payload to deliver.</param>
    public ReminderEnvelope(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        ReminderDeadline deadline,
        object message)
    {
        Entity = entity;
        Key = key;
        DueTimeUtc = dueTimeUtc.ToUniversalTime();
        Deadline = deadline;
        Message = message;
    }
}

/// <summary>
/// Strongly-typed wrapper for a reminder message, providing compile-time access to the payload type.
/// </summary>
/// <typeparam name="T">The type of the reminder payload.</typeparam>
public sealed class ReminderEnvelope<T> : ReminderEnvelope
{
    /// <summary>
    /// The strongly-typed payload that was originally scheduled.
    /// </summary>
    public new T Message { get; }

    /// <summary>
    /// Creates a new <see cref="ReminderEnvelope{T}"/>.
    /// </summary>
    /// <param name="entity">The entity that scheduled this reminder.</param>
    /// <param name="key">The unique key identifying this reminder.</param>
    /// <param name="dueTimeUtc">The original due time for this reminder occurrence.</param>
    /// <param name="deadline">The absolute delivery deadline for this reminder occurrence.</param>
    /// <param name="message">The strongly-typed payload to deliver.</param>
    public ReminderEnvelope(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        ReminderDeadline deadline,
        T message)
        : base(entity, key, dueTimeUtc, deadline, message!)
    {
        Message = message;
    }
}

public interface IReminderProtocol
{
    /// <summary>
    /// Which entity to send the reminder message to.
    /// </summary>
    public ReminderEntity Entity { get; }
}

public interface IReminderCommand : IReminderProtocol;

public interface IReminderQuery : IReminderProtocol;

public interface IReminderResponse : IReminderProtocol;

public enum ReminderCancelResponseCode
{
    /// <summary>
    /// Found and canceled the reminder(s)
    /// </summary>
    Success = 0,

    /// <summary>
    /// No reminders were found to cancel
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// An error occurred while attempting to cancel the reminder(s)
    /// </summary>
    Error = 2
}

public enum ReminderScheduleResponseCode
{
    /// <summary>
    /// Scheduled a new reminder for the entity with the given key.
    /// If a reminder with the same key already existed, it was overwritten.
    /// </summary>
    Success = 0,

    /// <summary>
    /// The entity type was not found.
    /// </summary>
    /// <remarks>
    /// Means the ShardRegion for the entity type was not found and thus this is likely a configuration error.
    /// </remarks>
    ShardRegionNotFound = 1,

    /// <summary>
    /// An error occurred while attempting to schedule the reminder.
    /// </summary>
    Error = 2,
}

public enum FetchRemindersResponseCode
{
    Success = 0,
    Error = 1,
    NotFound = 2,
}

/// <summary>
/// Response codes for a reminder acknowledgement operation.
/// </summary>
public enum ReminderAckResponseCode
{
    /// <summary>
    /// The reminder was found and successfully acknowledged.
    /// </summary>
    Success = 0,

    /// <summary>
    /// No reminder was found matching the entity and key.
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// An error occurred while attempting to acknowledge the reminder.
    /// </summary>
    Error = 2
}

/// <summary>
/// Response codes for a negative acknowledgement operation.
/// </summary>
public enum ReminderNackResponseCode
{
    RetryScheduled = 0,
    Failed = 1,
    Expired = 2,
    NotFound = 3,
    Error = 4
}

/// <summary>
/// Response codes for an occurrence status query.
/// </summary>
public enum ReminderOccurrenceStatusResponseCode
{
    Success = 0,
    NotFound = 1,
    Error = 2
}

public static class ReminderProtocol
{
    public sealed record ScheduleReminder(
        ReminderEntity Entity,
        ReminderKey Key,
        DateTimeOffset When,
        object Message,
        TimeSpan? RepeatInterval = null,
        TimeSpan? MaxDeliveryWindow = null) : IReminderCommand, IReminderWireMessage
    {
        public ScheduledReminder ToScheduledReminder() => new(
            Entity,
            Key,
            When,
            Message,
            RepeatInterval,
            MaxDeliveryWindow: MaxDeliveryWindow);
    }

    public sealed record CancelReminder(ReminderEntity Entity, ReminderKey Key) : IReminderCommand, IReminderWireMessage;

    public sealed record CancelAllReminders(ReminderEntity Entity) : IReminderCommand, IReminderWireMessage;

    public sealed record RemindersCancelled(
        ReminderEntity Entity,
        ReminderCancelResponseCode ResponseCode,
        IReadOnlyList<ReminderKey> Keys,
        string? Message = null) : IReminderResponse, IReminderWireMessage;

    public sealed record ReminderScheduled(
        ScheduleReminder OriginalCommand,
        ReminderScheduleResponseCode ResponseCode,
        string? Message = null) : IReminderResponse, IReminderWireMessage
    {
        /// <inheritdoc />
        public ReminderEntity Entity => OriginalCommand.Entity;

        /// <summary>
        /// The key of the reminder that was scheduled.
        /// </summary>
        public ReminderKey Key => OriginalCommand.Key;

        /// <summary>
        /// When the reminder is scheduled to fire.
        /// </summary>
        public DateTimeOffset When => OriginalCommand.When;
    }

    public sealed record GetReminders(ReminderEntity Entity) : IReminderQuery, IReminderWireMessage;

    public sealed record RemindersForEntity(
        ReminderEntity Entity,
        FetchRemindersResponseCode ResponseCode,
        IReadOnlyList<ScheduledReminder> Reminders,
        string? Message = null) : IReminderResponse, IReminderWireMessage;

    /// <summary>
    /// Sent by a recipient to confirm that a reminder has been successfully processed.
    /// Prevents duplicate delivery for at-least-once reminders.
    /// </summary>
    public sealed record ReminderAck(
        ReminderEntity Entity,
        ReminderKey Key,
        DateTimeOffset DueTimeUtc) : IReminderCommand, IReminderWireMessage;

    /// <summary>
    /// Returned by the scheduler after processing a <see cref="ReminderAck"/>.
    /// </summary>
    public sealed record ReminderAckResponse(
        ReminderEntity Entity,
        ReminderKey Key,
        DateTimeOffset DueTimeUtc,
        ReminderAckResponseCode ResponseCode,
        string? Message = null) : IReminderResponse, IReminderWireMessage;

    /// <summary>
    /// Reports a failed reminder delivery attempt.
    /// </summary>
    public sealed record ReminderNack(
        ReminderEntity Entity,
        ReminderKey Key,
        DateTimeOffset DueTimeUtc,
        string Reason) : IReminderCommand, IReminderWireMessage;

    /// <summary>
    /// Returns the retry or terminal result for a negative acknowledgement.
    /// </summary>
    public sealed record ReminderNackResponse(
        ReminderEntity Entity,
        ReminderKey Key,
        DateTimeOffset DueTimeUtc,
        ReminderNackResponseCode ResponseCode,
        int AttemptCount,
        DateTimeOffset? NextAttemptAtUtc = null,
        string? Message = null) : IReminderResponse, IReminderWireMessage;

    /// <summary>
    /// Queries one reminder occurrence by its durable identity.
    /// </summary>
    public sealed record GetReminderOccurrenceStatus(
        ReminderEntity Entity,
        ReminderKey Key,
        DateTimeOffset DueTimeUtc) : IReminderQuery, IReminderWireMessage;

    /// <summary>
    /// Returns the durable state for one reminder occurrence.
    /// </summary>
    public sealed record ReminderOccurrenceStatusResponse(
        ReminderEntity Entity,
        ReminderKey Key,
        DateTimeOffset DueTimeUtc,
        ReminderOccurrenceStatusResponseCode ResponseCode,
        ReminderOccurrenceStatus? Status = null,
        string? Message = null) : IReminderResponse, IReminderWireMessage;
}

/// <summary>
/// A unique identifier for a reminder, scoped to a <see cref="ReminderEntity"/>
/// </summary>
/// <param name="Name">An arbitrary name for this reminder.</param>
public readonly record struct ReminderKey(string Name);

/// <summary>
/// Tells the reminder system which ShardRegion to use for the reminder.
/// </summary>
/// <param name="ShardRegionName">The name of the entity type - this is part of the ShardRegion's configuration.</param>
/// <param name="EntityId">The id of the entity performing the scheduling.</param>
public readonly record struct ReminderEntity(string ShardRegionName, string EntityId);

/// <summary>
/// Durable status for one reminder occurrence.
/// </summary>
public sealed record ReminderOccurrenceStatus(
    ReminderEntity Entity,
    ReminderKey Key,
    DateTimeOffset DueTimeUtc,
    DateTimeOffset? NextAttemptAtUtc,
    int AttemptCount,
    string? LastFailureReason,
    ReminderCompletionStatus CompletionStatus,
    DateTimeOffset? DeliveryDeadlineUtc = null,
    DateTimeOffset? DeliveredAtUtc = null,
    DateTimeOffset? AckDeadlineUtc = null,
    DateTimeOffset? CompletedAtUtc = null);

/// <summary>
/// Represents a scheduled reminder to be executed in the future.
/// </summary>
/// <remarks>
/// <see cref="Message"/> will be sent to the <see cref="Entity"/> when the reminder is executed.
/// </remarks>
/// <param name="Entity">The entity identifier.</param>
/// <param name="Key">The identifier for this specific reminder for this entity.</param>
/// <param name="When">When the next delivery attempt for this occurrence should happen.</param>
/// <param name="Message">The payload to be delivered to <see cref="Entity"/>.
///
/// This will be serialized using the configured binary serialization available
/// for this type in Akka.NET and stored using the (serializerId, manifest) scheme that
/// Akka.Persistence also uses.</param>
/// <param name="RepeatInterval">If specified, this reminder will automatically create a new occurrence after delivery at the configured interval.</param>
/// <param name="AttemptCount">Number of delivery attempts made for this reminder. Starts at 0, increments on each retry.</param>
/// <param name="LastFailureReason">If the previous delivery attempt failed, contains the reason. Null if no failures or first attempt.</param>
/// <param name="MaxDeliveryWindow">Optional maximum amount of time this occurrence remains actionable after its due time.</param>
/// <param name="DeliveryDeadlineUtc">Absolute UTC deadline after which this occurrence is stale.</param>
/// <param name="OccurrenceDueTimeUtc">Original due time for this occurrence. Null means <paramref name="When"/> is the due time.</param>
public sealed record ScheduledReminder(
    ReminderEntity Entity,
    ReminderKey Key,
    DateTimeOffset When,
    object Message,
    TimeSpan? RepeatInterval = null,
    int AttemptCount = 0,
    string? LastFailureReason = null,
    TimeSpan? MaxDeliveryWindow = null,
    DateTimeOffset? DeliveryDeadlineUtc = null,
    DateTimeOffset? OccurrenceDueTimeUtc = null)
{
    /// <summary>
    /// The original due time for this occurrence in UTC.
    /// </summary>
    public DateTimeOffset DueTimeUtc => (OccurrenceDueTimeUtc ?? When).ToUniversalTime();

    /// <summary>
    /// Strongly-typed deadline metadata for this occurrence, if one exists.
    /// </summary>
    public ReminderDeadline Deadline => DeliveryDeadlineUtc.HasValue
        ? new ReminderDeadline(DeliveryDeadlineUtc.Value)
        : ReminderDeadline.Infinite;

    /// <summary>
     /// Converts this scheduled reminder back to a <see cref="ReminderProtocol.ScheduleReminder"/> command.
     /// Useful for retry scenarios where you need to resubmit the original command.
     /// </summary>
    public ReminderProtocol.ScheduleReminder ToScheduleReminder()
        => new(Entity, Key, DueTimeUtc, Message, RepeatInterval, MaxDeliveryWindow);
}
