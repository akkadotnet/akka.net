//-----------------------------------------------------------------------
// <copyright file="Offset.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Persistence.Query
{
    /// <summary>
    /// Used in <see cref="IEventsByTagQuery"/> implementations to signal to Akka.Persistence.Query
    /// where to begin and end event by tag queries.
    ///
    /// For concrete implementations, see <see cref="Sequence"/> and <see cref="NoOffset"/>.
    /// </summary>
    public abstract class Offset : IComparable<Offset>
    {
        /// <summary>
        /// Used when retrieving all events.
        /// </summary>
        public static Offset NoOffset() => Query.NoOffset.Instance;

        /// <summary>
        /// Factory to create an offset of type <see cref="Query.Sequence"/>
        /// </summary>
        public static Offset Sequence(long value) => new Sequence(value);

        /// <summary>
        /// Factory to create an offset of type <see cref="TimeBasedUuid"/>
        /// </summary>
        public static Offset TimeBasedUuid(Guid value) => new TimeBasedUuid(value);

        /// <summary>
        /// Used to compare to other <see cref="Offset"/> implementations.
        /// </summary>
        /// <param name="other">The other offset to compare.</param>
        public abstract int CompareTo(Offset other);
    }

    /// <summary>
    /// Corresponds to an ordered sequence number for the events.Note that the corresponding
    /// offset of each event is provided in the <see cref="EventEnvelope"/>,
    /// which makes it possible to resume the stream at a later point from a given offset.
    /// <para>
    /// The `offset` is exclusive, i.e.the event with the exact same sequence number will not be included
    /// in the returned stream. This means that you can use the offset that is returned in <see cref="EventEnvelope"/>
    /// as the `offset` parameter in a subsequent query.
    /// </para>
    /// </summary>
    public sealed class Sequence : Offset, IComparable<Sequence>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Sequence"/> class.
        /// </summary>
        public Sequence(long value)
        {
            Value = value;
        }

        public long Value { get; }

        public int CompareTo(Sequence other) => Value.CompareTo(other.Value);

        private bool Equals(Sequence other) => Value == other.Value;

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Sequence && Equals((Sequence)obj);
        }

        public override int GetHashCode() => Value.GetHashCode();

        public override int CompareTo(Offset other)
        {
            if (other is Sequence seq)
            {
                return CompareTo(seq);
            }

            throw new InvalidOperationException($"Can't compare offset of type {GetType()} to offset of type {other.GetType()}");
        }
    }

    /// <summary>
    /// Corresponds to an ordered unique identifier of the events. Note that the corresponding
    /// offset of each event is provided in the <see cref="EventEnvelope"/>, which makes it 
    /// possible to resume the stream at a later point from a given offset.
    /// <para>
    /// The `offset` is exclusive, i.e. the event with the exact same sequence number will not be included
    /// in the returned stream. This means that you can use the offset that is returned in `EventEnvelope`
    /// as the `offset` parameter in a subsequent query.
    /// </para>
    /// </summary>
    public sealed class TimeBasedUuid : Offset, IComparable<TimeBasedUuid>
    {
        public Guid Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TimeBasedUuid"/> class.
        /// </summary>
        public TimeBasedUuid(Guid value) => Value = value;

        public int CompareTo(TimeBasedUuid other) => Value.CompareTo(other.Value);

        private bool Equals(TimeBasedUuid other) => Value == other.Value;

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is TimeBasedUuid uUID && Equals(uUID);
        }

        public override int GetHashCode() => Value.GetHashCode();

        public override int CompareTo(Offset other)
        {
            return other is TimeBasedUuid seq
                ? CompareTo(seq)
                : throw new InvalidOperationException($"Can't compare offset of type {GetType()} to offset of type {other.GetType()}");
        }
    }

    /// <summary>
    /// Used when retrieving all events.
    /// </summary>
    public sealed class NoOffset : Offset
    {
        /// <summary>
        /// The singleton instance of <see cref="NoOffset"/>.
        /// </summary>
        public static NoOffset Instance { get; } = new NoOffset();
        private NoOffset() { }

        public override int CompareTo(Offset other)
        {
            if (other is NoOffset no)
            {
                return 0;
            }

            throw new InvalidOperationException($"Can't compare offset of type {GetType()} to offset of type {other.GetType()}");
        }
    }
}
