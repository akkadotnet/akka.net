namespace Akka.Reminders.Sqlite.Configuration;

/// <summary>
/// Configuration settings for SQLite reminder storage.
/// </summary>
public sealed record SqliteReminderStorageSettings
{
    /// <summary>
    /// The SQLite connection string.
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// The table name for storing reminders.
    /// Default: "scheduled_reminders"
    /// </summary>
    public string TableName { get; init; } = "scheduled_reminders";

    /// <summary>
    /// Whether to automatically create the table if it doesn't exist.
    /// Default: true
    /// </summary>
    public bool AutoInitialize { get; init; } = true;

    /// <summary>
    /// The timeout for database operations.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates settings for SQLite.
    /// </summary>
    public static SqliteReminderStorageSettings Create(
        string connectionString,
        string? tableName = null,
        bool? autoInitialize = null)
    {
        return new SqliteReminderStorageSettings
        {
            ConnectionString = connectionString,
            TableName = tableName ?? "scheduled_reminders",
            AutoInitialize = autoInitialize ?? true
        };
    }

    /// <summary>
    /// Validates the settings and throws if invalid.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("ConnectionString cannot be null or empty.", nameof(ConnectionString));

        if (string.IsNullOrWhiteSpace(TableName))
            throw new ArgumentException("TableName cannot be null or empty.", nameof(TableName));

        if (CommandTimeout <= TimeSpan.Zero)
            throw new ArgumentException("CommandTimeout must be positive.", nameof(CommandTimeout));
    }
}
