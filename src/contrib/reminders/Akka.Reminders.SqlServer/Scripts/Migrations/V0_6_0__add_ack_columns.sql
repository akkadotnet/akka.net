-- Migration for Akka.Reminders 0.6.0: Full schema upgrade from 0.5.x
-- Adds DueTimeUtc, MaxDeliveryWindowTicks, DeliveryDeadlineUtc, DeliveredAtUtc,
-- AckDeadlineUtc columns; expands PK to include DueTimeUtc; updates indexes.
--
-- This script is idempotent: each operation is guarded by sys.columns / sys.indexes checks.
--
-- Note: This script uses the default schema 'reminders' and table 'scheduled_reminders'.
--       If you use a custom schema or table name, replace them accordingly.

DECLARE @SchemaName NVARCHAR(255) = 'reminders';
DECLARE @TableName  NVARCHAR(255) = 'scheduled_reminders';
DECLARE @FullTableName NVARCHAR(500) = '[' + @SchemaName + '].[' + @TableName + ']';

-- ============================================================
-- 1. Add the five new columns
-- ============================================================

-- Add DueTimeUtc (initially nullable so we can backfill)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables  t ON c.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND c.name = 'DueTimeUtc'
)
BEGIN
    EXEC('ALTER TABLE ' + @FullTableName + ' ADD DueTimeUtc DATETIME2 NULL');
    PRINT 'Added column DueTimeUtc to ' + @FullTableName;
END
ELSE
BEGIN
    PRINT 'Column DueTimeUtc already exists on ' + @FullTableName;
END

-- Add MaxDeliveryWindowTicks
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables  t ON c.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND c.name = 'MaxDeliveryWindowTicks'
)
BEGIN
    EXEC('ALTER TABLE ' + @FullTableName + ' ADD MaxDeliveryWindowTicks BIGINT NULL');
    PRINT 'Added column MaxDeliveryWindowTicks to ' + @FullTableName;
END
ELSE
BEGIN
    PRINT 'Column MaxDeliveryWindowTicks already exists on ' + @FullTableName;
END

-- Add DeliveryDeadlineUtc
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables  t ON c.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND c.name = 'DeliveryDeadlineUtc'
)
BEGIN
    EXEC('ALTER TABLE ' + @FullTableName + ' ADD DeliveryDeadlineUtc DATETIME2 NULL');
    PRINT 'Added column DeliveryDeadlineUtc to ' + @FullTableName;
END
ELSE
BEGIN
    PRINT 'Column DeliveryDeadlineUtc already exists on ' + @FullTableName;
END

-- Add DeliveredAtUtc
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables  t ON c.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND c.name = 'DeliveredAtUtc'
)
BEGIN
    EXEC('ALTER TABLE ' + @FullTableName + ' ADD DeliveredAtUtc DATETIME2 NULL');
    PRINT 'Added column DeliveredAtUtc to ' + @FullTableName;
END
ELSE
BEGIN
    PRINT 'Column DeliveredAtUtc already exists on ' + @FullTableName;
END

-- Add AckDeadlineUtc
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables  t ON c.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND c.name = 'AckDeadlineUtc'
)
BEGIN
    EXEC('ALTER TABLE ' + @FullTableName + ' ADD AckDeadlineUtc DATETIME2 NULL');
    PRINT 'Added column AckDeadlineUtc to ' + @FullTableName;
END
ELSE
BEGIN
    PRINT 'Column AckDeadlineUtc already exists on ' + @FullTableName;
END

-- ============================================================
-- 2. Backfill DueTimeUtc from WhenUtc, then make NOT NULL
-- ============================================================
EXEC('UPDATE ' + @FullTableName + ' SET DueTimeUtc = WhenUtc WHERE DueTimeUtc IS NULL');
PRINT 'Backfilled DueTimeUtc from WhenUtc for existing rows.';

-- Make DueTimeUtc NOT NULL
EXEC('ALTER TABLE ' + @FullTableName + ' ALTER COLUMN DueTimeUtc DATETIME2 NOT NULL');
PRINT 'Set DueTimeUtc to NOT NULL.';

-- ============================================================
-- 3. Recreate primary key to include DueTimeUtc
-- ============================================================
DECLARE @PKName NVARCHAR(500) = 'PK_' + @TableName;

IF EXISTS (
    SELECT 1 FROM sys.key_constraints kc
    JOIN sys.tables  t ON kc.parent_object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND kc.name = @PKName AND kc.type = 'PK'
)
BEGIN
    EXEC('ALTER TABLE ' + @FullTableName + ' DROP CONSTRAINT ' + @PKName);
    PRINT 'Dropped old primary key: ' + @PKName;
END

EXEC('ALTER TABLE ' + @FullTableName + ' ADD CONSTRAINT ' + @PKName + ' PRIMARY KEY (ShardRegionName, EntityId, ReminderKey, DueTimeUtc)');
PRINT 'Created new primary key: ' + @PKName;

-- ============================================================
-- 4. Recreate the due-reminders index with updated filter
-- ============================================================
DECLARE @DueIndexName NVARCHAR(500) = 'IX_' + @TableName + '_DueReminders';

IF EXISTS (
    SELECT 1 FROM sys.indexes i
    JOIN sys.tables  t ON i.object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName AND i.name = @DueIndexName
)
BEGIN
    EXEC('DROP INDEX ' + @DueIndexName + ' ON ' + @FullTableName);
    PRINT 'Dropped old index: ' + @DueIndexName;
END

DECLARE @CreateDueIndexSql NVARCHAR(MAX) =
    'CREATE INDEX ' + @DueIndexName +
    ' ON ' + @FullTableName + ' (WhenUtc, ShardRegionName, EntityId)' +
    ' WHERE IsCompleted = 0 AND CompletionStatus = ''Pending'';';
EXEC(@CreateDueIndexSql);
PRINT 'Created index: ' + @DueIndexName;

-- ============================================================
-- 5. Create the awaiting-ack index
-- ============================================================
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
    PRINT 'Created index: ' + @AckIndexName;
END
ELSE
BEGIN
    PRINT 'Index ' + @AckIndexName + ' already exists.';
END

PRINT 'Migration V0_6_0 complete.';
