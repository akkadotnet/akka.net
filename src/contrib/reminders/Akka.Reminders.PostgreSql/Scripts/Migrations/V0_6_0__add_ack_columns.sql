-- Migration for Akka.Reminders 0.6.0: Full schema upgrade from 0.5.x
-- Adds due_time_utc, max_delivery_window_ticks, delivery_deadline_utc, delivered_at_utc,
-- ack_deadline_utc columns; expands PK to include due_time_utc; updates indexes.
--
-- This script is idempotent: IF NOT EXISTS / ADD COLUMN IF NOT EXISTS guards
-- prevent errors on repeated runs.
--
-- Note: This script uses the default schema 'reminders' and table 'scheduled_reminders'.
--       If you use a custom schema or table name, replace them accordingly.

-- 1. Add the five new columns
ALTER TABLE reminders.scheduled_reminders ADD COLUMN IF NOT EXISTS due_time_utc TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE reminders.scheduled_reminders ADD COLUMN IF NOT EXISTS max_delivery_window_ticks BIGINT NULL;
ALTER TABLE reminders.scheduled_reminders ADD COLUMN IF NOT EXISTS delivery_deadline_utc TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE reminders.scheduled_reminders ADD COLUMN IF NOT EXISTS delivered_at_utc TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE reminders.scheduled_reminders ADD COLUMN IF NOT EXISTS ack_deadline_utc TIMESTAMP WITH TIME ZONE NULL;

-- 2. Backfill due_time_utc from when_utc for existing rows, then make NOT NULL
UPDATE reminders.scheduled_reminders SET due_time_utc = when_utc WHERE due_time_utc IS NULL;
ALTER TABLE reminders.scheduled_reminders ALTER COLUMN due_time_utc SET NOT NULL;

-- 3. Recreate primary key to include due_time_utc
--    Drop the old PK (region, entity, key) and create the new one (region, entity, key, due_time_utc)
ALTER TABLE reminders.scheduled_reminders DROP CONSTRAINT IF EXISTS pk_scheduled_reminders;
ALTER TABLE reminders.scheduled_reminders
    ADD CONSTRAINT pk_scheduled_reminders PRIMARY KEY (shard_region_name, entity_id, reminder_key, due_time_utc);

-- 4. Recreate the due-reminders index with updated filter
DROP INDEX IF EXISTS reminders.ix_scheduled_reminders_due_reminders;
CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_due_reminders
ON reminders.scheduled_reminders (when_utc, shard_region_name, entity_id)
WHERE is_completed = FALSE AND completion_status = 'Pending';

-- 5. Create the awaiting-ack index
CREATE INDEX IF NOT EXISTS ix_scheduled_reminders_awaiting_ack
ON reminders.scheduled_reminders (ack_deadline_utc)
WHERE completion_status = 'AwaitingAck' AND is_completed = FALSE;
