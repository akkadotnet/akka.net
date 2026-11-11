using System.Data;
using System.Globalization;
using Akka.Actor;
using Akka.Reminders.Sqlite.Configuration;
using Akka.Reminders.Sqlite.Internal;
using Akka.Reminders.Storage;

namespace Akka.Reminders.Sqlite;

/// <summary>
/// SQLite implementation of <see cref="IReminderStorage"/>.
/// </summary>
public sealed class SqliteReminderStorage : IConditionalReminderMutationStorage
{
    private readonly SqliteReminderStorageSettings _settings;
    private readonly ISqlDialect _dialect;
    private readonly Akka.Serialization.Serialization _serialization;
    private readonly ILoggingAdapter _log;
    private readonly object _initLock = new();
    private volatile bool _initialized;

    private const int MaxUpsertedRemindersPerStatement = 60;
    private const int MaxRemindersPerStatusUpdate = 200;

    public SqliteReminderStorage(SqliteReminderStorageSettings settings, ActorSystem system)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Validate();

        _serialization = system.Serialization;
        _log = Logging.GetLogger(system, GetType());
        _dialect = SqliteDialect.Instance;
    }

    public async Task<ReminderProtocol.ReminderScheduled> ScheduleReminderAsync(
        ScheduledReminder reminder,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var cancelCommand = CreateCommand(connection, transaction))
            {
                cancelCommand.CommandText = _dialect.GetCancelReminderSql(_settings.TableName);
                cancelCommand.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
                _dialect.AddParameter(cancelCommand, "@ShardRegionName", reminder.Entity.ShardRegionName);
                _dialect.AddParameter(cancelCommand, "@EntityId", reminder.Entity.EntityId);
                _dialect.AddParameter(cancelCommand, "@ReminderKey", reminder.Key.Name);
                _dialect.AddParameter(cancelCommand, "@CompletedAtUtc", DateTimeOffset.UtcNow.UtcDateTime);
                await cancelCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!await UpsertReminderOccurrencesAsync(connection, transaction, [reminder], cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ReminderProtocol.ReminderScheduled(
                    reminder.ToScheduleReminder(),
                    ReminderScheduleResponseCode.Error,
                    "Failed to persist reminder occurrence");
            }

            await transaction.CommitAsync(cancellationToken);

            return new ReminderProtocol.ReminderScheduled(
                reminder.ToScheduleReminder(),
                ReminderScheduleResponseCode.Success);
        }
        catch (Exception ex)
        {
            return new ReminderProtocol.ReminderScheduled(
                reminder.ToScheduleReminder(),
                ReminderScheduleResponseCode.Error,
                ex.Message);
        }
    }

    public async Task<bool> UpsertReminderOccurrencesAsync(
        IEnumerable<ScheduledReminder> reminders,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await UpsertReminderOccurrencesAsync(connection, null, reminders, cancellationToken);
    }

    public async Task<bool> CommitReminderMutationsAsync(ReminderMutationBatch mutationBatch, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (mutationBatch.IsEmpty)
            return true;

        await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await UpsertReminderOccurrencesAsync(connection, transaction, mutationBatch.PendingInserts, cancellationToken, insertIfAbsent: true))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            if (!await UpsertReminderOccurrencesAsync(connection, transaction, mutationBatch.PendingUpserts, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            if (!await MarkRemindersAsCompletedAsync(connection, transaction, mutationBatch.CompletedReminders, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            if (!await MarkRemindersAsCompletedAsync(connection, transaction, mutationBatch.ActiveCompletions, cancellationToken, activeOnly: true))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            if (!await MarkRemindersAsAwaitingAckAsync(connection, transaction, mutationBatch.AwaitingAckReminders, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
    }

    private async Task<bool> UpsertReminderOccurrencesAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        IEnumerable<ScheduledReminder> reminders,
        CancellationToken cancellationToken,
        bool insertIfAbsent = false)
    {
        var remindersList = reminders.ToList();
        if (remindersList.Count == 0)
            return true;

        try
        {
            for (var offset = 0; offset < remindersList.Count; offset += MaxUpsertedRemindersPerStatement)
            {
                var chunk = remindersList.Skip(offset).Take(MaxUpsertedRemindersPerStatement).ToList();
                await using var command = CreateCommand(connection, transaction);
                command.CommandText = _dialect.GetBatchUpsertRemindersSql(_settings.TableName, chunk.Count, insertIfAbsent);
                command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;

                for (var i = 0; i < chunk.Count; i++)
                {
                    BindReminderParameters(command, i, chunk[i]);
                }

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<PendingRemindersWithSummary> GetNextRemindersAsync(
        DateTimeOffset untilDeadline,
        DateTimeOffset now,
        ReminderBatchSize maxCount,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var reminders = new List<ScheduledReminder>();

        await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var unreadable = new List<CompletedReminder>();
        var skipped = 0;
        UnreadablePayloadException? firstSkipped = null;
        var seen = new HashSet<(ReminderEntity, ReminderKey, DateTimeOffset)>();

        // A live row whose payload cannot be read is skipped and stays Pending, so the page is
        // widened past such rows until it holds maxCount readable rows or no more rows are due.
        // The limit doubles each pass, and rows already seen are skipped before their payload is read.
        for (var limit = maxCount.Value; ; limit = (int)Math.Min(int.MaxValue, Math.Max(limit * 2L, (long)maxCount.Value + skipped + unreadable.Count)))
        {
            var unusable = skipped + unreadable.Count;
            var rows = 0;
            await using var command = connection.CreateCommand();
            command.CommandText = _dialect.GetSelectDueRemindersSql(_settings.TableName, limit);
            command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
            _dialect.AddParameter(command, "@UntilDeadline", untilDeadline.UtcDateTime);
            _dialect.AddParameter(command, "@Now", now.UtcDateTime);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows++;
                if (!seen.Add(ReadKeyFromReader(reader)))
                    continue;

                try
                {
                    reminders.Add(ReadReminderFromReader(reader));
                    if (reminders.Count >= maxCount.Value)
                        break;
                }
                catch (UnreadablePayloadException ex)
                {
                    if (ex.DeliveryDeadlineUtc <= now)
                    {
                        // Past its deadline, where storage expired it before 1.5.73: end it as Failed.
                        var row = new CompletedReminder(ex.Entity, ex.Key, ex.DueTimeUtc, now, ReminderCompletionStatus.Failed);
                        _log.Error(ex, "Marking reminder occurrence {0} as Failed", row);
                        unreadable.Add(row);
                    }
                    else
                    {
                        // Still live: it stays Pending, so fixing the type mapping recovers it.
                        skipped++;
                        firstSkipped ??= ex;
                    }
                }
            }

            if (reminders.Count >= maxCount.Value || skipped + unreadable.Count == unusable || rows < limit)
                break;
        }

        if (firstSkipped is not null)
            _log.Error(firstSkipped, "Skipped [{0}] pending reminder occurrence(s) whose payload could not be deserialized; they stay Pending. First: [{1}] / [{2}] due at [{3}]",
                skipped, firstSkipped.Entity, firstSkipped.Key, firstSkipped.DueTimeUtc);

        if (unreadable.Count > 0)
            await MarkRemindersAsCompletedAsync(connection, null, unreadable, cancellationToken);

        await using var conn2 = _dialect.CreateConnection(_settings.ConnectionString);
        await conn2.OpenAsync(cancellationToken);

        long totalPending = 0;
        await using (var cmd2 = conn2.CreateCommand())
        {
            cmd2.CommandText = _dialect.GetOverviewAggregateSql(_settings.TableName);
            cmd2.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
            _dialect.AddParameter(cmd2, "@Now", now.UtcDateTime);
            await using var reader2 = await cmd2.ExecuteReaderAsync(cancellationToken);
            if (await reader2.ReadAsync(cancellationToken))
            {
                totalPending = Convert.ToInt64(reader2.GetValue(reader2.GetOrdinal("total_count")), CultureInfo.InvariantCulture);
            }
        }

        // Skipped rows stay Pending ahead of the rest. Leave them out of the count and the next due
        // time, or an overdue skipped row would arm an immediate tick after every fetch.
        var consumed = reminders.Count + skipped;
        var remainingCount = totalPending - consumed;
        var timeUntilNext = TimeSpan.MaxValue;

        if (remainingCount > 0)
        {
            await using var cmd3 = conn2.CreateCommand();
            cmd3.CommandText = _dialect.GetNextReminderTimeSql(_settings.TableName);
            cmd3.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
            _dialect.AddParameter(cmd3, "@Skip", consumed);
            _dialect.AddParameter(cmd3, "@Now", now.UtcDateTime);

            var result = await cmd3.ExecuteScalarAsync(cancellationToken);
            if (result != null && result != DBNull.Value)
            {
                var nextWhenUtc = ParseDateTimeOffset(result);
                timeUntilNext = nextWhenUtc - now;
            }
        }

        return new PendingRemindersWithSummary(reminders, new ReminderOverview
        {
            TimeUntilNext = timeUntilNext,
            TotalPendingReminders = remainingCount
        });
    }

    public async Task<bool> MarkRemindersAsCompletedAsync(
        IEnumerable<CompletedReminder> completedReminders,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var remindersList = completedReminders.ToList();
        if (remindersList.Count == 0)
            return true;

        try
        {
            await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return await MarkRemindersAsCompletedAsync(connection, null, completedReminders, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> ExpireRemindersAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = _dialect.GetExpireRemindersSql(_settings.TableName);
        command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
        _dialect.AddParameter(command, "@Now", now.UtcDateTime);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ReminderOverview> GetRemindersOverviewAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = _dialect.GetOverviewAggregateSql(_settings.TableName);
        command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
        _dialect.AddParameter(command, "@Now", now.UtcDateTime);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            var totalCount = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("total_count")), CultureInfo.InvariantCulture);
            var nextWhenUtcOrdinal = reader.GetOrdinal("next_when_utc");

            if (totalCount == 0 || reader.IsDBNull(nextWhenUtcOrdinal))
                return ReminderOverview.Empty;

            var nextWhenUtc = ParseDateTimeOffset(reader.GetValue(nextWhenUtcOrdinal));
            return new ReminderOverview
            {
                TotalPendingReminders = totalCount,
                TimeUntilNext = nextWhenUtc - now
            };
        }

        return ReminderOverview.Empty;
    }

    public async Task<bool> CleanUpCompletedRemindersAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = _dialect.GetCleanupSql(_settings.TableName);
            command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
            _dialect.AddParameter(command, "@OlderThan", olderThan.UtcDateTime);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ReminderProtocol.RemindersCancelled> CancelReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = _dialect.GetCancelReminderSql(_settings.TableName);
            command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
            _dialect.AddParameter(command, "@ShardRegionName", entity.ShardRegionName);
            _dialect.AddParameter(command, "@EntityId", entity.EntityId);
            _dialect.AddParameter(command, "@ReminderKey", key.Name);
            _dialect.AddParameter(command, "@CompletedAtUtc", DateTimeOffset.UtcNow.UtcDateTime);

            var count = await command.ExecuteNonQueryAsync(cancellationToken);
            return count > 0
                ? new ReminderProtocol.RemindersCancelled(entity, ReminderCancelResponseCode.Success, [key])
                : new ReminderProtocol.RemindersCancelled(entity, ReminderCancelResponseCode.NotFound, []);
        }
        catch (Exception ex)
        {
            return new ReminderProtocol.RemindersCancelled(entity, ReminderCancelResponseCode.Error, [], ex.Message);
        }
    }

    public async Task<ReminderProtocol.RemindersCancelled> CancelAllRemindersForEntityAsync(
        ReminderEntity entity,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var cancelledKeys = new HashSet<ReminderKey>();

            await using (var selectCommand = connection.CreateCommand())
            {
                selectCommand.CommandText = _dialect.GetFetchRemindersSql(_settings.TableName);
                selectCommand.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
                _dialect.AddParameter(selectCommand, "@ShardRegionName", entity.ShardRegionName);
                _dialect.AddParameter(selectCommand, "@EntityId", entity.EntityId);
                _dialect.AddParameter(selectCommand, "@Now", DateTimeOffset.UtcNow.UtcDateTime);

                await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    cancelledKeys.Add(new ReminderKey(reader.GetString(reader.GetOrdinal("reminder_key"))));
                }
            }

            if (cancelledKeys.Count == 0)
            {
                return new ReminderProtocol.RemindersCancelled(entity, ReminderCancelResponseCode.NotFound, []);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = _dialect.GetCancelAllRemindersSql(_settings.TableName);
            command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
            _dialect.AddParameter(command, "@ShardRegionName", entity.ShardRegionName);
            _dialect.AddParameter(command, "@EntityId", entity.EntityId);
            _dialect.AddParameter(command, "@CompletedAtUtc", DateTimeOffset.UtcNow.UtcDateTime);
            await command.ExecuteNonQueryAsync(cancellationToken);

            return new ReminderProtocol.RemindersCancelled(entity, ReminderCancelResponseCode.Success, cancelledKeys.ToList());
        }
        catch (Exception ex)
        {
            return new ReminderProtocol.RemindersCancelled(entity, ReminderCancelResponseCode.Error, [], ex.Message);
        }
    }

    public async Task<IReadOnlyList<ScheduledReminder>> GetRemindersForEntityAsync(
        ReminderEntity entity,
        int take = 10,
        int skip = 0,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var reminders = new List<ScheduledReminder>();

        await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = _dialect.GetFetchRemindersSql(_settings.TableName);
        command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
        _dialect.AddParameter(command, "@ShardRegionName", entity.ShardRegionName);
        _dialect.AddParameter(command, "@EntityId", entity.EntityId);
        _dialect.AddParameter(command, "@Now", DateTimeOffset.UtcNow.UtcDateTime);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            try
            {
                reminders.Add(ReadReminderFromReader(reader));
            }
            catch (UnreadablePayloadException ex)
            {
                _log.Error(ex, "Skipping reminder [{0}] / [{1}] due at [{2}] in listing: payload could not be deserialized", ex.Entity, ex.Key, ex.DueTimeUtc);
            }
        }

        return reminders.Skip(skip).Take(take).ToList();
    }

    public async Task<bool> MarkRemindersAsAwaitingAckAsync(
        IEnumerable<AwaitingAckReminder> reminders,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var remindersList = reminders.ToList();
        if (remindersList.Count == 0)
            return true;

        try
        {
            await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return await MarkRemindersAsAwaitingAckAsync(connection, null, reminders, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ScheduledReminder>> GetTimedOutAckRemindersAsync(
        DateTimeOffset now,
        ReminderBatchSize maxCount,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var reminders = new List<ScheduledReminder>();

        await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = _dialect.GetTimedOutAckRemindersSql(_settings.TableName, maxCount.Value);
        command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
        _dialect.AddParameter(command, "@Now", now.UtcDateTime);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            reminders.Add(ReadReminderFromReader(reader));
        }

        return reminders;
    }

    public async Task<ScheduledReminder?> GetAwaitingAckReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT *
            FROM "{_settings.TableName}"
            WHERE shard_region_name = @ShardRegionName
              AND entity_id = @EntityId
              AND reminder_key = @ReminderKey
              AND due_time_utc = @DueTimeUtc
              AND completion_status = 'AwaitingAck'
              AND is_completed = 0
            LIMIT 1;
            """;
        command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
        _dialect.AddParameter(command, "@ShardRegionName", entity.ShardRegionName);
        _dialect.AddParameter(command, "@EntityId", entity.EntityId);
        _dialect.AddParameter(command, "@ReminderKey", key.Name);
        _dialect.AddParameter(command, "@DueTimeUtc", dueTimeUtc.ToUniversalTime().UtcDateTime);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadReminderFromReader(reader) : null;
    }

    public async Task<DateTimeOffset?> GetNextAwaitingAckDeadlineAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MIN(ack_deadline_utc) FROM \"{_settings.TableName}\" WHERE completion_status = 'AwaitingAck' AND is_completed = 0;";
        command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result == DBNull.Value ? null : ParseDateTimeOffset(result);
    }

    public async Task<AckResult> AcknowledgeReminderAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        DateTimeOffset ackedAt,
        CancellationToken cancellationToken = default)
        => (await AcknowledgeRemindersAsync([new ReminderAcknowledgement(entity, key, dueTimeUtc, ackedAt)], cancellationToken))[0];

    public async Task<IReadOnlyList<AckResult>> AcknowledgeRemindersAsync(
        IEnumerable<ReminderAcknowledgement> acknowledgements,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var acknowledgementList = acknowledgements.ToList();
        var results = new List<AckResult>(acknowledgementList.Count);

        if (acknowledgementList.Count == 0)
            return results;

        await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        for (var offset = 0; offset < acknowledgementList.Count; offset += MaxRemindersPerStatusUpdate)
        {
            var chunk = acknowledgementList.Skip(offset).Take(MaxRemindersPerStatusUpdate).ToList();
            results.AddRange(await AcknowledgeReminderChunkAsync(connection, chunk, cancellationToken));
        }

        return results;
    }

    public async Task<ReminderOccurrenceStatus?> GetReminderOccurrenceStatusAsync(
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT when_utc, attempt_count, last_failure_reason, completion_status,
                   delivery_deadline_utc, delivered_at_utc, ack_deadline_utc, completed_at_utc
            FROM "{_settings.TableName}"
            WHERE shard_region_name = @ShardRegionName
              AND entity_id = @EntityId
              AND reminder_key = @ReminderKey
              AND due_time_utc = @DueTimeUtc
            LIMIT 1;
            """;
        command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
        _dialect.AddParameter(command, "@ShardRegionName", entity.ShardRegionName);
        _dialect.AddParameter(command, "@EntityId", entity.EntityId);
        _dialect.AddParameter(command, "@ReminderKey", key.Name);
        _dialect.AddParameter(command, "@DueTimeUtc", dueTimeUtc.ToUniversalTime().UtcDateTime);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadOccurrenceStatus(reader, entity, key, dueTimeUtc);
    }

    private static ReminderOccurrenceStatus ReadOccurrenceStatus(
        IDataReader reader,
        ReminderEntity entity,
        ReminderKey key,
        DateTimeOffset dueTimeUtc)
    {
        var statusText = reader.GetString(reader.GetOrdinal("completion_status"));
        if (!Enum.TryParse<ReminderCompletionStatus>(statusText, out var completionStatus))
            throw new InvalidOperationException($"Unknown reminder completion status '{statusText}'.");

        return new ReminderOccurrenceStatus(
            entity,
            key,
            dueTimeUtc.ToUniversalTime(),
            completionStatus == ReminderCompletionStatus.Pending
                ? ParseDateTimeOffset(reader.GetValue(reader.GetOrdinal("when_utc")))
                : null,
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("attempt_count")), CultureInfo.InvariantCulture),
            ReadNullableString(reader, "last_failure_reason"),
            completionStatus,
            ReadNullableDateTimeOffset(reader, "delivery_deadline_utc"),
            ReadNullableDateTimeOffset(reader, "delivered_at_utc"),
            ReadNullableDateTimeOffset(reader, "ack_deadline_utc"),
            ReadNullableDateTimeOffset(reader, "completed_at_utc"));
    }

    private static string? ReadNullableString(IDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(IDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : ParseDateTimeOffset(reader.GetValue(ordinal));
    }

    private async Task<IReadOnlyList<AckResult>> AcknowledgeReminderChunkAsync(
        System.Data.Common.DbConnection connection,
        IReadOnlyList<ReminderAcknowledgement> acknowledgements,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = CreateCommand(connection, transaction);
            command.CommandText = _dialect.GetBatchAcknowledgeRemindersSql(_settings.TableName, acknowledgements.Count);
            command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;

            for (var i = 0; i < acknowledgements.Count; i++)
            {
                var acknowledgement = acknowledgements[i];
                _dialect.AddParameter(command, $"@sr{i}", acknowledgement.Entity.ShardRegionName);
                _dialect.AddParameter(command, $"@eid{i}", acknowledgement.Entity.EntityId);
                _dialect.AddParameter(command, $"@rk{i}", acknowledgement.Key.Name);
                _dialect.AddParameter(command, $"@due{i}", acknowledgement.DueTimeUtc.UtcDateTime);
                _dialect.AddParameter(command, $"@acked{i}", acknowledgement.AckedAt.UtcDateTime);
            }

            var updated = await command.ExecuteNonQueryAsync(cancellationToken);
            if (updated == acknowledgements.Count)
            {
                await transaction.CommitAsync(cancellationToken);
                return acknowledgements.Select(ToSuccessfulAckResult).ToList();
            }

            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return acknowledgements.Select(a => new AckResult(
                a.Entity,
                a.Key,
                a.DueTimeUtc,
                ReminderAckStorageStatus.Error,
                ex.Message)).ToList();
        }

        return await AcknowledgeRemindersIndividuallyAsync(connection, acknowledgements, cancellationToken);
    }

    private async Task<IReadOnlyList<AckResult>> AcknowledgeRemindersIndividuallyAsync(
        System.Data.Common.DbConnection connection,
        IReadOnlyList<ReminderAcknowledgement> acknowledgements,
        CancellationToken cancellationToken)
    {
        var results = new List<AckResult>(acknowledgements.Count);

        foreach (var acknowledgement in acknowledgements)
        {
            try
            {
                await using var command = CreateCommand(connection, null);
                command.CommandText = _dialect.GetAcknowledgeReminderSql(_settings.TableName);
                command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;

                _dialect.AddParameter(command, "@ShardRegionName", acknowledgement.Entity.ShardRegionName);
                _dialect.AddParameter(command, "@EntityId", acknowledgement.Entity.EntityId);
                _dialect.AddParameter(command, "@ReminderKey", acknowledgement.Key.Name);
                _dialect.AddParameter(command, "@DueTimeUtc", acknowledgement.DueTimeUtc.UtcDateTime);
                _dialect.AddParameter(command, "@AckedAtUtc", acknowledgement.AckedAt.UtcDateTime);

                var count = await command.ExecuteNonQueryAsync(cancellationToken);
                results.Add(count > 0
                    ? ToSuccessfulAckResult(acknowledgement)
                    : ToMissingAckResult(acknowledgement));
            }
            catch (Exception ex)
            {
                results.Add(new AckResult(
                    acknowledgement.Entity,
                    acknowledgement.Key,
                    acknowledgement.DueTimeUtc,
                    ReminderAckStorageStatus.Error,
                    ex.Message));
            }
        }

        return results;
    }

    private static AckResult ToSuccessfulAckResult(ReminderAcknowledgement acknowledgement)
        => new(
            acknowledgement.Entity,
            acknowledgement.Key,
            acknowledgement.DueTimeUtc,
            ReminderAckStorageStatus.Success);

    private static AckResult ToMissingAckResult(ReminderAcknowledgement acknowledgement)
        => new(
            acknowledgement.Entity,
            acknowledgement.Key,
            acknowledgement.DueTimeUtc,
            ReminderAckStorageStatus.NotFound,
            "Reminder occurrence was not awaiting acknowledgement or was already stale.");

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized || !_settings.AutoInitialize)
            return Task.CompletedTask;

        lock (_initLock)
        {
            if (_initialized)
                return Task.CompletedTask;

            Task.Run(async () =>
            {
                await using var connection = _dialect.CreateConnection(_settings.ConnectionString);
                await connection.OpenAsync(cancellationToken);

                await using var command = connection.CreateCommand();
                command.CommandText = _dialect.GetCreateTableSql(_settings.TableName);
                command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }, cancellationToken).Wait(cancellationToken);

            _initialized = true;
        }

        return Task.CompletedTask;
    }

    private void BindReminderParameters(System.Data.Common.DbCommand command, int index, ScheduledReminder reminder)
    {
        var (serializerId, manifest, payload) = SerializeMessage(reminder.Message);

        _dialect.AddParameter(command, $"@ShardRegionName{index}", reminder.Entity.ShardRegionName);
        _dialect.AddParameter(command, $"@EntityId{index}", reminder.Entity.EntityId);
        _dialect.AddParameter(command, $"@ReminderKey{index}", reminder.Key.Name);
        _dialect.AddParameter(command, $"@WhenUtc{index}", reminder.When.UtcDateTime);
        _dialect.AddParameter(command, $"@DueTimeUtc{index}", reminder.DueTimeUtc.UtcDateTime);
        _dialect.AddParameter(command, $"@RepeatIntervalTicks{index}", reminder.RepeatInterval?.Ticks ?? (object)DBNull.Value);
        _dialect.AddParameter(command, $"@SerializerId{index}", serializerId);
        _dialect.AddParameter(command, $"@Manifest{index}", manifest ?? (object)DBNull.Value);
        _dialect.AddParameter(command, $"@Payload{index}", payload);
        _dialect.AddParameter(command, $"@AttemptCount{index}", reminder.AttemptCount);
        _dialect.AddParameter(command, $"@LastFailureReason{index}", reminder.LastFailureReason ?? (object)DBNull.Value);
        _dialect.AddParameter(command, $"@MaxDeliveryWindowTicks{index}", reminder.MaxDeliveryWindow?.Ticks ?? (object)DBNull.Value);
        _dialect.AddParameter(command, $"@DeliveryDeadlineUtc{index}", reminder.DeliveryDeadlineUtc?.UtcDateTime ?? (object)DBNull.Value);
    }

    private async Task<bool> MarkRemindersAsCompletedAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        IEnumerable<CompletedReminder> completedReminders,
        CancellationToken cancellationToken,
        bool activeOnly = false)
    {
        var remindersList = completedReminders.ToList();
        if (remindersList.Count == 0)
            return true;

        var groups = remindersList.GroupBy(r => (r.Status, r.CompletedAt));
        foreach (var group in groups)
        {
            var items = group.ToList();
            for (var offset = 0; offset < items.Count; offset += MaxRemindersPerStatusUpdate)
            {
                var chunk = items.Skip(offset).Take(MaxRemindersPerStatusUpdate).ToList();
                await using var command = CreateCommand(connection, transaction);
                command.CommandText = _dialect.GetBatchMarkCompletedSql(_settings.TableName, chunk.Count, activeOnly);
                command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;
                _dialect.AddParameter(command, "@CompletedAtUtc", group.Key.CompletedAt.UtcDateTime);
                _dialect.AddParameter(command, "@CompletionStatus", group.Key.Status.ToString());

                for (var i = 0; i < chunk.Count; i++)
                {
                    _dialect.AddParameter(command, $"@sr{i}", chunk[i].Entity.ShardRegionName);
                    _dialect.AddParameter(command, $"@eid{i}", chunk[i].Entity.EntityId);
                    _dialect.AddParameter(command, $"@rk{i}", chunk[i].Key.Name);
                    _dialect.AddParameter(command, $"@due{i}", chunk[i].DueTimeUtc.UtcDateTime);
                }

                var updated = await command.ExecuteNonQueryAsync(cancellationToken);
                if (!activeOnly && updated != chunk.Count)
                    return false;
            }
        }

        return true;
    }

    private async Task<bool> MarkRemindersAsAwaitingAckAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        IEnumerable<AwaitingAckReminder> reminders,
        CancellationToken cancellationToken)
    {
        var remindersList = reminders.ToList();
        if (remindersList.Count == 0)
            return true;

        for (var offset = 0; offset < remindersList.Count; offset += MaxRemindersPerStatusUpdate)
        {
            var chunk = remindersList.Skip(offset).Take(MaxRemindersPerStatusUpdate).ToList();
            await using var command = CreateCommand(connection, transaction);
            command.CommandText = _dialect.GetBatchMarkAsAwaitingAckSql(_settings.TableName, chunk.Count);
            command.CommandTimeout = (int)_settings.CommandTimeout.TotalSeconds;

            for (var i = 0; i < chunk.Count; i++)
            {
                _dialect.AddParameter(command, $"@sr{i}", chunk[i].Entity.ShardRegionName);
                _dialect.AddParameter(command, $"@eid{i}", chunk[i].Entity.EntityId);
                _dialect.AddParameter(command, $"@rk{i}", chunk[i].Key.Name);
                _dialect.AddParameter(command, $"@due{i}", chunk[i].DueTimeUtc.UtcDateTime);
                _dialect.AddParameter(command, $"@del{i}", chunk[i].DeliveredAt.UtcDateTime);
                _dialect.AddParameter(command, $"@ack{i}", chunk[i].AckDeadline.UtcDateTime);
            }

            var updated = await command.ExecuteNonQueryAsync(cancellationToken);
            if (updated != chunk.Count)
                return false;
        }

        return true;
    }

    private static System.Data.Common.DbCommand CreateCommand(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction)
    {
        var command = connection.CreateCommand();
        if (transaction is not null)
            command.Transaction = transaction;
        return command;
    }

    private (int serializerId, string? manifest, byte[] payload) SerializeMessage(object message)
    {
        var serializer = _serialization.FindSerializerFor(message);
        var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, message);
        var payload = serializer.ToBinary(message);
        return (serializer.Identifier, manifest, payload);
    }

    private object DeserializeMessage(int serializerId, string? manifest, byte[] payload)
    {
        return _serialization.Deserialize(payload, serializerId, manifest ?? string.Empty);
    }

    private static DateTimeOffset ParseDateTimeOffset(object value)
    {
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            _ => throw new InvalidOperationException($"Unexpected datetime value type '{value.GetType()}'.")
        };
    }

    private static (ReminderEntity, ReminderKey, DateTimeOffset) ReadKeyFromReader(IDataReader reader)
        => (new ReminderEntity(reader.GetString(reader.GetOrdinal("shard_region_name")), reader.GetString(reader.GetOrdinal("entity_id"))),
            new ReminderKey(reader.GetString(reader.GetOrdinal("reminder_key"))),
            ParseDateTimeOffset(reader.GetValue(reader.GetOrdinal("due_time_utc"))));

    private ScheduledReminder ReadReminderFromReader(IDataReader reader)
    {
        var shardRegionName = reader.GetString(reader.GetOrdinal("shard_region_name"));
        var entityId = reader.GetString(reader.GetOrdinal("entity_id"));
        var reminderKey = reader.GetString(reader.GetOrdinal("reminder_key"));
        var whenUtc = ParseDateTimeOffset(reader.GetValue(reader.GetOrdinal("when_utc")));
        var dueTimeUtc = ParseDateTimeOffset(reader.GetValue(reader.GetOrdinal("due_time_utc")));

        var repeatIntervalTicksOrdinal = reader.GetOrdinal("repeat_interval_ticks");
        var repeatInterval = reader.IsDBNull(repeatIntervalTicksOrdinal)
            ? (TimeSpan?)null
            : TimeSpan.FromTicks(Convert.ToInt64(reader.GetValue(repeatIntervalTicksOrdinal), CultureInfo.InvariantCulture));

        var serializerId = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("serializer_id")), CultureInfo.InvariantCulture);
        var manifestOrdinal = reader.GetOrdinal("manifest");
        var manifest = reader.IsDBNull(manifestOrdinal) ? null : reader.GetString(manifestOrdinal);
        var payload = (byte[])reader.GetValue(reader.GetOrdinal("payload"));
        var attemptCount = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("attempt_count")), CultureInfo.InvariantCulture);

        var lastFailureReasonOrdinal = reader.GetOrdinal("last_failure_reason");
        var lastFailureReason = reader.IsDBNull(lastFailureReasonOrdinal) ? null : reader.GetString(lastFailureReasonOrdinal);

        var maxWindowOrdinal = reader.GetOrdinal("max_delivery_window_ticks");
        var maxDeliveryWindow = reader.IsDBNull(maxWindowOrdinal)
            ? (TimeSpan?)null
            : TimeSpan.FromTicks(Convert.ToInt64(reader.GetValue(maxWindowOrdinal), CultureInfo.InvariantCulture));

        var deadlineOrdinal = reader.GetOrdinal("delivery_deadline_utc");
        var deliveryDeadlineUtc = reader.IsDBNull(deadlineOrdinal)
            ? (DateTimeOffset?)null
            : ParseDateTimeOffset(reader.GetValue(deadlineOrdinal));

        object message;
        try
        {
            message = DeserializeMessage(serializerId, manifest, payload);
        }
        catch (Exception ex)
        {
            throw new UnreadablePayloadException(new ReminderEntity(shardRegionName, entityId), new ReminderKey(reminderKey),
                dueTimeUtc, deliveryDeadlineUtc, serializerId, manifest, ex);
        }

        return new ScheduledReminder(
            new ReminderEntity(shardRegionName, entityId),
            new ReminderKey(reminderKey),
            whenUtc,
            message,
            repeatInterval,
            attemptCount,
            lastFailureReason,
            maxDeliveryWindow,
            deliveryDeadlineUtc,
            dueTimeUtc);
    }

    private sealed class UnreadablePayloadException(ReminderEntity entity, ReminderKey key, DateTimeOffset dueTimeUtc,
        DateTimeOffset? deliveryDeadlineUtc, int serializerId, string? manifest, Exception inner)
        : Exception($"Reminder payload could not be deserialized (serializer [{serializerId}], manifest [{manifest}]): {inner.Message}", inner)
    {
        public ReminderEntity Entity { get; } = entity;
        public ReminderKey Key { get; } = key;
        public DateTimeOffset DueTimeUtc { get; } = dueTimeUtc;
        public DateTimeOffset? DeliveryDeadlineUtc { get; } = deliveryDeadlineUtc;
    }
}
