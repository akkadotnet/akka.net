-- Run against the configured SQLite file after stopping scheduler/storage providers.
-- Replace the quoted table name if SqliteReminderStorageSettings differs.
-- First run the precheck in README.md. Execute this script only if retry_policy is absent.
-- SQLite does not support ADD COLUMN IF NOT EXISTS; this is a one-time migration.
BEGIN IMMEDIATE;
ALTER TABLE "scheduled_reminders" ADD COLUMN retry_policy INTEGER NOT NULL DEFAULT 0;
COMMIT;
