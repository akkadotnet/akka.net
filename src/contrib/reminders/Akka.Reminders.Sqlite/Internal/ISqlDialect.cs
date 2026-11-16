#nullable enable

using System.Data.Common;

namespace Akka.Reminders.Sqlite.Internal;

internal interface ISqlDialect
{
    string GetCreateTableSql(string tableName);
    string GetBatchUpsertRemindersSql(string tableName, int count, bool insertIfAbsent = false, bool includeRetryPolicy = true);
    string GetSelectDueRemindersSql(string tableName, int maxCount, bool includeRetryPolicy = true);
    string GetBatchMarkCompletedSql(string tableName, int count, bool activeOnly = false);
    string GetExpireRemindersSql(string tableName);
    string GetCleanupSql(string tableName);
    string GetOverviewAggregateSql(string tableName);
    string GetNextReminderTimeSql(string tableName);
    string GetCancelReminderSql(string tableName);
    string GetCancelAllRemindersSql(string tableName);
    string GetFetchRemindersSql(string tableName, bool includeRetryPolicy = true);
    string GetBatchMarkAsAwaitingAckSql(string tableName, int count);
    string GetTimedOutAckRemindersSql(string tableName, int maxCount, bool includeRetryPolicy = true);
    string GetAcknowledgeReminderSql(string tableName);
    string GetBatchAcknowledgeRemindersSql(string tableName, int count);
    DbConnection CreateConnection(string connectionString);
    void AddParameter(DbCommand command, string name, object value);
}
