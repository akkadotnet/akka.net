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

    // #8965: with the interval far below MaxSlippage and the region missing, every slot in the window is
    // written back as its own retry row. Older rows back off longer, so the fetch returns the newest first.
    // When the region returns, the pass sends the newest slot and must end every older live one; it used to
    // end only the slot before it and then send an older row too.
    [Fact(DisplayName = "Should_DeliverOnlyTheNewestOccurrence_When_ShardRegionReturnsWithSeveralSlotsPending")]
    public Task OnlyTheNewestOccurrenceIsSentWhenTheRegionReturnsWithSeveralSlotsPending() => RunStrictAsync(
        new ModelSettings(5000, 500, 100, 600000, 10, 1000, 100, 256),
        new ScheduleRecurring(2, 1, 1000, 2000, null),
        new SetRegion(1, false),
        new Tick(1300),
        new SetRegion(1, true),
        new Tick(1000));

    // The two sends of #8965 need not share a commit, so the outcome must not depend on how the pass is
    // cut into fetches (MaxBatch) and commits (Chunk). Chunk 1 and 2 split the sends of one pass.
    [Theory(DisplayName = "Should_DeliverOnlyTheNewestOccurrence_When_ThePassIsCutIntoSmallBatchesAndChunks")]
    [InlineData(1000, 1)]
    [InlineData(1000, 2)]
    [InlineData(1, 100)]
    [InlineData(2, 100)]
    [InlineData(3, 100)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public Task OnlyTheNewestOccurrenceIsSentForEveryBatchAndChunkSize(int maxBatch, int chunk) => RunStrictAsync(
        new ModelSettings(5000, 500, 100, 600000, 10, maxBatch, chunk, 256),
        new ScheduleRecurring(2, 1, 1000, 2000, null),
        new SetRegion(1, false),
        new Tick(1300),
        new SetRegion(1, true),
        new Tick(1000));

    // The interval is a tenth of MaxSlippage, so ten slots wait as retry rows at once and one send must end
    // all of the older live ones, not only the slot before it. The short window moves each slot's deadline
    // to 100 ms after its due time.
    [Theory(DisplayName = "Should_DeliverOnlyTheNewestOccurrence_When_TheIntervalIsFarBelowMaxSlippage")]
    [InlineData(1000, 100, null)]
    [InlineData(1000, 1, null)]
    [InlineData(1, 100, null)]
    [InlineData(1000, 100, 100)]
    [InlineData(1000, 1, 100)]
    public Task OnlyTheNewestOccurrenceIsSentWithTenSlotsPending(int maxBatch, int chunk, int? window) => RunStrictAsync(
        new ModelSettings(5000, 500, 100, 600000, 10, maxBatch, chunk, 256),
        new ScheduleRecurring(2, 1, 1000, 500, window),
        new SetRegion(1, false),
        new Tick(1300),
        new SetRegion(1, true),
        new Tick(1000));

    // The commit that sends the newest slot fails, lands and reports failure, or times out. Nothing older may
    // go out before it or after it, and the series must keep running. A commit that did not land leaves every
    // row as it was; one that landed leaves the newest awaiting its ack and the older ones expired.
    [Theory(DisplayName = "Should_NeverSendAnOlderOccurrence_When_TheCommitThatSendsTheNewestOneFails")]
    [InlineData(FaultKind.Fail, 1000, 100)]
    [InlineData(FaultKind.Fail, 1000, 1)]
    [InlineData(FaultKind.AppliedThenFail, 1000, 100)]
    [InlineData(FaultKind.AppliedThenFail, 1000, 1)]
    [InlineData(FaultKind.AppliedThenFail, 1, 100)]
    [InlineData(FaultKind.Timeout, 1000, 100)]
    [InlineData(FaultKind.Timeout, 1000, 1)]
    public Task NoOlderOccurrenceIsSentWhenTheNewestCommitFails(FaultKind kind, int maxBatch, int chunk) => RunStrictAsync(
        new ModelSettings(5000, 500, 100, 600000, 10, maxBatch, chunk, 256),
        new ScheduleRecurring(2, 1, 1000, 2000, null),
        new SetRegion(1, false),
        new Tick(1300),
        new SetRegion(1, true),
        new InjectFault(StorageCall.Commit, kind, 1, 0, false),
        new Tick(10000));

    // Two commits in a row fail, so the write circuit stays open while the pass probes with one row.
    [Fact(DisplayName = "Should_NeverSendAnOlderOccurrence_When_SeveralCommitsFailBeforeTheNewestOneIsSent")]
    public Task NoOlderOccurrenceIsSentWhileTheWriteCircuitIsOpen() => RunStrictAsync(
        new ModelSettings(5000, 500, 100, 600000, 10, 1000, 100, 256),
        new ScheduleRecurring(2, 1, 1000, 2000, null),
        new SetRegion(1, false),
        new Tick(1300),
        new SetRegion(1, true),
        new InjectFault(StorageCall.Commit, FaultKind.Fail, 2, 0, false),
        new InjectFault(StorageCall.Commit, FaultKind.AppliedThenFail, 1, 0, false),
        new Tick(10000));

    // The recipient answers the older occurrences before the pass that sends the newest one. An acked
    // older slot is terminal and stays acked; a nacked one is a Pending retry that the send must end.
    [Theory(DisplayName = "Should_DeliverOnlyTheNewestOccurrence_When_OlderOccurrencesWereAnsweredBeforeTheRegionReturned")]
    [InlineData(Recipient.Ack)]
    [InlineData(Recipient.Nack)]
    public Task OnlyTheNewestOccurrenceIsSentAfterOlderOnesWereAnswered(Recipient answer) => RunStrictAsync(
        new ModelSettings(5000, 30000, 100, 600000, 10, 1000, 1, 256),
        new SetRecipient(2, answer),
        new ScheduleRecurring(2, 1, 1000, 2000, null),
        new Tick(1050),
        new SetRegion(1, false),
        new Tick(5000),
        new SetRegion(1, true),
        new Tick(2000));

    // The recipient stays silent. Its late answers for older occurrences arrive before the region returns
    // (the ack may still work) and again after the pass that sent the newest one (it must be NotFound).
    [Fact(DisplayName = "Should_RejectALateAck_When_ANewerOccurrenceWasSentAfterTheRegionReturned")]
    public Task LateAcksAreRejectedAfterTheNewestOccurrenceIsSent() => RunStrictAsync(
        new ModelSettings(5000, 30000, 100, 600000, 10, 1000, 1, 256),
        new SetRecipient(2, Recipient.Ignore),
        new ScheduleRecurring(2, 1, 1000, 2000, null),
        new Tick(1050),
        new AckOutstanding(2),
        new SetRegion(1, false),
        new Tick(1300),
        new SetRegion(1, true),
        new Tick(1000),
        new AckOutstanding(2),
        new Tick(3000));

    // The region goes away and comes back several times while slots pile up and are sent.
    [Theory(DisplayName = "Should_DeliverOnlyTheNewestOccurrence_When_TheRegionFlapsWhileSlotsArePending")]
    [InlineData(100)]
    [InlineData(1)]
    public Task OnlyTheNewestOccurrenceIsSentWhileTheRegionFlaps(int chunk) => RunStrictAsync(
        new ModelSettings(5000, 500, 100, 600000, 10, 1000, chunk, 256),
        new ScheduleRecurring(2, 1, 1000, 2000, null),
        new SetRegion(1, false),
        new Tick(1300),
        new SetRegion(1, true),
        new Tick(10),
        new SetRegion(1, false),
        new Tick(400),
        new SetRegion(1, true),
        new Tick(1000),
        new SetRegion(1, false),
        new Tick(2500),
        new SetRegion(1, true),
        new Tick(3000));

    // One attempt leaves a missing region no retry: each slot ends Failed on its own. Two attempts leave
    // one. A backoff at or above the interval pushes a slot's retry past its own deadline.
    [Theory(DisplayName = "Should_DeliverOnlyTheNewestOccurrence_When_AttemptsAndBackoffVary")]
    [InlineData(1, 100)]
    [InlineData(2, 100)]
    [InlineData(2, 1000)]
    [InlineData(3, 2000)]
    [InlineData(10, 2000)]
    [InlineData(10, 5000)]
    public Task OnlyTheNewestOccurrenceIsSentForEveryAttemptAndBackoff(int attempts, int backoff) => RunStrictAsync(
        new ModelSettings(5000, 500, backoff, 600000, attempts, 1000, 1, 256),
        new ScheduleRecurring(2, 1, 1000, 2000, null),
        new SetRegion(1, false),
        new Tick(1300),
        new SetRegion(1, true),
        new Tick(1000));

    // Controls from #8965. Neither fails on the old scheduler: MaxSlippage is shorter than the interval, so
    // only one slot is ever pending; or the region never goes down, so each slot is sent on its own.
    [Fact(DisplayName = "Should_DeliverEverySlot_When_MaxSlippageIsShorterThanTheInterval")]
    public Task ControlSlippageShorterThanTheInterval() => RunStrictAsync(
        new ModelSettings(1000, 500, 100, 600000, 10, 1000, 100, 256),
        new ScheduleRecurring(2, 1, 1000, 2000, null),
        new SetRegion(1, false),
        new Tick(1300),
        new SetRegion(1, true),
        new Tick(1000));

    [Fact(DisplayName = "Should_DeliverEverySlot_When_TheRegionNeverGoesDown")]
    public Task ControlRegionNeverDown() => RunStrictAsync(
        new ModelSettings(5000, 500, 100, 600000, 10, 1000, 100, 256),
        new ScheduleRecurring(2, 1, 1000, 2000, null),
        new Tick(1300),
        new Tick(1000),
        new Tick(8000));

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
