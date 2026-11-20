using Akka.Actor;
using Akka.Configuration;

namespace Akka.Reminders.Tests.Model;

/// <summary>Tests of the harness itself. A check that cannot fail hides bugs, so each check gets one that makes it fail.</summary>
public sealed class HarnessSpecs
{
    [Fact(DisplayName = "Should_FailSchedulerStops_When_TheSchedulerDoesNotStopBeforeRestart")]
    public async Task SchedulerStopsRuleFires()
    {
        // The scheduler ignores this message, so the stop times out. GracefulStop answers false; it does not throw.
        var app = new ReminderApp(ModelSettings.Default) { StopMessage = new object(), StopTimeout = TimeSpan.FromMilliseconds(500) };
        try
        {
            var failure = await Assert.ThrowsAsync<ModelViolation>(() => app.Restart());
            Assert.StartsWith("SchedulerStops", failure.Message);
        }
        finally
        {
            app.Stop();
            await ReminderApp.DisposeStoppedAsync(); // retires the host, because the scheduler never stopped
        }
    }

    [Fact(DisplayName = "Should_JournalTheReplyBeforeTellReturns_When_TheSchedulerAnswers")]
    public async Task ReplyIsJournaledInsideTell()
    {
        var host = await ModelHost.RentAsync();
        try
        {
            var journaled = new List<string>();
            var replyTo = new JournalingReply<string>(host.System, journaled.Add);

            replyTo.Tell("first", ActorRefs.NoSender);
            Assert.Equal(["first"], journaled); // nobody has awaited the reply yet
            replyTo.Tell("second", ActorRefs.NoSender);

            Assert.Equal("first", await replyTo.Reply);
            Assert.Equal(["first"], journaled); // one answer per call
        }
        finally
        {
            ModelHost.Return(host);
        }
    }

    [Fact(DisplayName = "Should_SendTimersInDueOrder_When_TwoThreadsFireDueTimers")]
    public async Task FireDueCallsDoNotOvertakeEachOther()
    {
        var clock = new VirtualClock(Config.Empty, null!);
        var order = new List<string>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        clock.ScheduleTellOnce(TimeSpan.Zero, new Receiver("first", order, entered, release), "m", ActorRefs.Nobody);

        // The first call is inside its send when the second timer comes due and a second call fires it.
        var firstCall = Task.Run(clock.FireDue, TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        clock.ScheduleTellOnce(TimeSpan.Zero, new Receiver("second", order, null, null), "m", ActorRefs.Nobody);
        var secondCall = Task.Run(clock.FireDue, TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken); // room for the second call to overtake

        release.Set();
        await Task.WhenAll(firstCall, secondCall);
        Assert.Equal(["first", "second"], order);
    }

    // Three entities each own a reminder due at the same moment, so one fetch delivers three occurrences and the
    // harness acks them as one round. The first Ack storage call fails. Which acks share that call decides which
    // replies are errors, so the replies show the batches.
    private static async Task<(List<int> Delivered, List<int> Acked, string Replies)> AckRoundAsync(ModelSettings settings)
    {
        var app = new ReminderApp(settings);
        try
        {
            for (var entity = 0; entity < ReminderApp.Entities; entity++)
                await app.ScheduleOnce(entity, 0, TimeSpan.FromSeconds(1));
            await app.InjectFault(StorageCall.Ack, FaultKind.Fail, 1, TimeSpan.Zero, fireTimersWhileSlow: false);
            await app.Tick(TimeSpan.FromSeconds(2)); // the ack timeout is 10 s: nothing is delivered twice

            var history = app.Journal.Read();
            return ([.. history.Deliveries.Select(d => d.Id)], [.. history.Acks.Select(a => a.Id)],
                string.Join(",", history.Acks.Select(a => a.Reply)));
        }
        finally
        {
            app.Stop();
            await ReminderApp.DisposeStoppedAsync();
        }
    }

    // Each case is an exact answer, not "the same as last time": the group and the flush batch decide the batches.
    [Theory(DisplayName = "Should_AnswerEveryAckTheSameWay_When_ThreeAcksShareARoundAndAnAckCallFails")]
    [InlineData(0, 256, "Error,Error,Error")] // one group, one flush: the failed call covers all three
    [InlineData(0, 2, "Error,Error,Success")] // one group, flushes of two: the first flush fails
    [InlineData(1, 256, "Error,Success,Success")] // one ack per group: the first ack's own flush fails
    [InlineData(2, 256, "Error,Error,Success")] // groups of two: the first group's flush fails
    public async Task AckBatchesDoNotDependOnTiming(int ackGroup, int ackFlushBatch, string expected)
    {
        var settings = new ModelSettings(1000, 10_000, 1000, 10_000, 3, AckFlushBatch: ackFlushBatch, AckGroup: ackGroup);
        for (var run = 0; run < 200; run++)
        {
            var round = await AckRoundAsync(settings);
            Assert.Equal(ReminderApp.Entities, round.Delivered.Count);
            Assert.Equal(round.Delivered, round.Acked); // replies come in the order the acks were sent
            Assert.Equal(expected, round.Replies);
        }
    }

    [Fact(DisplayName = "Should_ReadNothing_UntilTheHoldIsReleased")]
    public async Task HeldSchedulerReadsNothing()
    {
        var app = new ReminderApp(ModelSettings.Default);
        SchedulerHold? hold = null;
        try
        {
            await app.List(0); // starts the scheduler
            hold = await app.HoldSchedulerAsync();

            var schedule = app.ScheduleOnce(0, 0, TimeSpan.FromHours(1));
            await Task.Delay(300, TestContext.Current.CancellationToken); // a live scheduler answers in a few ms
            Assert.False(schedule.IsCompleted);

            hold.Release();
            Assert.Equal(ReminderScheduleResponseCode.Success, await schedule);
        }
        finally
        {
            hold?.Release();
            app.Stop();
            await ReminderApp.DisposeStoppedAsync();
        }
    }

    [Fact(DisplayName = "Should_StopTheScheduler_WhileItIsHeld")]
    public async Task HeldSchedulerStillStops()
    {
        var app = new ReminderApp(ModelSettings.Default);
        SchedulerHold? hold = null;
        try
        {
            await app.List(0);
            hold = await app.HoldSchedulerAsync();

            // A restart stops the scheduler and starts a new one. The stop is a system message, which a suspended
            // mailbox still reads. (The new scheduler runs on a mailbox of its own, with no hold.)
            await app.Restart();
            Assert.Equal(ReminderScheduleResponseCode.Success, await app.ScheduleOnce(0, 0, TimeSpan.FromHours(1)));
        }
        finally
        {
            hold?.Release();
            app.Stop();
            await ReminderApp.DisposeStoppedAsync();
        }
    }

    private sealed class Receiver(string name, List<string> order, ManualResetEventSlim? entered, ManualResetEventSlim? release) : ICanTell
    {
        public void Tell(object message, IActorRef sender)
        {
            entered?.Set();
            release?.Wait(TimeSpan.FromSeconds(30));
            lock (order)
                order.Add(name);
        }
    }
}
