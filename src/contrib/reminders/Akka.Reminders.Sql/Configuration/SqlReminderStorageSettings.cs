using Akka.Reminders.PostgreSql.Configuration;
using Akka.Reminders.Sqlite.Configuration;
using Akka.Reminders.SqlServer.Configuration;

namespace Akka.Reminders.Sql.Configuration;

/// <summary>
/// Compatibility settings for SQL-based reminder storage.
/// </summary>
public sealed record SqlReminderStorageSettings
{
    /// <summary>
    /// The database connection string.
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// The database provider name.
    /// Supported values: "SqlServer", "PostgreSql", "Sqlite"
    /// </summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// The schema name for providers that support schemas.
    /// Default: "reminders"
    /// </summary>
    public string SchemaName { get; init; } = "reminders";

    /// <summary>
    /// The table name for storing reminders.
    /// Default: "scheduled_reminders"
    /// </summary>
    public string TableName { get; init; } = "scheduled_reminders";

    /// <summary>
    /// Whether to automatically create schema/table if they don't exist.
    /// Default: true
    /// </summary>
    public bool AutoInitialize { get; init; } = true;

    /// <summary>
    /// The timeout for database operations.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public static SqlReminderStorageSettings CreateSqlServer(
        string connectionString,
        string? schemaName = null,
        string? tableName = null,
        bool? autoInitialize = null)
    {
        return new SqlReminderStorageSettings
        {
            ConnectionString = connectionString,
            ProviderName = "SqlServer",
            SchemaName = schemaName ?? "reminders",
            TableName = tableName ?? "scheduled_reminders",
            AutoInitialize = autoInitialize ?? true
        };
    }

    public static SqlReminderStorageSettings CreatePostgreSql(
        string connectionString,
        string? schemaName = null,
        string? tableName = null,
        bool? autoInitialize = null)
    {
        return new SqlReminderStorageSettings
        {
            ConnectionString = connectionString,
            ProviderName = "PostgreSql",
            SchemaName = schemaName ?? "reminders",
            TableName = tableName ?? "scheduled_reminders",
            AutoInitialize = autoInitialize ?? true
        };
    }

    public static SqlReminderStorageSettings CreateSqlite(
        string connectionString,
        string? tableName = null,
        bool? autoInitialize = null)
    {
        return new SqlReminderStorageSettings
        {
            ConnectionString = connectionString,
            ProviderName = "Sqlite",
            TableName = tableName ?? "scheduled_reminders",
            AutoInitialize = autoInitialize ?? true
        };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("ConnectionString cannot be null or empty.", nameof(ConnectionString));

        if (string.IsNullOrWhiteSpace(ProviderName))
            throw new ArgumentException("ProviderName cannot be null or empty.", nameof(ProviderName));

        if (ProviderName != "SqlServer" && ProviderName != "PostgreSql" && ProviderName != "Sqlite")
            throw new ArgumentException(
                $"Unsupported provider: {ProviderName}. Supported providers: SqlServer, PostgreSql, Sqlite",
                nameof(ProviderName));

        if (ProviderName != "Sqlite" && string.IsNullOrWhiteSpace(SchemaName))
            throw new ArgumentException("SchemaName cannot be null or empty.", nameof(SchemaName));

        if (string.IsNullOrWhiteSpace(TableName))
            throw new ArgumentException("TableName cannot be null or empty.", nameof(TableName));

        if (CommandTimeout <= TimeSpan.Zero)
            throw new ArgumentException("CommandTimeout must be positive.", nameof(CommandTimeout));
    }

    internal SqlServerReminderStorageSettings ToSqlServerSettings()
    {
        return new SqlServerReminderStorageSettings
        {
            ConnectionString = ConnectionString,
            SchemaName = SchemaName,
            TableName = TableName,
            AutoInitialize = AutoInitialize,
            CommandTimeout = CommandTimeout
        };
    }

    internal PostgreSqlReminderStorageSettings ToPostgreSqlSettings()
    {
        return new PostgreSqlReminderStorageSettings
        {
            ConnectionString = ConnectionString,
            SchemaName = SchemaName,
            TableName = TableName,
            AutoInitialize = AutoInitialize,
            CommandTimeout = CommandTimeout
        };
    }

    internal SqliteReminderStorageSettings ToSqliteSettings()
    {
        return new SqliteReminderStorageSettings
        {
            ConnectionString = ConnectionString,
            TableName = TableName,
            AutoInitialize = AutoInitialize,
            CommandTimeout = CommandTimeout
        };
    }
}
