using Akka.Actor;
using Akka.Reminders.PostgreSql;
using Akka.Reminders.Sql.Configuration;
using Akka.Reminders.Sqlite;
using Akka.Reminders.SqlServer;
using Akka.Reminders.Storage;

namespace Akka.Reminders.Sql;

/// <summary>
/// Compatibility wrapper for SQL storage providers.
/// </summary>
public sealed class SqlReminderStorage : IConditionalReminderMutationStorage
{
    private readonly IReminderStorage _storage;

    public SqlReminderStorage(SqlReminderStorageSettings settings, ActorSystem system)
    {
        settings.Validate();

        _storage = settings.ProviderName switch
        {
            "SqlServer" => new SqlServerReminderStorage(settings.ToSqlServerSettings(), system),
            "PostgreSql" => new PostgreSqlReminderStorage(settings.ToPostgreSqlSettings(), system),
            "Sqlite" => new SqliteReminderStorage(settings.ToSqliteSettings(), system),
            _ => throw new ArgumentException($"Unsupported provider: {settings.ProviderName}", nameof(settings))
        };
    }

    public Task<ReminderProtocol.ReminderScheduled> ScheduleReminderAsync(
        ScheduledReminder reminder,
        CancellationToken cancellationToken = default)
        => _storage.ScheduleReminderAsync(reminder, cancellationToken);

    public Task<bool> UpsertReminderOccurrencesAsync(
        IEnumerable<ScheduledReminder> reminders,
        CancellationToken cancellationToken = default)
        => _storage.UpsertReminderOccurrencesAsync(reminders, cancellationToken);

    public Task<bool> CommitReminderMutationsAsync(
        ReminderMutationBatch mutationBatch,
        CancellationToken cancellationToken = default)
        => _storage.CommitReminderMutationsAsync(mutationBatch, cancellationToken);

    public Task<ReminderProtocol.RemindersCancelled> CancelReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        CancellationToken cancellationToken = default)
        => _storage.CancelReminderAsync(entity, key, cancellationToken);

    public Task<IReadOnlyList<ScheduledReminder>> GetRemindersForEntityAsync(
        ReminderEntity entity,
        int take = 10,
        int skip = 0,
        CancellationToken cancellationToken = default)
        => _storage.GetRemindersForEntityAsync(entity, take, skip, cancellationToken);

    public Task<ReminderProtocol.RemindersCancelled> CancelAllRemindersForEntityAsync(
        ReminderEntity entity,
        CancellationToken cancellationToken = default)
        => _storage.CancelAllRemindersForEntityAsync(entity, cancellationToken);

    public Task<ReminderOverview> GetRemindersOverviewAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => _storage.GetRemindersOverviewAsync(now, cancellationToken);

    public Task<PendingRemindersWithSummary> GetNextRemindersAsync(
        DateTimeOffset untilDeadline,
        DateTimeOffset now,
        ReminderBatchSize maxCount,
        CancellationToken cancellationToken = default)
        => _storage.GetNextRemindersAsync(untilDeadline, now, maxCount, cancellationToken);

    public Task<bool> MarkRemindersAsCompletedAsync(
        IEnumerable<CompletedReminder> completedReminders,
        CancellationToken cancellationToken = default)
        => _storage.MarkRemindersAsCompletedAsync(completedReminders, cancellationToken);

    public Task<bool> CleanUpCompletedRemindersAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
        => _storage.CleanUpCompletedRemindersAsync(olderThan, cancellationToken);

    public Task<int> ExpireRemindersAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => _storage.ExpireRemindersAsync(now, cancellationToken);

    public Task<bool> MarkRemindersAsAwaitingAckAsync(
        IEnumerable<AwaitingAckReminder> reminders,
        CancellationToken ct = default)
        => _storage.MarkRemindersAsAwaitingAckAsync(reminders, ct);

    public Task<IReadOnlyList<ScheduledReminder>> GetTimedOutAckRemindersAsync(
        DateTimeOffset now,
        ReminderBatchSize maxCount,
        CancellationToken ct = default)
        => _storage.GetTimedOutAckRemindersAsync(now, maxCount, ct);

    public Task<ScheduledReminder?> GetAwaitingAckReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        CancellationToken ct = default)
        => _storage.GetAwaitingAckReminderAsync(entity, key, dueTimeUtc, ct);

    public Task<DateTimeOffset?> GetNextAwaitingAckDeadlineAsync(CancellationToken ct = default)
        => _storage.GetNextAwaitingAckDeadlineAsync(ct);

    public Task<AckResult> AcknowledgeReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        DateTimeOffset ackedAt,
        CancellationToken ct = default)
        => _storage.AcknowledgeReminderAsync(entity, key, dueTimeUtc, ackedAt, ct);

    public Task<IReadOnlyList<AckResult>> AcknowledgeRemindersAsync(
        IEnumerable<ReminderAcknowledgement> acknowledgements,
        CancellationToken ct = default)
        => _storage.AcknowledgeRemindersAsync(acknowledgements, ct);

    public Task<ReminderOccurrenceStatus?> GetReminderOccurrenceStatusAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        CancellationToken ct = default)
        => _storage.GetReminderOccurrenceStatusAsync(entity, key, dueTimeUtc, ct);
}
