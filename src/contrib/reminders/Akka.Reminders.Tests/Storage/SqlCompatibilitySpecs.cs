using Akka.Actor;
using Akka.Reminders;
using Akka.Reminders.Sql;
using Akka.Reminders.Sql.Configuration;

namespace Akka.Reminders.Tests.Storage;

public sealed class SqlCompatibilitySpecs : IAsyncLifetime
{
    private ActorSystem? _system;
    private SqlReminderStorage? _storage;
    private string? _databasePath;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("compatibility-system");
        _databasePath = Path.Combine(Path.GetTempPath(), $"akka-reminders-compat-{Guid.NewGuid():N}.db");

        var connectionString = $"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared";
        var settings = SqlReminderStorageSettings.CreateSqlite(connectionString);

        _storage = new SqlReminderStorage(settings, _system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_system != null)
        {
            await _system.Terminate();
        }

        if (!string.IsNullOrWhiteSpace(_databasePath) && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task LegacySqlReminderStorage_ShouldScheduleAndFetch_WithSqliteProvider()
    {
        var entity = new ReminderEntity("compat", "entity-1");
        var key = new ReminderKey("compat-key");
        var reminder = new ScheduledReminder(entity, key, DateTimeOffset.UtcNow.AddMinutes(5), "hello");

        var scheduled = await _storage!.ScheduleReminderAsync(reminder, TestContext.Current.CancellationToken);
        var reminders = await _storage.GetRemindersForEntityAsync(entity, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ReminderScheduleResponseCode.Success, scheduled.ResponseCode);
        Assert.Single(reminders);
        Assert.Equal(key, reminders[0].Key);
    }
}
