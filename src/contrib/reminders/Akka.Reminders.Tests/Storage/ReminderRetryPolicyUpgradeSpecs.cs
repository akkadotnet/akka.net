#nullable enable

using System.Data.Common;
using Akka.Actor;
using Akka.Reminders.PostgreSql;
using Akka.Reminders.PostgreSql.Configuration;
using Akka.Reminders.SqlServer;
using Akka.Reminders.SqlServer.Configuration;
using Akka.Reminders.Sqlite;
using Akka.Reminders.Sqlite.Configuration;
using Akka.Reminders.Storage;
using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Akka.Reminders.Tests.Storage;

public sealed class ReminderRetryPolicyUpgradeSpecs
{
    [Theory(DisplayName = "Should_UpgradeExistingSchemaWithoutChangingOldPolicy_When_ProviderInitializes")]
    [InlineData("sqlserver")]
    [InlineData("postgres")]
    [InlineData("sqlite")]
    public async Task Should_UpgradeExistingSchemaWithoutChangingOldPolicy_When_ProviderInitializes(string provider)
    {
        var ct = TestContext.Current.CancellationToken;
        var system = ActorSystem.Create("retry-policy-upgrade");
        IContainer? container = null;
        var directory = Directory.CreateTempSubdirectory("reminder-policy-upgrade-");
        try
        {
            string connectionString;
            if (provider == "sqlserver")
            {
                var sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
                container = sql;
                await sql.StartAsync(ct);
                connectionString = sql.GetConnectionString();
            }
            else if (provider == "postgres")
            {
                var pg = new PostgreSqlBuilder("postgres:16-alpine").Build();
                container = pg;
                await pg.StartAsync(ct);
                connectionString = pg.GetConnectionString();
            }
            else
                connectionString = $"Data Source={Path.Combine(directory.FullName, "reminders.db")}";

            IReminderStorage OpenStorage() => provider switch
            {
                "sqlserver" => new SqlServerReminderStorage(SqlServerReminderStorageSettings.Create(connectionString), system),
                "postgres" => new PostgreSqlReminderStorage(PostgreSqlReminderStorageSettings.Create(connectionString), system),
                _ => new SqliteReminderStorage(SqliteReminderStorageSettings.Create(connectionString), system)
            };
            var entity = new ReminderEntity("upgrade", "e1");
            var old = new ScheduledReminder(entity, new ReminderKey("old"), DateTimeOffset.UtcNow.AddHours(1), "old");
            Assert.Equal(ReminderScheduleResponseCode.Success, (await OpenStorage().ScheduleReminderAsync(old, ct)).ResponseCode);
            await using (DbConnection connection = provider switch
            {
                "sqlserver" => new Microsoft.Data.SqlClient.SqlConnection(connectionString),
                "postgres" => new Npgsql.NpgsqlConnection(connectionString),
                _ => new Microsoft.Data.Sqlite.SqliteConnection(connectionString)
            })
            {
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                // Remove only the new column to reproduce the schema shipped before this feature.
                command.CommandText = provider switch
                {
                    "sqlserver" => """
                        DECLARE @constraint sysname;
                        SELECT @constraint = dc.name FROM sys.default_constraints dc
                        JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                        WHERE dc.parent_object_id = OBJECT_ID('reminders.scheduled_reminders') AND c.name = 'RetryPolicy';
                        EXEC('ALTER TABLE reminders.scheduled_reminders DROP CONSTRAINT [' + @constraint + ']');
                        ALTER TABLE reminders.scheduled_reminders DROP COLUMN RetryPolicy;
                        """,
                    "postgres" => "ALTER TABLE reminders.scheduled_reminders DROP COLUMN retry_policy;",
                    _ => "ALTER TABLE scheduled_reminders DROP COLUMN retry_policy;"
                };
                await command.ExecuteNonQueryAsync(ct);
            }
            var upgraded = OpenStorage();
            var newRow = old with { Key = new ReminderKey("new"), RetryPolicy = ReminderRetryPolicy.UntilAcknowledged };
            Assert.Equal(ReminderScheduleResponseCode.Success, (await upgraded.ScheduleReminderAsync(newRow, ct)).ResponseCode);
            // A fresh instance also exercises repeated initialization and persisted reads.
            var rows = await OpenStorage().GetRemindersForEntityAsync(entity, ct: ct);
            Assert.Equal(ReminderRetryPolicy.UseConfiguredAttemptLimit, Assert.Single(rows, row => row.Key == old.Key).RetryPolicy);
            Assert.Equal(ReminderRetryPolicy.UntilAcknowledged, Assert.Single(rows, row => row.Key == newRow.Key).RetryPolicy);
        }
        finally
        {
            await system.Terminate();
            if (container is not null)
                await container.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }
}
