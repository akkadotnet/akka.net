using Akka.Actor;
using Akka.Reminders.PostgreSql;
using Akka.Reminders.PostgreSql.Configuration;
using Akka.Reminders.Storage;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Akka.Reminders.Migration.Tests;

[Collection("PostgreSQL-Migration")]
public class PostgreSqlMigrationSpecs : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private ActorSystem? _actorSystem;
    private IReminderStorage? _storage;

    /// <summary>
    /// The 0.5.x schema: PK is (shard_region_name, entity_id, reminder_key).
    /// No due_time_utc, max_delivery_window_ticks, delivery_deadline_utc, delivered_at_utc, ack_deadline_utc.
    /// Index filter is just WHERE is_completed = FALSE.
    /// </summary>
    private const string V05xCreateSchema = @"
CREATE SCHEMA IF NOT EXISTS reminders;

CREATE TABLE IF NOT EXISTS reminders.scheduled_reminders (
    shard_region_name VARCHAR(255) NOT NULL,
    entity_id VARCHAR(255) NOT NULL,
    reminder_key VARCHAR(255) NOT NULL,
    when_utc TIMESTAMP WITH TIME ZONE NOT NULL,
    repeat_interval_ticks BIGINT NULL,
    serializer_id INTEGER NOT NULL,
    manifest VARCHAR(500) NULL,
    payload BYTEA NOT NULL,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    last_failure_reason TEXT NULL,
    is_completed BOOLEAN NOT NULL DEFAULT FALSE,
    completed_at_utc TIMESTAMP WITH TIME ZONE NULL,
    completion_status VARCHAR(20) NOT NULL DEFAULT 'Pending',

    CONSTRAINT pk_scheduled_reminders PRIMARY KEY (shard_region_name, entity_id, reminder_key)
);

CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_due_reminders
ON reminders.scheduled_reminders (when_utc, shard_region_name, entity_id)
WHERE is_completed = FALSE;

CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_cleanup
ON reminders.scheduled_reminders (completed_at_utc)
WHERE is_completed = TRUE;
";

    private const string MigrationSql = @"
ALTER TABLE reminders.scheduled_reminders ADD COLUMN IF NOT EXISTS due_time_utc TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE reminders.scheduled_reminders ADD COLUMN IF NOT EXISTS max_delivery_window_ticks BIGINT NULL;
ALTER TABLE reminders.scheduled_reminders ADD COLUMN IF NOT EXISTS delivery_deadline_utc TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE reminders.scheduled_reminders ADD COLUMN IF NOT EXISTS delivered_at_utc TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE reminders.scheduled_reminders ADD COLUMN IF NOT EXISTS ack_deadline_utc TIMESTAMP WITH TIME ZONE NULL;

UPDATE reminders.scheduled_reminders SET due_time_utc = when_utc WHERE due_time_utc IS NULL;
ALTER TABLE reminders.scheduled_reminders ALTER COLUMN due_time_utc SET NOT NULL;

ALTER TABLE reminders.scheduled_reminders DROP CONSTRAINT IF EXISTS pk_scheduled_reminders;
ALTER TABLE reminders.scheduled_reminders
    ADD CONSTRAINT pk_scheduled_reminders PRIMARY KEY (shard_region_name, entity_id, reminder_key, due_time_utc);

DROP INDEX IF EXISTS reminders.ix_scheduled_reminders_due_reminders;
CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_due_reminders
ON reminders.scheduled_reminders (when_utc, shard_region_name, entity_id)
WHERE is_completed = FALSE AND completion_status = 'Pending';

CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_awaiting_ack
ON reminders.scheduled_reminders (ack_deadline_utc)
WHERE completion_status = 'AwaitingAck' AND is_completed = FALSE;
";

    private const string SeedDataSql = @"
INSERT INTO reminders.scheduled_reminders
    (shard_region_name, entity_id, reminder_key, when_utc, repeat_interval_ticks,
     serializer_id, manifest, payload, attempt_count, is_completed, completion_status)
VALUES
    ('test-region', 'entity-1', 'seed-reminder',
     NOW() + INTERVAL '1 hour', NULL,
     1, 'System.String', E'\\x01', 0, FALSE, 'Pending');
";

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
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
            "UPDATE reminders.scheduled_reminders SET is_completed = TRUE, completion_status = 'Delivered', completed_at_utc = NOW() WHERE reminder_key = 'seed-reminder'");

        // Create ActorSystem and storage provider
        _actorSystem = ActorSystem.Create("pg-migration-test");
        var settings = PostgreSqlReminderStorageSettings.Create(connStr) with { AutoInitialize = false };
        _storage = new PostgreSqlReminderStorage(settings, _actorSystem);
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
        await using var conn = new Npgsql.NpgsqlConnection(_container!.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT when_utc, due_time_utc FROM reminders.scheduled_reminders WHERE reminder_key = 'seed-reminder'",
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
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
