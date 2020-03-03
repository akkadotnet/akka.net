//-----------------------------------------------------------------------
// <copyright file="Reachability.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// Immutable data structure that holds the reachability status of subject nodes as seen
    /// from observer nodes. Failure detector for the subject nodes exist on the
    /// observer nodes. Changes (reachable, unreachable, terminated) are only performed
    /// by observer nodes to its own records. Each change bumps the version number of the
    /// record, and thereby it is always possible to determine which record is newest 
    /// merging two instances.
    ///
    /// Aggregated status of a subject node is defined as (in this order):
    /// - Terminated if any observer node considers it as Terminated
    /// - Unreachable if any observer node considers it as Unreachable
    /// - Reachable otherwise, i.e. no observer node considers it as Unreachable
    /// </summary>
    internal class Reachability //TODO: ISerializable?
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Reachability Empty = 
            new Reachability(ImmutableList.Create<Record>(), ImmutableDictionary.Create<UniqueAddress, long>());

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="records">TBD</param>
        /// <param name="versions">TBD</param>
        public Reachability(ImmutableList<Record> records, ImmutableDictionary<UniqueAddress, long> versions)
        {
            _cache = new Lazy<Cache>(() => new Cache(records));
            _versions = versions;
            _records = records;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Record
        {
            readonly UniqueAddress _observer;
            /// <summary>
            /// TBD
            /// </summary>
            public UniqueAddress Observer { get { return _observer; } }
            readonly UniqueAddress _subject;
            /// <summary>
            /// TBD
            /// </summary>
            public UniqueAddress Subject { get { return _subject; } }
            readonly ReachabilityStatus _status;
            /// <summary>
            /// TBD
            /// </summary>
            public ReachabilityStatus Status { get { return _status; } }
            readonly long _version;
            /// <summary>
            /// TBD
            /// </summary>
            public long Version { get { return _version; } }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="observer">TBD</param>
            /// <param name="subject">TBD</param>
            /// <param name="status">TBD</param>
            /// <param name="version">TBD</param>
            public Record(UniqueAddress observer, UniqueAddress subject, ReachabilityStatus status, long version)
            {
                _observer = observer;
                _subject = subject;
                _status = status;
                _version = version;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as Record;
                if (other == null) return false;
                return _version.Equals(other._version) &&
                       _status == other.Status &&
                       _observer.Equals(other._observer) &&
                       _subject.Equals(other._subject);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (Observer != null ? Observer.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ Version.GetHashCode();
                    hashCode = (hashCode * 397) ^ Status.GetHashCode();
                    hashCode = (hashCode * 397) ^ (Subject != null ? Subject.GetHashCode() : 0);
                    return hashCode;
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public enum ReachabilityStatus
        {
            /// <summary>
            /// TBD
            /// </summary>
            Reachable,
            /// <summary>
            /// TBD
            /// </summary>
            Unreachable,
            /// <summary>
            /// TBD
            /// </summary>
            Terminated
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableList<Record> Records { get { return _records; } }
        readonly ImmutableList<Record> _records;

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableDictionary<UniqueAddress, long> Versions { get { return _versions; } }
        readonly ImmutableDictionary<UniqueAddress, long> _versions;

        /// <summary>
        /// TBD
        /// </summary>
        class Cache
        {
            /// <summary>
            /// TBD
            /// </summary>
            public ImmutableDictionary<UniqueAddress, ImmutableDictionary<UniqueAddress, Record>> ObserverRowMap { get { return _observerRowsMap; } }
            readonly ImmutableDictionary<UniqueAddress, ImmutableDictionary<UniqueAddress, Record>> _observerRowsMap;

            /// <summary>
            /// TBD
            /// </summary>
            public ImmutableHashSet<UniqueAddress> AllTerminated { get { return _allTerminated; } }
            readonly ImmutableHashSet<UniqueAddress> _allTerminated;

            /// <summary>
            /// TBD
            /// </summary>
            public ImmutableHashSet<UniqueAddress> AllUnreachable { get { return _allUnreachable; } }
            readonly ImmutableHashSet<UniqueAddress> _allUnreachable;

            /// <summary>
            /// TBD
            /// </summary>
            public ImmutableHashSet<UniqueAddress> AllUnreachableOrTerminated { get { return _allUnreachableOrTerminated; } }
            readonly ImmutableHashSet<UniqueAddress> _allUnreachableOrTerminated;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="records">TBD</param>
            public Cache(ImmutableList<Record> records)
            {
                if (records.IsEmpty)
                {
                    _observerRowsMap = ImmutableDictionary.Create<UniqueAddress, ImmutableDictionary<UniqueAddress, Record>>();
                    _allTerminated = ImmutableHashSet.Create<UniqueAddress>();
                    _allUnreachable = ImmutableHashSet.Create<UniqueAddress>();
                }
                else
                {
                    var mapBuilder = new Dictionary<UniqueAddress, ImmutableDictionary<UniqueAddress, Record>>();
                    var terminatedBuilder = ImmutableHashSet.CreateBuilder<UniqueAddress>();
                    var unreachableBuilder = ImmutableHashSet.CreateBuilder<UniqueAddress>();

                    foreach (var r in records)
                    {
                        ImmutableDictionary<UniqueAddress, Record> m = mapBuilder.TryGetValue(r.Observer, out m) 
                            ? m.SetItem(r.Subject, r) 
                            //TODO: Other collections take items for Create. Create unnecessary array here
                            : ImmutableDictionary.CreateRange(new[] { new KeyValuePair<UniqueAddress, Record>(r.Subject, r) });
                        

                        mapBuilder.AddOrSet(r.Observer, m);

                        if (r.Status == ReachabilityStatus.Unreachable) unreachableBuilder.Add(r.Subject);
                        else if (r.Status == ReachabilityStatus.Terminated) terminatedBuilder.Add(r.Subject);
                    }

                    _observerRowsMap = ImmutableDictionary.CreateRange(mapBuilder);
                    _allTerminated = terminatedBuilder.ToImmutable();
                    _allUnreachable = unreachableBuilder.ToImmutable().Except(AllTerminated);
                }

                _allUnreachableOrTerminated = _allTerminated.IsEmpty
                    ? AllUnreachable
                    : AllUnreachable.Union(AllTerminated);
            }
        }

        //TODO: Serialization should ignore
        readonly Lazy<Cache> _cache;

        ImmutableDictionary<UniqueAddress, Record> ObserverRows(UniqueAddress observer)
        {
            _cache.Value.ObserverRowMap.TryGetValue(observer, out var observerRows);
            return observerRows;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="observer">TBD</param>
        /// <param name="subject">TBD</param>
        /// <returns>TBD</returns>
        public Reachability Unreachable(UniqueAddress observer, UniqueAddress subject)
        {
            return Change(observer, subject, ReachabilityStatus.Unreachable);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="observer">TBD</param>
        /// <param name="subject">TBD</param>
        /// <returns>TBD</returns>
        public Reachability Reachable(UniqueAddress observer, UniqueAddress subject)
        {
            return Change(observer, subject, ReachabilityStatus.Reachable);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="observer">TBD</param>
        /// <param name="subject">TBD</param>
        /// <returns>TBD</returns>
        public Reachability Terminated(UniqueAddress observer, UniqueAddress subject)
        {
            return Change(observer, subject, ReachabilityStatus.Terminated);
        }

        long CurrentVersion(UniqueAddress observer)
        {
            return _versions.TryGetValue(observer, out long version) ? version : 0;
        }

        long NextVersion(UniqueAddress observer)
        {
            return CurrentVersion(observer) + 1;
        }

        private Reachability Change(UniqueAddress observer, UniqueAddress subject, ReachabilityStatus status)
        {
            var v = NextVersion(observer);
            var newVersions = _versions.SetItem(observer, v);
            var newRecord = new Record(observer, subject, status, v);
            var oldObserverRows = ObserverRows(observer);
            if (oldObserverRows == null && status == ReachabilityStatus.Reachable) return this;
            if (oldObserverRows == null) return new Reachability(_records.Add(newRecord), newVersions);

            if(!oldObserverRows.TryGetValue(subject, out var oldRecord))
            {
                if (status == ReachabilityStatus.Reachable &&
                    oldObserverRows.Values.All(r => r.Status == ReachabilityStatus.Reachable))
                    return new Reachability(_records.FindAll(r => !r.Observer.Equals(observer)), newVersions);
                return new Reachability(_records.Add(newRecord), newVersions);
            }

            if (oldRecord.Status == ReachabilityStatus.Terminated || oldRecord.Status == status)
                return this;

            if(status == ReachabilityStatus.Reachable && 
                oldObserverRows.Values.All(r => r.Status == ReachabilityStatus.Reachable || r.Subject.Equals(subject)))
                return new Reachability(_records.FindAll(r => !r.Observer.Equals(observer)), newVersions);

            var newRecords = _records.SetItem(_records.IndexOf(oldRecord), newRecord);
            return new Reachability(newRecords, newVersions);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="allowed">TBD</param>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public Reachability Merge(IEnumerable<UniqueAddress> allowed, Reachability other)
        {
            var recordBuilder = ImmutableList.CreateBuilder<Record>();
            //TODO: Size hint somehow?
            var newVersions = _versions;
            foreach (var observer in allowed)
            {
                var observerVersion1 = CurrentVersion(observer);
                var observerVersion2 = other.CurrentVersion(observer);

                var rows1 = ObserverRows(observer);
                var rows2 = other.ObserverRows(observer);

                if (rows1 != null && rows2 != null)
                {
                    var rows = observerVersion1 > observerVersion2 ? rows1 : rows2;
                    foreach(var record in rows.Values.Where(r => allowed.Contains(r.Subject)))
                        recordBuilder.Add(record);
                }
                if (rows1 != null && rows2 == null)
                {
                    if(observerVersion1 > observerVersion2)
                        foreach (var record in rows1.Values.Where(r => allowed.Contains(r.Subject)))
                            recordBuilder.Add(record);
                }
                if (rows1 == null && rows2 != null)
                {
                    if (observerVersion2 > observerVersion1)
                        foreach (var record in rows2.Values.Where(r => allowed.Contains(r.Subject)))
                           recordBuilder.Add(record);
                }

                if (observerVersion2 > observerVersion1)
                    newVersions = newVersions.SetItem(observer, observerVersion2);
            }

            newVersions = ImmutableDictionary.CreateRange(newVersions.Where(p => allowed.Contains(p.Key)));

            return new Reachability(recordBuilder.ToImmutable(), newVersions);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="nodes">TBD</param>
        /// <returns>TBD</returns>
        public Reachability Remove(IEnumerable<UniqueAddress> nodes)
        {
            var nodesSet = nodes.ToImmutableHashSet();
            var newRecords = _records.FindAll(r => !nodesSet.Contains(r.Observer) && !nodesSet.Contains(r.Subject));
            var newVersions = _versions.RemoveRange(nodes);
            return new Reachability(newRecords, newVersions);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="nodes">TBD</param>
        /// <returns>TBD</returns>
        public Reachability RemoveObservers(ImmutableHashSet<UniqueAddress> nodes)
        {
            if (nodes.Count == 0)
            {
                return this;
            }
            else
            {
                var newRecords = _records.FindAll(r => !nodes.Contains(r.Observer));
                var newVersions = _versions.RemoveRange(nodes);
                return new Reachability(newRecords, newVersions);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="observer">TBD</param>
        /// <param name="subject">TBD</param>
        /// <returns>TBD</returns>
        public ReachabilityStatus Status(UniqueAddress observer, UniqueAddress subject)
        {
            var observerRows = ObserverRows(observer);
            if (observerRows == null) return ReachabilityStatus.Reachable;

            if(!observerRows.TryGetValue(subject, out var record))
                return ReachabilityStatus.Reachable;

            return record.Status;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public ReachabilityStatus Status(UniqueAddress node)
        {
            if (_cache.Value.AllTerminated.Contains(node)) return ReachabilityStatus.Terminated;
            if (_cache.Value.AllUnreachable.Contains(node)) return ReachabilityStatus.Unreachable;
            return ReachabilityStatus.Reachable;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public bool IsReachable(UniqueAddress node)
        {
            return IsAllReachable || !AllUnreachableOrTerminated.Contains(node);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="observer">TBD</param>
        /// <param name="subject">TBD</param>
        /// <returns>TBD</returns>
        public bool IsReachable(UniqueAddress observer, UniqueAddress subject)
        {
            return Status(observer, subject) == ReachabilityStatus.Reachable;
        }

        /*
         *  def isReachable(observer: UniqueAddress, subject: UniqueAddress): Boolean =
            status(observer, subject) == Reachable
         */

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsAllReachable
        {
            get { return _records.IsEmpty; } 
        }

        /// <summary>
        /// Doesn't include terminated
        /// </summary>
        public ImmutableHashSet<UniqueAddress> AllUnreachable
        {
            get { return _cache.Value.AllUnreachable; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableHashSet<UniqueAddress> AllUnreachableOrTerminated
        {
            get { return _cache.Value.AllUnreachableOrTerminated; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="observer">TBD</param>
        /// <returns>TBD</returns>
        public ImmutableHashSet<UniqueAddress> AllUnreachableFrom(UniqueAddress observer)
        {
            var observerRows = ObserverRows(observer);
            if (observerRows == null) return ImmutableHashSet.Create<UniqueAddress>();
            return
                ImmutableHashSet.CreateRange(
                    observerRows.Where(p => p.Value.Status == ReachabilityStatus.Unreachable).Select(p => p.Key));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableDictionary<UniqueAddress, ImmutableHashSet<UniqueAddress>> ObserversGroupedByUnreachable
        {
            get
            {
                var builder = new Dictionary<UniqueAddress, ImmutableHashSet<UniqueAddress>>();

                var grouped = _records.GroupBy(p => p.Subject);
                foreach (var records in grouped)
                {
                    if (records.Any(r => r.Status == ReachabilityStatus.Unreachable))
                    {
                        builder.Add(records.Key, records.Where(r => r.Status == ReachabilityStatus.Unreachable)
                            .Select(r => r.Observer).ToImmutableHashSet());
                    }
                }
                return builder.ToImmutableDictionary();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableHashSet<UniqueAddress> AllObservers
        {
            get { return ImmutableHashSet.CreateRange(_versions.Keys); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="observer">TBD</param>
        /// <returns>TBD</returns>
        public ImmutableList<Record> RecordsFrom(UniqueAddress observer)
        {
            var rows = ObserverRows(observer);
            if (rows == null) return ImmutableList.Create<Record>();
            return rows.Values.ToImmutableList();
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return _versions.GetHashCode();
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            var other = obj as Reachability;
            if (other == null) return false;
            return _records.Count == other._records.Count && 
                _versions.Equals(other._versions) && 
                _cache.Value.ObserverRowMap.Equals(other._cache.Value.ObserverRowMap);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var builder = new StringBuilder("Reachability(");

            foreach (var observer in _versions.Keys)
            {
                var rows = ObserverRows(observer);
                if (rows == null) continue;

                builder.AppendJoin(", ", rows, (b, row, index) =>
                    b.AppendFormat("[{0} -> {1}: {2} [{3}] ({4})]",
                        observer.Address, row.Key, row.Value.Status, Status(row.Key), row.Value.Version));
            }

            return builder.Append(')').ToString();
        }
    }
}
