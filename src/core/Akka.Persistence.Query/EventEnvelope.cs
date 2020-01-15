//-----------------------------------------------------------------------
// <copyright file="EventEnvelope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Persistence.Query
{
    /// <summary>
    /// Event wrapper adding meta data for the events in the result stream of
    /// <see cref="IEventsByTagQuery"/> query, or similar queries.
    /// </summary>
    public sealed class EventEnvelope : IEquatable<EventEnvelope>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EventEnvelope"/> class.
        /// </summary>
        public EventEnvelope(Offset offset, string persistenceId, long sequenceNr, object @event)
        {
            Offset = offset;
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            Event = @event;
        }

        public Offset Offset { get; }

        public string PersistenceId { get; }

        public long SequenceNr { get; }

        public object Event { get; }

        public bool Equals(EventEnvelope other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (ReferenceEquals(other, null)) return false;

            return Offset == other.Offset
                   && PersistenceId == other.PersistenceId
                   && SequenceNr == other.SequenceNr
                   && Equals(Event, other.Event);
        }

        public override bool Equals(object obj) => obj is EventEnvelope evt && Equals(evt);

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

        public override string ToString() => $"EventEnvelope(persistenceId:{PersistenceId}, seqNr:{SequenceNr}, offset:{Offset}, event:{Event})";
    }
}
