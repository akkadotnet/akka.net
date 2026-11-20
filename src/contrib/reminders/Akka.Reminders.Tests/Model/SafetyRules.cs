namespace Akka.Reminders.Tests.Model;

/// <summary>One rule: a name, what it says, where the spec says it, and a check that lists what breaks it.</summary>
public sealed record Rule(string Name, string Statement, string Source, Func<History, ModelSettings, IEnumerable<string>> Broken);

/// <summary>
/// What must hold after every operation, whatever storage does. Each check reads only the list of
/// things the application did and saw. "docs" is docs/design/failure-modes.md.
/// </summary>
public static class SafetyRules
{
    public static readonly Rule[] All =
    [
        new("OnlyWhatWasScheduled", "Every delivery matches a schedule call: same entity and key, and a due time that call asked for.",
            "README: Schedule Single / Recurring Reminder", OnlyWhatWasScheduled),
        new("NotEarly", "Nothing is delivered more than MaxSlippage before its due time.",
            "docs: Scheduler tick", NotEarly),
        new("NotAfterDeadline", "Nothing is committed for delivery at or after its deadline: due + MaxDeliveryWindow, or the next due time if that is sooner.",
            "docs: Latest-only recurring reminders; ruling: deadlines are judged at commit time", NotAfterDeadline),
        new("HonestEnvelopeDeadline", "The deadline on the envelope is never later than the occurrence's deadline.",
            "README: Acknowledgement Protocol", HonestEnvelopeDeadline),
        new("AttemptCap", "One occurrence is delivered at most MaxDeliveryAttempts times.",
            "docs: Delivery Semantics", AttemptCap),
        new("AckedNeverRedelivered", "Once an ack succeeds or is known to persist before its reply is lost, that occurrence is never delivered again.",
            "ruling: an acknowledged occurrence is never delivered again", AckedNeverRedelivered),
        new("NothingAfterCancel", "After a cancel is answered, or a new schedule call for the same key succeeds, the old reminder delivers nothing.",
            "README: Cancel Reminder", NothingAfterCancel),
        new("LatestOnly", "A recurring reminder never delivers an older occurrence after a newer one.",
            "docs: Latest-only recurring reminders", LatestOnly),
        new("LateAckIsNotFound", "Once a newer occurrence is delivered, an ack for an older one does not succeed.",
            "docs: Late ack for superseded recurring occurrence", LateAckIsNotFound),
        new("NoRepeatWithoutCause", "An occurrence is delivered again only AckTimeout after the attempt before, or after a nack and then at most MaxSlippage before the retry time the nack reply gave.",
            "docs: Ack lost or recipient crashes before acking; Current mitigation", NoRepeatWithoutCause),
        new("ListShowsNothingCancelled", "ListReminders never shows a reminder that was cancelled or replaced.",
            "README: List Reminders", ListShowsNothingCancelled),
    ];

    private static string Show(Delivered d) => $"reminder {d.Id} (entity {d.Entity}, key {d.Key}) due {Journal.T(d.Due)}, delivered at {Journal.T(d.At)},";

    private static IEnumerable<string> OnlyWhatWasScheduled(History h, ModelSettings s) =>
        from d in h.Deliveries
        let call = h.Call(d.Id)
        where call is null || call.Entity != d.Entity || call.Key != d.Key || !call.Definition.IsSlot(d.Due)
        select $"{Show(d)} matches no schedule call";

    private static IEnumerable<string> NotEarly(History h, ModelSettings s) =>
        from d in h.Deliveries
        where d.At < d.Due - s.MaxSlippage
        select $"{Show(d)} is more than {s.MaxSlippageMs}ms early";

    private static IEnumerable<string> NotAfterDeadline(History h, ModelSettings s) =>
        from d in h.Deliveries
        let deadline = h.Call(d.Id)?.Definition.Deadline(d.Due)
        where h.CommittedAt(d.At) >= deadline
        select $"{Show(d)} is at or after its deadline {Journal.T(deadline)}";

    private static IEnumerable<string> HonestEnvelopeDeadline(History h, ModelSettings s) =>
        from d in h.Deliveries
        let deadline = h.Call(d.Id)?.Definition.Deadline(d.Due)
        where d.EnvelopeDeadline > deadline
        select $"{Show(d)} carries deadline {Journal.T(d.EnvelopeDeadline)}; the occurrence ends at {Journal.T(deadline)}";

    private static IEnumerable<string> AttemptCap(History h, ModelSettings s) =>
        from attempts in h.Deliveries.GroupBy(d => (d.Id, d.Due))
        where attempts.Count() > s.MaxAttempts
        select $"{Show(attempts.Last())} is attempt {attempts.Count()}; MaxDeliveryAttempts is {s.MaxAttempts}";

    private static IEnumerable<string> AckedNeverRedelivered(History h, ModelSettings s)
    {
        var byOccurrence = h.Deliveries.ToLookup(d => (d.Entity, d.Key, d.Due));
        foreach (var ack in h.Acks.Where(a => a.Reply == ReminderAckResponseCode.Success ||
                     a.Reply == ReminderAckResponseCode.Error && h.KnownLandedAcknowledgement(s, a)))
        {
            var deliveries = byOccurrence[(ack.Entity, ack.Key, ack.Due)].ToList();
            var acked = deliveries.Where(d => d.Seq <= ack.Asked).Select(d => d.Id).ToHashSet();
            foreach (var again in deliveries.Where(d => d.Seq > ack.Seq && acked.Contains(d.Id)))
                yield return $"{Show(again)} came after its ack persisted at {Journal.T(ack.At)}";
        }
    }

    private static IEnumerable<string> NothingAfterCancel(History h, ModelSettings s)
    {
        var byCall = h.Deliveries.ToLookup(d => d.Id);
        foreach (var call in h.Calls)
        {
            if (h.EndOf(call) is not { } end)
                continue;
            foreach (var late in byCall[call.Id].Where(d => d.Seq > end.Seq))
                yield return $"{Show(late)} came after the reminder ended at {Journal.T(end.At)} ({end.GetType().Name})";
        }
    }

    private static IEnumerable<string> LatestOnly(History h, ModelSettings s)
    {
        foreach (var series in h.Deliveries.GroupBy(d => d.Id))
        {
            var newest = DateTimeOffset.MinValue;
            foreach (var d in series)
            {
                if (d.Due < newest)
                    yield return $"{Show(d)} came after the newer occurrence due {Journal.T(newest)}";
                newest = d.Due > newest ? d.Due : newest;
            }
        }
    }

    private static IEnumerable<string> LateAckIsNotFound(History h, ModelSettings s)
    {
        var byKey = h.Deliveries.ToLookup(d => (d.Entity, d.Key));
        foreach (var ack in h.Acks.Where(a => a.Reply == ReminderAckResponseCode.Success))
        {
            var before = byKey[(ack.Entity, ack.Key)].Where(d => d.Seq <= ack.Asked).ToList();
            var calls = before.Where(d => d.Due == ack.Due).Select(d => d.Id).Distinct().ToList();
            if (calls.Count > 0 && calls.All(id => before.Any(d => d.Id == id && d.Due > ack.Due)))
                yield return $"ack of reminder {ack.Id} (entity {ack.Entity}, key {ack.Key}) due {Journal.T(ack.Due)} succeeded at {Journal.T(ack.At)} after a newer occurrence was delivered";
        }
    }

    private static IEnumerable<string> NoRepeatWithoutCause(History h, ModelSettings s)
    {
        var nacks = h.Nacks.Where(n => n.Reply is ReminderNackResponseCode.RetryScheduled or ReminderNackResponseCode.Error)
            .ToLookup(n => (n.Entity, n.Key, n.Due));
        foreach (var attempts in h.Deliveries.GroupBy(d => (d.Id, d.Due)).Select(g => g.ToList()))
        {
            foreach (var (previous, next) in attempts.Zip(attempts.Skip(1)))
            {
                var nack = nacks[(next.Entity, next.Key, next.Due)].LastOrDefault(n => n.Asked >= previous.Seq && n.Asked < next.Seq);
                var earliest = nack is null ? h.CommittedAt(previous.At) + s.AckTimeout : (nack.RetryAt ?? next.At) - s.MaxSlippage;
                if (next.At < earliest)
                    yield return $"{Show(next)} repeats the attempt at {Journal.T(previous.At)}; the earliest allowed time was {Journal.T(earliest)}";
            }
        }
    }

    private static IEnumerable<string> ListShowsNothingCancelled(History h, ModelSettings s) =>
        from list in h.Lists
        from item in list.Items
        let call = h.Call(item.Id)
        where call is null || h.EndOf(call) is { } end && end.Seq < list.Seq
        select $"list of entity {list.Entity} at {Journal.T(list.At)} shows reminder {item.Id} (key {item.Key}, due {Journal.T(item.Due)}), which was cancelled or replaced";
}
