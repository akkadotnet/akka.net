-- Akka.Reminders PostgreSQL Schema Creation Script
-- This script creates the schema and table for storing reminders in PostgreSQL.
--
-- Usage:
--   1. Review and modify the schema name if needed (default: 'reminders')
--   2. Review and modify the table name if needed (default: 'scheduled_reminders')
--   3. Execute this script against your PostgreSQL database
--
-- Note: This script is idempotent - it will only create objects if they don't already exist.

-- Create schema if it doesn't exist
CREATE SCHEMA IF NOT EXISTS reminders;

-- Create table if it doesn't exist
CREATE TABLE IF NOT EXISTS reminders.scheduled_reminders (
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

    CONSTRAINT pk_scheduled_reminders PRIMARY KEY (shard_region_name, entity_id, reminder_key, due_time_utc)
);

-- Create filtered index for efficient queries on pending reminders
CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_due_reminders
ON reminders.scheduled_reminders (when_utc, shard_region_name, entity_id)
WHERE is_completed = FALSE AND completion_status = 'Pending';

-- Create index for cleanup operations
CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_cleanup
ON reminders.scheduled_reminders (completed_at_utc)
WHERE is_completed = TRUE;

-- Create filtered index for efficient ack timeout scanning
CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_awaiting_ack
ON reminders.scheduled_reminders (ack_deadline_utc)
WHERE completion_status = 'AwaitingAck' AND is_completed = FALSE;

-- Display confirmation
DO $$
BEGIN
    RAISE NOTICE 'Schema setup complete!';
    RAISE NOTICE 'Schema: reminders';
    RAISE NOTICE 'Table: scheduled_reminders';
END $$;
