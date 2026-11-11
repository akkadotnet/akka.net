using CsCheck;

namespace Akka.Reminders.Tests.Model;

/// <summary>One recurring reminder under the conformance test's eager-fetch settings; at most four occurrences.</summary>
public static class RecurringSpec
{
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan EarlyDeliveryAllowance = TimeSpan.FromSeconds(1);

    public readonly record struct FirstDueTime(TimeSpan Offset)
    {
        public static readonly FirstDueTime DueNow = new(TimeSpan.Zero);
        // Three seconds overdue: with a four-second interval the next occurrence is due in one second.
        public static readonly FirstDueTime Overdue = new(TimeSpan.FromSeconds(-3));
        public TimeSpan DueAt(Occurrence occurrence) => Offset + RepeatInterval * occurrence.Index;
        public override string ToString() => this == DueNow ? "DueNow" : this == Overdue ? "Overdue" : $"Due at {Offset}";
    }

    /// <summary>Identifies one occurrence in this reminder's sequence; the first has index zero.</summary>
    public readonly record struct Occurrence(int Index)
    {
        public override string ToString() => $"Occurrence #{Index + 1}";
    }

    /// <summary>A number of occurrences, never a duration or an occurrence's index.</summary>
    public readonly record struct OccurrenceCount(int Value)
    {
        public Occurrence? Latest => Value == 0 ? null : new Occurrence(Value - 1);
        public bool Includes(Occurrence occurrence) => occurrence.Index >= 0 && occurrence.Index < Value;
        public OccurrenceCount Add(OccurrenceCount additional) => new(Value + additional.Value);
        public override string ToString() => Value == 1 ? "1 occurrence" : $"{Value} occurrences";
    }

    public static readonly FirstDueTime[] FirstDueTimes = [FirstDueTime.Overdue, FirstDueTime.DueNow];
    // Wait for one or two more occurrences, not one or two seconds.
    public static readonly OccurrenceCount[] WaitCounts = [new(1), new(2)];
    public static readonly Occurrence[] Occurrences = [new(0), new(1), new(2), new(3)];

    // Null means not scheduled, not acknowledged, or no acknowledgement requested yet, respectively.
    // ExpectedDeliveries predicts distinct occurrences delivered, not delivery attempts or observations.
    public sealed record State(FirstDueTime? FirstDue = null, TimeSpan CurrentTime = default,
        OccurrenceCount ExpectedDeliveries = default, Occurrence? LastAcknowledgedOccurrence = null,
        Occurrence? RequestedAcknowledgement = null, ReminderAckResponseCode? ExpectedAcknowledgementReply = null)
    {
        public TimeSpan DueAt(Occurrence occurrence) => FirstDue!.Value.DueAt(occurrence);

        public State Schedule(FirstDueTime firstDue) => this with
        {
            FirstDue = firstDue,
            ExpectedDeliveries = new(Occurrences.Count(occurrence => firstDue.DueAt(occurrence) <= EarlyDeliveryAllowance))
        };

        public State WaitFor(OccurrenceCount additional)
        {
            var delivered = ExpectedDeliveries.Add(additional);
            return this with { CurrentTime = DueAt(delivered.Latest!.Value), ExpectedDeliveries = delivered };
        }

        public State Acknowledge(Occurrence occurrence) => occurrence == ExpectedDeliveries.Latest && occurrence != LastAcknowledgedOccurrence
            ? this with { RequestedAcknowledgement = occurrence, ExpectedAcknowledgementReply = ReminderAckResponseCode.Success, LastAcknowledgedOccurrence = occurrence }
            : this with { RequestedAcknowledgement = occurrence, ExpectedAcknowledgementReply = ReminderAckResponseCode.NotFound };
    }

    public static Spec<State> Create() => Spec.From(new State())
        // With eager fetching, Overdue delivers both the overdue occurrence and the next one within the early allowance.
        .Action("Schedule", FirstDueTimes, (s, firstDue) => s.FirstDue is null,
            (s, firstDue) => s.Schedule(firstDue))
        .Action("WaitFor", WaitCounts, (s, count) => s.FirstDue is not null && s.ExpectedDeliveries.Add(count).Value <= Occurrences.Length,
            (s, count) => s.WaitFor(count))
        // Keep old and duplicate acknowledgements enabled; only unseen occurrences are excluded.
        .Action("Acknowledge", Occurrences, (s, occurrence) => s.ExpectedDeliveries.Includes(occurrence),
            (s, occurrence) => s.Acknowledge(occurrence))
        .Invariant("OnlyDeliveredOccurrencesAcknowledged", "Only a delivered occurrence can have been acknowledged.",
            s => s.LastAcknowledgedOccurrence is not { } acknowledged || s.ExpectedDeliveries.Includes(acknowledged))
        .Rule("AcknowledgeCurrentOccurrence", "An unacknowledged current occurrence accepts its acknowledgement.",
            on: "Acknowledge",
            (before, after) => after.RequestedAcknowledgement == before.ExpectedDeliveries.Latest && before.LastAcknowledgedOccurrence != after.RequestedAcknowledgement,
            (before, after) => after.ExpectedAcknowledgementReply == ReminderAckResponseCode.Success && after.LastAcknowledgedOccurrence == after.RequestedAcknowledgement)
        .Rule("AcknowledgeOlderOccurrence", "A superseded occurrence answers NotFound and leaves acknowledgement state alone.",
            on: "Acknowledge",
            (before, after) => after.RequestedAcknowledgement!.Value.Index < before.ExpectedDeliveries.Latest!.Value.Index,
            (before, after) => after.ExpectedAcknowledgementReply == ReminderAckResponseCode.NotFound && after.LastAcknowledgedOccurrence == before.LastAcknowledgedOccurrence)
        .Rule("AcknowledgeSameOccurrenceAgain", "An already acknowledged occurrence answers NotFound.",
            on: "Acknowledge",
            (before, after) => after.RequestedAcknowledgement == before.LastAcknowledgedOccurrence,
            (before, after) => after.ExpectedAcknowledgementReply == ReminderAckResponseCode.NotFound && after.LastAcknowledgedOccurrence == before.LastAcknowledgedOccurrence);
}
