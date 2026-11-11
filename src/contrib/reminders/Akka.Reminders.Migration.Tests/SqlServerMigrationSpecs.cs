using Akka.Actor;
using Akka.Reminders.SqlServer;
using Akka.Reminders.SqlServer.Configuration;
using Akka.Reminders.Storage;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Akka.Reminders.Migration.Tests;

[Collection("SqlServer-Migration")]
public class SqlServerMigrationSpecs : IAsyncLifetime
{
    private MsSqlContainer? _container;
    private ActorSystem? _actorSystem;
    private IReminderStorage? _storage;

    /// <summary>
    /// The 0.5.x schema: PK is (ShardRegionName, EntityId, ReminderKey).
    /// No DueTimeUtc, MaxDeliveryWindowTicks, DeliveryDeadlineUtc, DeliveredAtUtc, AckDeadlineUtc.
    /// Index filter is just WHERE IsCompleted = 0.
    /// </summary>
    private const string V05xCreateSchema = @"
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'reminders')
BEGIN
    EXEC('CREATE SCHEMA [reminders]');
END

IF NOT EXISTS (
    SELECT * FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'reminders' AND t.name = 'scheduled_reminders'
)
BEGIN
    CREATE TABLE [reminders].[scheduled_reminders] (
        ShardRegionName NVARCHAR(255) NOT NULL,
        EntityId NVARCHAR(255) NOT NULL,
        ReminderKey NVARCHAR(255) NOT NULL,
        WhenUtc DATETIME2 NOT NULL,
        RepeatIntervalTicks BIGINT NULL,
        SerializerId INT NOT NULL,
        Manifest NVARCHAR(500) NULL,
        Payload VARBINARY(MAX) NOT NULL,
        AttemptCount INT NOT NULL DEFAULT 0,
        LastFailureReason NVARCHAR(MAX) NULL,
        IsCompleted BIT NOT NULL DEFAULT 0,
        CompletedAtUtc DATETIME2 NULL,
        CompletionStatus VARCHAR(20) NOT NULL DEFAULT 'Pending',

        CONSTRAINT PK_scheduled_reminders PRIMARY KEY (ShardRegionName, EntityId, ReminderKey)
    );

    CREATE INDEX IX_scheduled_reminders_DueReminders
    ON [reminders].[scheduled_reminders] (WhenUtc, ShardRegionName, EntityId)
    WHERE IsCompleted = 0;

    CREATE INDEX IX_scheduled_reminders_Cleanup
    ON [reminders].[scheduled_reminders] (CompletedAtUtc)
    WHERE IsCompleted = 1;
END
";

    private const string MigrationSql = @"
DECLARE @SchemaName NVARCHAR(255) = 'reminders';
DECLARE @TableName  NVARCHAR(255) = 'scheduled_reminders';
DECLARE @FullTableName NVARCHAR(500) = '[' + @SchemaName + '].[' + @TableName + ']';

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables  t ON c.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND c.name = 'DueTimeUtc'
)
BEGIN
    EXEC('ALTER TABLE ' + @FullTableName + ' ADD DueTimeUtc DATETIME2 NULL');
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables  t ON c.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND c.name = 'MaxDeliveryWindowTicks'
)
BEGIN
    EXEC('ALTER TABLE ' + @FullTableName + ' ADD MaxDeliveryWindowTicks BIGINT NULL');
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables  t ON c.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND c.name = 'DeliveryDeadlineUtc'
)
BEGIN
    EXEC('ALTER TABLE ' + @FullTableName + ' ADD DeliveryDeadlineUtc DATETIME2 NULL');
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables  t ON c.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND c.name = 'DeliveredAtUtc'
)
BEGIN
    EXEC('ALTER TABLE ' + @FullTableName + ' ADD DeliveredAtUtc DATETIME2 NULL');
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables  t ON c.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND c.name = 'AckDeadlineUtc'
)
BEGIN
    EXEC('ALTER TABLE ' + @FullTableName + ' ADD AckDeadlineUtc DATETIME2 NULL');
END

EXEC('UPDATE ' + @FullTableName + ' SET DueTimeUtc = WhenUtc WHERE DueTimeUtc IS NULL');
EXEC('ALTER TABLE ' + @FullTableName + ' ALTER COLUMN DueTimeUtc DATETIME2 NOT NULL');

DECLARE @PKName NVARCHAR(500) = 'PK_' + @TableName;

IF EXISTS (
    SELECT 1 FROM sys.key_constraints kc
    JOIN sys.tables  t ON kc.parent_object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND kc.name = @PKName AND kc.type = 'PK'
)
BEGIN
    EXEC('ALTER TABLE ' + @FullTableName + ' DROP CONSTRAINT ' + @PKName);
END

EXEC('ALTER TABLE ' + @FullTableName + ' ADD CONSTRAINT ' + @PKName + ' PRIMARY KEY (ShardRegionName, EntityId, ReminderKey, DueTimeUtc)');

DECLARE @DueIndexName NVARCHAR(500) = 'IX_' + @TableName + '_DueReminders';

IF EXISTS (
    SELECT 1 FROM sys.indexes i
    JOIN sys.tables  t ON i.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND i.name = @DueIndexName
)
BEGIN
    EXEC('DROP INDEX ' + @DueIndexName + ' ON ' + @FullTableName);
END

DECLARE @CreateDueIndexSql NVARCHAR(MAX) =
    'CREATE INDEX ' + @DueIndexName +
    ' ON ' + @FullTableName + ' (WhenUtc, ShardRegionName, EntityId)' +
    ' WHERE IsCompleted = 0 AND CompletionStatus = ''Pending'';';
EXEC(@CreateDueIndexSql);

DECLARE @AckIndexName NVARCHAR(500) = 'IX_' + @TableName + '_AwaitingAck';

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    JOIN sys.tables  t ON i.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND i.name = @AckIndexName
)
BEGIN
    DECLARE @CreateAckIndexSql NVARCHAR(MAX) =
        'CREATE INDEX ' + @AckIndexName +
        ' ON ' + @FullTableName + ' (AckDeadlineUtc)' +
        ' WHERE CompletionStatus = ''AwaitingAck'' AND IsCompleted = 0;';
    EXEC(@CreateAckIndexSql);
END
";

    private const string SeedDataSql = @"
INSERT INTO [reminders].[scheduled_reminders]
    (ShardRegionName, EntityId, ReminderKey, WhenUtc, RepeatIntervalTicks,
     SerializerId, Manifest, Payload, AttemptCount, IsCompleted, CompletionStatus)
VALUES
    ('test-region', 'entity-1', 'seed-reminder',
     DATEADD(HOUR, 1, SYSUTCDATETIME()), NULL,
     1, 'System.String', 0x01, 0, 0, 'Pending');
";

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("yourStrong(!)Password")
            .Build();

        await _container.StartAsync();
        var connStr = _container.GetConnectionString();

        // Execute 0.5.x schema creation
        await ExecuteSqlAsync(connStr, V05xCreateSchema);

        // Insert seed data
        await ExecuteSqlAsync(connStr, SeedDataSql);

        // Run migration
        await ExecuteSqlAsync(connStr, MigrationSql);

        // Mark the seed row as completed so it doesn't interfere with API tests
        // (its payload isn't valid Akka serialization — it exists only to test the migration backfill)
        await ExecuteSqlAsync(connStr,
            "UPDATE [reminders].[scheduled_reminders] SET IsCompleted = 1, CompletionStatus = 'Delivered', CompletedAtUtc = SYSUTCDATETIME() WHERE ReminderKey = 'seed-reminder'");

        // Create ActorSystem and storage provider
        _actorSystem = ActorSystem.Create("sqlserver-migration-test");
        var settings = SqlServerReminderStorageSettings.Create(connStr) with { AutoInitialize = false };
        _storage = new SqlServerReminderStorage(settings, _actorSystem);
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }

        if (_actorSystem != null)
        {
            await _actorSystem.Terminate();
        }
    }

    [Fact]
    public async Task MigratedData_ShouldHaveDueTimeEqualToWhenUtc()
    {
        // Verify via raw SQL — the seed row's payload isn't a valid Akka serialized message,
        // so we can't read it through the storage API. We only need to check the column values.
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_container!.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
            "SELECT WhenUtc, DueTimeUtc FROM [reminders].[scheduled_reminders] WHERE ReminderKey = 'seed-reminder'",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var whenUtc = reader.GetDateTime(0);
        var dueTimeUtc = reader.GetDateTime(1);
        Assert.Equal(whenUtc, dueTimeUtc);
    }

    [Fact]
    public async Task MigratedSchema_ShouldSupportSchedulingNewReminders()
    {
        var entity = new ReminderEntity("test-region", "entity-new");
        var key = new ReminderKey("new-reminder");
        var dueTime = DateTimeOffset.UtcNow.AddMinutes(30);

        var reminder = new ScheduledReminder(
            entity,
            key,
            dueTime,
            "test message",
            RepeatInterval: null,
            MaxDeliveryWindow: null,
            DeliveryDeadlineUtc: null,
            OccurrenceDueTimeUtc: dueTime);

        var result = await _storage!.ScheduleReminderAsync(reminder, default);
        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        var fetched = await _storage.GetRemindersForEntityAsync(entity, ct: default);
        Assert.Contains(fetched, r => r.Key == key);
    }

    [Fact]
    public async Task MigratedSchema_ShouldSupportAckWorkflow()
    {
        var entity = new ReminderEntity("test-region", "entity-ack");
        var key = new ReminderKey("ack-test-reminder");
        var now = DateTimeOffset.UtcNow;
        var dueTime = now.AddMinutes(1);

        var reminder = new ScheduledReminder(
            entity,
            key,
            dueTime,
            "test message",
            RepeatInterval: null,
            MaxDeliveryWindow: null,
            DeliveryDeadlineUtc: null,
            OccurrenceDueTimeUtc: dueTime);

        await _storage!.ScheduleReminderAsync(reminder, default);

        // Fetch via GetNextRemindersAsync
        var pending = await _storage.GetNextRemindersAsync(
            dueTime.AddMinutes(1), now, new ReminderBatchSize(100), default);

        var targetReminder = pending.Reminders.FirstOrDefault(r => r.Key == key);
        Assert.NotNull(targetReminder);

        // Move to AwaitingAck via CommitReminderMutationsAsync
        var deliveredAt = now;
        var ackDeadline = now.AddMinutes(5);
        var awaitingAck = new AwaitingAckReminder(entity, key, targetReminder.DueTimeUtc, deliveredAt, ackDeadline);
        var batch = new ReminderMutationBatch([], [], [awaitingAck]);
        var commitResult = await _storage.CommitReminderMutationsAsync(batch, default);
        Assert.True(commitResult);

        // Acknowledge via AcknowledgeRemindersAsync
        var ack = new ReminderAcknowledgement(entity, key, targetReminder.DueTimeUtc, now.AddSeconds(30));
        var ackResults = await _storage.AcknowledgeRemindersAsync([ack], default);
        Assert.Single(ackResults);
        Assert.Equal(ReminderAckStorageStatus.Success, ackResults[0].Status);
    }

    [Fact]
    public async Task MigratedSchema_ShouldSupportOverviewQuery()
    {
        var entity = new ReminderEntity("test-region", "entity-overview");
        var dueTime = DateTimeOffset.UtcNow.AddMinutes(10);
        await _storage!.ScheduleReminderAsync(new ScheduledReminder(
            entity, new ReminderKey("overview-reminder"), dueTime, "test",
            RepeatInterval: null, MaxDeliveryWindow: null,
            DeliveryDeadlineUtc: null, OccurrenceDueTimeUtc: dueTime), default);

        var overview = await _storage.GetRemindersOverviewAsync(DateTimeOffset.UtcNow, default);
        Assert.True(overview.TotalPendingReminders >= 1);
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
