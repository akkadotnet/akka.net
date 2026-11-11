using Akka.Actor;
using Akka.Reminders.Sql.Configuration;
using Akka.Reminders.Sqlite;
using Akka.Reminders.Sqlite.Configuration;
using Akka.Reminders.SqlServer;
using Akka.Reminders.SqlServer.Configuration;
using Akka.Reminders.PostgreSql;
using Akka.Reminders.PostgreSql.Configuration;

namespace Akka.Reminders.Sql.Hosting;

/// <summary>
/// Extension methods for configuring SQL storage with Akka.Hosting.
/// </summary>
public static class SqlStorageExtensions
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

    public static ReminderConfigurationBuilder WithPostgreSqlStorage(
        this ReminderConfigurationBuilder builder,
        string connectionString,
        string? schemaName = null,
        string? tableName = null,
        bool? autoInitialize = null)
    {
        var settings = PostgreSqlReminderStorageSettings.Create(
            connectionString,
            schemaName,
            tableName,
            autoInitialize);

        return builder.WithStorage(system => new PostgreSqlReminderStorage(settings, system));
    }

    public static ReminderConfigurationBuilder WithPostgreSqlStorageFromConfig(
        this ReminderConfigurationBuilder builder,
        string configPath = PostgreSqlReminderStorageSettings.DefaultConfigPath,
        string? connectionString = null)
    {
        return builder.WithStorage(system =>
        {
            var config = system.Settings.Config.GetConfig(configPath);

            if (config == null)
                throw new ArgumentException($"HOCON path '{configPath}' was not found.", nameof(configPath));

            var settings = PostgreSqlReminderStorageSettings.Create(config, connectionString);
            return new PostgreSqlReminderStorage(settings, system);
        });
    }

    public static ReminderConfigurationBuilder WithSqliteStorage(
        this ReminderConfigurationBuilder builder,
        string connectionString,
        string? tableName = null,
        bool? autoInitialize = null)
    {
        var settings = SqliteReminderStorageSettings.Create(
            connectionString,
            tableName,
            autoInitialize);

        return builder.WithStorage(system => new SqliteReminderStorage(settings, system));
    }

    public static ReminderConfigurationBuilder WithSqlStorage(
        this ReminderConfigurationBuilder builder,
        SqlReminderStorageSettings settings)
    {
        return builder.WithStorage(system => new SqlReminderStorage(settings, system));
    }

    public static ReminderConfigurationBuilder WithSqlStorage(
        this ReminderConfigurationBuilder builder,
        Func<ActorSystem, SqlReminderStorageSettings> settingsFactory)
    {
        return builder.WithStorage(system =>
        {
            var settings = settingsFactory(system);
            return new SqlReminderStorage(settings, system);
        });
    }
}
