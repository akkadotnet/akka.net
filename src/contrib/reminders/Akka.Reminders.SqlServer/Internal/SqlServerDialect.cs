using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Akka.Reminders.SqlServer.Internal;

internal sealed class SqlServerDialect : ISqlDialect
{
    public static readonly SqlServerDialect Instance = new();

    private SqlServerDialect() { }

    public string GetCreateTableSql(string schemaName, string tableName)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";

        return $"""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schemaName}')
            BEGIN
                EXEC('CREATE SCHEMA [{schemaName}]')
            END

            IF NOT EXISTS (SELECT * FROM sys.tables t
                          JOIN sys.schemas s ON t.schema_id = s.schema_id
                          WHERE s.name = '{schemaName}' AND t.name = '{tableName}')
            BEGIN
                CREATE TABLE {fullTableName} (
                    ShardRegionName NVARCHAR(255) NOT NULL,
                    EntityId NVARCHAR(255) NOT NULL,
                    ReminderKey NVARCHAR(255) NOT NULL,
                    WhenUtc DATETIME2 NOT NULL,
                    DueTimeUtc DATETIME2 NOT NULL,
                    RepeatIntervalTicks BIGINT NULL,
                    SerializerId INT NOT NULL,
                    Manifest NVARCHAR(500) NULL,
                    Payload VARBINARY(MAX) NOT NULL,
                    AttemptCount INT NOT NULL DEFAULT 0,
                    LastFailureReason NVARCHAR(MAX) NULL,
                    MaxDeliveryWindowTicks BIGINT NULL,
                    DeliveryDeadlineUtc DATETIME2 NULL,
                    IsCompleted BIT NOT NULL DEFAULT 0,
                    CompletedAtUtc DATETIME2 NULL,
                    CompletionStatus VARCHAR(20) NOT NULL DEFAULT 'Pending',
                    DeliveredAtUtc DATETIME2 NULL,
                    AckDeadlineUtc DATETIME2 NULL,

                    CONSTRAINT PK_{tableName} PRIMARY KEY (ShardRegionName, EntityId, ReminderKey, DueTimeUtc)
                );

                CREATE INDEX IX_{tableName}_DueReminders
                ON {fullTableName} (WhenUtc, ShardRegionName, EntityId)
                WHERE IsCompleted = 0 AND CompletionStatus = 'Pending';

                CREATE INDEX IX_{tableName}_Cleanup
                ON {fullTableName} (CompletedAtUtc)
                WHERE IsCompleted = 1;

                CREATE INDEX IX_{tableName}_AwaitingAck
                ON {fullTableName} (AckDeadlineUtc)
                WHERE CompletionStatus = 'AwaitingAck' AND IsCompleted = 0;
            END
            """;
    }

    public string GetBatchUpsertRemindersSql(string schemaName, string tableName, int count, bool insertIfAbsent = false)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";
        var values = string.Join(",\n                ",
            Enumerable.Range(0, count).Select(i =>
                $"(@ShardRegionName{i}, @EntityId{i}, @ReminderKey{i}, @WhenUtc{i}, @DueTimeUtc{i}, @RepeatIntervalTicks{i}, @SerializerId{i}, @Manifest{i}, @Payload{i}, @AttemptCount{i}, @LastFailureReason{i}, @MaxDeliveryWindowTicks{i}, @DeliveryDeadlineUtc{i})"));

        var whenMatched = insertIfAbsent ? "" : """
            WHEN MATCHED THEN
                UPDATE SET
                    WhenUtc = source.WhenUtc,
                    RepeatIntervalTicks = source.RepeatIntervalTicks,
                    SerializerId = source.SerializerId,
                    Manifest = source.Manifest,
                    Payload = source.Payload,
                    AttemptCount = source.AttemptCount,
                    LastFailureReason = source.LastFailureReason,
                    MaxDeliveryWindowTicks = source.MaxDeliveryWindowTicks,
                    DeliveryDeadlineUtc = source.DeliveryDeadlineUtc,
                    IsCompleted = 0,
                    CompletedAtUtc = NULL,
                    CompletionStatus = 'Pending',
                    DeliveredAtUtc = NULL,
                    AckDeadlineUtc = NULL
            """;

        return $"""
            MERGE {fullTableName}{(insertIfAbsent ? " WITH (HOLDLOCK)" : "")} AS target
            USING (VALUES
                {values}
            ) AS source(ShardRegionName, EntityId, ReminderKey, WhenUtc, DueTimeUtc, RepeatIntervalTicks,
                        SerializerId, Manifest, Payload, AttemptCount, LastFailureReason,
                        MaxDeliveryWindowTicks, DeliveryDeadlineUtc)
            ON target.ShardRegionName = source.ShardRegionName
               AND target.EntityId = source.EntityId
               AND target.ReminderKey = source.ReminderKey
               AND target.DueTimeUtc = source.DueTimeUtc
            {whenMatched}
            WHEN NOT MATCHED THEN
                INSERT (ShardRegionName, EntityId, ReminderKey, WhenUtc, DueTimeUtc, RepeatIntervalTicks,
                        SerializerId, Manifest, Payload, AttemptCount, LastFailureReason,
                        MaxDeliveryWindowTicks, DeliveryDeadlineUtc,
                        IsCompleted, CompletedAtUtc, CompletionStatus, DeliveredAtUtc, AckDeadlineUtc)
                VALUES (source.ShardRegionName, source.EntityId, source.ReminderKey, source.WhenUtc, source.DueTimeUtc,
                        source.RepeatIntervalTicks, source.SerializerId, source.Manifest, source.Payload,
                        source.AttemptCount, source.LastFailureReason, source.MaxDeliveryWindowTicks,
                        source.DeliveryDeadlineUtc, 0, NULL, 'Pending', NULL, NULL);
            """;
    }

    public string GetSelectDueRemindersSql(string schemaName, string tableName, int maxCount)
    {
        if (maxCount < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than or equal to 1.");

        var fullTableName = $"[{schemaName}].[{tableName}]";

        return $"""
            SELECT TOP ({maxCount}) ShardRegionName, EntityId, ReminderKey, WhenUtc, DueTimeUtc, RepeatIntervalTicks,
                   SerializerId, Manifest, Payload, AttemptCount, LastFailureReason,
                   MaxDeliveryWindowTicks, DeliveryDeadlineUtc
            FROM {fullTableName}
            WHERE IsCompleted = 0
              AND CompletionStatus = 'Pending'
              AND WhenUtc <= @UntilDeadline
              AND (DeliveryDeadlineUtc IS NULL OR DeliveryDeadlineUtc > @Now OR RepeatIntervalTicks IS NOT NULL)
            ORDER BY WhenUtc ASC;
            """;
    }

    public string GetBatchMarkCompletedSql(string schemaName, string tableName, int count, bool activeOnly = false)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";
        var values = string.Join(",\n                ",
            Enumerable.Range(0, count).Select(i => $"(@sr{i}, @eid{i}, @rk{i}, @due{i})"));

        return $"""
            UPDATE t
            SET t.IsCompleted = 1,
                t.CompletedAtUtc = @CompletedAtUtc,
                t.CompletionStatus = @CompletionStatus,
                t.DeliveredAtUtc = NULL,
                t.AckDeadlineUtc = NULL
            FROM {fullTableName} t
            INNER JOIN (VALUES
                {values}
            ) AS v(ShardRegionName, EntityId, ReminderKey, DueTimeUtc)
            ON t.ShardRegionName = v.ShardRegionName
               AND t.EntityId = v.EntityId
               AND t.ReminderKey = v.ReminderKey
               AND t.DueTimeUtc = v.DueTimeUtc
            {(activeOnly ? "WHERE t.CompletionStatus IN ('Pending', 'AwaitingAck')" : "")};
            """;
    }

    public string GetExpireRemindersSql(string schemaName, string tableName)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";

        return $"""
            UPDATE {fullTableName}
            SET IsCompleted = 1,
                CompletedAtUtc = @Now,
                CompletionStatus = 'Expired',
                DeliveredAtUtc = NULL,
                AckDeadlineUtc = NULL
            WHERE IsCompleted = 0
              AND DeliveryDeadlineUtc IS NOT NULL
              AND DeliveryDeadlineUtc <= @Now
              AND (CompletionStatus <> 'Pending' OR RepeatIntervalTicks IS NULL);
            """;
    }

    public string GetCleanupSql(string schemaName, string tableName)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";

        return $"""
            DELETE FROM {fullTableName}
            WHERE IsCompleted = 1
              AND CompletedAtUtc < @OlderThan;
            """;
    }

    public string GetOverviewAggregateSql(string schemaName, string tableName)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";

        return $"""
            SELECT COUNT(*) AS TotalCount, MIN(WhenUtc) AS NextWhenUtc
            FROM {fullTableName}
            WHERE IsCompleted = 0
              AND CompletionStatus = 'Pending'
              AND (DeliveryDeadlineUtc IS NULL OR DeliveryDeadlineUtc > @Now OR RepeatIntervalTicks IS NOT NULL);
            """;
    }

    public string GetNextReminderTimeSql(string schemaName, string tableName)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";

        return $"""
            SELECT WhenUtc
            FROM {fullTableName}
            WHERE IsCompleted = 0
              AND CompletionStatus = 'Pending'
              AND (DeliveryDeadlineUtc IS NULL OR DeliveryDeadlineUtc > @Now OR RepeatIntervalTicks IS NOT NULL)
            ORDER BY WhenUtc ASC
            OFFSET @Skip ROWS FETCH NEXT 1 ROW ONLY;
            """;
    }

    public string GetCancelReminderSql(string schemaName, string tableName)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";

        return $"""
            UPDATE {fullTableName}
            SET IsCompleted = 1,
                CompletedAtUtc = @CompletedAtUtc,
                CompletionStatus = 'Cancelled',
                DeliveredAtUtc = NULL,
                AckDeadlineUtc = NULL
            WHERE ShardRegionName = @ShardRegionName
              AND EntityId = @EntityId
              AND ReminderKey = @ReminderKey
              AND IsCompleted = 0;
            """;
    }

    public string GetCancelAllRemindersSql(string schemaName, string tableName)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";

        return $"""
            UPDATE {fullTableName}
            SET IsCompleted = 1,
                CompletedAtUtc = @CompletedAtUtc,
                CompletionStatus = 'Cancelled',
                DeliveredAtUtc = NULL,
                AckDeadlineUtc = NULL
            WHERE ShardRegionName = @ShardRegionName
              AND EntityId = @EntityId
              AND IsCompleted = 0;
            """;
    }

    public string GetFetchRemindersSql(string schemaName, string tableName)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";

        return $"""
            SELECT ShardRegionName, EntityId, ReminderKey, WhenUtc, DueTimeUtc, RepeatIntervalTicks,
                   SerializerId, Manifest, Payload, AttemptCount, LastFailureReason,
                   MaxDeliveryWindowTicks, DeliveryDeadlineUtc
            FROM {fullTableName}
            WHERE ShardRegionName = @ShardRegionName
              AND EntityId = @EntityId
              AND IsCompleted = 0
              AND (DeliveryDeadlineUtc IS NULL
                   OR DeliveryDeadlineUtc > @Now
                   OR (RepeatIntervalTicks IS NOT NULL AND CompletionStatus = 'Pending'))
            ORDER BY WhenUtc ASC;
            """;
    }

    public string GetBatchMarkAsAwaitingAckSql(string schemaName, string tableName, int count)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";
        var values = string.Join(",\n                ",
            Enumerable.Range(0, count).Select(i => $"(@sr{i}, @eid{i}, @rk{i}, @due{i}, @del{i}, @ack{i})"));

        return $"""
            UPDATE t
            SET t.CompletionStatus = 'AwaitingAck',
                t.DeliveredAtUtc = v.DeliveredAtUtc,
                t.AckDeadlineUtc = v.AckDeadlineUtc
            FROM {fullTableName} t
            INNER JOIN (VALUES
                {values}
            ) AS v(ShardRegionName, EntityId, ReminderKey, DueTimeUtc, DeliveredAtUtc, AckDeadlineUtc)
            ON t.ShardRegionName = v.ShardRegionName
               AND t.EntityId = v.EntityId
               AND t.ReminderKey = v.ReminderKey
               AND t.DueTimeUtc = v.DueTimeUtc
            WHERE t.IsCompleted = 0
              AND t.CompletionStatus = 'Pending';
            """;
    }

    public string GetTimedOutAckRemindersSql(string schemaName, string tableName, int maxCount)
    {
        if (maxCount < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be greater than or equal to 1.");

        var fullTableName = $"[{schemaName}].[{tableName}]";

        return $"""
            SELECT TOP ({maxCount}) ShardRegionName, EntityId, ReminderKey, WhenUtc, DueTimeUtc, RepeatIntervalTicks,
                   SerializerId, Manifest, Payload, AttemptCount, LastFailureReason,
                   MaxDeliveryWindowTicks, DeliveryDeadlineUtc
            FROM {fullTableName}
            WHERE CompletionStatus = 'AwaitingAck'
              AND IsCompleted = 0
              AND AckDeadlineUtc <= @Now
            ORDER BY AckDeadlineUtc ASC;
            """;
    }

    public string GetAcknowledgeReminderSql(string schemaName, string tableName)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";

        return $"""
            UPDATE {fullTableName}
            SET IsCompleted = 1,
                CompletedAtUtc = @AckedAtUtc,
                CompletionStatus = 'Delivered',
                DeliveredAtUtc = NULL,
                AckDeadlineUtc = NULL
            WHERE ShardRegionName = @ShardRegionName
              AND EntityId = @EntityId
              AND ReminderKey = @ReminderKey
              AND DueTimeUtc = @DueTimeUtc
              AND CompletionStatus = 'AwaitingAck'
              AND IsCompleted = 0
              AND (DeliveryDeadlineUtc IS NULL OR DeliveryDeadlineUtc > @AckedAtUtc);
            """;
    }

    public string GetBatchAcknowledgeRemindersSql(string schemaName, string tableName, int count)
    {
        var fullTableName = $"[{schemaName}].[{tableName}]";
        var values = string.Join(",\n                ",
            Enumerable.Range(0, count).Select(i => $"(@sr{i}, @eid{i}, @rk{i}, @due{i}, @acked{i})"));

        return $"""
            UPDATE t
            SET t.IsCompleted = 1,
                t.CompletedAtUtc = v.AckedAtUtc,
                t.CompletionStatus = 'Delivered',
                t.DeliveredAtUtc = NULL,
                t.AckDeadlineUtc = NULL
            FROM {fullTableName} t
            INNER JOIN (VALUES
                {values}
            ) AS v(ShardRegionName, EntityId, ReminderKey, DueTimeUtc, AckedAtUtc)
            ON t.ShardRegionName = v.ShardRegionName
               AND t.EntityId = v.EntityId
               AND t.ReminderKey = v.ReminderKey
               AND t.DueTimeUtc = v.DueTimeUtc
            WHERE t.CompletionStatus = 'AwaitingAck'
              AND t.IsCompleted = 0
              AND (t.DeliveryDeadlineUtc IS NULL OR t.DeliveryDeadlineUtc > v.AckedAtUtc);
            """;
    }

    public DbConnection CreateConnection(string connectionString)
    {
        return new SqlConnection(connectionString);
    }

    public void AddParameter(DbCommand command, string name, object value)
    {
        var sqlCommand = (SqlCommand)command;

        if (value == null)
        {
            sqlCommand.Parameters.AddWithValue(name, DBNull.Value);
            return;
        }

        switch (value)
        {
            case DateTimeOffset dto:
                sqlCommand.Parameters.Add(name, SqlDbType.DateTime2).Value = dto.UtcDateTime;
                break;
            case DateTime dt:
                sqlCommand.Parameters.Add(name, SqlDbType.DateTime2).Value = dt;
                break;
            case byte[] bytes:
                sqlCommand.Parameters.Add(name, SqlDbType.VarBinary, -1).Value = bytes;
                break;
            case string str:
                sqlCommand.Parameters.Add(name, SqlDbType.NVarChar, -1).Value = str;
                break;
            case long lng:
                sqlCommand.Parameters.Add(name, SqlDbType.BigInt).Value = lng;
                break;
            case int i:
                sqlCommand.Parameters.Add(name, SqlDbType.Int).Value = i;
                break;
            case bool b:
                sqlCommand.Parameters.Add(name, SqlDbType.Bit).Value = b;
                break;
            default:
                sqlCommand.Parameters.AddWithValue(name, value);
                break;
        }
    }
}
