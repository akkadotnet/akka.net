using Akka.Actor;
using Akka.Reminders.Sqlite;
using Akka.Reminders.Sqlite.Configuration;
using Akka.Reminders.Storage;
using Microsoft.Data.Sqlite;

namespace Akka.Reminders.Migration.Tests;

[Collection("Sqlite-Migration")]
public class SqliteMigrationSpecs : IAsyncLifetime
{
    private ActorSystem? _actorSystem;
    private IReminderStorage? _storage;
    private string? _databasePath;

    /// <summary>
    /// The 0.5.x schema: PK is (shard_region_name, entity_id, reminder_key).
    /// No due_time_utc, max_delivery_window_ticks, delivery_deadline_utc, delivered_at_utc, ack_deadline_utc.
    /// Index filter is just WHERE is_completed = 0.
    /// </summary>
    private const string V05xCreateSchema = @"
CREATE TABLE IF NOT EXISTS scheduled_reminders (
    shard_region_name TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    reminder_key TEXT NOT NULL,
    when_utc TEXT NOT NULL,
    repeat_interval_ticks INTEGER NULL,
    serializer_id INTEGER NOT NULL,
    manifest TEXT NULL,
    payload BLOB NOT NULL,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    last_failure_reason TEXT NULL,
    is_completed INTEGER NOT NULL DEFAULT 0,
    completed_at_utc TEXT NULL,
    completion_status TEXT NOT NULL DEFAULT 'Pending',

    PRIMARY KEY (shard_region_name, entity_id, reminder_key)
);

CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_due_reminders
ON scheduled_reminders (when_utc, shard_region_name, entity_id)
WHERE is_completed = 0;

CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_cleanup
ON scheduled_reminders (completed_at_utc)
WHERE is_completed = 1;
";

    private const string MigrationSql = @"
ALTER TABLE scheduled_reminders RENAME TO scheduled_reminders_v05x;

CREATE TABLE IF NOT EXISTS scheduled_reminders (
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

INSERT INTO scheduled_reminders (
    shard_region_name, entity_id, reminder_key,
    when_utc, due_time_utc, repeat_interval_ticks,
    serializer_id, manifest, payload,
    attempt_count, last_failure_reason,
    max_delivery_window_ticks, delivery_deadline_utc,
    is_completed, completed_at_utc, completion_status,
    delivered_at_utc, ack_deadline_utc
)
SELECT
    shard_region_name, entity_id, reminder_key,
    when_utc, when_utc AS due_time_utc, repeat_interval_ticks,
    serializer_id, manifest, payload,
    attempt_count, last_failure_reason,
    NULL, NULL,
    is_completed, completed_at_utc, completion_status,
    NULL, NULL
FROM scheduled_reminders_v05x;

DROP TABLE scheduled_reminders_v05x;

CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_due_reminders
ON scheduled_reminders (when_utc, shard_region_name, entity_id)
WHERE is_completed = 0 AND completion_status = 'Pending';

CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_cleanup
ON scheduled_reminders (completed_at_utc)
WHERE is_completed = 1;

CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_awaiting_ack
ON scheduled_reminders (ack_deadline_utc)
WHERE completion_status = 'AwaitingAck' AND is_completed = 0;
";

    private const string SeedDataSql = @"
INSERT INTO scheduled_reminders
    (shard_region_name, entity_id, reminder_key, when_utc, repeat_interval_ticks,
     serializer_id, manifest, payload, attempt_count, is_completed, completion_status)
VALUES
    ('test-region', 'entity-1', 'seed-reminder',
     datetime('now', '+1 hour'), NULL,
     1, 'System.String', X'01', 0, 0, 'Pending');
";

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"akka-reminders-migration-{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared";

        // Execute 0.5.x schema creation
        await ExecuteSqlAsync(connStr, V05xCreateSchema);

        // Insert seed data
        await ExecuteSqlAsync(connStr, SeedDataSql);

        // Run migration
        await ExecuteSqlAsync(connStr, MigrationSql);

        // Mark the seed row as completed so it doesn't interfere with API tests
        // (its payload isn't valid Akka serialization — it exists only to test the migration backfill)
        await ExecuteSqlAsync(connStr,
            "UPDATE scheduled_reminders SET is_completed = 1, completion_status = 'Delivered', completed_at_utc = datetime('now') WHERE reminder_key = 'seed-reminder'");

        // Create ActorSystem and storage provider
        _actorSystem = ActorSystem.Create("sqlite-migration-test");
        var settings = SqliteReminderStorageSettings.Create(connStr) with { AutoInitialize = false };
        _storage = new SqliteReminderStorage(settings, _actorSystem);
    }

    public async Task DisposeAsync()
    {
        if (_actorSystem != null)
        {
            await _actorSystem.Terminate();
        }

        if (!string.IsNullOrWhiteSpace(_databasePath) && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task MigratedData_ShouldHaveDueTimeEqualToWhenUtc()
    {
        // Verify via raw SQL — the seed row's payload isn't a valid Akka serialized message,
        // so we can't read it through the storage API. We only need to check the column values.
        var connStr = $"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared";
        await using var connection = new SqliteConnection(connStr);
        await connection.OpenAsync();
        await using var cmd = new SqliteCommand(
            "SELECT when_utc, due_time_utc FROM scheduled_reminders WHERE reminder_key = 'seed-reminder'",
            connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var whenUtc = reader.GetString(0);
        var dueTimeUtc = reader.GetString(1);
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
        // Schedule a reminder so the overview has something to report
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
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
