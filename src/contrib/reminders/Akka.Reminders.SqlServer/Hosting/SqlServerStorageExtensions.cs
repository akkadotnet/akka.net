using System.Data.Common;
using Akka.Actor;
using Akka.Reminders.SqlServer.Configuration;

namespace Akka.Reminders.SqlServer.Hosting;

/// <summary>
/// Extension methods for configuring SQL Server reminder storage.
/// </summary>
public static class SqlServerStorageExtensions
{
    public static ReminderConfigurationBuilder WithSqlServerStorage(
        this ReminderConfigurationBuilder builder,
        string connectionString,
        string? schemaName = null,
        string? tableName = null,
        bool? autoInitialize = null)
    {
        var settings = SqlServerReminderStorageSettings.Create(
            connectionString,
            schemaName,
            tableName,
            autoInitialize);

        return builder.WithStorage(system => new SqlServerReminderStorage(settings, system));
    }

    /// <summary>
    /// Configures SQL Server reminder storage that gets its connections from <paramref name="connectionFactory"/>
    /// instead of a connection string, for example to authenticate with an Azure AD access token.
    /// </summary>
    /// <param name="builder">The reminder configuration builder.</param>
    /// <param name="connectionFactory">
    /// Returns a new, unopened connection. Called once per storage operation, including schema
    /// auto-initialization. The storage opens and disposes the connection.
    /// </param>
    /// <param name="schemaName">Optional schema name (defaults to "reminders").</param>
    /// <param name="tableName">Optional table name (defaults to "scheduled_reminders").</param>
    /// <param name="autoInitialize">Whether to create the schema and table if they don't exist (defaults to true).</param>
    /// <returns>The builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// reminders.WithSqlServerStorage(
    ///     connectionFactory: async ct => new SqlConnection(connectionString)
    ///     {
    ///         AccessToken = await tokenProvider.GetTokenAsync(ct)
    ///     },
    ///     autoInitialize: true);
    /// </code>
    /// </example>
    public static ReminderConfigurationBuilder WithSqlServerStorage(
        this ReminderConfigurationBuilder builder,
        Func<CancellationToken, ValueTask<DbConnection>> connectionFactory,
        string? schemaName = null,
        string? tableName = null,
        bool? autoInitialize = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        var settings = new SqlServerReminderStorageSettings
        {
            ConnectionFactory = connectionFactory,
            SchemaName = schemaName ?? "reminders",
            TableName = tableName ?? "scheduled_reminders",
            AutoInitialize = autoInitialize ?? true
        };

        return builder.WithStorage(system => new SqlServerReminderStorage(settings, system));
    }

    public static ReminderConfigurationBuilder WithSqlServerStorage(
        this ReminderConfigurationBuilder builder,
        SqlServerReminderStorageSettings settings)
    {
        return builder.WithStorage(system => new SqlServerReminderStorage(settings, system));
    }

    public static ReminderConfigurationBuilder WithSqlServerStorage(
        this ReminderConfigurationBuilder builder,
        Func<ActorSystem, SqlServerReminderStorageSettings> settingsFactory)
    {
        return builder.WithStorage(system =>
        {
            var settings = settingsFactory(system);
            return new SqlServerReminderStorage(settings, system);
        });
    }
}
