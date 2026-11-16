-- Run against the reminder database after stopping scheduler/storage providers.
-- Replace the quoted schema/table names if PostgreSqlReminderStorageSettings differs.
BEGIN;
ALTER TABLE "reminders"."scheduled_reminders"
    ADD COLUMN IF NOT EXISTS retry_policy INTEGER NOT NULL DEFAULT 0;
COMMIT;
