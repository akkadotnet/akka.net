using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Akka.Reminders.Tests.Golden;

/// <summary>One row of <c>scheduled_reminders</c> exactly as stored, with no deserialization.</summary>
public sealed record RawReminderRow(
    string ShardRegionName,
    string EntityId,
    string ReminderKey,
    DateTimeOffset WhenUtc,
    DateTimeOffset DueTimeUtc,
    long? RepeatIntervalTicks,
    int SerializerId,
    string? Manifest,
    byte[] Payload,
    int AttemptCount,
    string? LastFailureReason,
    long? MaxDeliveryWindowTicks,
    DateTimeOffset? DeliveryDeadlineUtc,
    bool IsCompleted,
    DateTimeOffset? CompletedAtUtc,
    string CompletionStatus,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? AckDeadlineUtc)
{
    public string Id => $"{ShardRegionName}/{EntityId}/{ReminderKey}@{DueTimeUtc:O}";
}

public static class GoldenRawRows
{
    public const string TableName = "scheduled_reminders";

    public static async Task<IReadOnlyList<RawReminderRow>> ReadAsync(string connectionString, CancellationToken ct = default)
    {
        var rows = new List<RawReminderRow>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT shard_region_name, entity_id, reminder_key, when_utc, due_time_utc, repeat_interval_ticks,
                   serializer_id, manifest, payload, attempt_count, last_failure_reason,
                   max_delivery_window_ticks, delivery_deadline_utc, is_completed, completed_at_utc,
                   completion_status, delivered_at_utc, ack_deadline_utc
            FROM "{TableName}"
            ORDER BY shard_region_name, entity_id, reminder_key, due_time_utc;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new RawReminderRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Date(reader.GetString(3)),
                Date(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                (byte[])reader.GetValue(8),
                reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetInt64(11),
                reader.IsDBNull(12) ? null : Date(reader.GetString(12)),
                reader.GetInt32(13) != 0,
                reader.IsDBNull(14) ? null : Date(reader.GetString(14)),
                reader.GetString(15),
                reader.IsDBNull(16) ? null : Date(reader.GetString(16)),
                reader.IsDBNull(17) ? null : Date(reader.GetString(17))));
        }

        return rows;
    }

    private static DateTimeOffset Date(string text)
        => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
