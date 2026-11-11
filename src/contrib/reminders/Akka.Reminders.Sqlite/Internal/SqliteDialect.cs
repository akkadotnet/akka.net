using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Akka.Reminders.Sqlite.Internal;

internal sealed class SqliteDialect : ISqlDialect
{
    public static readonly SqliteDialect Instance = new();

    private SqliteDialect() { }

    public string GetCreateTableSql(string tableName)
    {
        var fullTableName = $"\"{tableName}\"";

        return $"""
            CREATE TABLE IF NOT EXISTS {fullTableName} (
                shard_region_name TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                reminder_key TEXT NOT NULL,
                when_utc TEXT NOT NULL,
                due_time_utc TEXT NOT NULL,
                repeat_interval_ticks INTEGER NULL,
                serializer_id INTEGER NOT NULL,
                manifest TEXT NULL,
                payload BLOB NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                last_failure_reason TEXT NULL,
                max_delivery_window_ticks INTEGER NULL,
                delivery_deadline_utc TEXT NULL,
                is_completed INTEGER NOT NULL DEFAULT 0,
                completed_at_utc TEXT NULL,
                completion_status TEXT NOT NULL DEFAULT 'Pending',
                delivered_at_utc TEXT NULL,
                ack_deadline_utc TEXT NULL,

                PRIMARY KEY (shard_region_name, entity_id, reminder_key, due_time_utc)
            );

            CREATE INDEX IF NOT EXISTS ix_{tableName}_due_reminders
            ON {fullTableName} (when_utc, shard_region_name, entity_id)
            WHERE is_completed = 0 AND completion_status = 'Pending';

            CREATE INDEX IF NOT EXISTS ix_{tableName}_cleanup
            ON {fullTableName} (completed_at_utc)
            WHERE is_completed = 1;

            CREATE INDEX IF NOT EXISTS ix_{tableName}_awaiting_ack
            ON {fullTableName} (ack_deadline_utc)
            WHERE completion_status = 'AwaitingAck' AND is_completed = 0;
            """;
    }

    public string GetBatchUpsertRemindersSql(string tableName, int count, bool insertIfAbsent = false)
    {
        var fullTableName = $"\"{tableName}\"";
        var values = string.Join(",\n                ",
            Enumerable.Range(0, count).Select(i =>
                $"(@ShardRegionName{i}, @EntityId{i}, @ReminderKey{i}, @WhenUtc{i}, @DueTimeUtc{i}, @RepeatIntervalTicks{i}, @SerializerId{i}, @Manifest{i}, @Payload{i}, @AttemptCount{i}, @LastFailureReason{i}, @MaxDeliveryWindowTicks{i}, @DeliveryDeadlineUtc{i}, 0, NULL, 'Pending', NULL, NULL)"));

        var onConflict = insertIfAbsent ? "DO NOTHING" : """
            DO UPDATE SET
                when_utc = excluded.when_utc,
                repeat_interval_ticks = excluded.repeat_interval_ticks,
                serializer_id = excluded.serializer_id,
                manifest = excluded.manifest,
                payload = excluded.payload,
                attempt_count = excluded.attempt_count,
                last_failure_reason = excluded.last_failure_reason,
                max_delivery_window_ticks = excluded.max_delivery_window_ticks,
                delivery_deadline_utc = excluded.delivery_deadline_utc,
                is_completed = 0,
                completed_at_utc = NULL,
                completion_status = 'Pending',
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            """;

        return $"""
            INSERT INTO {fullTableName}
                (shard_region_name, entity_id, reminder_key, when_utc, due_time_utc, repeat_interval_ticks,
                 serializer_id, manifest, payload, attempt_count, last_failure_reason,
                 max_delivery_window_ticks, delivery_deadline_utc,
                 is_completed, completed_at_utc, completion_status, delivered_at_utc, ack_deadline_utc)
            VALUES
                {values}
            ON CONFLICT (shard_region_name, entity_id, reminder_key, due_time_utc)
            {onConflict};
            """;
    }

    public string GetSelectDueRemindersSql(string tableName, int maxCount)
    {
        if (maxCount < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than or equal to 1.");

        var fullTableName = $"\"{tableName}\"";

        return $"""
            SELECT shard_region_name, entity_id, reminder_key, when_utc, due_time_utc, repeat_interval_ticks,
                   serializer_id, manifest, payload, attempt_count, last_failure_reason,
                   max_delivery_window_ticks, delivery_deadline_utc
            FROM {fullTableName}
            WHERE is_completed = 0
              AND completion_status = 'Pending'
              AND when_utc <= @UntilDeadline
              AND (delivery_deadline_utc IS NULL OR delivery_deadline_utc > @Now OR repeat_interval_ticks IS NOT NULL)
            ORDER BY when_utc ASC
            LIMIT {maxCount};
            """;
    }

    public string GetBatchMarkCompletedSql(string tableName, int count, bool activeOnly = false)
    {
        var fullTableName = $"\"{tableName}\"";

        var predicates = string.Join(" OR ",
            Enumerable.Range(0, count).Select(i =>
                $"(shard_region_name = @sr{i} AND entity_id = @eid{i} AND reminder_key = @rk{i} AND due_time_utc = @due{i})"));

        return $"""
            UPDATE {fullTableName}
            SET is_completed = 1,
                completed_at_utc = @CompletedAtUtc,
                completion_status = @CompletionStatus,
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            WHERE ({predicates}){(activeOnly ? " AND completion_status IN ('Pending', 'AwaitingAck')" : "")};
            """;
    }

    public string GetExpireRemindersSql(string tableName)
    {
        var fullTableName = $"\"{tableName}\"";

        return $"""
            UPDATE {fullTableName}
            SET is_completed = 1,
                completed_at_utc = @Now,
                completion_status = 'Expired',
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            WHERE is_completed = 0
              AND delivery_deadline_utc IS NOT NULL
              AND delivery_deadline_utc <= @Now
              AND (completion_status <> 'Pending' OR repeat_interval_ticks IS NULL);
            """;
    }

    public string GetCleanupSql(string tableName)
    {
        var fullTableName = $"\"{tableName}\"";

        return $"""
            DELETE FROM {fullTableName}
            WHERE is_completed = 1
              AND completed_at_utc < @OlderThan;
            """;
    }

    public string GetOverviewAggregateSql(string tableName)
    {
        var fullTableName = $"\"{tableName}\"";

        return $"""
            SELECT COUNT(*) AS total_count, MIN(when_utc) AS next_when_utc
            FROM {fullTableName}
            WHERE is_completed = 0
              AND completion_status = 'Pending'
              AND (delivery_deadline_utc IS NULL OR delivery_deadline_utc > @Now OR repeat_interval_ticks IS NOT NULL);
            """;
    }

    public string GetNextReminderTimeSql(string tableName)
    {
        var fullTableName = $"\"{tableName}\"";

        return $"""
            SELECT when_utc
            FROM {fullTableName}
            WHERE is_completed = 0
              AND completion_status = 'Pending'
              AND (delivery_deadline_utc IS NULL OR delivery_deadline_utc > @Now OR repeat_interval_ticks IS NOT NULL)
            ORDER BY when_utc ASC
            LIMIT 1 OFFSET @Skip;
            """;
    }

    public string GetCancelReminderSql(string tableName)
    {
        var fullTableName = $"\"{tableName}\"";

        return $"""
            UPDATE {fullTableName}
            SET is_completed = 1,
                completed_at_utc = @CompletedAtUtc,
                completion_status = 'Cancelled',
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            WHERE shard_region_name = @ShardRegionName
              AND entity_id = @EntityId
              AND reminder_key = @ReminderKey
              AND is_completed = 0;
            """;
    }

    public string GetCancelAllRemindersSql(string tableName)
    {
        var fullTableName = $"\"{tableName}\"";

        return $"""
            UPDATE {fullTableName}
            SET is_completed = 1,
                completed_at_utc = @CompletedAtUtc,
                completion_status = 'Cancelled',
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            WHERE shard_region_name = @ShardRegionName
              AND entity_id = @EntityId
              AND is_completed = 0;
            """;
    }

    public string GetFetchRemindersSql(string tableName)
    {
        var fullTableName = $"\"{tableName}\"";

        return $"""
            SELECT shard_region_name, entity_id, reminder_key, when_utc, due_time_utc, repeat_interval_ticks,
                   serializer_id, manifest, payload, attempt_count, last_failure_reason,
                   max_delivery_window_ticks, delivery_deadline_utc
            FROM {fullTableName}
            WHERE shard_region_name = @ShardRegionName
              AND entity_id = @EntityId
              AND is_completed = 0
              AND (delivery_deadline_utc IS NULL
                   OR delivery_deadline_utc > @Now
                   OR (repeat_interval_ticks IS NOT NULL AND completion_status = 'Pending'))
            ORDER BY when_utc ASC;
            """;
    }

    public string GetBatchMarkAsAwaitingAckSql(string tableName, int count)
    {
        var fullTableName = $"\"{tableName}\"";
        var predicates = string.Join(" OR ",
            Enumerable.Range(0, count).Select(i =>
                $"(shard_region_name = @sr{i} AND entity_id = @eid{i} AND reminder_key = @rk{i} AND due_time_utc = @due{i})"));
        var deliveredCases = string.Join("\n                ",
            Enumerable.Range(0, count).Select(i =>
                $"WHEN shard_region_name = @sr{i} AND entity_id = @eid{i} AND reminder_key = @rk{i} AND due_time_utc = @due{i} THEN @del{i}"));
        var ackCases = string.Join("\n                ",
            Enumerable.Range(0, count).Select(i =>
                $"WHEN shard_region_name = @sr{i} AND entity_id = @eid{i} AND reminder_key = @rk{i} AND due_time_utc = @due{i} THEN @ack{i}"));

        return $"""
            UPDATE {fullTableName}
            SET completion_status = 'AwaitingAck',
                delivered_at_utc = CASE
                    {deliveredCases}
                    ELSE delivered_at_utc
                END,
                ack_deadline_utc = CASE
                    {ackCases}
                    ELSE ack_deadline_utc
                END
            WHERE is_completed = 0
              AND completion_status = 'Pending'
              AND ({predicates});
            """;
    }

    public string GetTimedOutAckRemindersSql(string tableName, int maxCount)
    {
        if (maxCount < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than or equal to 1.");

        var fullTableName = $"\"{tableName}\"";

        return $"""
            SELECT shard_region_name, entity_id, reminder_key, when_utc, due_time_utc, repeat_interval_ticks,
                   serializer_id, manifest, payload, attempt_count, last_failure_reason,
                   max_delivery_window_ticks, delivery_deadline_utc
            FROM {fullTableName}
            WHERE completion_status = 'AwaitingAck'
              AND is_completed = 0
              AND ack_deadline_utc <= @Now
            ORDER BY ack_deadline_utc ASC
            LIMIT {maxCount};
            """;
    }

    public string GetAcknowledgeReminderSql(string tableName)
    {
        var fullTableName = $"\"{tableName}\"";

        return $"""
            UPDATE {fullTableName}
            SET is_completed = 1,
                completed_at_utc = @AckedAtUtc,
                completion_status = 'Delivered',
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            WHERE shard_region_name = @ShardRegionName
              AND entity_id = @EntityId
              AND reminder_key = @ReminderKey
              AND due_time_utc = @DueTimeUtc
              AND completion_status = 'AwaitingAck'
              AND is_completed = 0
              AND (delivery_deadline_utc IS NULL OR delivery_deadline_utc > @AckedAtUtc);
            """;
    }

    public string GetBatchAcknowledgeRemindersSql(string tableName, int count)
    {
        var fullTableName = $"\"{tableName}\"";
        var predicates = string.Join(" OR ",
            Enumerable.Range(0, count).Select(i =>
                $"(shard_region_name = @sr{i} AND entity_id = @eid{i} AND reminder_key = @rk{i} AND due_time_utc = @due{i})"));
        var ackedCases = string.Join("\n                ",
            Enumerable.Range(0, count).Select(i =>
                $"WHEN shard_region_name = @sr{i} AND entity_id = @eid{i} AND reminder_key = @rk{i} AND due_time_utc = @due{i} THEN @acked{i}"));

        return $"""
            UPDATE {fullTableName}
            SET is_completed = 1,
                completed_at_utc = CASE
                    {ackedCases}
                    ELSE completed_at_utc
                END,
                completion_status = 'Delivered',
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            WHERE completion_status = 'AwaitingAck'
              AND is_completed = 0
              AND ({predicates})
              AND (delivery_deadline_utc IS NULL OR delivery_deadline_utc > CASE
                    {ackedCases}
                    ELSE completed_at_utc
                END);
            """;
    }

    public DbConnection CreateConnection(string connectionString)
    {
        return new SqliteConnection(connectionString);
    }

    public void AddParameter(DbCommand command, string name, object value)
    {
        var sqliteCommand = (SqliteCommand)command;

        if (value == null)
        {
            sqliteCommand.Parameters.AddWithValue(name, DBNull.Value);
            return;
        }

        switch (value)
        {
            case DateTimeOffset dto:
                sqliteCommand.Parameters.AddWithValue(name, dto.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                sqliteCommand.Parameters.AddWithValue(name, dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                break;
            case bool b:
                sqliteCommand.Parameters.AddWithValue(name, b ? 1 : 0);
                break;
            default:
                sqliteCommand.Parameters.AddWithValue(name, value);
                break;
        }
    }
}
