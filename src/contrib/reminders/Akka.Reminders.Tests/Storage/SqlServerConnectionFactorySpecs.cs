using System.Collections.Concurrent;
using System.Data.Common;
using System.Security;
using Akka.Actor;
using Akka.Reminders.SqlServer;
using Akka.Reminders.SqlServer.Configuration;
using Akka.Reminders.SqlServer.Hosting;
using Akka.Reminders.Storage;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Akka.Reminders.Tests.Storage;

/// <summary>
/// One SQL Server container shared by the tests of a class.
/// </summary>
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("yourStrong(!)Password")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

/// <summary>
/// Hands out connections the way an access-token caller would: the credential is set on the
/// connection object, not in the connection string. Records every connection it creates and
/// whether the storage disposed it.
/// </summary>
internal sealed class TrackingSqlConnectionFactory
{
    private readonly string _connectionStringWithoutCredentials;
    private readonly SqlCredential _credential;
    private readonly ConcurrentBag<ConnectionRecord> _connections = new();

    public TrackingSqlConnectionFactory(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var password = new SecureString();
        foreach (var c in builder.Password)
            password.AppendChar(c);
        password.MakeReadOnly();
        _credential = new SqlCredential(builder.UserID, password);

        builder.Remove("User ID");
        builder.Remove("Password");
        _connectionStringWithoutCredentials = builder.ConnectionString;
    }

    public int Invocations => _connections.Count;

    public int Disposed => _connections.Count(c => c.Disposed);

    public ValueTask<DbConnection> CreateAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionStringWithoutCredentials, _credential);
        var record = new ConnectionRecord();
        connection.Disposed += (_, _) => record.Disposed = true;
        _connections.Add(record);
        return new ValueTask<DbConnection>(connection);
    }

    private sealed class ConnectionRecord
    {
        public volatile bool Disposed;
    }
}

[Collection("SqlServer")]
public sealed class SqlServerConnectionFactoryStorageSpecs(MsSqlContainerFixture database)
    : IClassFixture<MsSqlContainerFixture>, IAsyncLifetime
{
    private readonly ActorSystem _system = ActorSystem.Create("connection-factory-specs");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _system.Terminate();

    [Fact]
    public async Task Storage_WithConnectionFactory_ShouldRunEveryOperationThroughTheFactory()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = new TrackingSqlConnectionFactory(database.ConnectionString);
        var settings = new SqlServerReminderStorageSettings
        {
            ConnectionFactory = factory.CreateAsync,
            SchemaName = "factory_e2e",
            AutoInitialize = true
        };
        var storage = new SqlServerReminderStorage(settings, _system);
        Assert.Equal(0, factory.Invocations);

        var now = DateTimeOffset.UtcNow;
        var entity = new ReminderEntity("factory-region", "entity-1");
        var due = new ScheduledReminder(entity, new ReminderKey("due"), now.AddSeconds(-1), "due-payload");
        var later = new ScheduledReminder(entity, new ReminderKey("later"), now.AddHours(1), "later-payload");

        // The first operation creates the schema and table through the factory, then schedules through it.
        var scheduled = await storage.ScheduleReminderAsync(due, ct);
        Assert.Equal(ReminderScheduleResponseCode.Success, scheduled.ResponseCode);
        Assert.Equal(2, factory.Invocations);

        Assert.Equal(ReminderScheduleResponseCode.Success, (await storage.ScheduleReminderAsync(later, ct)).ResponseCode);

        var forEntity = await storage.GetRemindersForEntityAsync(entity, cancellationToken: ct);
        Assert.Equal(2, forEntity.Count);

        var fetched = await storage.GetNextRemindersAsync(now.AddSeconds(1), now, new ReminderBatchSize(10), ct);
        var fired = Assert.Single(fetched.Reminders);
        Assert.Equal(due.Key, fired.Key);
        Assert.Equal("due-payload", fired.Message);

        Assert.True(await storage.MarkRemindersAsAwaitingAckAsync(
            [new AwaitingAckReminder(entity, fired.Key, fired.DueTimeUtc, now, now.AddMinutes(1))], ct));
        Assert.NotNull(await storage.GetAwaitingAckReminderAsync(entity, fired.Key, fired.DueTimeUtc, ct));
        Assert.NotNull(await storage.GetNextAwaitingAckDeadlineAsync(ct));
        Assert.Empty(await storage.GetTimedOutAckRemindersAsync(now, new ReminderBatchSize(10), ct));

        var ack = await storage.AcknowledgeReminderAsync(entity, fired.Key, fired.DueTimeUtc, now, ct);
        Assert.Equal(ReminderAckStorageStatus.Success, ack.Status);
        Assert.NotNull(await storage.GetReminderOccurrenceStatusAsync(entity, fired.Key, fired.DueTimeUtc, ct));

        var cancelled = await storage.CancelReminderAsync(entity, later.Key, ct);
        Assert.Equal(ReminderCancelResponseCode.Success, cancelled.ResponseCode);

        var upserted = new ScheduledReminder(entity, new ReminderKey("upserted"), now.AddHours(2), "upserted-payload");
        var committed = new ScheduledReminder(entity, new ReminderKey("committed"), now.AddHours(3), "committed-payload");
        Assert.True(await storage.UpsertReminderOccurrencesAsync([upserted], ct));
        Assert.True(await storage.CommitReminderMutationsAsync(new ReminderMutationBatch([committed], [], []), ct));
        Assert.True(await storage.MarkRemindersAsCompletedAsync(
            [new CompletedReminder(entity, upserted.Key, upserted.DueTimeUtc, now)], ct));

        var cancelledAll = await storage.CancelAllRemindersForEntityAsync(entity, ct);
        Assert.Equal(ReminderCancelResponseCode.Success, cancelledAll.ResponseCode);
        Assert.Contains(committed.Key, cancelledAll.Keys);
        Assert.Empty(await storage.GetRemindersForEntityAsync(entity, cancellationToken: ct));

        var overview = await storage.GetRemindersOverviewAsync(now, ct);
        Assert.Equal(0, overview.TotalPendingReminders);

        await storage.ExpireRemindersAsync(now, ct);
        Assert.True(await storage.CleanUpCompletedRemindersAsync(now.AddDays(1), ct));

        // One connection per operation, and the storage disposed every one of them.
        Assert.True(factory.Invocations >= 21, $"Factory invoked {factory.Invocations} time(s)");
        Assert.Equal(factory.Invocations, factory.Disposed);
    }

    [Fact]
    public async Task WithSqlServerStorage_ConnectionFactory_ShouldCreateTableAndStoreThroughTheFactory()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = new TrackingSqlConnectionFactory(database.ConnectionString);
        var setup = new ReminderConfigurationBuilder()
            .WithSqlServerStorage(
                connectionFactory: factory.CreateAsync,
                schemaName: "factory_hosting",
                tableName: "hosted_reminders",
                autoInitialize: true)
            .Build();
        var storage = setup.StorageFactory(_system);

        var reminder = new ScheduledReminder(
            new ReminderEntity("factory-region", "entity-1"), new ReminderKey("hosted"), DateTimeOffset.UtcNow.AddMinutes(5), "hosted-payload");
        Assert.Equal(ReminderScheduleResponseCode.Success, (await storage.ScheduleReminderAsync(reminder, ct)).ResponseCode);
        Assert.Equal(2, factory.Invocations);

        // The overload's schema and table names were used: read the row back over a plain connection.
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [factory_hosting].[hosted_reminders] WHERE ReminderKey = 'hosted'";
        Assert.Equal(1, (int)(await command.ExecuteScalarAsync(ct))!);
        Assert.Equal(factory.Invocations, factory.Disposed);
    }

    [Fact]
    public async Task Storage_WithConnectionFactory_ShouldFail_WhenFactoryReturnsNull()
    {
        var settings = new SqlServerReminderStorageSettings
        {
            ConnectionFactory = _ => new ValueTask<DbConnection>((DbConnection)null!),
            AutoInitialize = false
        };
        var storage = new SqlServerReminderStorage(settings, _system);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.GetRemindersForEntityAsync(new ReminderEntity("r", "e"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains(nameof(SqlServerReminderStorageSettings.ConnectionFactory), ex.Message);
    }
}
