#nullable enable

using System.Data.Common;

namespace Akka.Reminders.SqlServer.Internal;

internal interface ISqlDialect
{
    string GetCreateTableSql(string schemaName, string tableName);
    string GetBatchUpsertRemindersSql(string schemaName, string tableName, int count, bool insertIfAbsent = false, bool includeRetryPolicy = true);
    string GetSelectDueRemindersSql(string schemaName, string tableName, int maxCount, bool includeRetryPolicy = true);
    string GetBatchMarkCompletedSql(string schemaName, string tableName, int count, bool activeOnly = false);
    string GetExpireRemindersSql(string schemaName, string tableName);
    string GetCleanupSql(string schemaName, string tableName);
    string GetOverviewAggregateSql(string schemaName, string tableName);
    string GetNextReminderTimeSql(string schemaName, string tableName);
    string GetCancelReminderSql(string schemaName, string tableName);
    string GetCancelAllRemindersSql(string schemaName, string tableName);
    string GetFetchRemindersSql(string schemaName, string tableName, bool includeRetryPolicy = true);
    string GetBatchMarkAsAwaitingAckSql(string schemaName, string tableName, int count);
    string GetTimedOutAckRemindersSql(string schemaName, string tableName, int maxCount, bool includeRetryPolicy = true);
    string GetAcknowledgeReminderSql(string schemaName, string tableName);
    string GetBatchAcknowledgeRemindersSql(string schemaName, string tableName, int count);
    DbConnection CreateConnection(string connectionString);
    void AddParameter(DbCommand command, string name, object value);
}
