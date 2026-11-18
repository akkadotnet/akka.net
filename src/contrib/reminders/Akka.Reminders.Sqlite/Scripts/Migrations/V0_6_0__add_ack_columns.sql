-- Migration for Akka.Reminders 0.6.0: Full schema upgrade from 0.5.x
-- SQLite cannot ALTER PRIMARY KEY, so we rename-and-recreate the table.
--
-- Steps:
--   1. Rename existing table to scheduled_reminders_v05x
--   2. Create new table with full 0.6.0 schema (includes due_time_utc in PK)
--   3. Copy data from old table, mapping when_utc -> due_time_utc
--   4. Drop old table
--   5. Create all indexes
--
-- Note: This script uses the default table name 'scheduled_reminders'.
--       If you use a custom table name, replace the table name accordingly.

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
