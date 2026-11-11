using Akka.Configuration;

namespace Akka.Reminders.PostgreSql.Configuration;

/// <summary>
/// Configuration settings for PostgreSQL reminder storage.
/// </summary>
public sealed record PostgreSqlReminderStorageSettings
{
    /// <summary>
    /// Default HOCON path for PostgreSQL reminder storage settings.
    /// </summary>
    public const string DefaultConfigPath = "akka.reminders.postgresql";

    /// <summary>
    /// The PostgreSQL connection string.
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// The schema name for the reminders table.
    /// Default: "reminders"
    /// </summary>
    public string SchemaName { get; init; } = "reminders";

    /// <summary>
    /// The table name for storing reminders.
    /// Default: "scheduled_reminders"
    /// </summary>
    public string TableName { get; init; } = "scheduled_reminders";

    /// <summary>
    /// Whether to automatically create the schema and table if they don't exist.
    /// Default: true
    /// </summary>
    public bool AutoInitialize { get; init; } = true;

    /// <summary>
    /// The timeout for database operations.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates settings for PostgreSQL.
    /// </summary>
    public static PostgreSqlReminderStorageSettings Create(
        string connectionString,
        string? schemaName = null,
        string? tableName = null,
        bool? autoInitialize = null)
    {
        return new PostgreSqlReminderStorageSettings
        {
            ConnectionString = connectionString,
            SchemaName = schemaName ?? "reminders",
            TableName = tableName ?? "scheduled_reminders",
            AutoInitialize = autoInitialize ?? true
        };
    }

    /// <summary>
    /// Creates settings from HOCON.
    /// </summary>
    /// <param name="config">HOCON config section.</param>
    /// <param name="connectionString">Optional connection string override.</param>
    public static PostgreSqlReminderStorageSettings Create(
        Config config,
        string? connectionString = null)
    {
        var resolvedConnectionString = connectionString ?? config.GetString("connection-string", null);

        if (string.IsNullOrWhiteSpace(resolvedConnectionString))
            throw new ArgumentException(
                "connection-string is required in HOCON config or must be provided explicitly.",
                nameof(connectionString));

        return new PostgreSqlReminderStorageSettings
        {
            ConnectionString = resolvedConnectionString,
            SchemaName = config.GetString("schema-name", "reminders"),
            TableName = config.GetString("table-name", "scheduled_reminders"),
            AutoInitialize = config.HasPath("auto-initialize") ? config.GetBoolean("auto-initialize") : true,
            CommandTimeout = config.HasPath("command-timeout")
                ? config.GetTimeSpan("command-timeout")
                : TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Validates the settings and throws if invalid.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("ConnectionString cannot be null or empty.", nameof(ConnectionString));

        if (string.IsNullOrWhiteSpace(SchemaName))
            throw new ArgumentException("SchemaName cannot be null or empty.", nameof(SchemaName));

        if (string.IsNullOrWhiteSpace(TableName))
            throw new ArgumentException("TableName cannot be null or empty.", nameof(TableName));

        if (CommandTimeout <= TimeSpan.Zero)
            throw new ArgumentException("CommandTimeout must be positive.", nameof(CommandTimeout));
    }
}
