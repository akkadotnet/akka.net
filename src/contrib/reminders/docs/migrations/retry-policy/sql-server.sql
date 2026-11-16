-- Run against the reminder database after stopping scheduler/storage providers.
-- Set these names to match SqlServerReminderStorageSettings.
SET XACT_ABORT ON;
DECLARE @SchemaName sysname = N'reminders';
DECLARE @TableName sysname = N'scheduled_reminders';
DECLARE @QualifiedName nvarchar(517) = QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName);

BEGIN TRANSACTION;
IF OBJECT_ID(@QualifiedName, N'U') IS NULL
    THROW 51000, 'The configured reminder table does not exist.', 1;

IF COL_LENGTH(@QualifiedName, N'RetryPolicy') IS NULL
    EXEC(N'ALTER TABLE ' + @QualifiedName + N' ADD [RetryPolicy] INT NOT NULL DEFAULT 0;');
COMMIT TRANSACTION;
