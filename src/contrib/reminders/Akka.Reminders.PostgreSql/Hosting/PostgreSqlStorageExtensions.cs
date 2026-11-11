using Akka.Actor;
using Akka.Reminders.PostgreSql.Configuration;

namespace Akka.Reminders.PostgreSql.Hosting;

/// <summary>
/// Extension methods for configuring PostgreSQL reminder storage.
/// </summary>
public static class PostgreSqlStorageExtensions
{
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

    public static ReminderConfigurationBuilder WithPostgreSqlStorage(
        this ReminderConfigurationBuilder builder,
        PostgreSqlReminderStorageSettings settings)
    {
        return builder.WithStorage(system => new PostgreSqlReminderStorage(settings, system));
    }

    public static ReminderConfigurationBuilder WithPostgreSqlStorage(
        this ReminderConfigurationBuilder builder,
        Func<ActorSystem, PostgreSqlReminderStorageSettings> settingsFactory)
    {
        return builder.WithStorage(system =>
        {
            var settings = settingsFactory(system);
            return new PostgreSqlReminderStorage(settings, system);
        });
    }

    /// <summary>
    /// Configures PostgreSQL storage using HOCON settings.
    /// </summary>
    /// <param name="builder">Reminder configuration builder.</param>
    /// <param name="configPath">HOCON section path. Defaults to akka.reminders.postgresql.</param>
    /// <param name="connectionString">Optional connection string override.</param>
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
}
