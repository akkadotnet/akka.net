using System.Data;
using System.Data.Common;
using Npgsql;
using NpgsqlTypes;

namespace Akka.Reminders.PostgreSql.Internal;

internal sealed class PostgreSqlDialect : ISqlDialect
{
    public static readonly PostgreSqlDialect Instance = new();

    private PostgreSqlDialect() { }

    public string GetCreateTableSql(string schemaName, string tableName)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";

        return $"""
            CREATE SCHEMA IF NOT EXISTS "{schemaName}";

            CREATE TABLE IF NOT EXISTS {fullTableName} (
                shard_region_name VARCHAR(255) NOT NULL,
                entity_id VARCHAR(255) NOT NULL,
                reminder_key VARCHAR(255) NOT NULL,
                when_utc TIMESTAMP WITH TIME ZONE NOT NULL,
                due_time_utc TIMESTAMP WITH TIME ZONE NOT NULL,
                repeat_interval_ticks BIGINT NULL,
                serializer_id INTEGER NOT NULL,
                manifest VARCHAR(500) NULL,
                payload BYTEA NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                last_failure_reason TEXT NULL,
                max_delivery_window_ticks BIGINT NULL,
                delivery_deadline_utc TIMESTAMP WITH TIME ZONE NULL,
                is_completed BOOLEAN NOT NULL DEFAULT FALSE,
                completed_at_utc TIMESTAMP WITH TIME ZONE NULL,
                completion_status VARCHAR(20) NOT NULL DEFAULT 'Pending',
                delivered_at_utc TIMESTAMP WITH TIME ZONE NULL,
                ack_deadline_utc TIMESTAMP WITH TIME ZONE NULL,

                CONSTRAINT pk_{tableName} PRIMARY KEY (shard_region_name, entity_id, reminder_key, due_time_utc)
            );

            CREATE INDEX IF NOT EXISTS ix_{tableName}_due_reminders
            ON {fullTableName} (when_utc, shard_region_name, entity_id)
            WHERE is_completed = FALSE AND completion_status = 'Pending';

            CREATE INDEX IF NOT EXISTS ix_{tableName}_cleanup
            ON {fullTableName} (completed_at_utc)
            WHERE is_completed = TRUE;

            CREATE INDEX IF NOT EXISTS ix_{tableName}_awaiting_ack
            ON {fullTableName} (ack_deadline_utc)
            WHERE completion_status = 'AwaitingAck' AND is_completed = FALSE;
            """;
    }

    public string GetBatchUpsertRemindersSql(string schemaName, string tableName, int count, bool insertIfAbsent = false)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";
        var values = string.Join(",\n                ",
            Enumerable.Range(0, count).Select(i =>
                $"(@ShardRegionName{i}, @EntityId{i}, @ReminderKey{i}, @WhenUtc{i}, @DueTimeUtc{i}, @RepeatIntervalTicks{i}, @SerializerId{i}, @Manifest{i}, @Payload{i}, @AttemptCount{i}, @LastFailureReason{i}, @MaxDeliveryWindowTicks{i}, @DeliveryDeadlineUtc{i}, FALSE, NULL, 'Pending', NULL, NULL)"));

        var onConflict = insertIfAbsent ? "DO NOTHING" : """
            DO UPDATE SET
                when_utc = EXCLUDED.when_utc,
                repeat_interval_ticks = EXCLUDED.repeat_interval_ticks,
                serializer_id = EXCLUDED.serializer_id,
                manifest = EXCLUDED.manifest,
                payload = EXCLUDED.payload,
                attempt_count = EXCLUDED.attempt_count,
                last_failure_reason = EXCLUDED.last_failure_reason,
                max_delivery_window_ticks = EXCLUDED.max_delivery_window_ticks,
                delivery_deadline_utc = EXCLUDED.delivery_deadline_utc,
                is_completed = FALSE,
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

    public string GetSelectDueRemindersSql(string schemaName, string tableName, int maxCount)
    {
        if (maxCount < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than or equal to 1.");

        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";

        return $"""
            SELECT shard_region_name, entity_id, reminder_key, when_utc, due_time_utc, repeat_interval_ticks,
                   serializer_id, manifest, payload, attempt_count, last_failure_reason,
                   max_delivery_window_ticks, delivery_deadline_utc
            FROM {fullTableName}
            WHERE is_completed = FALSE
              AND completion_status = 'Pending'
              AND when_utc <= @UntilDeadline
              AND (delivery_deadline_utc IS NULL OR delivery_deadline_utc > @Now OR repeat_interval_ticks IS NOT NULL)
            ORDER BY when_utc ASC
            LIMIT {maxCount};
            """;
    }

    public string GetBatchMarkCompletedSql(string schemaName, string tableName, int count, bool activeOnly = false)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";
        var values = string.Join(",\n                ",
            Enumerable.Range(0, count).Select(i =>
                $"(@sr{i}::varchar, @eid{i}::varchar, @rk{i}::varchar, @due{i}::timestamptz)"));

        return $"""
            UPDATE {fullTableName} t
            SET is_completed = TRUE,
                completed_at_utc = @CompletedAtUtc,
                completion_status = @CompletionStatus,
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            FROM (VALUES
                {values}
            ) AS v(shard_region_name, entity_id, reminder_key, due_time_utc)
            WHERE t.shard_region_name = v.shard_region_name
              AND t.entity_id = v.entity_id
              AND t.reminder_key = v.reminder_key
              AND t.due_time_utc = v.due_time_utc
              {(activeOnly ? "AND t.completion_status IN ('Pending', 'AwaitingAck')" : "")};
            """;
    }

    public string GetExpireRemindersSql(string schemaName, string tableName)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";

        return $"""
            UPDATE {fullTableName}
            SET is_completed = TRUE,
                completed_at_utc = @Now,
                completion_status = 'Expired',
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            WHERE is_completed = FALSE
              AND delivery_deadline_utc IS NOT NULL
              AND delivery_deadline_utc <= @Now
              AND (completion_status <> 'Pending' OR repeat_interval_ticks IS NULL);
            """;
    }

    public string GetCleanupSql(string schemaName, string tableName)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";

        return $"""
            DELETE FROM {fullTableName}
            WHERE is_completed = TRUE
              AND completed_at_utc < @OlderThan;
            """;
    }

    public string GetOverviewAggregateSql(string schemaName, string tableName)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";

        return $"""
            SELECT COUNT(*) AS total_count, MIN(when_utc) AS next_when_utc
            FROM {fullTableName}
            WHERE is_completed = FALSE
              AND completion_status = 'Pending'
              AND (delivery_deadline_utc IS NULL OR delivery_deadline_utc > @Now OR repeat_interval_ticks IS NOT NULL);
            """;
    }

    public string GetNextReminderTimeSql(string schemaName, string tableName)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";

        return $"""
            SELECT when_utc
            FROM {fullTableName}
            WHERE is_completed = FALSE
              AND completion_status = 'Pending'
              AND (delivery_deadline_utc IS NULL OR delivery_deadline_utc > @Now OR repeat_interval_ticks IS NOT NULL)
            ORDER BY when_utc ASC
            LIMIT 1 OFFSET @Skip;
            """;
    }

    public string GetCancelReminderSql(string schemaName, string tableName)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";

        return $"""
            UPDATE {fullTableName}
            SET is_completed = TRUE,
                completed_at_utc = @CompletedAtUtc,
                completion_status = 'Cancelled',
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            WHERE shard_region_name = @ShardRegionName
              AND entity_id = @EntityId
              AND reminder_key = @ReminderKey
              AND is_completed = FALSE;
            """;
    }

    public string GetCancelAllRemindersSql(string schemaName, string tableName)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";

        return $"""
            UPDATE {fullTableName}
            SET is_completed = TRUE,
                completed_at_utc = @CompletedAtUtc,
                completion_status = 'Cancelled',
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            WHERE shard_region_name = @ShardRegionName
              AND entity_id = @EntityId
              AND is_completed = FALSE;
            """;
    }

    public string GetFetchRemindersSql(string schemaName, string tableName)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";

        return $"""
            SELECT shard_region_name, entity_id, reminder_key, when_utc, due_time_utc, repeat_interval_ticks,
                   serializer_id, manifest, payload, attempt_count, last_failure_reason,
                   max_delivery_window_ticks, delivery_deadline_utc
            FROM {fullTableName}
            WHERE shard_region_name = @ShardRegionName
              AND entity_id = @EntityId
              AND is_completed = FALSE
              AND (delivery_deadline_utc IS NULL
                   OR delivery_deadline_utc > @Now
                   OR (repeat_interval_ticks IS NOT NULL AND completion_status = 'Pending'))
            ORDER BY when_utc ASC;
            """;
    }

    public string GetBatchMarkAsAwaitingAckSql(string schemaName, string tableName, int count)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";
        var values = string.Join(",\n                ",
            Enumerable.Range(0, count).Select(i =>
                $"(@sr{i}::varchar, @eid{i}::varchar, @rk{i}::varchar, @due{i}::timestamptz, @del{i}::timestamptz, @ack{i}::timestamptz)"));

        return $"""
            UPDATE {fullTableName} t
            SET completion_status = 'AwaitingAck',
                delivered_at_utc = v.delivered_at_utc,
                ack_deadline_utc = v.ack_deadline_utc
            FROM (VALUES
                {values}
            ) AS v(shard_region_name, entity_id, reminder_key, due_time_utc, delivered_at_utc, ack_deadline_utc)
            WHERE t.shard_region_name = v.shard_region_name
              AND t.entity_id = v.entity_id
              AND t.reminder_key = v.reminder_key
              AND t.due_time_utc = v.due_time_utc
              AND t.is_completed = FALSE
             AND t.completion_status = 'Pending';
            """;
    }

    public string GetTimedOutAckRemindersSql(string schemaName, string tableName, int maxCount)
    {
        if (maxCount < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than or equal to 1.");

        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";

        return $"""
            SELECT shard_region_name, entity_id, reminder_key, when_utc, due_time_utc, repeat_interval_ticks,
                   serializer_id, manifest, payload, attempt_count, last_failure_reason,
                   max_delivery_window_ticks, delivery_deadline_utc
            FROM {fullTableName}
            WHERE completion_status = 'AwaitingAck'
              AND is_completed = FALSE
              AND ack_deadline_utc <= @Now
            ORDER BY ack_deadline_utc ASC
            LIMIT {maxCount};
            """;
    }

    public string GetAcknowledgeReminderSql(string schemaName, string tableName)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";

        return $"""
            UPDATE {fullTableName}
            SET is_completed = TRUE,
                completed_at_utc = @AckedAtUtc,
                completion_status = 'Delivered',
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            WHERE shard_region_name = @ShardRegionName
              AND entity_id = @EntityId
              AND reminder_key = @ReminderKey
              AND due_time_utc = @DueTimeUtc
              AND completion_status = 'AwaitingAck'
              AND is_completed = FALSE
              AND (delivery_deadline_utc IS NULL OR delivery_deadline_utc > @AckedAtUtc);
            """;
    }

    public string GetBatchAcknowledgeRemindersSql(string schemaName, string tableName, int count)
    {
        var fullTableName = $"\"{schemaName}\".\"{tableName}\"";
        var values = string.Join(",\n                ",
            Enumerable.Range(0, count).Select(i =>
                $"(@sr{i}::varchar, @eid{i}::varchar, @rk{i}::varchar, @due{i}::timestamptz, @acked{i}::timestamptz)"));

        return $"""
            UPDATE {fullTableName} t
            SET is_completed = TRUE,
                completed_at_utc = v.acked_at_utc,
                completion_status = 'Delivered',
                delivered_at_utc = NULL,
                ack_deadline_utc = NULL
            FROM (VALUES
                {values}
            ) AS v(shard_region_name, entity_id, reminder_key, due_time_utc, acked_at_utc)
            WHERE t.shard_region_name = v.shard_region_name
              AND t.entity_id = v.entity_id
              AND t.reminder_key = v.reminder_key
              AND t.due_time_utc = v.due_time_utc
              AND t.completion_status = 'AwaitingAck'
              AND t.is_completed = FALSE
              AND (t.delivery_deadline_utc IS NULL OR t.delivery_deadline_utc > v.acked_at_utc);
            """;
    }

    public DbConnection CreateConnection(string connectionString)
    {
        return new NpgsqlConnection(connectionString);
    }

    public void AddParameter(DbCommand command, string name, object value)
    {
        var npgsqlCommand = (NpgsqlCommand)command;

        if (value == null)
        {
            npgsqlCommand.Parameters.AddWithValue(name, DBNull.Value);
            return;
        }

        switch (value)
        {
            case DateTimeOffset dto:
                var ticksToRemove = dto.Ticks % 10;
                var truncatedDto = ticksToRemove == 0 ? dto : new DateTimeOffset(dto.Ticks - ticksToRemove, dto.Offset);
                npgsqlCommand.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.TimestampTz) { Value = truncatedDto });
                break;
            case DateTime dt:
                var dtTicksToRemove = dt.Ticks % 10;
                var truncatedDt = dtTicksToRemove == 0 ? dt : new DateTime(dt.Ticks - dtTicksToRemove, dt.Kind);
                npgsqlCommand.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.TimestampTz) { Value = truncatedDt });
                break;
            case byte[] bytes:
                npgsqlCommand.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Bytea) { Value = bytes });
                break;
            case string str:
                npgsqlCommand.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Varchar) { Value = str });
                break;
            case long lng:
                npgsqlCommand.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Bigint) { Value = lng });
                break;
            case int i:
                npgsqlCommand.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Integer) { Value = i });
                break;
            case bool b:
                npgsqlCommand.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Boolean) { Value = b });
                break;
            default:
                npgsqlCommand.Parameters.AddWithValue(name, value);
                break;
        }
    }
}
