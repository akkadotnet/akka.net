using Akka.Configuration;
using Akka.Reminders.PostgreSql.Configuration;

namespace Akka.Reminders.Tests.Storage;

public sealed class PostgreSqlReminderStorageSettingsSpecs
{
    [Fact]
    public void CreateFromHocon_ShouldUseRemindersSchemaByDefault()
    {
        var config = ConfigurationFactory.ParseString("""
            akka.reminders.postgresql {
              connection-string = "Host=localhost;Database=reminders;Username=postgres;Password=postgres"
            }
            """).GetConfig(PostgreSqlReminderStorageSettings.DefaultConfigPath);

        var settings = PostgreSqlReminderStorageSettings.Create(config);

        Assert.Equal("reminders", settings.SchemaName);
        Assert.Equal("scheduled_reminders", settings.TableName);
        Assert.True(settings.AutoInitialize);
    }

    [Fact]
    public void CreateFromHocon_ShouldRespectConfiguredSchema()
    {
        var config = ConfigurationFactory.ParseString("""
            akka.reminders.postgresql {
              connection-string = "Host=localhost;Database=reminders;Username=postgres;Password=postgres"
              schema-name = "public"
              table-name = "Reminders"
              auto-initialize = false
            }
            """).GetConfig(PostgreSqlReminderStorageSettings.DefaultConfigPath);

        var settings = PostgreSqlReminderStorageSettings.Create(config);

        Assert.Equal("public", settings.SchemaName);
        Assert.Equal("Reminders", settings.TableName);
        Assert.False(settings.AutoInitialize);
    }
}
