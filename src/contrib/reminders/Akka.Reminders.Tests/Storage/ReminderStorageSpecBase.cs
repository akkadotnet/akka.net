using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Storage;

/// <summary>
/// Abstract base class for testing <see cref="IReminderStorage"/> implementations.
/// </summary>
/// <remarks>
/// Inherit from this class and implement <see cref="CreateStorage"/> to test your storage implementation.
/// All tests will be automatically run against your implementation.
/// </remarks>
public abstract partial class ReminderStorageSpecBase : IAsyncLifetime
{
    private static readonly ReminderBatchSize DefaultBatchSize = new(1000);

    /// <summary>
    /// Creates an instance of the storage implementation to test.
    /// </summary>
    /// <returns>A new storage instance for testing.</returns>
    protected abstract Task<IReminderStorage> CreateStorage();

    /// <summary>
    /// Cleans up the storage after each test.
    /// </summary>
    /// <param name="storage">The storage instance to clean up.</param>
    protected abstract Task CleanupStorage(IReminderStorage storage);

    protected IReminderStorage? Storage { get; private set; }

    public async ValueTask InitializeAsync()
    {
        Storage = await CreateStorage();
    }

    public async ValueTask DisposeAsync()
    {
        if (Storage != null)
        {
            await CleanupStorage(Storage);
        }
    }

    protected static ReminderEntity CreateTestEntity(string entityType = "test-entity", string entityId = "test-123")
    {
        return new ReminderEntity(entityType, entityId);
    }

    protected static ReminderKey CreateTestKey(string name = "test-reminder")
    {
        return new ReminderKey(name);
    }

    protected static ScheduledReminder CreateTestReminder(
        ReminderEntity? entity = null,
        ReminderKey? key = null,
        DateTimeOffset? when = null,
        object? message = null)
    {
        return new ScheduledReminder(
            entity ?? CreateTestEntity(),
            key ?? CreateTestKey(),
            when ?? DateTimeOffset.UtcNow.AddMinutes(5),
            message ?? "test message");
    }

    #region Schedule Reminder Tests

    [Fact]
    public async Task ScheduleReminder_ShouldReturnSuccess_WhenNewReminder()
    {
        // Arrange
        var reminder = CreateTestReminder();

        // Act
        var result = await Storage!.ScheduleReminderAsync(reminder, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);
        Assert.Equal(reminder.Entity, result.Entity);
        Assert.Equal(reminder.Key, result.Key);
        // Allow for microsecond precision differences (PostgreSQL has 6 decimals, .NET has 7)
        var timeDiff = Math.Abs((reminder.When - result.When).TotalMilliseconds);
        Assert.True(timeDiff < 0.001, $"Time difference {timeDiff}ms exceeds 1 microsecond tolerance");
    }

    [Fact]
    public async Task ScheduleReminder_ShouldOverwrite_WhenReminderWithSameKeyExists()
    {
        // Arrange
        var entity = CreateTestEntity();
        var key = CreateTestKey();
        var reminder1 = CreateTestReminder(entity, key, DateTimeOffset.UtcNow.AddMinutes(5), "message1");
        var reminder2 = CreateTestReminder(entity, key, DateTimeOffset.UtcNow.AddMinutes(10), "message2");

        await Storage!.ScheduleReminderAsync(reminder1, TestContext.Current.CancellationToken);

        // Act - schedule different reminder with same entity and key (should overwrite)
        var result = await Storage.ScheduleReminderAsync(reminder2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        // Verify only the new reminder exists
        var reminders = await Storage.GetRemindersForEntityAsync(entity, ct: TestContext.Current.CancellationToken);
        Assert.Single(reminders);
        // Allow for microsecond precision differences (PostgreSQL has 6 decimals, .NET has 7)
        var timeDiff = Math.Abs((reminder2.When - reminders[0].When).TotalMilliseconds);
        Assert.True(timeDiff < 0.001, $"Time difference {timeDiff}ms exceeds 1 microsecond tolerance");
        Assert.Equal("message2", reminders[0].Message);
    }

    [Fact]
    public async Task ScheduleReminder_ShouldAllowSameKey_ForDifferentEntities()
    {
        // Arrange
        var key = CreateTestKey("shared-key");
        var reminder1 = CreateTestReminder(CreateTestEntity("type1", "id1"), key);
        var reminder2 = CreateTestReminder(CreateTestEntity("type2", "id2"), key);

        // Act
        var result1 = await Storage!.ScheduleReminderAsync(reminder1, TestContext.Current.CancellationToken);
        var result2 = await Storage.ScheduleReminderAsync(reminder2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReminderScheduleResponseCode.Success, result1.ResponseCode);
        Assert.Equal(ReminderScheduleResponseCode.Success, result2.ResponseCode);
    }

    [Fact]
    public async Task ScheduleReminder_ShouldSupportRecurringReminders()
    {
        // Arrange
        var entity = CreateTestEntity();
        var key = CreateTestKey("recurring-reminder");
        var recurringReminder = new ScheduledReminder(
            entity,
            key,
            DateTimeOffset.UtcNow.AddMinutes(5),
            "test message",
            RepeatInterval: TimeSpan.FromHours(1));

        // Act
        var result = await Storage!.ScheduleReminderAsync(recurringReminder, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        // Verify the recurring reminder was stored with repeat interval
        var reminders = await Storage.GetRemindersForEntityAsync(entity, ct: TestContext.Current.CancellationToken);
        Assert.Single(reminders);
        Assert.Equal(TimeSpan.FromHours(1), reminders[0].RepeatInterval);
    }

    [Fact]
    public async Task ScheduleReminder_ShouldTrackRetryAttempts()
    {
        // Arrange
        var entity = CreateTestEntity();
        var key = CreateTestKey("retry-reminder");
        var reminderWithRetry = new ScheduledReminder(
            entity,
            key,
            DateTimeOffset.UtcNow.AddMinutes(5),
            "test message",
            RepeatInterval: null,
            AttemptCount: 2,
            LastFailureReason: "ShardRegion not found");

        // Act
        var result = await Storage!.ScheduleReminderAsync(reminderWithRetry, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReminderScheduleResponseCode.Success, result.ResponseCode);

        // Verify retry tracking was preserved
        var reminders = await Storage.GetRemindersForEntityAsync(entity, ct: TestContext.Current.CancellationToken);
        Assert.Single(reminders);
        Assert.Equal(2, reminders[0].AttemptCount);
        Assert.Equal("ShardRegion not found", reminders[0].LastFailureReason);
    }

    #endregion

    #region Cancel Reminder Tests

    [Fact]
    public async Task CancelReminder_ShouldReturnSuccess_WhenReminderExists()
    {
        // Arrange
        var reminder = CreateTestReminder();
        await Storage!.ScheduleReminderAsync(reminder, TestContext.Current.CancellationToken);

        // Act
        var result = await Storage.CancelReminderAsync(reminder.Entity, reminder.Key, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReminderCancelResponseCode.Success, result.ResponseCode);
        Assert.Contains(reminder.Key, result.Keys);
    }

    [Fact]
    public async Task CancelReminder_ShouldReturnNotFound_WhenReminderDoesNotExist()
    {
        // Arrange
        var entity = CreateTestEntity();
        var key = CreateTestKey();

        // Act
        var result = await Storage!.CancelReminderAsync(entity, key, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReminderCancelResponseCode.NotFound, result.ResponseCode);
    }

    [Fact]
    public async Task CancelReminder_ShouldOnlyCancelSpecificReminder()
    {
        // Arrange
        var entity = CreateTestEntity();
        var key1 = CreateTestKey("reminder1");
        var key2 = CreateTestKey("reminder2");

        await Storage!.ScheduleReminderAsync(CreateTestReminder(entity, key1), TestContext.Current.CancellationToken);
        await Storage.ScheduleReminderAsync(CreateTestReminder(entity, key2), TestContext.Current.CancellationToken);

        // Act
        await Storage.CancelReminderAsync(entity, key1, TestContext.Current.CancellationToken);

        // Assert - key2 should still exist
        var reminders = await Storage.GetRemindersForEntityAsync(entity, ct: TestContext.Current.CancellationToken);
        Assert.Single(reminders);
        Assert.Equal(key2, reminders[0].Key);
    }

    #endregion

    #region Cancel All Reminders Tests

    [Fact]
    public async Task CancelAllReminders_ShouldReturnSuccess_WhenRemindersExist()
    {
        // Arrange
        var entity = CreateTestEntity();
        var key1 = CreateTestKey("reminder1");
        var key2 = CreateTestKey("reminder2");

        await Storage!.ScheduleReminderAsync(CreateTestReminder(entity, key1), TestContext.Current.CancellationToken);
        await Storage.ScheduleReminderAsync(CreateTestReminder(entity, key2), TestContext.Current.CancellationToken);

        // Act
        var result = await Storage.CancelAllRemindersForEntityAsync(entity, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReminderCancelResponseCode.Success, result.ResponseCode);
        Assert.Equal(2, result.Keys.Count);
        Assert.Contains(key1, result.Keys);
        Assert.Contains(key2, result.Keys);
    }

    [Fact]
    public async Task CancelAllReminders_ShouldReturnNotFound_WhenNoRemindersExist()
    {
        // Arrange
        var entity = CreateTestEntity();

        // Act
        var result = await Storage!.CancelAllRemindersForEntityAsync(entity, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReminderCancelResponseCode.NotFound, result.ResponseCode);
    }

    [Fact]
    public async Task CancelAllReminders_ShouldOnlyAffectSpecificEntity()
    {
        // Arrange
        var entity1 = CreateTestEntity("type1", "id1");
        var entity2 = CreateTestEntity("type2", "id2");

        await Storage!.ScheduleReminderAsync(CreateTestReminder(entity1), TestContext.Current.CancellationToken);
        await Storage.ScheduleReminderAsync(CreateTestReminder(entity2), TestContext.Current.CancellationToken);

        // Act
        await Storage.CancelAllRemindersForEntityAsync(entity1, TestContext.Current.CancellationToken);

        // Assert - entity2 reminders should still exist
        var reminders = await Storage.GetRemindersForEntityAsync(entity2, ct: TestContext.Current.CancellationToken);
        Assert.Single(reminders);
    }

    #endregion

    #region Get Reminders Tests

    [Fact]
    public async Task GetRemindersForEntity_ShouldReturnEmpty_WhenNoRemindersExist()
    {
        // Arrange
        var entity = CreateTestEntity();

        // Act
        var result = await Storage!.GetRemindersForEntityAsync(entity, ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRemindersForEntity_ShouldReturnAllReminders_ForEntity()
    {
        // Arrange
        var entity = CreateTestEntity();
        var reminder1 = CreateTestReminder(entity, CreateTestKey("r1"));
        var reminder2 = CreateTestReminder(entity, CreateTestKey("r2"));

        await Storage!.ScheduleReminderAsync(reminder1, TestContext.Current.CancellationToken);
        await Storage.ScheduleReminderAsync(reminder2, TestContext.Current.CancellationToken);

        // Act
        var result = await Storage.GetRemindersForEntityAsync(entity, ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetRemindersForEntity_ShouldOnlyReturnRemindersForSpecificEntity()
    {
        // Arrange
        var entity1 = CreateTestEntity("type1", "id1");
        var entity2 = CreateTestEntity("type2", "id2");

        await Storage!.ScheduleReminderAsync(CreateTestReminder(entity1), TestContext.Current.CancellationToken);
        await Storage.ScheduleReminderAsync(CreateTestReminder(entity2), TestContext.Current.CancellationToken);

        // Act
        var result = await Storage.GetRemindersForEntityAsync(entity1, ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.Equal(entity1, result[0].Entity);
    }

    [Fact]
    public async Task GetRemindersForEntity_ShouldRespectPagination()
    {
        // Arrange
        var entity = CreateTestEntity();
        for (int i = 0; i < 20; i++)
        {
            await Storage!.ScheduleReminderAsync(
                CreateTestReminder(entity, CreateTestKey($"reminder-{i}")), TestContext.Current.CancellationToken);
        }

        // Act
        var page1 = await Storage!.GetRemindersForEntityAsync(entity, take: 10, skip: 0, TestContext.Current.CancellationToken);
        var page2 = await Storage.GetRemindersForEntityAsync(entity, take: 10, skip: 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(10, page1.Count);
        Assert.Equal(10, page2.Count);

        // Ensure no duplicates between pages
        var allKeys = page1.Select(r => r.Key).Concat(page2.Select(r => r.Key)).ToList();
        Assert.Equal(20, allKeys.Distinct().Count());
    }

    #endregion

    #region Overview Tests

    [Fact]
    public async Task GetRemindersOverview_ShouldReturnZero_WhenNoRemindersExist()
    {
        // Act
        var overview = await Storage!.GetRemindersOverviewAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, overview.TotalPendingReminders);
        Assert.Equal(TimeSpan.MaxValue, overview.TimeUntilNext);
    }

    [Fact]
    public async Task GetRemindersOverview_ApplyMethod_ShouldReturnHasNewerDate_WhenDatabaseIsEmpty()
    {
        // This test validates the fix for a bug where reminders scheduled against an empty
        // database were never executed until the system restarted.
        // The root cause was that SQL storage returned TimeSpan.Zero for empty databases,
        // causing Apply() to return hasNewerDate=false for future reminders.

        // Arrange - get overview from empty database
        var now = DateTimeOffset.UtcNow;
        var emptyOverview = await Storage!.GetRemindersOverviewAsync(now, TestContext.Current.CancellationToken);

        // Act - apply a new reminder scheduled for 5 minutes in the future
        var futureReminder = CreateTestReminder(when: now.AddMinutes(5));
        var (newOverview, hasNewerDate) = emptyOverview.Apply(futureReminder, now);

        // Assert - hasNewerDate must be true so TryScheduleFetchReminders() is called
        Assert.True(hasNewerDate,
            "When database is empty, any new reminder should be considered 'newer' to trigger scheduling. " +
            $"TimeUntilNext was: {emptyOverview.TimeUntilNext}");
        Assert.Equal(1, newOverview.TotalPendingReminders);
    }

    [Fact]
    public async Task GetRemindersOverview_ShouldReturnCorrectCount()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            await Storage!.ScheduleReminderAsync(
                CreateTestReminder(
                    CreateTestEntity("type", $"id-{i}"),
                    CreateTestKey($"key-{i}")), TestContext.Current.CancellationToken);
        }

        // Act
        var overview = await Storage!.GetRemindersOverviewAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, overview.TotalPendingReminders);
    }

    [Fact]
    public async Task GetRemindersOverview_ShouldReturnTimeToNextReminder()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var nearFuture = now.AddMinutes(5);
        var farFuture = now.AddHours(1);

        await Storage!.ScheduleReminderAsync(CreateTestReminder(when: farFuture), TestContext.Current.CancellationToken);
        await Storage.ScheduleReminderAsync(CreateTestReminder(CreateTestEntity("type2", "id2"), when: nearFuture), TestContext.Current.CancellationToken);

        // Act
        var overview = await Storage.GetRemindersOverviewAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(overview.TimeUntilNext <= TimeSpan.FromMinutes(6));
        Assert.True(overview.TimeUntilNext >= TimeSpan.FromMinutes(4));
    }

    #endregion

    #region Get Next Reminders Tests

    [Fact]
    public async Task GetNextReminders_ShouldReturnEmpty_WhenNoRemindersDue()
    {
        // Arrange
        var futureTime = DateTimeOffset.UtcNow.AddHours(1);
        await Storage!.ScheduleReminderAsync(CreateTestReminder(when: futureTime), TestContext.Current.CancellationToken);

        // Act
        var now = DateTimeOffset.UtcNow;
        var result = await Storage.GetNextRemindersAsync(now, now, DefaultBatchSize, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Reminders);
        Assert.Equal(1, result.NextOverview.TotalPendingReminders);
    }

    [Fact]
    public async Task GetNextReminders_ShouldReturnDueReminders()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var past = now.AddMinutes(-5);
        var nearFuture = now.AddMinutes(5);
        var farFuture = now.AddHours(1);

        await Storage!.ScheduleReminderAsync(CreateTestReminder(CreateTestEntity("t1", "i1"), when: past), TestContext.Current.CancellationToken);
        await Storage.ScheduleReminderAsync(CreateTestReminder(CreateTestEntity("t2", "i2"), when: nearFuture), TestContext.Current.CancellationToken);
        await Storage.ScheduleReminderAsync(CreateTestReminder(CreateTestEntity("t3", "i3"), when: farFuture), TestContext.Current.CancellationToken);

        // Act - get reminders due up until nearFuture
        var result = await Storage.GetNextRemindersAsync(nearFuture, nearFuture, DefaultBatchSize, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Reminders.Count); // past and nearFuture
        Assert.Equal(1, result.NextOverview.TotalPendingReminders); // farFuture remains
    }

    [Fact]
    public async Task GetNextReminders_ShouldUpdateOverviewCorrectly()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        await Storage!.ScheduleReminderAsync(CreateTestReminder(when: now.AddMinutes(1)), TestContext.Current.CancellationToken);
        await Storage.ScheduleReminderAsync(CreateTestReminder(CreateTestEntity("t2", "i2"), when: now.AddMinutes(10)), TestContext.Current.CancellationToken);

        // Act
        var deadline = now.AddMinutes(5);
        var result = await Storage.GetNextRemindersAsync(deadline, deadline, DefaultBatchSize, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result.Reminders);
        Assert.Equal(1, result.NextOverview.TotalPendingReminders);
        Assert.True(result.NextOverview.TimeUntilNext >= TimeSpan.FromMinutes(4));
    }

    [Fact]
    public async Task GetNextReminders_ShouldRespectMaxCount()
    {
        // Arrange - schedule N reminders that are all due now
        var now = DateTimeOffset.UtcNow;
        var past = now.AddMinutes(-1);
        const int totalReminders = 10;
        const int maxCount = 4;

        for (int i = 0; i < totalReminders; i++)
        {
            await Storage!.ScheduleReminderAsync(
                CreateTestReminder(
                    CreateTestEntity($"batch-type", $"batch-id-{i}"),
                    CreateTestKey($"batch-key-{i}"),
                    when: past), TestContext.Current.CancellationToken);
        }

        // Act - fetch with maxCount
        var result = await Storage!.GetNextRemindersAsync(now, now, maxCount: new ReminderBatchSize(maxCount), TestContext.Current.CancellationToken);

        // Assert - should only return maxCount reminders
        Assert.Equal(maxCount, result.Reminders.Count);

        // The overview should reflect the remaining reminders (not yet fetched)
        Assert.Equal(totalReminders - maxCount, result.NextOverview.TotalPendingReminders);
    }

    #endregion

    #region Awaiting Ack / Deadline Tests

    /// <summary>
    /// When a reminder is delivered, the scheduler moves it from Pending to AwaitingAck.
    /// AwaitingAck reminders must NOT appear in the pending overview — otherwise the
    /// scheduler would re-fetch and re-deliver them on the next tick, causing duplicates.
    /// </summary>
    [Fact]
    public async Task MarkRemindersAsAwaitingAck_ShouldRemoveReminderFromPendingOverview()
    {
        var now = DateTimeOffset.UtcNow;
        var reminder = CreateTestReminder(when: now.AddMinutes(5));
        await Storage!.ScheduleReminderAsync(reminder, TestContext.Current.CancellationToken);

        // Transition to AwaitingAck — simulates what the scheduler does after delivering
        // the reminder via Tell. The ackDeadline is when the scheduler will retry if no
        // ack arrives (now + AckTimeout).
        var awaitingAck = new AwaitingAckReminder(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            now,           // deliveredAt
            now.AddMinutes(1)); // ackDeadline

        var result = await Storage.MarkRemindersAsAwaitingAckAsync([awaitingAck], TestContext.Current.CancellationToken);
        Assert.True(result);

        // The overview only counts Pending rows. AwaitingAck rows are invisible to the
        // fetch loop — they're tracked by the ack-timeout checker instead.
        var overview = await Storage.GetRemindersOverviewAsync(now, TestContext.Current.CancellationToken);
        Assert.Equal(0, overview.TotalPendingReminders);
    }

    /// <summary>
    /// Acks are matched by the full occurrence key: (Entity, Key, DueTimeUtc).
    /// A DueTimeUtc mismatch — even by one second — must return NotFound.
    /// This is critical for recurring reminders: a late ack for an old occurrence
    /// (different DueTimeUtc) must not accidentally complete the current one.
    /// </summary>
    [Fact]
    public async Task AcknowledgeReminderAsync_ShouldRequireMatchingDueTime()
    {
        var now = DateTimeOffset.UtcNow;
        var reminder = CreateTestReminder(when: now.AddMinutes(5));
        await Storage!.ScheduleReminderAsync(reminder, TestContext.Current.CancellationToken);

        var awaitingAck = new AwaitingAckReminder(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            now,
            now.AddMinutes(1));
        await Storage.MarkRemindersAsAwaitingAckAsync([awaitingAck], TestContext.Current.CancellationToken);

        // Ack with wrong DueTimeUtc — off by 1 second. This simulates a late ack
        // arriving for a superseded occurrence of a recurring reminder.
        var wrongAck = await Storage.AcknowledgeReminderAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc.AddSeconds(1), // wrong occurrence
            now.AddSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(wrongAck.Success);
        Assert.Equal(ReminderAckStorageStatus.NotFound, wrongAck.Status);

        // Ack with correct DueTimeUtc — matches the occurrence identity exactly.
        var correctAck = await Storage.AcknowledgeReminderAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            now.AddSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(correctAck.Success);
        Assert.Equal(ReminderAckStorageStatus.Success, correctAck.Status);
    }

    /// <summary>
    /// This test exercises the core delivery-commit pattern: in a single atomic batch,
    /// the scheduler must (1) insert the next recurring occurrence as Pending and
    /// (2) move the current occurrence to AwaitingAck. Both must succeed or neither.
    ///
    /// After the commit:
    /// - The overview should show exactly 1 pending reminder (the next occurrence).
    ///   The current occurrence is AwaitingAck and invisible to the fetch loop.
    /// - Both occurrences should be visible via GetRemindersForEntityAsync (which
    ///   returns both Pending and AwaitingAck rows for the entity).
    /// - The current (AwaitingAck) occurrence should be ackable.
    /// </summary>
    [Fact]
    public async Task CommitReminderMutationsAsync_ShouldAtomicallyMovePendingToAwaitingAckAndUpsertNextOccurrence()
    {
        var now = DateTimeOffset.UtcNow;
        var reminder = new ScheduledReminder(
            CreateTestEntity(),
            CreateTestKey("atomic-recurring"),
            now.AddMinutes(1),
            "payload",
            RepeatInterval: TimeSpan.FromMinutes(1),
            OccurrenceDueTimeUtc: now.AddMinutes(1));

        await Storage!.ScheduleReminderAsync(reminder, TestContext.Current.CancellationToken);

        // Create the next recurring occurrence — due 1 interval later.
        // In production, CreateNextRecurringOccurrence does this.
        var nextOccurrence = reminder with
        {
            When = reminder.DueTimeUtc.AddMinutes(1),
            OccurrenceDueTimeUtc = reminder.DueTimeUtc.AddMinutes(1)
        };

        var awaiting = new AwaitingAckReminder(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            now,
            now.AddMinutes(1));

        // Commit both the next occurrence upsert and the AwaitingAck transition
        // in a single batch — this is the atomicity boundary that prevents
        // partial state during failures.
        var result = await Storage.CommitReminderMutationsAsync(new ReminderMutationBatch(
            [nextOccurrence],  // PendingUpserts: next recurring occurrence
            [],                // CompletedReminders: none
            [awaiting]), TestContext.Current.CancellationToken);      // AwaitingAckReminders: current occurrence

        Assert.True(result);

        // Only the next occurrence is Pending — the current one is AwaitingAck
        // and excluded from the overview.
        var overview = await Storage.GetRemindersOverviewAsync(now, TestContext.Current.CancellationToken);
        Assert.Equal(1, overview.TotalPendingReminders);

        // GetRemindersForEntityAsync returns both Pending and AwaitingAck rows,
        // so both occurrences should be visible (two different DueTimeUtc values).
        var reminders = await Storage.GetRemindersForEntityAsync(reminder.Entity, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, reminders.Count);
        Assert.Contains(reminders, r => Math.Abs((r.DueTimeUtc - reminder.DueTimeUtc).TotalMilliseconds) < 0.001);
        Assert.Contains(reminders, r => Math.Abs((r.DueTimeUtc - nextOccurrence.DueTimeUtc).TotalMilliseconds) < 0.001);

        // The AwaitingAck occurrence should be ackable by its original DueTimeUtc.
        var ack = await Storage.AcknowledgeReminderAsync(reminder.Entity, reminder.Key, reminder.DueTimeUtc, now.AddSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(ack.Success);
    }

    /// <summary>
    /// When acks are flushed in a batch, the storage must return per-occurrence results.
    /// A batch might contain both valid acks (matching an AwaitingAck row) and stale acks
    /// (no matching row — the occurrence was already completed, expired, or superseded).
    /// Each result must independently report Success or NotFound.
    /// </summary>
    [Fact]
    public async Task AcknowledgeRemindersAsync_ShouldReturnPerOccurrenceResults()
    {
        var now = DateTimeOffset.UtcNow;
        var reminder = CreateTestReminder(when: now.AddMinutes(5));
        await Storage!.ScheduleReminderAsync(reminder, TestContext.Current.CancellationToken);

        // Move to AwaitingAck so there's something to acknowledge.
        await Storage.CommitReminderMutationsAsync(new ReminderMutationBatch(
            [],
            [],
            [new AwaitingAckReminder(reminder.Entity, reminder.Key, reminder.DueTimeUtc, now, now.AddMinutes(1))]), TestContext.Current.CancellationToken);

        // Ack two occurrences in a single batch: one that exists (correct DueTimeUtc)
        // and one that doesn't (DueTimeUtc + 1 minute — no such occurrence).
        var missingDueTime = reminder.DueTimeUtc.AddMinutes(1);
        var results = await Storage.AcknowledgeRemindersAsync([
            new ReminderAcknowledgement(reminder.Entity, reminder.Key, reminder.DueTimeUtc, now.AddSeconds(5)),
            new ReminderAcknowledgement(reminder.Entity, reminder.Key, missingDueTime, now.AddSeconds(5))
        ], TestContext.Current.CancellationToken);

        // Results are positional — one per ack in the input batch.
        Assert.Equal(2, results.Count);
        Assert.Equal(ReminderAckStorageStatus.Success, results[0].Status);  // valid ack
        Assert.Equal(ReminderAckStorageStatus.NotFound, results[1].Status); // stale/missing ack
    }

    [Fact]
    public async Task OccurrenceStatus_ShouldExposeActiveAndTerminalStates()
    {
        var now = DateTimeOffset.UtcNow;
        var reminder = CreateTestReminder(when: now.AddMinutes(5)) with
        {
            AttemptCount = 2,
            LastFailureReason = "prior failure",
            DeliveryDeadlineUtc = now.AddHours(1),
            OccurrenceDueTimeUtc = now.AddMinutes(5)
        };
        await Storage!.ScheduleReminderAsync(reminder, ct: TestContext.Current.CancellationToken);

        var pending = await Storage.GetReminderOccurrenceStatusAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            ct: TestContext.Current.CancellationToken);
        Assert.NotNull(pending);
        Assert.Equal(ReminderCompletionStatus.Pending, pending.CompletionStatus);
        Assert.Equal(2, pending.AttemptCount);
        Assert.Equal("prior failure", pending.LastFailureReason);
        Assert.NotNull(pending.NextAttemptAtUtc);

        await Storage.CommitReminderMutationsAsync(new ReminderMutationBatch(
            [],
            [],
            [new AwaitingAckReminder(reminder.Entity, reminder.Key, reminder.DueTimeUtc, now, now.AddMinutes(1))]), ct: TestContext.Current.CancellationToken);

        var awaiting = await Storage.GetReminderOccurrenceStatusAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            ct: TestContext.Current.CancellationToken);
        Assert.NotNull(awaiting);
        Assert.Equal(ReminderCompletionStatus.AwaitingAck, awaiting.CompletionStatus);
        Assert.Null(awaiting.NextAttemptAtUtc);
        Assert.NotNull(awaiting.AckDeadlineUtc);
        Assert.True(Math.Abs((awaiting.AckDeadlineUtc.Value - now.AddMinutes(1)).TotalMilliseconds) < 0.001);

        var ack = await Storage.AcknowledgeReminderAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            now.AddSeconds(5),
            ct: TestContext.Current.CancellationToken);
        Assert.True(ack.Success);

        var delivered = await Storage.GetReminderOccurrenceStatusAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            ct: TestContext.Current.CancellationToken);
        Assert.NotNull(delivered);
        Assert.Equal(ReminderCompletionStatus.Delivered, delivered.CompletionStatus);
        Assert.Null(delivered.NextAttemptAtUtc);
        Assert.Equal(2, delivered.AttemptCount);
    }

    [Fact]
    public async Task GetAwaitingAckReminderAsync_ShouldMatchExactActiveOccurrence()
    {
        var now = DateTimeOffset.UtcNow;
        var reminder = CreateTestReminder(when: now.AddMinutes(5)) with
        {
            AttemptCount = 2,
            LastFailureReason = "prior failure",
            DeliveryDeadlineUtc = now.AddHours(1),
            OccurrenceDueTimeUtc = now.AddMinutes(5)
        };
        await Storage!.ScheduleReminderAsync(reminder, ct: TestContext.Current.CancellationToken);

        var missingBeforeDelivery = await Storage.GetAwaitingAckReminderAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            ct: TestContext.Current.CancellationToken);
        Assert.Null(missingBeforeDelivery);

        await Storage.CommitReminderMutationsAsync(new ReminderMutationBatch(
            [],
            [],
            [new AwaitingAckReminder(reminder.Entity, reminder.Key, reminder.DueTimeUtc, now, now.AddMinutes(1))]), ct: TestContext.Current.CancellationToken);

        var found = await Storage.GetAwaitingAckReminderAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            ct: TestContext.Current.CancellationToken);
        Assert.NotNull(found);
        Assert.Equal(2, found.AttemptCount);
        Assert.Equal("prior failure", found.LastFailureReason);
        Assert.NotNull(found.DeliveryDeadlineUtc);
        Assert.True(Math.Abs(
            (reminder.DeliveryDeadlineUtc!.Value - found.DeliveryDeadlineUtc.Value).TotalMilliseconds) < 0.001);

        var stale = await Storage.GetAwaitingAckReminderAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc.AddSeconds(1),
            ct: TestContext.Current.CancellationToken);
        Assert.Null(stale);
    }

    [Fact]
    public async Task TerminalStatus_ShouldPreserveFailureDetails()
    {
        var now = DateTimeOffset.UtcNow;
        var reminder = CreateTestReminder(when: now.AddMinutes(5)) with
        {
            AttemptCount = 3,
            LastFailureReason = "final failure",
            DeliveryDeadlineUtc = now.AddHours(1)
        };
        await Storage!.ScheduleReminderAsync(reminder, ct: TestContext.Current.CancellationToken);
        await Storage.MarkRemindersAsCompletedAsync([
            new CompletedReminder(
                reminder.Entity,
                reminder.Key,
                reminder.DueTimeUtc,
                now,
                ReminderCompletionStatus.Failed)
        ], ct: TestContext.Current.CancellationToken);

        var status = await Storage.GetReminderOccurrenceStatusAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal(ReminderCompletionStatus.Failed, status.CompletionStatus);
        Assert.Equal(3, status.AttemptCount);
        Assert.Equal("final failure", status.LastFailureReason);
        Assert.NotNull(status.DeliveryDeadlineUtc);
        Assert.True(Math.Abs(
            (reminder.DeliveryDeadlineUtc!.Value - status.DeliveryDeadlineUtc.Value).TotalMilliseconds) < 0.001);
        Assert.Null(status.NextAttemptAtUtc);
    }

    [Fact]
    public async Task CancelledStatus_ShouldPreserveOccurrenceDetails()
    {
        var now = DateTimeOffset.UtcNow;
        var reminder = CreateTestReminder(when: now.AddMinutes(5)) with
        {
            AttemptCount = 2,
            LastFailureReason = "prior failure"
        };
        await Storage!.ScheduleReminderAsync(reminder, ct: TestContext.Current.CancellationToken);
        await Storage.CancelReminderAsync(reminder.Entity, reminder.Key, ct: TestContext.Current.CancellationToken);

        var status = await Storage.GetReminderOccurrenceStatusAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal(ReminderCompletionStatus.Cancelled, status.CompletionStatus);
        Assert.Equal(2, status.AttemptCount);
        Assert.Equal("prior failure", status.LastFailureReason);
        Assert.Null(status.NextAttemptAtUtc);
    }

    [Fact]
    public async Task ExpiredStatus_ShouldPreserveOccurrenceDetails()
    {
        var now = DateTimeOffset.UtcNow;
        var reminder = CreateTestReminder(when: now.AddMinutes(-2)) with
        {
            AttemptCount = 2,
            LastFailureReason = "prior failure",
            DeliveryDeadlineUtc = now.AddMinutes(-1)
        };
        await Storage!.ScheduleReminderAsync(reminder, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await Storage.ExpireRemindersAsync(now, ct: TestContext.Current.CancellationToken));

        var status = await Storage.GetReminderOccurrenceStatusAsync(
            reminder.Entity,
            reminder.Key,
            reminder.DueTimeUtc,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal(ReminderCompletionStatus.Expired, status.CompletionStatus);
        Assert.Equal(2, status.AttemptCount);
        Assert.Equal("prior failure", status.LastFailureReason);
        Assert.Null(status.NextAttemptAtUtc);
    }

    [Fact]
    public async Task ReplacementStatus_ShouldPreserveCancelledOccurrenceDetails()
    {
        var now = DateTimeOffset.UtcNow;
        var original = CreateTestReminder(when: now.AddMinutes(5)) with
        {
            AttemptCount = 2,
            LastFailureReason = "prior failure"
        };
        await Storage!.ScheduleReminderAsync(original, ct: TestContext.Current.CancellationToken);
        await Storage.ScheduleReminderAsync(original with
        {
            When = now.AddMinutes(10),
            OccurrenceDueTimeUtc = now.AddMinutes(10),
            AttemptCount = 0,
            LastFailureReason = null
        }, ct: TestContext.Current.CancellationToken);

        var status = await Storage.GetReminderOccurrenceStatusAsync(
            original.Entity,
            original.Key,
            original.DueTimeUtc,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal(ReminderCompletionStatus.Cancelled, status.CompletionStatus);
        Assert.Equal(2, status.AttemptCount);
        Assert.Equal("prior failure", status.LastFailureReason);
    }

    /// <summary>
    /// The scheduler uses event-driven ack-timeout checking. After delivering reminders,
    /// it queries storage for the earliest ack deadline to schedule a one-shot timer.
    /// This test verifies that GetNextAwaitingAckDeadlineAsync returns the minimum
    /// ack deadline across all AwaitingAck rows, regardless of insertion order.
    /// </summary>
    [Fact]
    public async Task GetNextAwaitingAckDeadlineAsync_ShouldReturnEarliestAwaitingDeadline()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateTestReminder(CreateTestEntity("a", "1"), CreateTestKey("first"), now.AddMinutes(1));
        var second = CreateTestReminder(CreateTestEntity("a", "2"), CreateTestKey("second"), now.AddMinutes(2));

        await Storage!.ScheduleReminderAsync(first, TestContext.Current.CancellationToken);
        await Storage.ScheduleReminderAsync(second, TestContext.Current.CancellationToken);

        // Move both to AwaitingAck with different deadlines.
        // "first" has a later deadline (20s), "second" has an earlier one (10s).
        // The query must return the earlier deadline regardless of insertion order.
        await Storage.CommitReminderMutationsAsync(new ReminderMutationBatch(
            [],
            [],
            [
                new AwaitingAckReminder(first.Entity, first.Key, first.DueTimeUtc, now, now.AddSeconds(20)),
                new AwaitingAckReminder(second.Entity, second.Key, second.DueTimeUtc, now, now.AddSeconds(10))
            ]), TestContext.Current.CancellationToken);

        var deadline = await Storage.GetNextAwaitingAckDeadlineAsync(TestContext.Current.CancellationToken);

        // Should be the earlier deadline (now + 10s), not the first-inserted one (now + 20s).
        Assert.NotNull(deadline);
        Assert.True(Math.Abs((deadline!.Value - now.AddSeconds(10)).TotalMilliseconds) < 0.001);
    }

    /// <summary>
    /// Reminders with a MaxDeliveryWindow have a computed DeliveryDeadlineUtc.
    /// When the current time passes that deadline, ExpireRemindersAsync should
    /// mark the reminder as completed with status Expired and remove it from
    /// the pending set. This prevents stale reminders from being delivered
    /// long after they're relevant.
    ///
    /// In this test, the reminder was due 2 minutes ago with a 1-minute delivery
    /// window, so its deadline was 1 minute ago — it should be expired.
    /// </summary>
    [Fact]
    public async Task ExpireRemindersAsync_ShouldCompleteExpiredPendingReminder()
    {
        var now = DateTimeOffset.UtcNow;
        var dueTime = now.AddMinutes(-2); // due 2 minutes ago
        var reminder = new ScheduledReminder(
            CreateTestEntity(),
            CreateTestKey("expiring-reminder"),
            dueTime,
            "stale",
            MaxDeliveryWindow: TimeSpan.FromMinutes(1),           // 1-minute window
            DeliveryDeadlineUtc: dueTime.AddMinutes(1),           // deadline was 1 minute ago
            OccurrenceDueTimeUtc: dueTime);

        await Storage!.ScheduleReminderAsync(reminder, TestContext.Current.CancellationToken);

        // Expire should find this reminder because now > dueTime + 1 minute.
        var expiredCount = await Storage.ExpireRemindersAsync(now, TestContext.Current.CancellationToken);
        Assert.True(expiredCount >= 1);

        // The reminder is gone from the pending set — it won't be fetched or delivered.
        var overview = await Storage.GetRemindersOverviewAsync(now, TestContext.Current.CancellationToken);
        Assert.Equal(0, overview.TotalPendingReminders);

        // GetRemindersForEntityAsync also excludes expired reminders.
        var reminders = await Storage.GetRemindersForEntityAsync(reminder.Entity, ct: TestContext.Current.CancellationToken);
        Assert.Empty(reminders);
    }

    #endregion

    #region Mark Completed Tests

    [Fact]
    public async Task MarkRemindersAsCompleted_ShouldReturnTrue_WhenRemindersExist()
    {
        // Arrange
        var reminder = CreateTestReminder();
        await Storage!.ScheduleReminderAsync(reminder, TestContext.Current.CancellationToken);

        var completed = new CompletedReminder(reminder.Entity, reminder.Key, reminder.DueTimeUtc, DateTimeOffset.UtcNow);

        // Act
        var result = await Storage.MarkRemindersAsCompletedAsync(new[] { completed }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task MarkRemindersAsCompleted_ShouldRemoveFromPendingReminders()
    {
        // Arrange
        var reminder = CreateTestReminder();
        await Storage!.ScheduleReminderAsync(reminder, TestContext.Current.CancellationToken);

        var completed = new CompletedReminder(reminder.Entity, reminder.Key, reminder.DueTimeUtc, DateTimeOffset.UtcNow);
        await Storage.MarkRemindersAsCompletedAsync(new[] { completed }, TestContext.Current.CancellationToken);

        // Act
        var overview = await Storage.GetRemindersOverviewAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, overview.TotalPendingReminders);
    }

    [Fact]
    public async Task MarkRemindersAsCompleted_ShouldHandleMultipleReminders()
    {
        // Arrange
        var entity = CreateTestEntity();
        var reminder1 = CreateTestReminder(entity, CreateTestKey("r1"));
        var reminder2 = CreateTestReminder(entity, CreateTestKey("r2"));

        await Storage!.ScheduleReminderAsync(reminder1, TestContext.Current.CancellationToken);
        await Storage.ScheduleReminderAsync(reminder2, TestContext.Current.CancellationToken);

        var completed1 = new CompletedReminder(reminder1.Entity, reminder1.Key, reminder1.DueTimeUtc, DateTimeOffset.UtcNow);
        var completed2 = new CompletedReminder(reminder2.Entity, reminder2.Key, reminder2.DueTimeUtc, DateTimeOffset.UtcNow);

        // Act
        var result = await Storage.MarkRemindersAsCompletedAsync(new[] { completed1, completed2 }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        var overview = await Storage.GetRemindersOverviewAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        Assert.Equal(0, overview.TotalPendingReminders);
    }

    #endregion

    #region Cleanup Tests

    [Fact]
    public async Task CleanUpCompletedReminders_ShouldRemoveOldCompletedReminders()
    {
        // Arrange
        var reminder = CreateTestReminder();
        await Storage!.ScheduleReminderAsync(reminder, TestContext.Current.CancellationToken);

        var completedTime = DateTimeOffset.UtcNow.AddDays(-10);
        var completed = new CompletedReminder(reminder.Entity, reminder.Key, reminder.DueTimeUtc, completedTime);
        await Storage.MarkRemindersAsCompletedAsync(new[] { completed }, TestContext.Current.CancellationToken);

        // Act
        var result = await Storage.CleanUpCompletedRemindersAsync(DateTimeOffset.UtcNow.AddDays(-5), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CleanUpCompletedReminders_ShouldNotRemoveRecentCompletedReminders()
    {
        // Arrange
        var reminder = CreateTestReminder();
        await Storage!.ScheduleReminderAsync(reminder, TestContext.Current.CancellationToken);

        var recentCompletedTime = DateTimeOffset.UtcNow.AddDays(-3);
        var completed = new CompletedReminder(reminder.Entity, reminder.Key, reminder.DueTimeUtc, recentCompletedTime);
        await Storage.MarkRemindersAsCompletedAsync(new[] { completed }, TestContext.Current.CancellationToken);

        // Act - cleanup reminders older than 5 days
        var result = await Storage.CleanUpCompletedRemindersAsync(DateTimeOffset.UtcNow.AddDays(-5), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    #endregion
}
