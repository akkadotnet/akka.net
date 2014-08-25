using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

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
    class Reachability //TODO: ISerializable?
    {
        public static readonly Reachability Empty = 
            new Reachability(ImmutableList.Create<Record>(), ImmutableDictionary.Create<UniqueAddress, long>());

        public Reachability(ImmutableList<Record> records, ImmutableDictionary<UniqueAddress, long> versions)
        {
            _cache = new Lazy<Cache>(() => new Cache(records));
            _versions = versions;
            _records = records;
        }

        public sealed class Record
        {
            readonly UniqueAddress _observer;
            public UniqueAddress Observer { get { return _observer; } }
            readonly UniqueAddress _subject;
            public UniqueAddress Subject { get { return _subject; } }
            readonly ReachabilityStatus _status;
            public ReachabilityStatus Status { get { return _status; } }
            readonly long _version;
            public long Version { get { return _version; } }

            public Record(UniqueAddress observer, UniqueAddress subject, ReachabilityStatus status, long version)
            {
                _observer = observer;
                _subject = subject;
                _status = status;
                _version = version;
            }

            public override bool Equals(object obj)
            {
                var other = obj as Record;
                if (other == null) return false;
                return _version.Equals(other._version) &&
                       _status == other.Status &&
                       _observer.Equals(other._observer) &&
                       _subject.Equals(other._subject);
            }

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

        public enum ReachabilityStatus
        {
            Reachable,
            Unreachable,
            Terminated
        }

        readonly ImmutableList<Record> _records;
        public ImmutableList<Record> Records { get { return _records; } }
        readonly ImmutableDictionary<UniqueAddress, long> _versions;
        public ImmutableDictionary<UniqueAddress, long> Versions { get { return _versions; } }

        class Cache
        {
            readonly ImmutableDictionary<UniqueAddress, ImmutableDictionary<UniqueAddress, Record>>
                _observerRowsMap;
            public ImmutableDictionary<UniqueAddress, ImmutableDictionary<UniqueAddress, Record>> ObserverRowMap
            {
                get { return _observerRowsMap; }
            }

            readonly ImmutableHashSet<UniqueAddress> _allTerminated;
            public ImmutableHashSet<UniqueAddress> AllTerminated { get { return _allTerminated; } }

            readonly ImmutableHashSet<UniqueAddress> _allUnreachable;
            public ImmutableHashSet<UniqueAddress> AllUnreachable { get { return _allUnreachable; } }

            readonly ImmutableHashSet<UniqueAddress> _allUnreachableOrTerminated;
            public ImmutableHashSet<UniqueAddress> AllUnreachableOrTerminated
            {
                get { return _allUnreachableOrTerminated; }
            }

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
                        ImmutableDictionary<UniqueAddress, Record> m;
                        if(mapBuilder.TryGetValue(r.Observer, out m))
                        {
                            m = m.SetItem(r.Subject, r);                            
                        }
                        else
                        {
                            //TODO: Other collections take items for Create. Create unnecessary array here
                            m = ImmutableDictionary.CreateRange(new[] { new KeyValuePair<UniqueAddress, Record>(r.Subject, r) });
                        }
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
            ImmutableDictionary<UniqueAddress, Record> observerRows = null;
            _cache.Value.ObserverRowMap.TryGetValue(observer, out observerRows);

            return observerRows;
        }

        public Reachability Unreachable(UniqueAddress observer, UniqueAddress subject)
        {
            return Change(observer, subject, ReachabilityStatus.Unreachable);
        }

        public Reachability Reachable(UniqueAddress observer, UniqueAddress subject)
        {
            return Change(observer, subject, ReachabilityStatus.Reachable);
        }

        public Reachability Terminated(UniqueAddress observer, UniqueAddress subject)
        {
            return Change(observer, subject, ReachabilityStatus.Terminated);
        }

        long CurrentVersion(UniqueAddress observer)
        {
            long version;
            return _versions.TryGetValue(observer, out version) ? version : 0;
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

            Record oldRecord = null;
            oldObserverRows.TryGetValue(subject, out oldRecord);
            if (oldRecord == null)
            {
                if (status == ReachabilityStatus.Reachable &&
                    oldObserverRows.Values.All(r => r.Status == ReachabilityStatus.Reachable))
                {
                    return new Reachability(_records.FindAll(r => !r.Observer.Equals(observer)), newVersions);
                }
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

        public Reachability Remove(IEnumerable<UniqueAddress> nodes)
        {
            var newRecords = _records.FindAll(r => !nodes.Contains(r.Observer) && !nodes.Contains(r.Subject));
            if (newRecords.Count == _records.Count) return this;
 
            var newVersions = _versions.RemoveRange(nodes);
            return new Reachability(newRecords, newVersions);
        }

        public ReachabilityStatus Status(UniqueAddress observer, UniqueAddress subject)
        {
            var observerRows = ObserverRows(observer);
            if (observerRows == null) return ReachabilityStatus.Reachable;
            Record record;
            observerRows.TryGetValue(subject, out record);
            if (record == null) return ReachabilityStatus.Reachable;
            return record.Status;
        }

        public ReachabilityStatus Status(UniqueAddress node)
        {
            if (_cache.Value.AllTerminated.Contains(node)) return ReachabilityStatus.Terminated;
            if (_cache.Value.AllUnreachable.Contains(node)) return ReachabilityStatus.Unreachable;
            return ReachabilityStatus.Reachable;
        }

        public bool IsReachable(UniqueAddress node)
        {
            return IsAllReachable || !AllUnreachableOrTerminated.Contains(node);
        }

        public bool IsReachable(UniqueAddress observer, UniqueAddress subject)
        {
            return Status(observer, subject) == ReachabilityStatus.Reachable;
        }

        /*
         *  def isReachable(observer: UniqueAddress, subject: UniqueAddress): Boolean =
            status(observer, subject) == Reachable
         */

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

        public ImmutableHashSet<UniqueAddress> AllUnreachableOrTerminated
        {
            get { return _cache.Value.AllUnreachableOrTerminated; }
        }

        public ImmutableHashSet<UniqueAddress> AllUnreachableFrom(UniqueAddress observer)
        {
            var observerRows = ObserverRows(observer);
            if (observerRows == null) return ImmutableHashSet.Create<UniqueAddress>();
            return
                ImmutableHashSet.CreateRange(
                    observerRows.Where(p => p.Value.Status == ReachabilityStatus.Unreachable).Select(p => p.Key));
        }

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

        public ImmutableHashSet<UniqueAddress> AllObservers
        {
            get { return ImmutableHashSet.CreateRange(_versions.Keys); }
        }

        public ImmutableList<Record> RecordsFrom(UniqueAddress observer)
        {
            var rows = ObserverRows(observer);
            if (rows == null) return ImmutableList.Create<Record>();
            return rows.Values.ToImmutableList();
        }

        public override int GetHashCode()
        {
            return _versions.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var other = obj as Reachability;
            if (other == null) return false;
            return _records.Count == other._records.Count && 
                _versions.Equals(other._versions) && 
                _cache.Value.ObserverRowMap.Equals(other._cache.Value.ObserverRowMap);
        }

        public override string ToString()
        {
            var builder = new StringBuilder();

            foreach (var observer in _versions.Keys)
            {
                var rows = ObserverRows(observer);
                if(rows == null) continue;
                foreach(var row in rows)
                {
                    //TODO: ", " is wrong
                    builder.Append(String.Format("{0} -> {1}: {2} [{3}] ({4}), ", observer.Address, row.Key, row.Value.Status,
                        Status(row.Key), row.Value.Version));
                }
            }

            return builder.ToString();
        }
    }
}