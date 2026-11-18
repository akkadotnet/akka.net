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
