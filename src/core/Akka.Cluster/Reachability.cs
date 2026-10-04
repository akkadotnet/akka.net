//-----------------------------------------------------------------------
// <copyright file="Reachability.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Util.Internal;

namespace Akka.Cluster
{
    /// <summary>
    ///     Immutable data structure that holds the reachability status of subject nodes as seen
    ///     from observer nodes. Failure detector for the subject nodes exist on the
    ///     observer nodes. Changes (reachable, unreachable, terminated) are only performed
    ///     by observer nodes to its own records. Each change bumps the version number of the
    ///     record, and thereby it is always possible to determine which record is newest
    ///     merging two instances.
    ///     Aggregated status of a subject node is defined as (in this order):
    ///     - Terminated if any observer node considers it as Terminated
    ///     - Unreachable if any observer node considers it as Unreachable
    ///     - Reachable otherwise, i.e. no observer node considers it as Unreachable
    /// </summary>
    internal class Reachability
    {
        /// <summary>
    ///     Reachability observations indexed by observer and subject. Observer records are versioned so merges can retain the newer row.
        /// </summary>
        public enum ReachabilityStatus
        {
            /// <summary>
            ///     No observer currently records this subject as unreachable or terminated.
            /// </summary>
            Reachable,

            /// <summary>
            ///     At least one observer records this subject as unreachable.
            /// </summary>
            Unreachable,

            /// <summary>
            ///     At least one observer records this subject as terminated; this status takes precedence over unreachable.
            /// </summary>
            Terminated
        }

        /// <summary>
        ///     An empty table with no observer versions or reachability records.
        /// </summary>
        public static readonly Reachability Empty = new(ImmutableList.Create<Record>(), ImmutableDictionary.Create<UniqueAddress, long>());

        private readonly Lazy<Cache> _cache;

        /// <summary>
        ///     Creates a reachability table from observer records and their version counters.
        /// </summary>
        /// <param name="records">The observer-subject status records.</param>
        /// <param name="versions">The latest version counter for each observer.</param>
        public Reachability(ImmutableList<Record> records, ImmutableDictionary<UniqueAddress, long> versions)
        {
            _cache = new Lazy<Cache>(() => new Cache(records));
            Versions = versions;
            Records = records;
        }

        /// <summary>
        ///     The observer-subject reachability records.
        /// </summary>
        public ImmutableList<Record> Records { get; }

        /// <summary>
        ///     The latest record version for each observer.
        /// </summary>
        public ImmutableDictionary<UniqueAddress, long> Versions { get; }

        /// <summary>
        ///     Whether there are no records; in this state every subject is considered reachable.
        /// </summary>
        public bool IsAllReachable => Records.IsEmpty;

        /// <summary>
        ///     Doesn't include terminated
        /// </summary>
        public ImmutableHashSet<UniqueAddress> AllUnreachable => _cache.Value.AllUnreachable;

        /// <summary>
        ///     Subjects that at least one observer considers unreachable or terminated.
        /// </summary>
        public ImmutableHashSet<UniqueAddress> AllUnreachableOrTerminated => _cache.Value.AllUnreachableOrTerminated;

        /// <summary>
        ///     For each unreachable subject, the observers that report it as unreachable.
        /// </summary>
        public ImmutableDictionary<UniqueAddress, ImmutableHashSet<UniqueAddress>> ObserversGroupedByUnreachable
        {
            get
            {
                var builder = new Dictionary<UniqueAddress, ImmutableHashSet<UniqueAddress>>();

                var grouped = Records.GroupBy(p => p.Subject);
                foreach (var records in grouped)
                    if (records.Any(r => r.Status == ReachabilityStatus.Unreachable))
                        builder.Add(records.Key, records.Where(r => r.Status == ReachabilityStatus.Unreachable)
                            .Select(r => r.Observer).ToImmutableHashSet());
                return builder.ToImmutableDictionary();
            }
        }

        /// <summary>
        ///     The observers represented by the current records.
        /// </summary>
        public ImmutableHashSet<UniqueAddress> AllObservers => Records.Select(i => i.Observer).ToImmutableHashSet();

        private ImmutableDictionary<UniqueAddress, Record> ObserverRows(UniqueAddress observer)
        {
            _cache.Value.ObserverRowMap.TryGetValue(observer, out var observerRows);
            return observerRows;
        }

        /// <summary>
        ///     Records that this observer considers unreachable.
        /// </summary>
        /// <param name="observer">The node reporting the observation.</param>
        /// <param name="subject">The node observed as unreachable.</param>
        /// <returns>The table with this observer's record updated, or the current table if the record is already unreachable or terminal.</returns>
        public Reachability Unreachable(UniqueAddress observer, UniqueAddress subject)
        {
            return Change(observer, subject, ReachabilityStatus.Unreachable);
        }

        /// <summary>
        ///     Records that this observer considers the subject reachable.
        /// </summary>
        /// <param name="observer">The node reporting the observation.</param>
        /// <param name="subject">The node observed as reachable.</param>
        /// <returns>A table with the observer's reachable observation applied, or the current table when no record change is needed.</returns>
        public Reachability Reachable(UniqueAddress observer, UniqueAddress subject)
        {
            return Change(observer, subject, ReachabilityStatus.Reachable);
        }

        /// <summary>
        ///     Records that this observer considers the subject terminated.
        /// </summary>
        /// <param name="observer">The node reporting the observation.</param>
        /// <param name="subject">The node observed as terminated.</param>
        /// <returns>The table with this observer's terminated observation applied, or the current table if the subject is already terminal.</returns>
        public Reachability Terminated(UniqueAddress observer, UniqueAddress subject)
        {
            return Change(observer, subject, ReachabilityStatus.Terminated);
        }

        private long CurrentVersion(UniqueAddress observer)
        {
            return Versions.TryGetValue(observer, out var version) ? version : 0;
        }

        private long NextVersion(UniqueAddress observer)
        {
            return CurrentVersion(observer) + 1;
        }

        private Reachability Change(UniqueAddress observer, UniqueAddress subject, ReachabilityStatus status)
        {
            var v = NextVersion(observer);
            var newVersions = Versions.SetItem(observer, v);
            var newRecord = new Record(observer, subject, status, v);
            var oldObserverRows = ObserverRows(observer);

            // don't record Reachable observation if nothing has been noted so far
            if (oldObserverRows == null && status == ReachabilityStatus.Reachable) return this;

            // otherwise, create new instance including this first observation
            if (oldObserverRows == null) return new Reachability(Records.Add(newRecord), newVersions);

            if (!oldObserverRows.TryGetValue(subject, out var oldRecord))
            {
                if (status == ReachabilityStatus.Reachable &&
                    oldObserverRows.Values.All(r => r.Status == ReachabilityStatus.Reachable))
                    return new Reachability(Records.FindAll(r => !r.Observer.Equals(observer)), newVersions);
                return new Reachability(Records.Add(newRecord), newVersions);
            }

            if (oldRecord.Status == ReachabilityStatus.Terminated || oldRecord.Status == status)
                return this;

            if (status == ReachabilityStatus.Reachable &&
                oldObserverRows.Values.All(r => r.Status == ReachabilityStatus.Reachable || r.Subject.Equals(subject)))
                return new Reachability(Records.FindAll(r => !r.Observer.Equals(observer)), newVersions);

            var newRecords = Records.SetItem(Records.IndexOf(oldRecord), newRecord);
            return new Reachability(newRecords, newVersions);
        }

        /// <summary>
        ///     Merges observer rows from another table, keeping only observers and subjects in <paramref name="allowed"/>.
        /// </summary>
        /// <param name="allowed">The node identities permitted to remain as observers or subjects.</param>
        /// <param name="other">The reachability table to merge with this one.</param>
        /// <returns>A new table containing the selected observer rows and version counters.</returns>
        public Reachability Merge(IImmutableSet<UniqueAddress> allowed, Reachability other)
        {
            var recordBuilder = ImmutableList.CreateBuilder<Record>();
            //TODO: Size hint somehow?
            var newVersions = Versions;
            foreach (var observer in allowed)
            {
                var observerVersion1 = CurrentVersion(observer);
                var observerVersion2 = other.CurrentVersion(observer);

                var rows1 = ObserverRows(observer);
                var rows2 = other.ObserverRows(observer);

                if (rows1 != null && rows2 != null)
                {
                    var rows = observerVersion1 > observerVersion2 ? rows1 : rows2;
                    foreach (var record in rows.Values.Where(r => allowed.Contains(r.Subject)))
                        recordBuilder.Add(record);
                }

                if (rows1 != null && rows2 == null)
                    if (observerVersion1 > observerVersion2)
                        foreach (var record in rows1.Values.Where(r => allowed.Contains(r.Subject)))
                            recordBuilder.Add(record);
                if (rows1 == null && rows2 != null)
                    if (observerVersion2 > observerVersion1)
                        foreach (var record in rows2.Values.Where(r => allowed.Contains(r.Subject)))
                            recordBuilder.Add(record);

                if (observerVersion2 > observerVersion1)
                    newVersions = newVersions.SetItem(observer, observerVersion2);
            }

            newVersions = ImmutableDictionary.CreateRange(newVersions.Where(p => allowed.Contains(p.Key)));

            return new Reachability(recordBuilder.ToImmutable(), newVersions);
        }

        /// <summary>
        ///     Removes every supplied node from observer rows, subject records, and version counters.
        /// </summary>
        /// <param name="nodes">The nodes to remove as observers and subjects.</param>
        /// <returns>A table without records or version counters for the supplied nodes.</returns>
        public Reachability Remove(IEnumerable<UniqueAddress> nodes)
        {
            var nodesSet = nodes.ToImmutableHashSet();
            var newRecords = Records.FindAll(r => !nodesSet.Contains(r.Observer) && !nodesSet.Contains(r.Subject));
            var newVersions = Versions.RemoveRange(nodes);
            return new Reachability(newRecords, newVersions);
        }

        /// <summary>
        ///     Removes records and version counters for the supplied observers while retaining them as subjects where present.
        /// </summary>
        /// <param name="nodes">The observers whose rows and version counters should be removed.</param>
        /// <returns>The current table if no observers are supplied; otherwise, a table with those observer rows removed.</returns>
        public Reachability RemoveObservers(ImmutableHashSet<UniqueAddress> nodes)
        {
            if (nodes.Count == 0)
            {
                return this;
            }

            var newRecords = Records.FindAll(r => !nodes.Contains(r.Observer));
            var newVersions = Versions.RemoveRange(nodes);
            return new Reachability(newRecords, newVersions);
        }

        public Reachability FilterRecords(Predicate<Record> f)
        {
            return new Reachability(Records.FindAll(f), Versions);
        }

        /// <summary>
        ///     Returns this observer's status for a subject, defaulting to reachable when no record exists.
        /// </summary>
        /// <param name="observer">The node whose observation is queried.</param>
        /// <param name="subject">The node whose observed status is queried.</param>
        /// <returns>The status this observer records for the subject, or <see cref="ReachabilityStatus.Reachable"/> if no record exists.</returns>
        public ReachabilityStatus Status(UniqueAddress observer, UniqueAddress subject)
        {
            var observerRows = ObserverRows(observer);
            if (observerRows == null) return ReachabilityStatus.Reachable;

            if (!observerRows.TryGetValue(subject, out var record))
                return ReachabilityStatus.Reachable;

            return record.Status;
        }

        /// <summary>
        ///     Returns the aggregate status of a subject across all observers, with terminated taking precedence over unreachable.
        /// </summary>
        /// <param name="node">The node whose aggregate status is queried.</param>
        /// <returns>The aggregate status across observers: terminated if any observer reports termination, unreachable if any reports unreachable, otherwise reachable.</returns>
        public ReachabilityStatus Status(UniqueAddress node)
        {
            if (_cache.Value.AllTerminated.Contains(node)) return ReachabilityStatus.Terminated;
            if (_cache.Value.AllUnreachable.Contains(node)) return ReachabilityStatus.Unreachable;
            return ReachabilityStatus.Reachable;
        }

        /// <summary>
        ///     Checks whether a node is not in the aggregate unreachable or terminated sets.
        /// </summary>
        /// <param name="node">The node to check.</param>
        /// <returns><c>true</c> if the aggregate status is <see cref="ReachabilityStatus.Reachable"/>; otherwise, <c>false</c>.</returns>
        public bool IsReachable(UniqueAddress node)
        {
            return IsAllReachable || !AllUnreachableOrTerminated.Contains(node);
        }

        /// <summary>
        ///     Checks whether one observer's record for a subject is reachable.
        /// </summary>
        /// <param name="observer">The node whose observation is checked.</param>
        /// <param name="subject">The node whose observed status is checked.</param>
        /// <returns><c>true</c> if this observer's status for the subject is <see cref="ReachabilityStatus.Reachable"/>; otherwise, <c>false</c>.</returns>
        public bool IsReachable(UniqueAddress observer, UniqueAddress subject)
        {
            return Status(observer, subject) == ReachabilityStatus.Reachable;
        }

        /// <summary>
        ///     Returns the subjects this observer currently records as unreachable.
        /// </summary>
        /// <param name="observer">The observer whose unreachable subjects are requested.</param>
        /// <returns>The subjects this observer records as unreachable, or an empty set if it has no records.</returns>
        public ImmutableHashSet<UniqueAddress> AllUnreachableFrom(UniqueAddress observer)
        {
            var observerRows = ObserverRows(observer);
            if (observerRows == null) return ImmutableHashSet<UniqueAddress>.Empty;
            return
                ImmutableHashSet.CreateRange(
                    observerRows.Where(p => p.Value.Status == ReachabilityStatus.Unreachable).Select(p => p.Key));
        }

        /// <summary>
        ///     Returns the reachability records reported by this observer.
        /// </summary>
        /// <param name="observer">The observer whose records are requested.</param>
        /// <returns>The records reported by this observer, or an empty list if it has no records.</returns>
        public ImmutableList<Record> RecordsFrom(UniqueAddress observer)
        {
            var rows = ObserverRows(observer);
            if (rows == null) return ImmutableList<Record>.Empty;
            return rows.Values.ToImmutableList();
        }

        /// only used for testing
        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Versions.GetHashCode();
        }

        /// only used for testing
        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is not Reachability other) return false;
            return Records.Count == other.Records.Count &&
                   Versions.Equals(other.Versions) &&
                   _cache.Value.ObserverRowMap.Equals(other._cache.Value.ObserverRowMap);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var builder = new StringBuilder("Reachability(");

            foreach (var observer in Versions.Keys)
            {
                var rows = ObserverRows(observer);
                if (rows == null) continue;

                builder.AppendJoin(", ", rows, (b, row, _) =>
                    b.AppendFormat("[{0} -> {1}: {2} [{3}] ({4})]",
                        observer.Address, row.Key, row.Value.Status, Status(row.Key), row.Value.Version));
            }

            return builder.Append(')').ToString();
        }

        /// <summary>
        ///     One observer's versioned status observation of a subject node.
        /// </summary>
        public sealed class Record
        {
            /// <summary>
            ///     Creates a record for one observer's status observation of a subject.
            /// </summary>
            /// <param name="observer">The node that made the observation.</param>
            /// <param name="subject">The node whose status was observed.</param>
            /// <param name="status">The status reported by the observer.</param>
            /// <param name="version">The observer's record version.</param>
            public Record(UniqueAddress observer, UniqueAddress subject, ReachabilityStatus status, long version)
            {
                Observer = observer;
                Subject = subject;
                Status = status;
                Version = version;
            }

            /// <summary>
            ///     The node that made this observation.
            /// </summary>
            public UniqueAddress Observer { get; }

            /// <summary>
            ///     The node whose status was observed.
            /// </summary>
            public UniqueAddress Subject { get; }

            /// <summary>
            ///     The status reported by the observer.
            /// </summary>
            public ReachabilityStatus Status { get; }

            /// <summary>
            ///     The version assigned by the observer to this record.
            /// </summary>
            public long Version { get; }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                var other = obj as Record;
                if (other == null) return false;
                return Version.Equals(other.Version) &&
                       Status == other.Status &&
                       Observer.Equals(other.Observer) &&
                       Subject.Equals(other.Subject);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = Observer != null ? Observer.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ Version.GetHashCode();
                    hashCode = (hashCode * 397) ^ Status.GetHashCode();
                    hashCode = (hashCode * 397) ^ (Subject != null ? Subject.GetHashCode() : 0);
                    return hashCode;
                }
            }
        }

        /// <summary>
        ///     Lazily derived lookup maps and aggregate status sets for a reachability table.
        /// </summary>
        private class Cache
        {
            /// <summary>
            ///     Builds cached observer rows and aggregate subject-status sets from the records.
            /// </summary>
            /// <param name="records">The records to index and aggregate.</param>
            public Cache(ImmutableList<Record> records)
            {
                if (records.IsEmpty)
                {
                    ObserverRowMap = ImmutableDictionary<UniqueAddress, ImmutableDictionary<UniqueAddress, Record>>
                        .Empty;
                    AllTerminated = ImmutableHashSet<UniqueAddress>.Empty;
                    AllUnreachable = ImmutableHashSet<UniqueAddress>.Empty;
                }
                else
                {
                    var mapBuilder = new Dictionary<UniqueAddress, ImmutableDictionary<UniqueAddress, Record>>();
                    var terminatedBuilder = ImmutableHashSet.CreateBuilder<UniqueAddress>();
                    var unreachableBuilder = ImmutableHashSet.CreateBuilder<UniqueAddress>();

                    foreach (var r in records)
                    {
                        ImmutableDictionary<UniqueAddress, Record> m = mapBuilder.TryGetValue(r.Observer, out var mR)
                            ? mR.SetItem(r.Subject, r)
                            : ImmutableDictionary<UniqueAddress, Record>.Empty.Add(r.Subject, r);


                        mapBuilder[r.Observer] = m;

                        if (r.Status == ReachabilityStatus.Unreachable) unreachableBuilder.Add(r.Subject);
                        else if (r.Status == ReachabilityStatus.Terminated) terminatedBuilder.Add(r.Subject);
                    }

                    ObserverRowMap = ImmutableDictionary.CreateRange(mapBuilder);
                    AllTerminated = terminatedBuilder.ToImmutable();
                    AllUnreachable = unreachableBuilder.ToImmutable().Except(AllTerminated);
                }

                AllUnreachableOrTerminated = AllTerminated.IsEmpty
                    ? AllUnreachable
                    : AllUnreachable.Union(AllTerminated);
            }

            /// <summary>
            ///     The records grouped by observer and then by subject.
            /// </summary>
            public ImmutableDictionary<UniqueAddress, ImmutableDictionary<UniqueAddress, Record>> ObserverRowMap
            {
                get;
            }

            /// <summary>
            /// Contains all nodes that have been observed as Terminated by at least one other node.
            /// </summary>
            public ImmutableHashSet<UniqueAddress> AllTerminated { get; }

            /// <summary>
            ///  Contains all nodes that have been observed as Unreachable by at least one other node.
            /// </summary>
            public ImmutableHashSet<UniqueAddress> AllUnreachable { get; }

            /// <summary>
            ///     Subjects observed as either unreachable or terminated.
            /// </summary>
            public ImmutableHashSet<UniqueAddress> AllUnreachableOrTerminated { get; }
        }
    }
}
