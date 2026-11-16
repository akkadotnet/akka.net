#nullable enable

using Akka.Reminders.Serialization;

namespace Akka.Reminders.Tests.Serialization;

public partial class ReminderSerializerSpecs
{
    [Theory(DisplayName = "Should_PreserveRetryPolicyAndLegacyManifests_When_SchedulingAndListing")]
    [InlineData(ReminderRetryPolicy.UseConfiguredAttemptLimit, "sr", "rsd", "rfe")]
    [InlineData(ReminderRetryPolicy.UntilAcknowledged, "sr2", "rsd2", "rfe2")]
    public void Should_PreserveRetryPolicyAndLegacyManifests_When_SchedulingAndListing(
        ReminderRetryPolicy policy, string scheduleManifest, string responseManifest, string listManifest)
    {
        var serializer = new ReminderSerializer((Akka.Actor.ExtendedActorSystem)Sys);
        var entity = new ReminderEntity("region", "e1");
        var command = new ReminderProtocol.ScheduleReminder(entity, new ReminderKey("work"), DateTimeOffset.UnixEpoch, "payload")
        { RetryPolicy = policy };
        Assert.Equal(scheduleManifest, serializer.Manifest(command));
        AssertEqual(command);
        var response = new ReminderProtocol.ReminderScheduled(command, ReminderScheduleResponseCode.Success);
        Assert.Equal(responseManifest, serializer.Manifest(response));
        AssertEqual(response);
        var row = command.ToScheduledReminder();
        Assert.Equal(policy, row.ToScheduleReminder().RetryPolicy);
        var list = new ReminderProtocol.RemindersForEntity(entity, FetchRemindersResponseCode.Success,
            [row, row with { Key = new ReminderKey("bounded"), RetryPolicy = default }]);
        Assert.Equal(listManifest, serializer.Manifest(list));
        var decoded = AssertAndReturn(list);
        Assert.Equal(list.Reminders, decoded.Reminders);
    }
}
