//-----------------------------------------------------------------------
// <copyright file="Offset.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Persistence.Query
{
    /// <summary>
    /// Used in <see cref="IEventsByTagQuery"/> implementations to signal to Akka.Persistence.Query
    /// where to begin and end event by tag queries.
    ///
    /// For concrete implementations, see <see cref="Sequence"/>, <see cref="NoOffset"/> and <see cref="Query.FromEnd"/>.
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
        /// Factory to create an offset of type <see cref="Query.FromEnd"/>, which begins the query at the
        /// <paramref name="count"/>-th event from the end of history (inclusive), e.g. "the last 10 events".
        /// </summary>
        /// <param name="count">The number of events to read from the end of history. Must be greater than zero.</param>
        public static Offset FromEnd(int count) => new FromEnd(count);

        /// <summary>
        /// Used to compare to other <see cref="Offset"/> implementations.
        /// </summary>
        /// <param name="other">The other offset to compare.</param>
        public abstract int CompareTo(Offset other);

        /// <summary>
        /// Used to log offset's value
        /// </summary>
        public abstract override string ToString();
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
            return obj is Sequence sequence && Equals(sequence);
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

        public override string ToString() => Value.ToString();
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

        public override string ToString() => Value.ToString();
    }

    /// <summary>
    /// Used when retrieving all events.
    /// </summary>
    public sealed class NoOffset : Offset
    {
        /// <summary>
        /// The singleton instance of <see cref="NoOffset"/>.
        /// </summary>
        public static NoOffset Instance { get; } = new();
        private NoOffset() { }

        public override int CompareTo(Offset other)
        {
            if (other is NoOffset no)
            {
                return 0;
            }

            throw new InvalidOperationException($"Can't compare offset of type {GetType()} to offset of type {other.GetType()}");
        }

        public override string ToString() => "0";
    }

    /// <summary>
    /// Corresponds to a relative offset that begins the query at the <see cref="Count"/>-th event from the
    /// end of history (inclusive), e.g. "give me the last 10 events" for a tag or across all events.
    /// <para>
    /// Unlike <see cref="Sequence"/> and <see cref="TimeBasedUuid"/>, <see cref="FromEnd"/> is a query
    /// <b>input only</b>: it is never returned in an <see cref="EventEnvelope"/>. Each <see cref="EventEnvelope"/>
    /// still carries a concrete <see cref="Sequence"/> offset, so to resume a stream at a later point you use
    /// that absolute offset, not a <see cref="FromEnd"/> value. Because it is relative rather than absolute,
    /// <see cref="FromEnd"/> is not orderable and <see cref="CompareTo"/> always throws.
    /// </para>
    /// <para>
    /// The from-the-end position is resolved to a concrete start offset when the query is materialized, by
    /// reading how many matching events exist at that moment. For a <b>live</b> query (or any backend that
    /// resolves the count and reads the events in separate steps) this is best-effort: events written between
    /// resolving the count and reading the first batch may widen the initial window beyond <see cref="Count"/>.
    /// </para>
    /// <para>
    /// Not all read journals support this offset type. Implementations that cannot honor it will throw when it is
    /// supplied, the same way they reject any other unsupported offset type.
    /// </para>
    /// </summary>
    public sealed class FromEnd : Offset
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FromEnd"/> class.
        /// </summary>
        /// <param name="count">The number of events to read from the end of history. Must be greater than zero.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="count"/> is less than or equal to zero.</exception>
        public FromEnd(int count)
        {
            if (count <= 0)
                throw new ArgumentException("FromEnd offset count must be greater than zero.", nameof(count));

            Count = count;
        }

        /// <summary>
        /// The number of events to read from the end of history.
        /// </summary>
        public int Count { get; }

        private bool Equals(FromEnd other) => Count == other.Count;

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is FromEnd fromEnd && Equals(fromEnd);
        }

        public override int GetHashCode() => Count.GetHashCode();

        /// <summary>
        /// <see cref="FromEnd"/> is a relative, input-only offset and has no position in the stream, so it cannot
        /// be ordered against any offset (including another <see cref="FromEnd"/>). Always throws.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown.</exception>
        public override int CompareTo(Offset other)
            => throw new InvalidOperationException($"Offsets of type {GetType()} are relative inputs and cannot be compared.");

        public override string ToString() => $"FromEnd({Count})";
    }
}
