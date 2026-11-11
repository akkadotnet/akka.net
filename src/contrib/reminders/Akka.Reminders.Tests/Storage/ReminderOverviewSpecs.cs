using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Storage;

public class ReminderOverviewSpecs
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ReminderEntity Entity = new("region", "e1");

    private static ScheduledReminder DueIn(TimeSpan delay) => new(Entity, new ReminderKey("k"), Now + delay, "payload");

    [Fact(DisplayName = "Should_KeepAReminderDueNow_When_ALaterReminderIsApplied (#154)")]
    public void Should_KeepAReminderDueNow_When_ALaterReminderIsApplied()
    {
        var dueNow = ReminderOverview.Empty.Apply(DueIn(TimeSpan.Zero), Now).newOverview;
        Assert.Equal(TimeSpan.Zero, dueNow.TimeUntilNext);

        var (overview, isSooner) = dueNow.Apply(DueIn(TimeSpan.FromSeconds(30)), Now);

        Assert.False(isSooner);
        Assert.Equal(TimeSpan.Zero, overview.TimeUntilNext);
        Assert.Equal(2, overview.TotalPendingReminders);
    }

    [Fact(DisplayName = "Should_TakeAnyReminder_When_OverviewIsEmpty")]
    public void Should_TakeAnyReminder_When_OverviewIsEmpty()
    {
        var (overview, isSooner) = ReminderOverview.Empty.Apply(DueIn(TimeSpan.FromHours(1)), Now);

        Assert.True(isSooner);
        Assert.Equal(TimeSpan.FromHours(1), overview.TimeUntilNext);
    }
}
