using System.Data.Common;

namespace Akka.Reminders.SqlServer.Configuration;

/// <summary>
/// Configuration settings for SQL Server reminder storage.
/// </summary>
/// <remarks>
/// Set exactly one of <see cref="ConnectionString"/> or <see cref="ConnectionFactory"/>.
/// </remarks>
public sealed record SqlServerReminderStorageSettings
{
    /// <summary>
    /// The SQL Server connection string. Required unless <see cref="ConnectionFactory"/> is set.
    /// </summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Creates the connection for each storage operation, as an alternative to <see cref="ConnectionString"/>.
    /// Use it when the connection needs configuration a connection string can't carry, such as an
    /// Azure AD access token in <c>SqlConnection.AccessToken</c>.
    /// </summary>
    /// <remarks>
    /// The factory is called once per storage operation, including schema auto-initialization, so it can
    /// return a fresh token each time. It must return a new, unopened connection; the storage opens and
    /// disposes it. Must not be set together with <see cref="ConnectionString"/>.
    /// </remarks>
    public Func<CancellationToken, ValueTask<DbConnection>>? ConnectionFactory { get; init; }

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
    /// Creates settings for SQL Server.
    /// </summary>
    public static SqlServerReminderStorageSettings Create(
        string connectionString,
        string? schemaName = null,
        string? tableName = null,
        bool? autoInitialize = null)
    {
        return new SqlServerReminderStorageSettings
        {
            ConnectionString = connectionString,
            SchemaName = schemaName ?? "reminders",
            TableName = tableName ?? "scheduled_reminders",
            AutoInitialize = autoInitialize ?? true
        };
    }

    /// <summary>
    /// Validates the settings and throws if invalid.
    /// </summary>
    public void Validate()
    {
        var hasConnectionString = !string.IsNullOrWhiteSpace(ConnectionString);

        if (!hasConnectionString && ConnectionFactory is null)
            throw new ArgumentException(
                "ConnectionString cannot be null or empty. Set either ConnectionString or ConnectionFactory.",
                nameof(ConnectionString));

        if (hasConnectionString && ConnectionFactory is not null)
            throw new ArgumentException(
                "ConnectionString and ConnectionFactory cannot both be set. Set exactly one of them.",
                nameof(ConnectionFactory));

        if (string.IsNullOrWhiteSpace(SchemaName))
            throw new ArgumentException("SchemaName cannot be null or empty.", nameof(SchemaName));

        if (string.IsNullOrWhiteSpace(TableName))
            throw new ArgumentException("TableName cannot be null or empty.", nameof(TableName));

        if (CommandTimeout <= TimeSpan.Zero)
            throw new ArgumentException("CommandTimeout must be positive.", nameof(CommandTimeout));
    }
}
