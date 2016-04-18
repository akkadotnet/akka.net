using System;

namespace Akka.Persistence.Query
{
    /// <summary>
    /// Event wrapper adding meta data for the events in the result stream of
    /// <see cref="IEventsByTagQuery"/> query, or similar queries.
    /// </summary>
    [Serializable]
    public sealed class EventEnvelope : IEquatable<EventEnvelope>
    {
        public readonly long Offset;
        public readonly string PersistenceId;
        public readonly long SequenceNr;
        public readonly object Event;

        public EventEnvelope(long offset, string persistenceId, long sequenceNr, object @event)
        {
            Offset = offset;
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            Event = @event;
        }

        public bool Equals(EventEnvelope other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (ReferenceEquals(other, null)) return false;

            return Offset == other.Offset
                   && PersistenceId == other.PersistenceId
                   && SequenceNr == other.SequenceNr
                   && Equals(Event, other.Event);
        }

        public override bool Equals(object obj)
        {
            return obj is EventEnvelope && Equals((EventEnvelope) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Offset.GetHashCode();
                hashCode = (hashCode*397) ^ (PersistenceId != null ? PersistenceId.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ SequenceNr.GetHashCode();
                hashCode = (hashCode*397) ^ (Event != null ? Event.GetHashCode() : 0);
                return hashCode;
            }
        }
    }
}