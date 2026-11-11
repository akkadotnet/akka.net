-- Akka.Reminders SQL Server Schema Creation Script
-- This script creates the schema and table for storing reminders in SQL Server.
--
-- Usage:
--   1. Review and modify the schema name if needed (default: 'reminders')
--   2. Review and modify the table name if needed (default: 'scheduled_reminders')
--   3. Execute this script against your SQL Server database
--
-- Note: This script is idempotent - it will only create objects if they don't already exist.

-- Variables (modify as needed)
DECLARE @SchemaName NVARCHAR(255) = 'reminders';
DECLARE @TableName NVARCHAR(255) = 'scheduled_reminders';

-- Create schema if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = @SchemaName)
BEGIN
    EXEC('CREATE SCHEMA [' + @SchemaName + ']')
    PRINT 'Created schema: ' + @SchemaName
END
ELSE
BEGIN
    PRINT 'Schema already exists: ' + @SchemaName
END

-- Create table if it doesn't exist
DECLARE @FullTableName NVARCHAR(500) = '[' + @SchemaName + '].[' + @TableName + ']';

IF NOT EXISTS (
    SELECT * FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = @SchemaName AND t.name = @TableName
)
BEGIN
    DECLARE @CreateTableSql NVARCHAR(MAX) = '
    CREATE TABLE ' + @FullTableName + ' (
        ShardRegionName NVARCHAR(255) NOT NULL,
        EntityId NVARCHAR(255) NOT NULL,
        ReminderKey NVARCHAR(255) NOT NULL,
        WhenUtc DATETIME2 NOT NULL,
        DueTimeUtc DATETIME2 NOT NULL,
        RepeatIntervalTicks BIGINT NULL,
        SerializerId INT NOT NULL,
        Manifest NVARCHAR(500) NULL,
        Payload VARBINARY(MAX) NOT NULL,
        AttemptCount INT NOT NULL DEFAULT 0,
        LastFailureReason NVARCHAR(MAX) NULL,
        MaxDeliveryWindowTicks BIGINT NULL,
        DeliveryDeadlineUtc DATETIME2 NULL,
        IsCompleted BIT NOT NULL DEFAULT 0,
        CompletedAtUtc DATETIME2 NULL,
        CompletionStatus VARCHAR(20) NOT NULL DEFAULT ''Pending'',
        DeliveredAtUtc DATETIME2 NULL,
        AckDeadlineUtc DATETIME2 NULL,

        CONSTRAINT PK_' + @TableName + ' PRIMARY KEY (ShardRegionName, EntityId, ReminderKey, DueTimeUtc)
    );';

    EXEC(@CreateTableSql);
    PRINT 'Created table: ' + @FullTableName

    DECLARE @IndexName1 NVARCHAR(500) = 'IX_' + @TableName + '_DueReminders';
    DECLARE @CreateIndex1Sql NVARCHAR(MAX) = '
    CREATE INDEX ' + @IndexName1 + '
    ON ' + @FullTableName + ' (WhenUtc, ShardRegionName, EntityId)
    WHERE IsCompleted = 0 AND CompletionStatus = ''Pending'';';

    EXEC(@CreateIndex1Sql);
    PRINT 'Created index: ' + @IndexName1

    DECLARE @IndexName2 NVARCHAR(500) = 'IX_' + @TableName + '_Cleanup';
    DECLARE @CreateIndex2Sql NVARCHAR(MAX) = '
    CREATE INDEX ' + @IndexName2 + '
    ON ' + @FullTableName + ' (CompletedAtUtc)
    WHERE IsCompleted = 1;';

    EXEC(@CreateIndex2Sql);
    PRINT 'Created index: ' + @IndexName2

    DECLARE @IndexName3 NVARCHAR(500) = 'IX_' + @TableName + '_AwaitingAck';
    DECLARE @CreateIndex3Sql NVARCHAR(MAX) = '
    CREATE INDEX ' + @IndexName3 + '
    ON ' + @FullTableName + ' (AckDeadlineUtc)
    WHERE CompletionStatus = ''AwaitingAck'' AND IsCompleted = 0;';

    EXEC(@CreateIndex3Sql);
    PRINT 'Created index: ' + @IndexName3
END
ELSE
BEGIN
    PRINT 'Table already exists: ' + @FullTableName
END

PRINT 'Schema setup complete!'
