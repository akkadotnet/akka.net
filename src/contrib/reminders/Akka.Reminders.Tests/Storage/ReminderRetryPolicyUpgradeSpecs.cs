#nullable enable

using System.Data.Common;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Xunit;
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
    [Theory(DisplayName = "Should_PreserveRetryPolicies_When_CreatingOrUpgradingSchema")]
    [InlineData("sqlserver", true, false)]
    [InlineData("postgres", true, false)]
    [InlineData("sqlite", true, false)]
    [InlineData("sqlserver", false, false)]
    [InlineData("postgres", false, false)]
    [InlineData("sqlite", false, false)]
    [InlineData("sqlserver", false, true)]
    [InlineData("postgres", false, true)]
    [InlineData("sqlite", false, true)]
    public async Task Should_PreserveRetryPolicies_When_CreatingOrUpgradingSchema(string provider, bool autoInitialize, bool freshSchema)
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

            IReminderStorage OpenStorage(bool initialize = true) => provider switch
            {
                "sqlserver" => new SqlServerReminderStorage(SqlServerReminderStorageSettings.Create(connectionString) with { AutoInitialize = initialize }, system),
                "postgres" => new PostgreSqlReminderStorage(PostgreSqlReminderStorageSettings.Create(connectionString) with { AutoInitialize = initialize }, system),
                _ => new SqliteReminderStorage(SqliteReminderStorageSettings.Create(connectionString) with { AutoInitialize = initialize }, system)
            };
            DbConnection Connect() => provider switch
            {
                "sqlserver" => new Microsoft.Data.SqlClient.SqlConnection(connectionString),
                "postgres" => new Npgsql.NpgsqlConnection(connectionString),
                _ => new Microsoft.Data.Sqlite.SqliteConnection(connectionString)
            };
            if (freshSchema)
            {
                var assembly = provider switch
                {
                    "sqlserver" => typeof(SqlServerReminderStorage).Assembly,
                    "postgres" => typeof(PostgreSqlReminderStorage).Assembly,
                    _ => typeof(SqliteReminderStorage).Assembly
                };
                var resource = Assert.Single(assembly.GetManifestResourceNames(), name => name.EndsWith("-Create.sql", StringComparison.Ordinal));
                using var stream = assembly.GetManifestResourceStream(resource);
                Assert.NotNull(stream);
                using var reader = new StreamReader(stream);
                var creationSql = await reader.ReadToEndAsync(ct);
                await using var connection = Connect();
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = creationSql;
                await command.ExecuteNonQueryAsync(ct);
                await command.ExecuteNonQueryAsync(ct); // Fresh-install scripts remain safe to repeat.
            }
            var entity = new ReminderEntity("upgrade", "e1");
            var old = new ScheduledReminder(entity, new ReminderKey("old"), DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).AddHours(1), "old");
            Assert.Equal(ReminderScheduleResponseCode.Success, (await OpenStorage(initialize: !freshSchema).ScheduleReminderAsync(old, ct)).ResponseCode);
            if (!freshSchema)
            {
                await using (var connection = Connect())
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
            }
            if (!autoInitialize && !freshSchema)
            {
                var warnings = new TestProbe(system, new XunitAssertions());
                system.EventStream.Subscribe(warnings.Ref, typeof(Warning));
                var legacy = OpenStorage(false);
                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => legacy.GetRemindersOverviewAsync(old.When, cancelled.Token));
                }
                // Concurrent first use must publish one schema decision and one upgrade warning.
                await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => legacy.GetRemindersOverviewAsync(old.When, ct)));
                var warning = await warnings.ExpectMsgAsync<Warning>(TimeSpan.FromSeconds(5), cancellationToken: ct);
                const string procedure = "https://github.com/akkadotnet/akka.net/blob/dev/src/contrib/reminders/docs/migrations/retry-policy/README.md";
                Assert.Contains(procedure, warning.Message.ToString());
                Assert.Contains("configured attempt limit", warning.Message.ToString());
                var unsupported = old with { RetryPolicy = ReminderRetryPolicy.UntilAcknowledged };
                var rejection = await legacy.ScheduleReminderAsync(unsupported, ct);
                Assert.Equal(ReminderScheduleResponseCode.Error, rejection.ResponseCode);
                Assert.Contains(procedure, rejection.Message);
                var retained = Assert.Single(await legacy.GetRemindersForEntityAsync(entity, ct: ct));
                Assert.Equal(old.Key, retained.Key);
                Assert.Equal(ReminderRetryPolicy.UseConfiguredAttemptLimit, retained.RetryPolicy);

                var now = old.When.AddHours(-1);
                var bounded = old with { Key = new ReminderKey("bounded"), When = now.AddSeconds(-1), OccurrenceDueTimeUtc = now.AddSeconds(-1) };
                Assert.Equal(ReminderScheduleResponseCode.Success, (await legacy.ScheduleReminderAsync(bounded, ct)).ResponseCode);
                var due = Assert.Single((await legacy.GetNextRemindersAsync(now, now, new ReminderBatchSize(10), ct)).Reminders);
                Assert.Equal(bounded.Key, due.Key);
                Assert.Equal(ReminderRetryPolicy.UseConfiguredAttemptLimit, due.RetryPolicy);
                Assert.True(await legacy.MarkRemindersAsAwaitingAckAsync(
                    [new AwaitingAckReminder(entity, bounded.Key, bounded.DueTimeUtc, now, now.AddMinutes(1))], ct));
                Assert.Equal(ReminderRetryPolicy.UseConfiguredAttemptLimit,
                    (await legacy.GetAwaitingAckReminderAsync(entity, bounded.Key, bounded.DueTimeUtc, ct))?.RetryPolicy);
                Assert.Equal(ReminderRetryPolicy.UseConfiguredAttemptLimit,
                    Assert.Single(await legacy.GetTimedOutAckRemindersAsync(now.AddMinutes(2), new ReminderBatchSize(10), ct)).RetryPolicy);
                var retry = bounded with { When = now.AddSeconds(10), AttemptCount = 1 };
                Assert.True(await legacy.CommitReminderMutationsAsync(new ReminderMutationBatch([retry], [], [])
                { PendingInserts = [old with { Key = new ReminderKey("conditional") }] }, ct));
                var retried = Assert.Single(await legacy.GetRemindersForEntityAsync(entity, ct: ct), row => row.Key == bounded.Key);
                Assert.Equal(1, retried.AttemptCount);
                Assert.Equal(ReminderRetryPolicy.UseConfiguredAttemptLimit, retried.RetryPolicy);
                Assert.True(await legacy.MarkRemindersAsAwaitingAckAsync(
                    [new AwaitingAckReminder(entity, bounded.Key, bounded.DueTimeUtc, now, now.AddMinutes(1))], ct));
                Assert.True((await legacy.AcknowledgeReminderAsync(entity, bounded.Key, bounded.DueTimeUtc, now, ct)).Success);
                Assert.Equal(ReminderCompletionStatus.Delivered,
                    (await legacy.GetReminderOccurrenceStatusAsync(entity, bounded.Key, bounded.DueTimeUtc, ct))?.CompletionStatus);

                Assert.Equal(ReminderCancelResponseCode.Success,
                    (await legacy.CancelReminderAsync(entity, new ReminderKey("conditional"), ct)).ResponseCode);
                // More rows than an upsert chunk: rejecting the last row must leave no partial writes.
                var mixed = Enumerable.Range(0, 130).Select(i => old with { Key = new ReminderKey($"mixed-{i}") }).Append(unsupported).ToArray();
                Assert.False(await legacy.UpsertReminderOccurrencesAsync(mixed, ct));
                Assert.False(await legacy.CommitReminderMutationsAsync(new ReminderMutationBatch([unsupported], [], [])
                { PendingInserts = [mixed[0]] }, ct));
                Assert.Equal(old.Key, Assert.Single(await legacy.GetRemindersForEntityAsync(entity, take: 1000, ct: ct)).Key);
                Assert.Equal(ReminderCancelResponseCode.Success, (await legacy.CancelAllRemindersForEntityAsync(entity, ct)).ResponseCode);
                Assert.True(await legacy.CleanUpCompletedRemindersAsync(now.AddDays(1), ct));
                await warnings.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100), ct);
                system.EventStream.Unsubscribe(warnings.Ref);
                await using var connection = Connect();
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                var script = provider == "sqlserver" ? "sql-server.sql" : provider == "postgres" ? "postgresql.sql" : "sqlite.sql";
                command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "RetryPolicyMigrations", script), ct);
                await command.ExecuteNonQueryAsync(ct);
                // Recreate instances after DDL, as the procedure instructs; old capability is intentionally cached.
                Assert.Equal(ReminderScheduleResponseCode.Success, (await OpenStorage(false).ScheduleReminderAsync(old, ct)).ResponseCode);
            }
            var upgraded = OpenStorage(autoInitialize);
            var newRow = old with { Key = new ReminderKey("new"), RetryPolicy = ReminderRetryPolicy.UntilAcknowledged };
            Assert.Equal(ReminderScheduleResponseCode.Success, (await upgraded.ScheduleReminderAsync(newRow, ct)).ResponseCode);
            // A fresh instance also exercises repeated initialization and persisted reads.
            var rows = await OpenStorage(autoInitialize).GetRemindersForEntityAsync(entity, ct: ct);
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
