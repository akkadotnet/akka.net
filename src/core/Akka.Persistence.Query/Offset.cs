//-----------------------------------------------------------------------
// <copyright file="Offset.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Persistence.Query
{
    public abstract class Offset
    {
        public static Offset NoOffset() => Query.NoOffset.Instance;
        public static Offset Sequence(long value) => new Sequence(value);
        public static Offset TimeBasedGuid(Guid value) => new TimeBasedGuid(value);
    }

    public sealed class Sequence : Offset
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Sequence"/> class.
        /// </summary>
        public Sequence(long value)
        {
            Value = value;
        }

        public long Value { get; }

        private bool Equals(Sequence other) => Value == other.Value;

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Sequence && Equals((Sequence)obj);
        }

        public override int GetHashCode() => Value.GetHashCode();
    }

    public sealed class TimeBasedGuid : Offset
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TimeBasedGuid"/> class.
        /// </summary>
        public TimeBasedGuid(Guid value)
        {
            Value = value;
        }

        public Guid Value { get; }

        private bool Equals(TimeBasedGuid other) => Value.Equals(other.Value);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is TimeBasedGuid && Equals((TimeBasedGuid)obj);
        }

        public override int GetHashCode() => Value.GetHashCode();
    }

    public sealed class NoOffset : Offset
    {
        /// <summary>
        /// The singleton instance of <see cref="NoOffset"/>.
        /// </summary>
        public static NoOffset Instance { get; } = new NoOffset();
        private NoOffset() { }
    }
}