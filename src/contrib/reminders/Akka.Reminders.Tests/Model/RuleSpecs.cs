using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Model;

/// <summary>
/// Checks the checkers: each safety rule gets a short hand-written history that breaks it and must
/// report it, and a clean history must break none. The model gets a few worked examples.
/// </summary>
public sealed class RuleSpecs
{
    private static readonly ModelSettings Settings = ModelSettings.Default; // slippage 1 s, ack timeout 10 s, 3 attempts
    private static DateTimeOffset T(double seconds) => VirtualClock.Origin.AddSeconds(seconds);

    private static History HistoryOf(params (double At, Event E)[] events)
    {
        var seq = 0;
        foreach (var (at, e) in events)
            (e.Seq, e.At) = (++seq, T(at));
        return new History(events.Select(x => x.E).ToList());
    }

    private static Scheduled Recurring(int id = 1) => new(id, 0, 0, T(0), TimeSpan.FromSeconds(5), null, ReminderScheduleResponseCode.Success);
    private static Scheduled OneOff(TimeSpan? window = null) => new(1, 0, 0, T(0), null, window, ReminderScheduleResponseCode.Success);
    private static Delivered Sent(double due, int id = 1, double? envelopeDeadline = null) => new(id, 0, 0, T(due), T(envelopeDeadline ?? due + 1));
    private static AckAnswered Ack(double due, long asked) => new(asked, 1, 0, 0, T(due), ReminderAckResponseCode.Success);

    private static Trouble AckFault(double until, FaultKind kind, int entity = 0, int key = 0, double due = 0, double? ackedAt = null) =>
        new(T(until), null, StorageCall.Ack, kind)
        {
            AckTargets = [new ReminderAcknowledgement(ReminderApp.EntityOf(entity), ReminderApp.KeyOf(key), T(due), T(ackedAt ?? until - 30))]
        };

    public static TheoryData<string, History> BrokenHistories() => new()
    {
        { "OnlyWhatWasScheduled", HistoryOf((0, Recurring()), (7, Sent(7))) },
        { "NotEarly", HistoryOf((0, Recurring()), (3, Sent(5))) },
        { "NotAfterDeadline", HistoryOf((0, Recurring()), (5, Sent(0))) },
        { "HonestEnvelopeDeadline", HistoryOf((0, Recurring()), (0, Sent(0, envelopeDeadline: 6))) },
        { "AttemptCap", HistoryOf((0, OneOff()), (0, Sent(0)), (11, Sent(0)), (22, Sent(0)), (33, Sent(0))) },
        { "AckedNeverRedelivered", HistoryOf((0, OneOff()), (0, Sent(0)), (1, Ack(0, asked: 2)), (11, Sent(0))) },
        { "NothingAfterCancel", HistoryOf((0, Recurring()), (1, new CancelAnswered(0, 0, ReminderCancelResponseCode.Success)), (5, Sent(5))) },
        { "NothingAfterCancel", HistoryOf((0, Recurring(1)), (1, Recurring(2)), (5, Sent(5, id: 1))) },
        { "LatestOnly", HistoryOf((0, Recurring()), (4, Sent(5)), (4.5, Sent(0))) },
        { "LateAckIsNotFound", HistoryOf((0, Recurring()), (0, Sent(0)), (4, Sent(5)), (4, Ack(0, asked: 3))) },
        { "NoRepeatWithoutCause", HistoryOf((0, OneOff()), (0, Sent(0)), (2, Sent(0))) },
        { "NoRepeatWithoutCause", HistoryOf((0, OneOff()), (0, Sent(0)),
            (0, new NackAnswered(2, 1, 0, 0, T(0), ReminderNackResponseCode.RetryScheduled, T(4))), (2, Sent(0))) },
        { "ListShowsNothingCancelled", HistoryOf((0, OneOff()), (1, new CancelAnswered(0, null, ReminderCancelResponseCode.Success)),
            (2, new Listed(0, FetchRemindersResponseCode.Success, [(0, 1, T(0))]))) },
    };

    [Theory(DisplayName = "Should_ReportTheRule_When_AHistoryBreaksIt")]
    [MemberData(nameof(BrokenHistories))]
    public void RuleReportsItsViolation(string rule, History history) =>
        Assert.NotEmpty(SafetyRules.All.Single(r => r.Name == rule).Broken(history, Settings));

    [Fact(DisplayName = "Should_ReportNothing_When_TheHistoryIsClean")]
    public void CleanHistoryBreaksNoRule()
    {
        var nack = new NackAnswered(5, 1, 0, 0, T(5), ReminderNackResponseCode.RetryScheduled, T(6));
        var history = HistoryOf((0, Recurring()), (0, Sent(0)), (0, Ack(0, asked: 2)), (4, Sent(5)), (4, nack), (6, Sent(5)),
            (7, new Listed(0, FetchRemindersResponseCode.Success, [(0, 1, T(10))])));
        Assert.All(SafetyRules.All, rule => Assert.Empty(rule.Broken(history, Settings)));
    }

    [Fact]
    public void HealthyDeliveryCheckRejectsMissingReplacementPayload()
    {
        var history = HistoryOf(
            (0, new Scheduled(1, 0, 0, T(1), null, null, ReminderScheduleResponseCode.Success)),
            (0, Sent(1, id: 1)),
            (0, new Scheduled(2, 0, 0, T(1), null, null, ReminderScheduleResponseCode.Success)),
            (1, new Done()));
        var failure = Assert.Throws<ModelViolation>(() => ReminderFaultSpecs.RequireHealthyDeliveries(history));
        Assert.Contains("reminder 2", failure.Message);
    }

    [Fact]
    public void HealthyDeliveryCheckAcceptsDeliveredAndCancelledOccurrences()
    {
        var history = HistoryOf(
            (0, new Scheduled(1, 0, 0, T(1), TimeSpan.FromSeconds(5), null, ReminderScheduleResponseCode.Success)),
            (1, Sent(1)),
            (2, new CancelAnswered(0, 0, ReminderCancelResponseCode.Success)),
            (10, new Done()));
        ReminderFaultSpecs.RequireHealthyDeliveries(history);
    }

    [Fact(DisplayName = "Should_SkipMissedSlots_When_TheSchedulerWakesAfterALag")]
    public void ModelSkipsMissedSlots()
    {
        var model = new ReminderModel(Settings);
        var r = new Reminder(1, 0, 0, T(0), TimeSpan.FromSeconds(5), null);
        model.Schedule(r);
        model.WakeAt(T(12)); // slots at 5 s and 10 s came due during the stall; 5 s is past its deadline
        model.RunTo(T(16));
        Assert.Equal([T(0), T(10), T(15)], r.Slots.Where(s => s.Value.RequiredAt is not null).Select(s => s.Key));
        Assert.Equal(T(12), r.Slots[T(10)].RequiredAt);
    }

    [Fact(DisplayName = "Should_FollowAnOccurrenceThroughItsPhases_When_NoAckArrives")]
    public void ModelFollowsRetries()
    {
        var model = new ReminderModel(Settings); // ack timeout 10 s, backoff 1 s then 2 s, 3 attempts
        var r = new Reminder(1, 0, 0, T(0), null, null);
        model.Schedule(r);
        model.Delivered(1, T(0), T(0));
        Assert.Equal(Phase.AwaitingAck, model.PhaseOf(r, T(0)));
        model.RunTo(T(10.5));
        Assert.Equal(Phase.RetryPending, model.PhaseOf(r, T(0))); // retry is due at 11 s
        model.RunTo(T(11));
        Assert.Equal(Phase.RetryOverdue, model.PhaseOf(r, T(0)));
        model.Delivered(1, T(0), T(11));
        Assert.True(model.Ack(0, 0, T(0)));
        Assert.Equal(Phase.Done, model.PhaseOf(r, T(0)));
        Assert.False(model.Ack(0, 0, T(0)));
    }

    [Theory(DisplayName = "Should_CheckRetryRecovery_AfterTheHealthyObservationWindow")]
    [InlineData(StorageCall.Overview)]
    [InlineData(StorageCall.Status)]
    public void RetryLivenessReturnsAfterReadTrouble(StorageCall call)
    {
        var model = new ReminderModel(Settings);
        model.Schedule(new Reminder(1, 0, 0, T(0), null, null));
        model.Delivered(1, T(0), T(0));
        var history = HistoryOf((0, OneOff()), (0, Sent(0)),
            (1, new Trouble(T(30), null, call, FaultKind.Fail)));
        var rule = Liveness.All.Single(r => r.Name == "RetriedOnTime");

        model.RunTo(T(29));
        Assert.Empty(rule.Broken(model, history, Settings));
        model.RunTo(T(30));
        Assert.Single(rule.Broken(model, history, Settings)); // healing must not permanently excuse a missing retry

        model.Delivered(1, T(0), T(30));
        Assert.Empty(rule.Broken(model, history, Settings));
        model.RunTo(T(42)); // second retry: ack timeout 10 s, backoff 2 s
        Assert.Single(rule.Broken(model, history, Settings));
    }

    [Fact(DisplayName = "Should_ExtendTheRetryRecoveryWindow_When_AnotherReadFails")]
    public void RetryRecoveryWaitsForTheLastOverlappingFailure()
    {
        var model = new ReminderModel(Settings);
        model.Schedule(new Reminder(1, 0, 0, T(0), null, null));
        model.Delivered(1, T(0), T(0));
        var history = HistoryOf((0, OneOff()), (0, Sent(0)),
            (1, new Trouble(T(30), null, StorageCall.Overview, FaultKind.Fail)),
            (25, new Trouble(T(55), null, StorageCall.NextAckDeadline, FaultKind.Timeout)));
        var rule = Liveness.All.Single(r => r.Name == "RetriedOnTime");

        model.RunTo(T(54));
        Assert.Empty(rule.Broken(model, history, Settings));
        model.RunTo(T(55));
        Assert.Single(rule.Broken(model, history, Settings));
    }

    [Fact(DisplayName = "Should_NotOweARetry_When_AmbiguousCommitsExhaustedItsAttempts")]
    public void RetryRecoveryPreservesAmbiguousAttemptCosts()
    {
        var model = new ReminderModel(Settings);
        model.Schedule(new Reminder(1, 0, 0, T(0), null, null));
        model.Delivered(1, T(0), T(0));
        var history = HistoryOf((0, OneOff()), (0, Sent(0)),
            (11, new Trouble(T(41), null, StorageCall.Commit, FaultKind.AppliedThenFail)),
            (22, new Trouble(T(52), null, StorageCall.Commit, FaultKind.AppliedThenFail)));
        model.RunTo(T(60));
        Assert.Empty(Liveness.All.Single(r => r.Name == "RetriedOnTime").Broken(model, history, Settings));
    }

    [Fact(DisplayName = "Should_CountAmbiguousAttempts_BeforeTheFirstObservedDelivery")]
    public void RetryRecoveryCountsUnsentCommittedAttempts()
    {
        var model = new ReminderModel(Settings);
        model.Schedule(new Reminder(1, 0, 0, T(0), null, null));
        model.Delivered(1, T(0), T(22)); // two earlier sends committed without reaching the application
        var history = HistoryOf((0, OneOff()),
            (0, new Trouble(T(1), null, StorageCall.Commit, FaultKind.AppliedThenFail)),
            (11, new Trouble(T(12), null, StorageCall.Commit, FaultKind.AppliedThenFail)),
            (22, Sent(0)));
        model.RunTo(T(60));
        Assert.Empty(Liveness.All.Single(r => r.Name == "RetriedOnTime").Broken(model, history, Settings));
    }

    [Fact(DisplayName = "Should_NotChargeANewOccurrence_ForAmbiguousCommitsBeforeItCouldExist")]
    public void RetryRecoveryDoesNotBorrowOlderAttemptCosts()
    {
        var model = new ReminderModel(Settings);
        model.RunTo(T(60));
        model.Schedule(new Reminder(1, 0, 0, T(60), null, null));
        model.Delivered(1, T(60), T(60));
        var history = HistoryOf(
            (0, new Trouble(T(120), null, StorageCall.Commit, FaultKind.AppliedThenFail)),
            (1, new Trouble(T(120), null, StorageCall.Commit, FaultKind.AppliedThenFail)),
            (60, new Scheduled(1, 0, 0, T(60), null, null, ReminderScheduleResponseCode.Success)),
            (60, Sent(60)));
        model.RunTo(T(121));
        Assert.Single(Liveness.All.Single(r => r.Name == "RetriedOnTime").Broken(model, history, Settings));
    }

    [Theory(DisplayName = "Should_RespectTheDurableAckOutcome_When_ItsResponseWasLost")]
    [InlineData(FaultKind.AppliedThenFail, false)]
    [InlineData(FaultKind.Fail, true)]
    public void ModelDistinguishesLandedAndFailedAcknowledgements(FaultKind kind, bool retryOwed)
    {
        var app = new ReminderApp(Settings);
        app.Journal.Add(OneOff());
        app.Journal.Add(Sent(0));
        app.Journal.Add(AckFault(30, kind));
        app.Journal.Add(new AckAnswered(2, 1, 0, 0, T(0), ReminderAckResponseCode.Error));
        app.Journal.Add(new Done()).At = T(31);
        var model = new ReminderModel(app.Settings);
        if (retryOwed)
            Assert.Contains("RetriedOnTime", Assert.Throws<ModelViolation>(() => model.Observe(app.Journal.Read())).Message);
        else
            model.Observe(app.Journal.Read());
    }

    [Theory(DisplayName = "Should_RejectRedeliveryOnly_When_TheAcknowledgementPersistedBeforeItsReplyWasLost")]
    [InlineData(FaultKind.AppliedThenFail, true)]
    [InlineData(FaultKind.Fail, false)]
    public void ModelChecksRedeliveryAfterAnAcknowledgementError(FaultKind kind, bool redeliveryForbidden)
    {
        var app = new ReminderApp(Settings);
        app.Journal.Add(OneOff());
        app.Journal.Add(Sent(0));
        app.Journal.Add(AckFault(30, kind));
        app.Journal.Add(new AckAnswered(2, 1, 0, 0, T(0), ReminderAckResponseCode.Error));
        app.Journal.Add(new Done());
        var model = new ReminderModel(app.Settings);
        model.Observe(app.Journal.Read());

        app.Journal.Add(Sent(0)).At = T(31);
        app.Journal.Add(new Done()).At = T(31);
        if (redeliveryForbidden)
            Assert.Contains("AckedNeverRedelivered", Assert.Throws<ModelViolation>(() => model.Observe(app.Journal.Read())).Message);
        else
            model.Observe(app.Journal.Read());
    }

    [Fact(DisplayName = "Should_AllowAnExplicitNewRegistration_AfterAnAcknowledgementPersistsWithALostReply")]
    public void LandedAcknowledgementDoesNotBlockAnExplicitNewRegistration()
    {
        var app = new ReminderApp(Settings);
        app.Journal.Add(OneOff());
        app.Journal.Add(Sent(0));
        app.Journal.Add(AckFault(30, FaultKind.AppliedThenFail));
        app.Journal.Add(new AckAnswered(2, 1, 0, 0, T(0), ReminderAckResponseCode.Error));
        app.Journal.Add(new Done());
        var model = new ReminderModel(app.Settings);
        model.Observe(app.Journal.Read());

        app.Journal.Add(new Scheduled(2, 0, 0, T(0), null, null, ReminderScheduleResponseCode.Success)).At = T(31);
        app.Journal.Add(Sent(0, id: 2)).At = T(31);
        app.Journal.Add(new Done()).At = T(31);
        model.Observe(app.Journal.Read());
    }

    [Fact(DisplayName = "Should_NotAttributeAnEarlierLandedAcknowledgement_ToALaterFailedOperation")]
    public void AckSafetyMatchesTheFaultToItsOwnOperation()
    {
        var history = HistoryOf((0, OneOff()), (0, Sent(0)),
            (1, AckFault(30, FaultKind.AppliedThenFail)),
            (2, new Done()),
            (3, new AckAnswered(4, 1, 0, 0, T(0), ReminderAckResponseCode.Error)),
            (31, Sent(0)));
        Assert.Empty(SafetyRules.All.Single(r => r.Name == "AckedNeverRedelivered").Broken(history, Settings));
    }

    [Fact(DisplayName = "Should_AllowThePendingRetry_When_ALateAcknowledgementReachedStorageButWasNotAccepted")]
    public void LateAcknowledgementWithLostNotFoundReplyDoesNotForbidRetry()
    {
        var app = new ReminderApp(Settings);
        app.Journal.Add(OneOff());
        app.Journal.Add(Sent(0));
        app.Journal.Add(new Done());
        var model = new ReminderModel(app.Settings);
        model.Observe(app.Journal.Read());

        // Timeout at 10 s made the row Pending until 11 s. Applying this ack call returns
        // NotFound; its lost response is not evidence of a durable acknowledgement.
        app.Journal.Add(AckFault(40.5, FaultKind.AppliedThenFail)).At = T(10.5);
        app.Journal.Add(new AckAnswered(3, 1, 0, 0, T(0), ReminderAckResponseCode.Error)).At = T(10.5);
        app.Journal.Add(Sent(0)).At = T(11);
        app.Journal.Add(new Done()).At = T(11);
        model.Observe(app.Journal.Read());
    }

    public static TheoryData<History> UncertainAckHistories() => new()
    {
        { HistoryOf((0, OneOff()), (0, Sent(0)), (10, AckFault(40, FaultKind.AppliedThenFail)),
            (10, new AckAnswered(2, 1, 0, 0, T(0), ReminderAckResponseCode.Error)), (11, Sent(0))) },
        { HistoryOf((0, OneOff()), (0, new Trouble(T(8), null, StorageCall.Commit, FaultKind.Slow)),
            (8, Sent(0)), (12, AckFault(42, FaultKind.AppliedThenFail)),
            (12, new AckAnswered(3, 1, 0, 0, T(0), ReminderAckResponseCode.Error)), (20, Sent(0))) },
        { HistoryOf((0, OneOff()), (0, Sent(0)),
            (1, new NackAnswered(2, 1, 0, 0, T(0), ReminderNackResponseCode.RetryScheduled, T(5))),
            (2, AckFault(32, FaultKind.AppliedThenFail)),
            (2, new AckAnswered(3, 1, 0, 0, T(0), ReminderAckResponseCode.Error)), (5, Sent(0))) },
        { HistoryOf((0, OneOff()), (0, Sent(0)),
            (1, new NackAnswered(2, 1, 0, 0, T(0), ReminderNackResponseCode.Error, null)),
            (2, AckFault(32, FaultKind.AppliedThenFail)),
            (2, new AckAnswered(3, 1, 0, 0, T(0), ReminderAckResponseCode.Error)), (11, Sent(0))) },
        { HistoryOf((0, OneOff()), (0, Sent(0)),
            (1, new Scheduled(2, 0, 0, T(0), null, null, ReminderScheduleResponseCode.Error)),
            (2, AckFault(32, FaultKind.AppliedThenFail)),
            (2, new AckAnswered(3, 1, 0, 0, T(0), ReminderAckResponseCode.Error)), (11, Sent(0))) },
        { HistoryOf((0, OneOff()), (0, Sent(0)), (1, new CancelAnswered(0, 0, ReminderCancelResponseCode.Error)),
            (2, AckFault(32, FaultKind.AppliedThenFail)),
            (2, new AckAnswered(3, 1, 0, 0, T(0), ReminderAckResponseCode.Error)), (11, Sent(0))) },
        { HistoryOf((0, OneOff()), (0, Sent(0)), (1, new Trouble(T(31), null, StorageCall.Overview, FaultKind.Fail)),
            (2, AckFault(32, FaultKind.AppliedThenFail)),
            (2, new AckAnswered(3, 1, 0, 0, T(0), ReminderAckResponseCode.Error)), (11, Sent(0))) },
        { HistoryOf((0, Recurring()), (0, Sent(0)), (4, Sent(5)), (4.5, AckFault(34.5, FaultKind.AppliedThenFail)),
            (4.5, new AckAnswered(3, 1, 0, 0, T(0), ReminderAckResponseCode.Error)), (11, Sent(0))) },
        { HistoryOf((0, OneOff(TimeSpan.FromSeconds(5))), (0, Sent(0)), (5, AckFault(35, FaultKind.AppliedThenFail)),
            (5, new AckAnswered(2, 1, 0, 0, T(0), ReminderAckResponseCode.Error)), (11, Sent(0))) },
    };

    [Theory(DisplayName = "Should_NotInferAckAcceptance_When_IndependentObservationsNoLongerEstablishEligibility")]
    [MemberData(nameof(UncertainAckHistories))]
    public void AckSafetyDoesNotInferAcceptanceFromReachingStorageAlone(History history) =>
        Assert.Empty(SafetyRules.All.Single(r => r.Name == "AckedNeverRedelivered").Broken(history, Settings));

    [Fact(DisplayName = "Should_AttributeOverlappingAckErrors_ToTheirOwnInjectedBatchTargets")]
    public void ConcurrentAcknowledgementsDoNotBorrowAnotherBatchsLandedWrite()
    {
        var app = new ReminderApp(Settings);
        app.Journal.Add(OneOff());
        app.Journal.Add(Sent(0));
        app.Journal.Add(new Scheduled(2, 0, 1, T(0), null, null, ReminderScheduleResponseCode.Success));
        app.Journal.Add(new Delivered(2, 0, 1, T(0), DateTimeOffset.MaxValue));
        app.Journal.Add(new Done());
        var model = new ReminderModel(app.Settings);
        model.Observe(app.Journal.Read());

        // Both asks begin at the same position, then flush in separate batches. Only key 0 lands.
        app.Journal.Add(AckFault(31, FaultKind.AppliedThenFail, key: 0)).At = T(1);
        app.Journal.Add(AckFault(31, FaultKind.Fail, key: 1)).At = T(1);
        app.Journal.Add(new AckAnswered(5, 1, 0, 0, T(0), ReminderAckResponseCode.Error)).At = T(1);
        app.Journal.Add(new AckAnswered(5, 2, 0, 1, T(0), ReminderAckResponseCode.Error)).At = T(1);
        app.Journal.Add(new Delivered(2, 0, 1, T(0), DateTimeOffset.MaxValue)).At = T(31);
        app.Journal.Add(new Done()).At = T(31);
        model.Observe(app.Journal.Read());

        // Exact attribution retains the safety obligation for the batch that did land.
        app.Journal.Add(Sent(0)).At = T(32);
        app.Journal.Add(new Done()).At = T(32);
        Assert.Contains("AckedNeverRedelivered", Assert.Throws<ModelViolation>(() => model.Observe(app.Journal.Read())).Message);
    }

    [Fact(DisplayName = "Should_ResolveOccurrenceAckAmbiguity_When_AnotherDeliveryIsObserved")]
    public void UncertainAckAfterNackErrorRestoresRetryChecksAfterAnotherDelivery()
    {
        var app = new ReminderApp(Settings with { MaxAttempts = 10 });
        app.Journal.Add(OneOff());
        app.Journal.Add(Sent(0));
        app.Journal.Add(new Done());
        app.Journal.Add(new Trouble(T(31), null, StorageCall.Commit, FaultKind.AppliedThenFail)).At = T(1);
        app.Journal.Add(new NackAnswered(3, 1, 0, 0, T(0), ReminderNackResponseCode.Error, null)).At = T(1);
        app.Journal.Add(new Done()).At = T(1);
        app.Journal.Add(AckFault(32, FaultKind.AppliedThenFail)).At = T(2);
        app.Journal.Add(new AckAnswered(6, 1, 0, 0, T(0), ReminderAckResponseCode.Error)).At = T(2);
        app.Journal.Add(new Done()).At = T(2);
        var model = new ReminderModel(app.Settings);
        model.Observe(app.Journal.Read());

        app.Journal.Add(new Done()).At = T(33);
        model.Observe(app.Journal.Read()); // prior trouble prevents claiming whether the targeted ack was accepted
        app.Journal.Add(Sent(0)).At = T(33);
        app.Journal.Add(new Done()).At = T(33);
        model.Observe(app.Journal.Read()); // the observed delivery resolves that occurrence's acceptance ambiguity
        app.Journal.Add(new Done()).At = T(48); // two observed + one possible unsent attempt: backoff is 4 s
        Assert.Contains("RetriedOnTime", Assert.Throws<ModelViolation>(() => model.Observe(app.Journal.Read())).Message);
    }

    [Fact(DisplayName = "Should_AllowSilence_When_AcknowledgementAcceptanceIsAmbiguousAfterUnrelatedTrouble")]
    public void UnrelatedReadTroubleDoesNotInventARetryAfterALandedAcknowledgement()
    {
        var app = new ReminderApp(Settings);
        app.Journal.Add(OneOff());
        app.Journal.Add(Sent(0));
        app.Journal.Add(new Done());
        app.Journal.Add(new Trouble(T(31), null, StorageCall.Overview, FaultKind.Fail)).At = T(1);
        app.Journal.Add(AckFault(32, FaultKind.AppliedThenFail)).At = T(2);
        app.Journal.Add(new AckAnswered(4, 1, 0, 0, T(0), ReminderAckResponseCode.Error)).At = T(2);
        app.Journal.Add(new Done()).At = T(40);
        new ReminderModel(app.Settings).Observe(app.Journal.Read());
    }

    [Theory(DisplayName = "Should_KeepLateAckAcceptanceAmbiguousOnly_When_TheInputWasBufferedBeforeTheFlush")]
    [InlineData(false)]
    [InlineData(true)]
    public void LateAckRetryObligationRespectsBufferedInputTime(bool buffered)
    {
        var app = new ReminderApp(Settings);
        app.Journal.Add(OneOff());
        app.Journal.Add(Sent(0));
        app.Journal.Add(new Done());
        app.Journal.Add(AckFault(41, FaultKind.AppliedThenFail, ackedAt: buffered ? 2 : 11)).At = T(11);
        app.Journal.Add(new AckAnswered(3, 1, 0, 0, T(0), ReminderAckResponseCode.Error)).At = T(11);
        app.Journal.Add(new Done()).At = T(42);
        var model = new ReminderModel(app.Settings);
        if (buffered)
            model.Observe(app.Journal.Read()); // the pre-timeout buffered ack may have landed when the timeout pass flushed it
        else
            Assert.Contains("RetriedOnTime", Assert.Throws<ModelViolation>(() => model.Observe(app.Journal.Read())).Message);
    }

    [Theory(DisplayName = "Should_AccountForUnobservedAttempts_WithoutLooseningHealthyBackoff")]
    [InlineData(0, 102)]
    [InlineData(2, 108)]
    public void RetryBackoffIncludesPossibleUnsentAttempts(int hiddenAttempts, int owedAt)
    {
        var settings = Settings with { AckTimeoutMs = 30000, BackoffBaseMs = 1000, MaxBackoffMs = 10000, MaxAttempts = 10 };
        var model = new ReminderModel(settings);
        model.Schedule(new Reminder(1, 0, 0, T(0), null, null));
        model.Delivered(1, T(0), T(0));
        model.Delivered(1, T(0), T(70));
        var events = new List<(double At, Event E)> { (0, OneOff()), (0, Sent(0)) };
        for (var i = 0; i < hiddenAttempts; i++)
            events.Add((35 + i, new Trouble(T(40), null, StorageCall.Commit, FaultKind.AppliedThenFail)));
        events.Add((70, Sent(0)));
        var history = HistoryOf(events.ToArray());
        var rule = Liveness.All.Single(r => r.Name == "RetriedOnTime");

        model.RunTo(T(owedAt - 1));
        Assert.Empty(rule.Broken(model, history, settings));
        model.RunTo(T(owedAt));
        Assert.Single(rule.Broken(model, history, settings));
    }

    [Fact(DisplayName = "Should_StartTimeoutBackoff_When_TheSchedulerCanNoticeTheExpiredAck")]
    public void SlowStorageDelaysTimeoutDetectionBeforeBackoff()
    {
        var model = new ReminderModel(Settings);
        model.Schedule(new Reminder(1, 0, 0, T(0), null, null));
        model.Delivered(1, T(0), T(0));
        var history = HistoryOf((0, OneOff()), (0, Sent(0)),
            (0, new Trouble(T(20), null, StorageCall.Cancel, FaultKind.Slow)));
        var rule = Liveness.All.Single(r => r.Name == "RetriedOnTime");

        model.RunTo(T(20)); // the ack expired at 10 s, but its timeout pass could not run
        Assert.Empty(rule.Broken(model, history, Settings));
        model.RunTo(T(21)); // noticed at 20 s, then 1 s backoff
        Assert.Single(rule.Broken(model, history, Settings));
    }

    [Fact(DisplayName = "Should_NotRestartPromisedNackBackoff_AfterASlowStorageCall")]
    public void SlowStorageDoesNotAddBackoffToAnAlreadyPendingRetry()
    {
        var model = new ReminderModel(Settings);
        model.Schedule(new Reminder(1, 0, 0, T(0), null, null));
        model.Delivered(1, T(0), T(0));
        Assert.Equal(T(1), model.Nack(0, 0, T(0)).RetryAt);
        var history = HistoryOf((0, OneOff()), (0, Sent(0)),
            (0, new Trouble(T(20), null, StorageCall.Cancel, FaultKind.Slow)));
        model.RunTo(T(20));
        Assert.Single(Liveness.All.Single(r => r.Name == "RetriedOnTime").Broken(model, history, Settings));
    }
}
