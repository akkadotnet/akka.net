using System.Text;
using Akka.Actor;
using Akka.Reminders.Storage;
using Akka.Serialization;

namespace Akka.Reminders.Serialization;

/// <summary>
/// Custom Akka serializer for all <see cref="IReminderWireMessage"/> types. Handles cross-node
/// serialization for Akka.Remote and Akka.Cluster deployments. Registered via
/// <c>WithCustomSerializer</c> in <see cref="AkkaHostingExtensions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Uses a hand-rolled binary format via <see cref="BinaryWriter"/>/<see cref="BinaryReader"/>.
/// Strings are length-prefixed UTF-8 (BinaryWriter's default string encoding). Timestamps are
/// stored as <c>Int64</c> UTC ticks. The inner message payload is delegated to Akka's own
/// serialization infrastructure, so whatever serializer the user has configured for their
/// message type handles that part.
/// </para>
///
/// <para><b>Wire format: ReminderEnvelope (manifest "re")</b></para>
/// <code>
/// ┌──────────────────────────────────────────────────────────────┐
/// │ ShardRegionName    : length-prefixed UTF-8 string           │
/// │ EntityId           : length-prefixed UTF-8 string           │
/// │ Key.Name           : length-prefixed UTF-8 string           │
/// │ DueTimeUtc         : Int64 (UTC ticks)                      │
/// │ Deadline           : Int64 (UTC ticks)                      │
/// │ InnerSerializerId  : Int32 (Akka serializer identifier)     │
/// │ InnerManifest      : length-prefixed UTF-8 string           │
/// │ InnerPayloadLength : Int32                                  │
/// │ InnerPayload       : byte[] (serialized by Akka)            │
/// └──────────────────────────────────────────────────────────────┘
/// </code>
///
/// <para><b>Wire format: ReminderAck (manifest "ra")</b></para>
/// <code>
/// ┌──────────────────────────────────────────────────────────────┐
/// │ ShardRegionName    : length-prefixed UTF-8 string           │
/// │ EntityId           : length-prefixed UTF-8 string           │
/// │ Key.Name           : length-prefixed UTF-8 string           │
/// │ DueTimeUtc         : Int64 (UTC ticks)                      │
/// └──────────────────────────────────────────────────────────────┘
/// </code>
///
/// <para><b>Wire format: ReminderAckResponse (manifest "rar")</b></para>
/// <code>
/// ┌──────────────────────────────────────────────────────────────┐
/// │ ShardRegionName    : length-prefixed UTF-8 string           │
/// │ EntityId           : length-prefixed UTF-8 string           │
/// │ Key.Name           : length-prefixed UTF-8 string           │
/// │ DueTimeUtc         : Int64 (UTC ticks)                      │
/// │ ResponseCode       : Int32 (enum)                           │
/// │ Message            : length-prefixed UTF-8 string (or "")   │
/// └──────────────────────────────────────────────────────────────┘
/// </code>
///
/// <para><b>Wire format: ScheduleReminder (manifest "sr")</b></para>
/// <code>
/// ┌──────────────────────────────────────────────────────────────┐
/// │ ShardRegionName      : length-prefixed UTF-8 string         │
/// │ EntityId             : length-prefixed UTF-8 string         │
/// │ Key.Name             : length-prefixed UTF-8 string         │
/// │ When                 : Int64 (UTC ticks)                    │
/// │ HasRepeatInterval    : bool                                 │
/// │ RepeatInterval       : Int64 (ticks) — only if present      │
/// │ HasMaxDeliveryWindow : bool                                 │
/// │ MaxDeliveryWindow    : Int64 (ticks) — only if present      │
/// │ InnerSerializerId    : Int32 (Akka serializer identifier)   │
/// │ InnerManifest        : length-prefixed UTF-8 string         │
/// │ InnerPayloadLength   : Int32                                │
/// │ InnerPayload         : byte[] (serialized by Akka)          │
/// └──────────────────────────────────────────────────────────────┘
/// </code>
///
/// <para><b>Wire format: ReminderScheduled (manifest "rsd")</b></para>
/// <code>
/// ┌──────────────────────────────────────────────────────────────┐
/// │ [ScheduleReminder fields — same layout as above]            │
/// │ ResponseCode         : Int32 (enum)                         │
/// │ Message              : length-prefixed UTF-8 string (or "") │
/// └──────────────────────────────────────────────────────────────┘
/// </code>
///
/// <para><b>Wire format: RemindersForEntity (manifest "rfe")</b></para>
/// <code>
/// ┌──────────────────────────────────────────────────────────────┐
/// │ ShardRegionName      : length-prefixed UTF-8 string         │
/// │ EntityId             : length-prefixed UTF-8 string         │
/// │ ResponseCode         : Int32 (enum)                         │
/// │ Message              : length-prefixed UTF-8 string (or "") │
/// │ ReminderCount        : Int32                                │
/// │ [For each ScheduledReminder:]                               │
/// │   Key.Name             : length-prefixed UTF-8 string       │
/// │   When                 : Int64 (UTC ticks)                  │
/// │   HasRepeatInterval    : bool                               │
/// │   RepeatInterval       : Int64 (ticks) — only if present    │
/// │   AttemptCount         : Int32                               │
/// │   HasLastFailureReason : bool                               │
/// │   LastFailureReason    : length-prefixed UTF-8 — only if    │
/// │   HasMaxDeliveryWindow : bool                               │
/// │   MaxDeliveryWindow    : Int64 (ticks) — only if present    │
/// │   HasDeliveryDeadline  : bool                               │
/// │   DeliveryDeadlineUtc  : Int64 (UTC ticks) — only if present│
/// │   HasOccurrenceDueTime : bool                               │
/// │   OccurrenceDueTimeUtc : Int64 (UTC ticks) — only if present│
/// │   InnerSerializerId    : Int32                               │
/// │   InnerManifest        : length-prefixed UTF-8 string       │
/// │   InnerPayloadLength   : Int32                               │
/// │   InnerPayload         : byte[]                              │
/// └──────────────────────────────────────────────────────────────┘
/// </code>
///
/// <para>
/// Deserialization of <see cref="ReminderEnvelope"/> reconstructs the strongly-typed
/// <see cref="ReminderEnvelope{T}"/> for the runtime type of the deserialized inner message.
/// Types registered through <see cref="ReminderMessageTypes"/> are built without reflection;
/// others fall back to reflection, which Native AOT does not support.
/// </para>
/// </remarks>
public sealed class ReminderSerializer : SerializerWithStringManifest
{
    private const string ReminderEnvelopeManifest = "re";
    private const string ReminderAckManifest = "ra";
    private const string ReminderAckResponseManifest = "rar";
    private const string ScheduleReminderManifest = "sr";
    private const string ReminderScheduledManifest = "rsd";
    private const string RemindersForEntityManifest = "rfe";
    private const string ReminderNackManifest = "rn";
    private const string ReminderNackResponseManifest = "rnr";
    private const string GetReminderOccurrenceStatusManifest = "rosq";
    private const string ReminderOccurrenceStatusResponseManifest = "rosr";
    private const string CancelReminderManifest = "cr";
    private const string CancelAllRemindersManifest = "car";
    private const string RemindersCancelledManifest = "rc";
    private const string GetRemindersManifest = "gr";

    private readonly ExtendedActorSystem _system;
    private Akka.Serialization.Serialization? _serialization;

    /// <summary>
    /// Creates a new <see cref="ReminderSerializer"/> bound to the given actor system.
    /// </summary>
    public ReminderSerializer(ExtendedActorSystem system) : base(system)
    {
        _system = system;
        // Lazy — we cannot create a new Serialization instance here because that would
        // re-instantiate all registered serializers including this one, causing a stack overflow.
    }

    private Akka.Serialization.Serialization SerializationSystem
        => _serialization ??= _system.Serialization;

    /// <inheritdoc />
    public override int Identifier => 22550;

    /// <inheritdoc />
    public override string Manifest(object o) => o switch
    {
        ReminderEnvelope => ReminderEnvelopeManifest,
        ReminderProtocol.ReminderAck => ReminderAckManifest,
        ReminderProtocol.ReminderAckResponse => ReminderAckResponseManifest,
        ReminderProtocol.ScheduleReminder => ScheduleReminderManifest,
        ReminderProtocol.ReminderScheduled => ReminderScheduledManifest,
        ReminderProtocol.RemindersForEntity => RemindersForEntityManifest,
        ReminderProtocol.ReminderNack => ReminderNackManifest,
        ReminderProtocol.ReminderNackResponse => ReminderNackResponseManifest,
        ReminderProtocol.GetReminderOccurrenceStatus => GetReminderOccurrenceStatusManifest,
        ReminderProtocol.ReminderOccurrenceStatusResponse => ReminderOccurrenceStatusResponseManifest,
        ReminderProtocol.CancelReminder => CancelReminderManifest,
        ReminderProtocol.CancelAllReminders => CancelAllRemindersManifest,
        ReminderProtocol.RemindersCancelled => RemindersCancelledManifest,
        ReminderProtocol.GetReminders => GetRemindersManifest,
        _ => throw new ArgumentException($"{nameof(ReminderSerializer)} does not support serializing [{o.GetType().FullName}]", nameof(o))
    };

    /// <inheritdoc />
    public override byte[] ToBinary(object obj) => obj switch
    {
        ReminderEnvelope envelope => SerializeReminderEnvelope(envelope),
        ReminderProtocol.ReminderAck ack => SerializeReminderAck(ack),
        ReminderProtocol.ReminderAckResponse ackResponse => SerializeReminderAckResponse(ackResponse),
        ReminderProtocol.ScheduleReminder cmd => SerializeScheduleReminder(cmd),
        ReminderProtocol.ReminderScheduled scheduled => SerializeReminderScheduled(scheduled),
        ReminderProtocol.RemindersForEntity reminders => SerializeRemindersForEntity(reminders),
        ReminderProtocol.ReminderNack nack => SerializeReminderNack(nack),
        ReminderProtocol.ReminderNackResponse nackResponse => SerializeReminderNackResponse(nackResponse),
        ReminderProtocol.GetReminderOccurrenceStatus query => SerializeGetReminderOccurrenceStatus(query),
        ReminderProtocol.ReminderOccurrenceStatusResponse statusResponse => SerializeReminderOccurrenceStatusResponse(statusResponse),
        ReminderProtocol.CancelReminder cancel => SerializeCancelReminder(cancel),
        ReminderProtocol.CancelAllReminders cancelAll => SerializeCancelAllReminders(cancelAll),
        ReminderProtocol.RemindersCancelled cancelled => SerializeRemindersCancelled(cancelled),
        ReminderProtocol.GetReminders getReminders => SerializeGetReminders(getReminders),
        _ => throw new ArgumentException($"{nameof(ReminderSerializer)} does not support serializing [{obj.GetType().FullName}]", nameof(obj))
    };

    /// <inheritdoc />
    public override object FromBinary(byte[] bytes, string manifest) => manifest switch
    {
        ReminderEnvelopeManifest => DeserializeReminderEnvelope(bytes),
        ReminderAckManifest => DeserializeReminderAck(bytes),
        ReminderAckResponseManifest => DeserializeReminderAckResponse(bytes),
        ScheduleReminderManifest => DeserializeScheduleReminder(bytes),
        ReminderScheduledManifest => DeserializeReminderScheduled(bytes),
        RemindersForEntityManifest => DeserializeRemindersForEntity(bytes),
        ReminderNackManifest => DeserializeReminderNack(bytes),
        ReminderNackResponseManifest => DeserializeReminderNackResponse(bytes),
        GetReminderOccurrenceStatusManifest => DeserializeGetReminderOccurrenceStatus(bytes),
        ReminderOccurrenceStatusResponseManifest => DeserializeReminderOccurrenceStatusResponse(bytes),
        CancelReminderManifest => DeserializeCancelReminder(bytes),
        CancelAllRemindersManifest => DeserializeCancelAllReminders(bytes),
        RemindersCancelledManifest => DeserializeRemindersCancelled(bytes),
        GetRemindersManifest => DeserializeGetReminders(bytes),
        _ => throw new ArgumentException($"{nameof(ReminderSerializer)} does not recognize manifest [{manifest}]", nameof(manifest))
    };

    private byte[] SerializeReminderEnvelope(ReminderEnvelope envelope)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Write entity fields
        writer.Write(envelope.Entity.ShardRegionName);
        writer.Write(envelope.Entity.EntityId);

        // Write key field
        writer.Write(envelope.Key.Name);

        // Write occurrence metadata
        writer.Write(envelope.DueTimeUtc.UtcTicks);
        writer.Write(envelope.Deadline.UtcDateTime.UtcTicks);

        // Delegate inner message serialization to Akka's serialization infrastructure
        var innerSerializer = SerializationSystem.FindSerializerFor(envelope.Message);
        var innerManifest = Akka.Serialization.Serialization.ManifestFor(innerSerializer, envelope.Message);
        var innerBytes = innerSerializer.ToBinary(envelope.Message);

        writer.Write(innerSerializer.Identifier);
        writer.Write(innerManifest);
        writer.Write(innerBytes.Length);
        writer.Write(innerBytes);

        writer.Flush();
        return stream.ToArray();
    }

    private ReminderEnvelope DeserializeReminderEnvelope(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var shardRegionName = reader.ReadString();
        var entityId = reader.ReadString();
        var entity = new ReminderEntity(shardRegionName, entityId);

        var keyName = reader.ReadString();
        var key = new ReminderKey(keyName);

        var dueTimeUtc = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
        var deadline = new ReminderDeadline(new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero));

        var innerSerializerId = reader.ReadInt32();
        var innerManifest = reader.ReadString();
        var innerLength = reader.ReadInt32();
        var innerBytes = reader.ReadBytes(innerLength);

        var innerMessage = SerializationSystem.Deserialize(innerBytes, innerSerializerId, innerManifest);

        // Rebuild ReminderEnvelope<T> for the runtime type of the inner message. Registered types
        // (ReminderMessageTypes / WithReminderMessage<T>) need no reflection, so they work under Native AOT.
        return ReminderEnvelopeFactory.Create(entity, key, dueTimeUtc, deadline, innerMessage);
    }

    private static byte[] SerializeReminderAck(ReminderProtocol.ReminderAck ack)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(ack.Entity.ShardRegionName);
        writer.Write(ack.Entity.EntityId);
        writer.Write(ack.Key.Name);
        writer.Write(ack.DueTimeUtc.UtcTicks);

        writer.Flush();
        return stream.ToArray();
    }

    private static ReminderProtocol.ReminderAck DeserializeReminderAck(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var shardRegionName = reader.ReadString();
        var entityId = reader.ReadString();
        var keyName = reader.ReadString();
        var dueTimeUtc = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);

        return new ReminderProtocol.ReminderAck(
            new ReminderEntity(shardRegionName, entityId),
            new ReminderKey(keyName),
            dueTimeUtc);
    }

    private static byte[] SerializeReminderAckResponse(ReminderProtocol.ReminderAckResponse response)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(response.Entity.ShardRegionName);
        writer.Write(response.Entity.EntityId);
        writer.Write(response.Key.Name);
        writer.Write(response.DueTimeUtc.UtcTicks);
        writer.Write((int)response.ResponseCode);
        writer.Write(response.Message ?? string.Empty);

        writer.Flush();
        return stream.ToArray();
    }

    private static ReminderProtocol.ReminderAckResponse DeserializeReminderAckResponse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var shardRegionName = reader.ReadString();
        var entityId = reader.ReadString();
        var keyName = reader.ReadString();
        var dueTimeUtc = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
        var responseCode = (ReminderAckResponseCode)reader.ReadInt32();
        var message = reader.ReadString();

        return new ReminderProtocol.ReminderAckResponse(
            new ReminderEntity(shardRegionName, entityId),
            new ReminderKey(keyName),
            dueTimeUtc,
            responseCode,
            string.IsNullOrEmpty(message) ? null : message);
    }

    private static byte[] SerializeReminderNack(ReminderProtocol.ReminderNack nack)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteOccurrenceIdentity(writer, nack.Entity, nack.Key, nack.DueTimeUtc);
        writer.Write(nack.Reason);
        writer.Flush();
        return stream.ToArray();
    }

    private static ReminderProtocol.ReminderNack DeserializeReminderNack(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var (entity, key, dueTimeUtc) = ReadOccurrenceIdentity(reader);
        return new ReminderProtocol.ReminderNack(entity, key, dueTimeUtc, reader.ReadString());
    }

    private static byte[] SerializeReminderNackResponse(ReminderProtocol.ReminderNackResponse response)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteOccurrenceIdentity(writer, response.Entity, response.Key, response.DueTimeUtc);
        writer.Write((int)response.ResponseCode);
        writer.Write(response.AttemptCount);
        WriteNullableDateTimeOffset(writer, response.NextAttemptAtUtc);
        writer.Write(response.Message ?? string.Empty);
        writer.Flush();
        return stream.ToArray();
    }

    private static ReminderProtocol.ReminderNackResponse DeserializeReminderNackResponse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var (entity, key, dueTimeUtc) = ReadOccurrenceIdentity(reader);
        var responseCode = (ReminderNackResponseCode)reader.ReadInt32();
        var attemptCount = reader.ReadInt32();
        var nextAttemptAtUtc = ReadNullableDateTimeOffset(reader);
        var message = reader.ReadString();
        return new ReminderProtocol.ReminderNackResponse(
            entity,
            key,
            dueTimeUtc,
            responseCode,
            attemptCount,
            nextAttemptAtUtc,
            string.IsNullOrEmpty(message) ? null : message);
    }

    private static byte[] SerializeGetReminderOccurrenceStatus(ReminderProtocol.GetReminderOccurrenceStatus query)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteOccurrenceIdentity(writer, query.Entity, query.Key, query.DueTimeUtc);
        writer.Flush();
        return stream.ToArray();
    }

    private static ReminderProtocol.GetReminderOccurrenceStatus DeserializeGetReminderOccurrenceStatus(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var (entity, key, dueTimeUtc) = ReadOccurrenceIdentity(reader);
        return new ReminderProtocol.GetReminderOccurrenceStatus(entity, key, dueTimeUtc);
    }

    private static byte[] SerializeReminderOccurrenceStatusResponse(ReminderProtocol.ReminderOccurrenceStatusResponse response)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteOccurrenceIdentity(writer, response.Entity, response.Key, response.DueTimeUtc);
        writer.Write((int)response.ResponseCode);
        writer.Write(response.Message ?? string.Empty);
        writer.Write(response.Status is not null);
        if (response.Status is { } status)
        {
            WriteNullableDateTimeOffset(writer, status.NextAttemptAtUtc);
            writer.Write(status.AttemptCount);
            writer.Write(status.LastFailureReason ?? string.Empty);
            writer.Write((int)status.CompletionStatus);
            WriteNullableDateTimeOffset(writer, status.DeliveryDeadlineUtc);
            WriteNullableDateTimeOffset(writer, status.DeliveredAtUtc);
            WriteNullableDateTimeOffset(writer, status.AckDeadlineUtc);
            WriteNullableDateTimeOffset(writer, status.CompletedAtUtc);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static ReminderProtocol.ReminderOccurrenceStatusResponse DeserializeReminderOccurrenceStatusResponse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var (entity, key, dueTimeUtc) = ReadOccurrenceIdentity(reader);
        var responseCode = (ReminderOccurrenceStatusResponseCode)reader.ReadInt32();
        var message = reader.ReadString();
        ReminderOccurrenceStatus? status = null;
        if (reader.ReadBoolean())
        {
            var nextAttemptAtUtc = ReadNullableDateTimeOffset(reader);
            var attemptCount = reader.ReadInt32();
            var lastFailureReason = reader.ReadString();
            var completionStatus = (ReminderCompletionStatus)reader.ReadInt32();
            status = new ReminderOccurrenceStatus(
                entity,
                key,
                dueTimeUtc,
                nextAttemptAtUtc,
                attemptCount,
                string.IsNullOrEmpty(lastFailureReason) ? null : lastFailureReason,
                completionStatus,
                ReadNullableDateTimeOffset(reader),
                ReadNullableDateTimeOffset(reader),
                ReadNullableDateTimeOffset(reader),
                ReadNullableDateTimeOffset(reader));
        }
        return new ReminderProtocol.ReminderOccurrenceStatusResponse(
            entity,
            key,
            dueTimeUtc,
            responseCode,
            status,
            string.IsNullOrEmpty(message) ? null : message);
    }

    private static byte[] SerializeCancelReminder(ReminderProtocol.CancelReminder cancel)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(cancel.Entity.ShardRegionName);
        writer.Write(cancel.Entity.EntityId);
        writer.Write(cancel.Key.Name);

        writer.Flush();
        return stream.ToArray();
    }

    private static ReminderProtocol.CancelReminder DeserializeCancelReminder(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var entity = new ReminderEntity(reader.ReadString(), reader.ReadString());
        var key = new ReminderKey(reader.ReadString());

        return new ReminderProtocol.CancelReminder(entity, key);
    }

    private static byte[] SerializeCancelAllReminders(ReminderProtocol.CancelAllReminders cancelAll)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(cancelAll.Entity.ShardRegionName);
        writer.Write(cancelAll.Entity.EntityId);

        writer.Flush();
        return stream.ToArray();
    }

    private static ReminderProtocol.CancelAllReminders DeserializeCancelAllReminders(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var entity = new ReminderEntity(reader.ReadString(), reader.ReadString());

        return new ReminderProtocol.CancelAllReminders(entity);
    }

    private static byte[] SerializeRemindersCancelled(ReminderProtocol.RemindersCancelled cancelled)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(cancelled.Entity.ShardRegionName);
        writer.Write(cancelled.Entity.EntityId);
        writer.Write((int)cancelled.ResponseCode);

        writer.Write(cancelled.Keys.Count);
        foreach (var key in cancelled.Keys)
            writer.Write(key.Name);

        writer.Write(cancelled.Message ?? string.Empty);

        writer.Flush();
        return stream.ToArray();
    }

    private static ReminderProtocol.RemindersCancelled DeserializeRemindersCancelled(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var entity = new ReminderEntity(reader.ReadString(), reader.ReadString());
        var responseCode = (ReminderCancelResponseCode)reader.ReadInt32();

        var count = reader.ReadInt32();
        var keys = new List<ReminderKey>(count);
        for (var i = 0; i < count; i++)
            keys.Add(new ReminderKey(reader.ReadString()));

        var message = reader.ReadString();

        return new ReminderProtocol.RemindersCancelled(
            entity,
            responseCode,
            keys,
            string.IsNullOrEmpty(message) ? null : message);
    }

    private static byte[] SerializeGetReminders(ReminderProtocol.GetReminders getReminders)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(getReminders.Entity.ShardRegionName);
        writer.Write(getReminders.Entity.EntityId);

        writer.Flush();
        return stream.ToArray();
    }

    private static ReminderProtocol.GetReminders DeserializeGetReminders(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var entity = new ReminderEntity(reader.ReadString(), reader.ReadString());

        return new ReminderProtocol.GetReminders(entity);
    }

    private static void WriteOccurrenceIdentity(
        BinaryWriter writer,
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc)
    {
        writer.Write(entity.ShardRegionName);
        writer.Write(entity.EntityId);
        writer.Write(key.Name);
        writer.Write(dueTimeUtc.UtcTicks);
    }

    private static (ReminderEntity Entity, ReminderKey Key, DateTimeOffset DueTimeUtc) ReadOccurrenceIdentity(BinaryReader reader)
        => (
            new ReminderEntity(reader.ReadString(), reader.ReadString()),
            new ReminderKey(reader.ReadString()),
            new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero));

    private static void WriteNullableDateTimeOffset(BinaryWriter writer, DateTimeOffset? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
            writer.Write(value.Value.UtcTicks);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(BinaryReader reader)
        => reader.ReadBoolean()
            ? new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero)
            : null;

    private byte[] SerializeScheduleReminder(ReminderProtocol.ScheduleReminder cmd)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        WriteScheduleReminderFields(writer, cmd);

        writer.Flush();
        return stream.ToArray();
    }

    private ReminderProtocol.ScheduleReminder DeserializeScheduleReminder(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        return ReadScheduleReminderFields(reader);
    }

    private byte[] SerializeReminderScheduled(ReminderProtocol.ReminderScheduled scheduled)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Write the ScheduleReminder fields inline
        WriteScheduleReminderFields(writer, scheduled.OriginalCommand);

        // Write ReminderScheduled-specific fields
        writer.Write((int)scheduled.ResponseCode);
        writer.Write(scheduled.Message ?? string.Empty);

        writer.Flush();
        return stream.ToArray();
    }

    private ReminderProtocol.ReminderScheduled DeserializeReminderScheduled(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var originalCommand = ReadScheduleReminderFields(reader);

        var responseCode = (ReminderScheduleResponseCode)reader.ReadInt32();
        var message = reader.ReadString();

        return new ReminderProtocol.ReminderScheduled(
            originalCommand,
            responseCode,
            string.IsNullOrEmpty(message) ? null : message);
    }

    private byte[] SerializeRemindersForEntity(ReminderProtocol.RemindersForEntity reminders)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Write entity fields
        writer.Write(reminders.Entity.ShardRegionName);
        writer.Write(reminders.Entity.EntityId);

        // Write response metadata
        writer.Write((int)reminders.ResponseCode);
        writer.Write(reminders.Message ?? string.Empty);

        // Write reminder list
        writer.Write(reminders.Reminders.Count);
        foreach (var reminder in reminders.Reminders)
        {
            WriteScheduledReminderFields(writer, reminder);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private ReminderProtocol.RemindersForEntity DeserializeRemindersForEntity(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var shardRegionName = reader.ReadString();
        var entityId = reader.ReadString();
        var entity = new ReminderEntity(shardRegionName, entityId);

        var responseCode = (FetchRemindersResponseCode)reader.ReadInt32();
        var message = reader.ReadString();

        var count = reader.ReadInt32();
        var list = new List<ScheduledReminder>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(ReadScheduledReminderFields(reader, entity));
        }

        return new ReminderProtocol.RemindersForEntity(
            entity,
            responseCode,
            list,
            string.IsNullOrEmpty(message) ? null : message);
    }

    /// <summary>
    /// Writes ScheduleReminder fields to the writer (shared between ScheduleReminder and ReminderScheduled).
    /// </summary>
    private void WriteScheduleReminderFields(BinaryWriter writer, ReminderProtocol.ScheduleReminder cmd)
    {
        writer.Write(cmd.Entity.ShardRegionName);
        writer.Write(cmd.Entity.EntityId);
        writer.Write(cmd.Key.Name);
        writer.Write(cmd.When.UtcTicks);

        // Nullable RepeatInterval
        writer.Write(cmd.RepeatInterval.HasValue);
        if (cmd.RepeatInterval.HasValue)
            writer.Write(cmd.RepeatInterval.Value.Ticks);

        // Nullable MaxDeliveryWindow
        writer.Write(cmd.MaxDeliveryWindow.HasValue);
        if (cmd.MaxDeliveryWindow.HasValue)
            writer.Write(cmd.MaxDeliveryWindow.Value.Ticks);

        // Delegate inner message serialization to Akka's serialization infrastructure
        var innerSerializer = SerializationSystem.FindSerializerFor(cmd.Message);
        var innerManifest = Akka.Serialization.Serialization.ManifestFor(innerSerializer, cmd.Message);
        var innerBytes = innerSerializer.ToBinary(cmd.Message);

        writer.Write(innerSerializer.Identifier);
        writer.Write(innerManifest);
        writer.Write(innerBytes.Length);
        writer.Write(innerBytes);
    }

    /// <summary>
    /// Reads ScheduleReminder fields from the reader (shared between ScheduleReminder and ReminderScheduled).
    /// </summary>
    private ReminderProtocol.ScheduleReminder ReadScheduleReminderFields(BinaryReader reader)
    {
        var shardRegionName = reader.ReadString();
        var entityId = reader.ReadString();
        var entity = new ReminderEntity(shardRegionName, entityId);

        var keyName = reader.ReadString();
        var key = new ReminderKey(keyName);

        var when = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);

        // Nullable RepeatInterval
        TimeSpan? repeatInterval = null;
        if (reader.ReadBoolean())
            repeatInterval = TimeSpan.FromTicks(reader.ReadInt64());

        // Nullable MaxDeliveryWindow
        TimeSpan? maxDeliveryWindow = null;
        if (reader.ReadBoolean())
            maxDeliveryWindow = TimeSpan.FromTicks(reader.ReadInt64());

        // Inner message
        var innerSerializerId = reader.ReadInt32();
        var innerManifest = reader.ReadString();
        var innerLength = reader.ReadInt32();
        var innerBytes = reader.ReadBytes(innerLength);
        var innerMessage = SerializationSystem.Deserialize(innerBytes, innerSerializerId, innerManifest);

        return new ReminderProtocol.ScheduleReminder(
            entity,
            key,
            when,
            innerMessage,
            repeatInterval,
            maxDeliveryWindow);
    }

    /// <summary>
    /// Writes a single ScheduledReminder's fields to the writer (used in RemindersForEntity).
    /// </summary>
    private void WriteScheduledReminderFields(BinaryWriter writer, ScheduledReminder reminder)
    {
        writer.Write(reminder.Key.Name);
        writer.Write(reminder.When.UtcTicks);

        // Nullable RepeatInterval
        writer.Write(reminder.RepeatInterval.HasValue);
        if (reminder.RepeatInterval.HasValue)
            writer.Write(reminder.RepeatInterval.Value.Ticks);

        // AttemptCount
        writer.Write(reminder.AttemptCount);

        // Nullable LastFailureReason
        var hasFailureReason = !string.IsNullOrEmpty(reminder.LastFailureReason);
        writer.Write(hasFailureReason);
        if (hasFailureReason)
            writer.Write(reminder.LastFailureReason!);

        // Nullable MaxDeliveryWindow
        writer.Write(reminder.MaxDeliveryWindow.HasValue);
        if (reminder.MaxDeliveryWindow.HasValue)
            writer.Write(reminder.MaxDeliveryWindow.Value.Ticks);

        // Nullable DeliveryDeadlineUtc
        writer.Write(reminder.DeliveryDeadlineUtc.HasValue);
        if (reminder.DeliveryDeadlineUtc.HasValue)
            writer.Write(reminder.DeliveryDeadlineUtc.Value.UtcTicks);

        // Nullable OccurrenceDueTimeUtc
        writer.Write(reminder.OccurrenceDueTimeUtc.HasValue);
        if (reminder.OccurrenceDueTimeUtc.HasValue)
            writer.Write(reminder.OccurrenceDueTimeUtc.Value.UtcTicks);

        // Inner message
        var innerSerializer = SerializationSystem.FindSerializerFor(reminder.Message);
        var innerManifest = Akka.Serialization.Serialization.ManifestFor(innerSerializer, reminder.Message);
        var innerBytes = innerSerializer.ToBinary(reminder.Message);

        writer.Write(innerSerializer.Identifier);
        writer.Write(innerManifest);
        writer.Write(innerBytes.Length);
        writer.Write(innerBytes);
    }

    /// <summary>
    /// Reads a single ScheduledReminder's fields from the reader (used in RemindersForEntity).
    /// </summary>
    private ScheduledReminder ReadScheduledReminderFields(BinaryReader reader, ReminderEntity entity)
    {
        var keyName = reader.ReadString();
        var key = new ReminderKey(keyName);

        var when = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);

        // Nullable RepeatInterval
        TimeSpan? repeatInterval = null;
        if (reader.ReadBoolean())
            repeatInterval = TimeSpan.FromTicks(reader.ReadInt64());

        // AttemptCount
        var attemptCount = reader.ReadInt32();

        // Nullable LastFailureReason
        string? lastFailureReason = null;
        if (reader.ReadBoolean())
            lastFailureReason = reader.ReadString();

        // Nullable MaxDeliveryWindow
        TimeSpan? maxDeliveryWindow = null;
        if (reader.ReadBoolean())
            maxDeliveryWindow = TimeSpan.FromTicks(reader.ReadInt64());

        // Nullable DeliveryDeadlineUtc
        DateTimeOffset? deliveryDeadlineUtc = null;
        if (reader.ReadBoolean())
            deliveryDeadlineUtc = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);

        // Nullable OccurrenceDueTimeUtc
        DateTimeOffset? occurrenceDueTimeUtc = null;
        if (reader.ReadBoolean())
            occurrenceDueTimeUtc = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);

        // Inner message
        var innerSerializerId = reader.ReadInt32();
        var innerManifest = reader.ReadString();
        var innerLength = reader.ReadInt32();
        var innerBytes = reader.ReadBytes(innerLength);
        var innerMessage = SerializationSystem.Deserialize(innerBytes, innerSerializerId, innerManifest);

        return new ScheduledReminder(
            entity,
            key,
            when,
            innerMessage,
            repeatInterval,
            attemptCount,
            lastFailureReason,
            maxDeliveryWindow,
            deliveryDeadlineUtc,
            occurrenceDueTimeUtc);
    }
}
