using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests.Golden;

/// <summary>How the generator moves a row to its final state through the 1.5.73 storage API.</summary>
public enum GoldenPath
{
    /// <summary>Scheduled and left open (Pending).</summary>
    Open,

    /// <summary><c>MarkRemindersAsAwaitingAckAsync</c>, left open.</summary>
    AwaitingAck,

    /// <summary><c>MarkRemindersAsCompletedAsync</c> with the row's status.</summary>
    MarkCompleted,

    /// <summary><c>MarkRemindersAsAwaitingAckAsync</c> then <c>AcknowledgeReminderAsync</c> (Delivered).</summary>
    Acknowledged,

    /// <summary><c>ExpireRemindersAsync</c> at <see cref="GoldenRow.CompletedAt"/>.</summary>
    Expire
}

/// <summary>What one fixture row must hold, and read back as, in both golden databases.</summary>
public sealed record GoldenRow(
    string Group,
    GoldenKind Kind,
    ReminderEntity Entity,
    ReminderKey Key,
    DateTimeOffset DueTime,
    DateTimeOffset When,
    object Payload,
    TimeSpan? RepeatInterval,
    TimeSpan? MaxDeliveryWindow,
    DateTimeOffset? DeliveryDeadline,
    int AttemptCount,
    string? LastFailureReason,
    ReminderCompletionStatus Status,
    GoldenPath Path,
    DateTimeOffset? CompletedAt = null,
    DateTimeOffset? DeliveredAt = null,
    DateTimeOffset? AckDeadline = null)
{
    public bool IsOpen => Status is ReminderCompletionStatus.Pending or ReminderCompletionStatus.AwaitingAck;

    public string Id => $"{Entity.ShardRegionName}/{Entity.EntityId}/{Key.Name}@{DueTime:O}";

    public ScheduledReminder ToScheduledReminder()
        => new(Entity, Key, When, Payload, RepeatInterval, AttemptCount, LastFailureReason,
            MaxDeliveryWindow, DeliveryDeadline, DueTime);
}

/// <summary>
/// The deterministic row list. The generator writes it through 1.5.73; the test reads the
/// database back and compares every row to it. Nothing here depends on the current time.
/// </summary>
public static class GoldenRows
{
    public const string NotesRegion = "golden-notes";
    public const string OrdersRegion = "golden-orders";
    public const string MiscRegion = "golden-misc";
    public const string LiveRegion = "golden-live";

    /// <summary>Completed rows are dated in the past, relative to this instant.</summary>
    public static readonly DateTimeOffset Past = new(2025, 3, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Live rows are due long after any test run will happen.</summary>
    public static readonly DateTimeOffset Far = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The generator runs ExpireRemindersAsync at these two instants.</summary>
    public static readonly DateTimeOffset ExpireAtFirst = new(2025, 6, 30, 0, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset ExpireAtSecond = new(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The three overdue one-off rows in <see cref="LiveRegion"/> that the scheduler delivers.</summary>
    public static readonly DateTimeOffset OverdueDue = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

    private static readonly TimeSpan[] Intervals =
        [TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromDays(1), TimeSpan.FromDays(7)];

    private static readonly string[] FailureReasons =
    [
        "Delivery failed: shard region unavailable",
        "Ack timeout after 30s",
        "Delivery failed: entity did not respond",
        "Storage write failed: database is locked"
    ];

    private static readonly GoldenKind[] AllKinds = Enum.GetValues<GoldenKind>();

    public static IReadOnlyList<GoldenRow> Build()
    {
        var rows = new List<GoldenRow>();
        AddKindSweep(rows);
        AddConsumerShape(rows);
        AddOverdue(rows);

        var duplicate = rows.GroupBy(r => r.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate golden row id {duplicate.Key}");

        return rows;
    }

    private static string RegionFor(GoldenKind kind) => kind switch
    {
        GoldenKind.Poco => OrdersRegion,
        GoldenKind.CustomManifestV1 or GoldenKind.CustomManifestV2 => NotesRegion,
        _ => MiscRegion
    };

    // Every payload kind x 2 rounds x 5 shapes: pending, recurring, windowed, delivered, cancelled.
    private static void AddKindSweep(List<GoldenRow> rows)
    {
        foreach (var kind in AllKinds)
        {
            var ki = (int)kind;
            for (var round = 0; round < 2; round++)
            {
                var entity = new ReminderEntity(RegionFor(kind), $"{kind.ToString().ToLowerInvariant()}-{round}");
                var farBase = Far.AddHours((ki * 100) + (round * 50));
                var pastBase = Past.AddDays((ki * 3) + round);

                // Pending one-off
                rows.Add(Row("kind", kind, entity, "pending", farBase, Sample(kind, round, 0)));

                // Pending recurring
                rows.Add(Row("kind", kind, entity, "recurring", farBase.AddHours(7), Sample(kind, round, 1),
                    repeat: round == 0 ? TimeSpan.FromDays(1) : TimeSpan.FromHours(6)));

                // Pending with a delivery window and deadline
                rows.Add(Row("kind", kind, entity, "windowed", farBase.AddHours(14), Sample(kind, round, 2),
                    window: TimeSpan.FromHours(2)));

                // Delivered
                var delivered = pastBase.AddHours(1);
                rows.Add(Row("kind", kind, entity, "delivered", delivered, Sample(kind, round, 3),
                    attempts: 1, status: ReminderCompletionStatus.Delivered, path: GoldenPath.MarkCompleted,
                    completedAt: delivered.AddSeconds(2)));

                // Cancelled before it came due
                var cancelledDue = pastBase.AddHours(5);
                rows.Add(Row("kind", kind, entity, "cancelled", cancelledDue, Sample(kind, round, 4),
                    status: ReminderCompletionStatus.Cancelled, path: GoldenPath.MarkCompleted,
                    completedAt: cancelledDue.AddHours(-3)));
            }
        }
    }

    // Shaped like a consumer that stores every payload through its own serializer:
    // mostly rp-v1 / rp-v2, all statuses, recurring series with delivered history.
    private static void AddConsumerShape(List<GoldenRow> rows)
    {
        // 40 pending one-offs
        for (var i = 0; i < 40; i++)
        {
            rows.Add(NotesRow("pending", $"note-p{i:D3}", UserFor(i), Far.AddHours(1000 + (i * 5)), i));
        }

        // 24 recurring series: two delivered occurrences, then the pending next occurrence
        for (var s = 0; s < 24; s++)
        {
            var interval = Intervals[s % Intervals.Length];
            var user = UserFor(s);
            var key = $"series-{s:D2}";
            for (var d = 0; d < 2; d++)
            {
                var due = Past.AddDays(s).AddTicks(interval.Ticks * d);
                rows.Add(NotesRow("series-delivered", key, user, due, (s * 3) + d,
                    repeat: interval, attempts: 1, status: ReminderCompletionStatus.Delivered,
                    path: d == 1 && s % 2 == 1 ? GoldenPath.Acknowledged : GoldenPath.MarkCompleted,
                    completedAt: due.AddSeconds(3)));
            }

            rows.Add(NotesRow("series-pending", key, user, Far.AddDays(s), (s * 3) + 2,
                repeat: interval, window: s % 3 == 0 ? interval / 2 : null));
        }

        // 30 delivered one-offs, some after a retry
        for (var i = 0; i < 30; i++)
        {
            var due = Past.AddDays(40 + i).AddHours(8);
            var retried = i % 5 == 0;
            rows.Add(NotesRow("delivered", $"note-d{i:D3}", UserFor(i), due, i,
                attempts: retried ? 2 : 1, failure: retried ? FailureReasons[1] : null,
                status: ReminderCompletionStatus.Delivered,
                path: i % 2 == 1 ? GoldenPath.Acknowledged : GoldenPath.MarkCompleted,
                completedAt: due.AddSeconds(2 + i)));
        }

        // 20 cancelled: half cancelled before a past due time, half before a far future one
        for (var i = 0; i < 20; i++)
        {
            var due = i % 2 == 0 ? Past.AddDays(80 + i).AddHours(9) : Far.AddDays(500 + i);
            rows.Add(NotesRow("cancelled", $"note-c{i:D3}", UserFor(i), due, i,
                status: ReminderCompletionStatus.Cancelled, path: GoldenPath.MarkCompleted,
                completedAt: new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero).AddMinutes(i * 11)));
        }

        // 12 expired: deadline passed before the generator's ExpireRemindersAsync call
        for (var i = 0; i < 12; i++)
        {
            var first = i < 6;
            var due = first ? Past.AddDays(100 + i).AddHours(6) : Past.AddDays(190 + i).AddHours(6);
            rows.Add(NotesRow("expired", $"note-e{i:D3}", UserFor(i), due, i,
                window: i % 2 == 0 ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(30),
                attempts: i % 3, failure: i % 3 == 0 ? null : FailureReasons[0],
                status: ReminderCompletionStatus.Expired, path: GoldenPath.Expire,
                completedAt: first ? ExpireAtFirst : ExpireAtSecond));
        }

        // 12 failed after exhausting retries
        for (var i = 0; i < 12; i++)
        {
            var due = Past.AddDays(200 + i).AddHours(10);
            rows.Add(NotesRow("failed", $"note-f{i:D3}", UserFor(i), due, i,
                attempts: 3 + (i % 3), failure: FailureReasons[i % FailureReasons.Length],
                status: ReminderCompletionStatus.Failed, path: GoldenPath.MarkCompleted,
                completedAt: due.AddMinutes(5)));
        }

        // 10 waiting for an ack. The ack deadline is far away so opening the file never redelivers them.
        for (var i = 0; i < 10; i++)
        {
            var due = Past.AddDays(300 + i).AddHours(11);
            rows.Add(NotesRow("awaiting-ack", $"note-a{i:D3}", UserFor(i), due, i,
                attempts: 1, status: ReminderCompletionStatus.AwaitingAck, path: GoldenPath.AwaitingAck,
                deliveredAt: due.AddSeconds(1), ackDeadline: Far.AddDays(i)));
        }

        // 10 pending retries: a failed attempt moved "when" past the occurrence's due time
        for (var i = 0; i < 10; i++)
        {
            var due = Far.AddDays(600 + i);
            rows.Add(NotesRow("pending-retry", $"note-r{i:D3}", UserFor(i), due, i,
                attempts: 1 + (i % 2), failure: FailureReasons[i % FailureReasons.Length],
                when: due.AddMinutes(2 << (i % 3))));
        }

        // 14 pending with a delivery window, recurring and one-off
        for (var i = 0; i < 14; i++)
        {
            var window = TimeSpan.FromHours(1 + (i * 6));
            rows.Add(NotesRow("pending-window", $"note-w{i:D3}", UserFor(i), Far.AddDays(700 + i), i,
                window: window, repeat: i % 2 == 0 ? TimeSpan.FromDays(1) : null));
        }
    }

    // Three overdue one-off rows: the scheduler must deliver these when the file is opened.
    private static void AddOverdue(List<GoldenRow> rows)
    {
        rows.Add(Row("overdue", GoldenKind.CustomManifestV1, new ReminderEntity(LiveRegion, "live-notes"),
            "overdue", OverdueDue, GoldenPayloads.Note(901)));
        rows.Add(Row("overdue", GoldenKind.Poco, new ReminderEntity(LiveRegion, "live-order"),
            "overdue", OverdueDue, GoldenPayloads.Sample(GoldenKind.Poco, 902)));
        rows.Add(Row("overdue", GoldenKind.String, new ReminderEntity(LiveRegion, "live-text"),
            "overdue", OverdueDue, GoldenPayloads.Sample(GoldenKind.String, 903)));
    }

    private static string UserFor(int i) => $"user-{i % 16:D2}";

    private static object Sample(GoldenKind kind, int round, int shape)
        => GoldenPayloads.Sample(kind, (round * 5) + shape);

    // Alternates rp-v1 and rp-v2 payloads, two of every three rows being rp-v1.
    private static GoldenRow NotesRow(
        string group,
        string key,
        string user,
        DateTimeOffset due,
        int index,
        TimeSpan? repeat = null,
        TimeSpan? window = null,
        int attempts = 0,
        string? failure = null,
        ReminderCompletionStatus status = ReminderCompletionStatus.Pending,
        GoldenPath path = GoldenPath.Open,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? deliveredAt = null,
        DateTimeOffset? ackDeadline = null,
        DateTimeOffset? when = null)
    {
        var v2 = index % 3 == 2;
        return Row(group, v2 ? GoldenKind.CustomManifestV2 : GoldenKind.CustomManifestV1,
            new ReminderEntity(NotesRegion, user), key, due,
            v2 ? GoldenPayloads.FollowUp(index) : GoldenPayloads.Note(index),
            repeat, window, attempts, failure, status, path, completedAt, deliveredAt, ackDeadline, when);
    }

    private static GoldenRow Row(
        string group,
        GoldenKind kind,
        ReminderEntity entity,
        string key,
        DateTimeOffset due,
        object payload,
        TimeSpan? repeat = null,
        TimeSpan? window = null,
        int attempts = 0,
        string? failure = null,
        ReminderCompletionStatus status = ReminderCompletionStatus.Pending,
        GoldenPath path = GoldenPath.Open,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? deliveredAt = null,
        DateTimeOffset? ackDeadline = null,
        DateTimeOffset? when = null)
        => new(group, kind, entity, new ReminderKey(key), due, when ?? due, payload, repeat, window,
            window.HasValue ? due + window.Value : null, attempts, failure, status, path,
            completedAt, deliveredAt, ackDeadline);
}
