namespace Akka.Reminders.Tests.Model;

/// <summary>
/// Fixed sequences found by <see cref="ReminderFaultSpecs"/>, replayed as plain tests with every
/// check on. Nothing here is skipped: a failing test is an open bug or an open question.
/// </summary>
public sealed class ModelRegressionSpecs
{
    private static Task<History> RunStrictAsync(ModelSettings settings, params Op[] ops) => ReminderFaultSpecs.ReplayAsync(settings, ops);

    [Fact(DisplayName = "Should_KeepRecurringSeriesAlive_When_SchedulerLagsPastAnInterval (#143)")]
    public Task Issue143_SeriesSurvivesLag() => RunStrictAsync(ModelSettings.Default,
        new ScheduleRecurring(0, 0, 0, 1000, null),
        new Lag(2000),
        new Tick(3000));

    [Fact(DisplayName = "Should_KeepRecurringSeriesAlive_When_ShardRegionIsMissingPastTheDeadline")]
    public Task SeriesSurvivesMissingRegion() => RunStrictAsync(new ModelSettings(0, 500, 1000, 600000, 3, 1000, 3, 256),
        new ScheduleRecurring(0, 0, 0, 1000, null),
        new SetRegion(0, false),
        new Tick(1000),
        new SetRegion(0, true),
        new Tick(3000));

    [Fact(DisplayName = "Should_KeepSeriesAlive_When_ReRegisteredWithAPastAnchor")]
    public Task SeriesSurvivesReRegistrationWithPastAnchor() => RunStrictAsync(ModelSettings.Default,
        new ScheduleRecurring(0, 0, 0, 5000, null),
        new Tick(12000),
        new ScheduleRecurring(0, 0, -60000, 5000, null),
        new Tick(12000));

    // The retry of an older occurrence re-writes its successor as a fresh Pending row. Here the
    // successor (due +10 s) is delivered early and acked at +9 s; the retry of the first occurrence at
    // +9.3 s resets it, and it is delivered a second time at +10 s.
    [Fact(DisplayName = "Should_NotRedeliverAnAckedOccurrence_When_AnOlderOccurrenceIsRetried")]
    public Task AckedOccurrenceIsNotResetByOlderRetry() => RunStrictAsync(new ModelSettings(1000, 3000, 100, 100, 10, 1000, 100, 256),
        new SetRecipient(0, Recipient.Ignore),
        new ScheduleRecurring(0, 0, 0, 10000, null),
        new Tick(8900),
        new SetRecipient(0, Recipient.Ack),
        new ScheduleOnce(1, 0, 100, null),
        new Tick(1200));

    // Latest-only (#150): with MaxSlippage longer than the interval the scheduler sends several slots in one
    // pass. Each send must expire the previous unacked occurrence, so only the newest stays live.
    [Fact(DisplayName = "Should_ExpireThePreviousUnackedOccurrence_When_TheNextOneIsDeliveredEarly")]
    public Task OnlyTheNewestDeliveredOccurrenceStaysLive() => RunStrictAsync(new ModelSettings(5000, 2000, 1000, 1000, 3, 1, 2, 256),
        new SetRecipient(0, Recipient.Ignore),
        new ScheduleRecurring(0, 0, 0, 1000, null),
        new Tick(3000));

    // Captured from the previous generator before changing its composition. Keep the concrete input:
    // seeds are tied to a generator's shape, but this superseded-ack counterexample must still be checked.
    [Fact(DisplayName = "Should_RejectTheSupersededAck_When_APastAnchorAndItsEarlySuccessorAreDeliveredTogether")]
    public Task PastAnchorDoesNotKeepItsSupersededAcknowledgementAlive() => RunStrictAsync(
        new ModelSettings(1, 500, 60000, 60000, 3, 1, 1, 256),
        new ScheduleRecurring(1, 0, -3599999, 3600000, null));

    // A re-registration leaves the old registration's rows behind. A retried occurrence of the new
    // series must not mistake one of them for its own next occurrence and stop writing one.
    [Fact(DisplayName = "Should_KeepSeriesAlive_When_AnOldRegistrationLeftARowOnTheNextSlot")]
    public Task SeriesSurvivesLeftoverRowOnNextSlot() => RunStrictAsync(new ModelSettings(5000, 500, 1000, 10000, 5, 1, 100, 256),
        new ScheduleRecurring(2, 1, 0, 5000, null),
        new ScheduleRecurring(2, 1, 0, 1000, null),
        new SetRegion(1, false),
        new Tick(10000));

    // The scheduler reads the clock before a fetch that takes 1 ms; the occurrence's 1 ms window
    // closes meanwhile and it is still committed for delivery.
    [Fact(DisplayName = "Should_NotDeliverPastTheDeadline_When_StorageIsSlowDuringAPass")]
    public Task NoDeliveryPastDeadlineAfterSlowFetch() => RunStrictAsync(new ModelSettings(1, 10000, 60000, 60000, 5, 1000, 100, 256),
        new ScheduleRecurring(0, 0, 0, 1000, 1),
        new InjectFault(StorageCall.Fetch, FaultKind.Slow, 1, 1, false),
        new Tick(1000));

    // With the shard region gone, a stale occurrence rolls forward onto a slot that is also retried in
    // the same commit, so the commit upserts that row twice. PostgreSQL and SQL Server reject such a
    // statement: the commit fails and the write circuit opens (the one-row probe then gets past it).
    [Fact(DisplayName = "Should_UpsertEachOccurrenceOnce_When_RollForwardAndRetryMeetInOneCommit")]
    public Task OneCommitNeverUpsertsARowTwice() => RunStrictAsync(new ModelSettings(1000, 5000, 1000, 1000, 10, 3, 3, 256),
        new SetRecipient(0, Recipient.Nack),
        new ScheduleRecurring(0, 0, -5000, 3500, null),
        new SetRegion(0, false),
        new Tick(1000));

    // A retry row processed at the exact instant it is due gives the in-memory overview a
    // "time until next" of zero, which the next upsert treats as "no reminders". The other
    // series (every 1.5 s) is then not fetched until +4 s.
    [Fact(DisplayName = "Should_FetchEveryDueReminder_When_AnotherRowIsDueAtTheExactPassTime")]
    public Task OverviewKeepsOtherRemindersWhenOneIsDueNow() => RunStrictAsync(new ModelSettings(500, 10000, 5000, 10000, 2, 4, 100, 256),
        new ScheduleRecurring(0, 0, 0, 1500, null),
        new SetRegion(0, false),
        new ScheduleRecurring(2, 0, 0, 2000, null),
        new Tick(4000));

    // The second series is registered one interval in the past, so its first slot expires and the next
    // one is due at the exact instant of the pass. A later upsert in the same pass must not hide it.
    [Fact(DisplayName = "Should_FetchAReminderDueExactlyNow_When_ALaterRowIsWrittenInTheSamePass")]
    public Task OverviewKeepsARowDueExactlyNow() => RunStrictAsync(new ModelSettings(5000, 5000, 100, 600000, 2, 3, 1, 256),
        new ScheduleRecurring(0, 0, 0, 1000, null),
        new ScheduleRecurring(0, 1, -1000, 1000, null),
        new Tick(500));

    // The delivery commit takes 2 s. The next fetch timer is "8 s from the time the pass began" but is
    // started 2 s later, so the next occurrence (due +8.001 s) is still waiting at +10 s.
    [Fact(DisplayName = "Should_FetchOnTime_When_APreviousStorageCallWasSlow")]
    public Task FetchTimerIsNotLateAfterSlowCommit() => RunStrictAsync(new ModelSettings(0, 10000, 100, 600000, 1, 3, 100, 256),
        new InjectFault(StorageCall.Commit, FaultKind.Slow, 1, 2000, false),
        new ScheduleRecurring(0, 0, 1, 8000, null),
        new Tick(10000));

    // The delivery commit reaches storage but the call reports a failure (for example a client-side
    // timeout). The row is AwaitingAck, nothing was sent, and no ack-timeout check is scheduled.
    [Fact(DisplayName = "Should_RetryDelivery_When_CommitSucceededButReportedFailure")]
    public Task ReminderIsNotStrandedAfterAmbiguousCommit() => RunStrictAsync(new ModelSettings(1000, 10000, 1000, 1000, 5, 2, 3, 256),
        new InjectFault(StorageCall.Commit, FaultKind.AppliedThenFail, 1, 0, false),
        new ScheduleOnce(0, 0, 0, null),
        new Tick(20000));

    // A scheduling Error means acceptance is unknown to the caller. Here the injected fault tells
    // the test that storage accepted the reminder, so eligible persisted work must recover without
    // another schedule command or a manual restart (docs: Meaning of a scheduling error).
    [Fact(DisplayName = "Should_DeliverStoredReminder_When_SaveSucceededButReportedFailure")]
    public async Task ReminderIsNotStrandedAfterAmbiguousSave()
    {
        var history = await RunStrictAsync(new ModelSettings(1000, 30000, 100, 10000, 3, 4, 1, 256),
            new InjectFault(StorageCall.Schedule, FaultKind.AppliedThenFail, 1, 0, false),
            new ScheduleRecurring(0, 0, 0, 1000, null),
            new Tick(5000));
        Assert.True(history.Deliveries.Count > 0, "the stored reminder was never delivered");
    }

    // A nack stores the retry, then the overview read fails. The retry must still be sent (#151's fix
    // re-arms a check when that reload fails).
    [Fact(DisplayName = "Should_SendTheRetry_When_OverviewReloadFailsAfterANack")]
    public Task RetryIsNotStrandedWhenOverviewReloadFailsAfterNack() => RunStrictAsync(new ModelSettings(500, 500, 1000, 10000, 5, 3, 100, 256),
        new SetRecipient(2, Recipient.Nack),
        new ScheduleRecurring(2, 0, 0, 5000, null),
        new InjectFault(StorageCall.Overview, FaultKind.Fail, 1, 0, false),
        new Tick(3000));

    // Read failures can restart the actor and delay its durable retry. After healing, the retry must
    // arrive within the model's recovery observation window, extended by each subsequent fault.
    // That window is not a production wall-clock SLA (docs: Storage read failure and automatic recovery).
    [Fact(DisplayName = "Should_SendTheRetryOnTime_When_OverviewReadsFailAfterAnAckTimeout")]
    public async Task RetryIsNotLateWhenOverviewReadsFailAfterAckTimeout()
    {
        var settings = new ModelSettings(1000, 10000, 1000, 1000, 10, 1000, 100, 256);
        var history = await RunStrictAsync(settings,
            new SetRecipient(0, Recipient.Ignore),
            new InjectFault(StorageCall.Overview, FaultKind.Timeout, 3, 0, false),
            new ScheduleOnce(0, 0, 0, null),
            new Tick(10000));
        var first = Assert.Single(history.Deliveries.Take(1));
        var retry = history.Deliveries.Skip(1).FirstOrDefault();
        Assert.NotNull(retry);
        var retryDue = first.At + settings.AckTimeout + settings.Backoff(0);
        var recoveredBy = history.TroubleOver(first.Entity, retryDue);
        Assert.True(retry.At <= recoveredBy, $"the retry went out at {Journal.T(retry.At)}; recovery was owed by {Journal.T(recoveredBy)}");
    }

    // The reminder is stored, then reloading the overview fails. The caller gets Error and no fetch
    // timer is set, so the stored reminder is not delivered.
    [Fact(DisplayName = "Should_DeliverStoredReminder_When_OverviewReloadFailsAfterSchedule")]
    public Task ReminderIsNotStrandedWhenOverviewReloadFails() => RunStrictAsync(new ModelSettings(0, 10000, 1000, 10000, 2, 2, 100, 256),
        new InjectFault(StorageCall.Overview, FaultKind.Fail, 1, 0, false),
        new ScheduleRecurring(0, 0, 0, 1000, null),
        new Tick(5000));

    // Checks the oracle, not the scheduler. Sends are committed but report failure while overview reads
    // are slow for 30 s each. The scheduler cannot recover while it waits on a slow read, so the
    // recovery wait must start again when the read returns.
    [Fact(DisplayName = "Should_RestartTheRecoveryWait_When_StorageIsSlowDuringRecovery")]
    public Task SlowStorageDuringRecoveryRestartsTheWait() => RunStrictAsync(new ModelSettings(1000, 10000, 100, 10000, 10, 2, 2, 256),
        new ScheduleOnce(0, 0, 1, null),
        new InjectFault(StorageCall.Overview, FaultKind.Slow, 1, 1, false),
        new InjectFault(StorageCall.Overview, FaultKind.Slow, 3, 30000, false),
        new InjectFault(StorageCall.Commit, FaultKind.AppliedThenFail, 3, 0, false),
        new Tick(15000));
}
