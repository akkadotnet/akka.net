//-----------------------------------------------------------------------
// <copyright file="Hints.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Persistence.Sql.Common.Journal;

namespace Akka.Persistence.Sql.Common.Queries
{
    /// <summary>
    /// TBD
    /// </summary>
    [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
    public interface IHint { }

    /// <summary>
    /// TBD
    /// </summary>
    [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
    public static class Hints
    {
        /// <summary>
        /// Returns a hint that expects a reply with events with matching manifest.
        /// </summary>
        /// <param name="manifest">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
        public static IHint Manifest(string manifest)
        {
            return new WithManifest(manifest);
        }

        /// <summary>
        /// Returns a hint that expects a reply with events from provided set of persistence ids.
        /// </summary>
        /// <param name="persistenceIds">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
        public static IHint PersistenceIds(IEnumerable<string> persistenceIds)
        {
            return new PersistenceIdRange(persistenceIds);
        }

        /// <summary>
        /// Returns a hint that expects a reply with events, that have timestamp value before provided date.
        /// </summary>
        /// <param name="to">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
        public static IHint TimestampBefore(DateTime to)
        {
            return new TimestampRange(null, to.Ticks);
        }

        /// <summary>
        /// Returns a hint that expects a reply with events, that have timestamp value after or equal provided date.
        /// </summary>
        /// <param name="from">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
        public static IHint TimestampAfter(DateTime from)
        {
            return new TimestampRange(from.Ticks, null);
        }

        /// <summary>
        /// Returns a hint that expects a reply with events, that have timestamp from between provided range of values (left side inclusive).
        /// </summary>
        /// <param name="from">TBD</param>
        /// <param name="to">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
        public static IHint TimestampBetween(DateTime from, DateTime to)
        {
            return new TimestampRange(from.Ticks, to.Ticks);
        }

        /// <summary>
        /// Returns a hint that expects a reply with events, that have timestamp value before provided date.
        /// </summary>
        /// <param name="to">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
        public static IHint TimestampBefore(long to)
        {
            return new TimestampRange(null, to);
        }

        /// <summary>
        /// Returns a hint that expects a reply with events, that have timestamp value after or equal provided date.
        /// </summary>
        /// <param name="from">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
        public static IHint TimestampAfter(long from)
        {
            return new TimestampRange(from, null);
        }

        /// <summary>
        /// Returns a hint that expects a reply with events, that have timestamp from between provided range of values (left side inclusive).
        /// </summary>
        /// <param name="from">TBD</param>
        /// <param name="to">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
        public static IHint TimestampBetween(long from, long to)
        {
            return new TimestampRange(from, to);
        }
    }

    /// <summary>
    /// Hint for the SQL journal used to filter journal entries returned in the response based on the manifest.
    /// </summary>
    [Serializable]
    [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
    public sealed class WithManifest : IHint, IEquatable<WithManifest>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string Manifest;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="manifest">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public WithManifest(string manifest)
        {
            if (string.IsNullOrEmpty(manifest)) throw new ArgumentException("Hint expected manifest, but none has been provided", "manifest");
            Manifest = manifest;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(WithManifest other)
        {
            return other != null && other.Manifest.Equals(Manifest);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as WithManifest);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            return (Manifest != null ? Manifest.GetHashCode() : 0);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return string.Format("WithManifest<manifest: {0}>", Manifest);
        }
    }

    /// <summary>
    /// Hint for the SQL journal used to filter journal entries returned in the response based on set of perisistence ids provided.
    /// </summary>
    [Serializable]
    [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
    public sealed class PersistenceIdRange : IHint, IEquatable<PersistenceIdRange>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly ISet<string> PersistenceIds;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceIds">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public PersistenceIdRange(IEnumerable<string> persistenceIds)
        {
            if (persistenceIds == null) throw new ArgumentException("Hint expected persistence ids, but none has been provided", "persistenceIds");
            PersistenceIds = new HashSet<string>(persistenceIds);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(PersistenceIdRange other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return other.PersistenceIds.SetEquals(PersistenceIds);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as PersistenceIdRange);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            return (PersistenceIds != null ? PersistenceIds.GetHashCode() : 0);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return string.Format("PersistenceIdRange<pids: [{0}]>", string.Join(", ", PersistenceIds));
        }
    }

    /// <summary>
    /// Hint for the SQL journal used to filter journal entries returned in the response based on their timestamp range.
    /// Desired behavior of timestamp range is &lt;from, to) - left side inclusive, right side exclusive.
    /// Timestamp is generated by <see cref="JournalDbEngine.GenerateTimestamp"/> method, which may be overloaded.
    /// </summary>
    [Serializable]
    [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
    public sealed class TimestampRange : IHint, IEquatable<TimestampRange>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly long? From;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly long? To;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="from">TBD</param>
        /// <param name="to">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public TimestampRange(long? @from, long? to)
        {
            if (!from.HasValue && !to.HasValue)
                throw new ArgumentException("TimestampRange hint requires either 'From' or 'To' or both range limiters provided");

            if (from.HasValue && to.HasValue && from > to)
                throw new ArgumentException("TimestampRange hint requires 'From' date to occur before 'To' date");

            From = @from;
            To = to;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(TimestampRange other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(From, other.From) && Equals(To, other.To);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as TimestampRange);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return (From.GetHashCode() * 397) ^ To.GetHashCode();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return string.Format("TimestampRange<from: {0}, to: {1}>",
                From.HasValue ? From.Value.ToString() : "undefined",
                To.HasValue ? To.Value.ToString() : "undefined");
        }
    }
}