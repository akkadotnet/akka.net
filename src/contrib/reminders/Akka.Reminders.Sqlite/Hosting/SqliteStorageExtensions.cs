using Akka.Actor;
using Akka.Reminders.Sqlite.Configuration;

namespace Akka.Reminders.Sqlite.Hosting;

/// <summary>
/// Extension methods for configuring SQLite reminder storage.
/// </summary>
public static class SqliteStorageExtensions
{
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

    public static ReminderConfigurationBuilder WithSqliteStorage(
        this ReminderConfigurationBuilder builder,
        SqliteReminderStorageSettings settings)
    {
        return builder.WithStorage(system => new SqliteReminderStorage(settings, system));
    }

    public static ReminderConfigurationBuilder WithSqliteStorage(
        this ReminderConfigurationBuilder builder,
        Func<ActorSystem, SqliteReminderStorageSettings> settingsFactory)
    {
        return builder.WithStorage(system =>
        {
            var settings = settingsFactory(system);
            return new SqliteReminderStorage(settings, system);
        });
    }
}
