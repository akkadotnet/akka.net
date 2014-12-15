using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;

namespace Akka.Persistence.Snapshot
{
    public class LocalSnapshotStore : SnapshotStore, IActorLogging
    {
        private static readonly Regex FilenameRegex = new Regex(@"^snapshot-(.+)-(\d+)-(\d+)", RegexOptions.Compiled);

        private readonly DirectoryInfo _snapshotDirectory;
        private readonly ISet<SnapshotMetadata> _saving;

        public LocalSnapshotStore()
        {
            Log = Context.System.Log;
            var config = Context.System.Settings.Config.GetConfig("akka.persistence.snapshot-store.local");
            _snapshotDirectory = new DirectoryInfo(config.GetString("dir"));
            _saving = new SortedSet<SnapshotMetadata>(SnapshotMetadata.TimestampComparer);
        }

        public LoggingAdapter Log { get; private set; }

        protected override void PreStart()
        {
            if (!_snapshotDirectory.Exists)
            {
                _snapshotDirectory.Create();
            }

            base.PreStart();
        }

        protected override Task<SelectedSnapshot?> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            // Heurisitics: take 3 latest snapshots
            var metas = GetSnapshotMetadata(persistenceId, criteria).OrderByDescending(x => x.Timestamp).Take(3);
            return Task.Factory.StartNew(() => Load(metas)); // Future(load(metadata))(streamDispatcher)
        }

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            _saving.Add(metadata);
            return Task.Factory.StartNew(() => Save(metadata, snapshot));
        }

        protected override void Saved(SnapshotMetadata metadata)
        {
            _saving.Remove(metadata);
        }

        protected override void Delete(SnapshotMetadata metadata)
        {
            _saving.Remove(metadata);
            GetSnapshotFile(metadata).Delete();
        }

        protected override void Delete(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var metas = GetSnapshotMetadata(persistenceId, criteria).ToList();
            foreach (var snapshotMetadata in metas)
            {
                Delete(snapshotMetadata);
            }
        }

        private SelectedSnapshot? Load(IEnumerable<SnapshotMetadata> metas)
        {
            if (metas.Any())
            {
                var metadata = metas.Last();
                throw new NotImplementedException();
            }

            return null;
        }

        private void Save(SnapshotMetadata metadata, object snapshot)
        {
            throw new NotImplementedException();
        }

        private IEnumerable<SnapshotMetadata> GetSnapshotMetadata(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var snapshots = _snapshotDirectory
                .EnumerateFiles("snapshot-" + persistenceId, SearchOption.TopDirectoryOnly)
                .Select(ExtractSnapshotMetadata)
                .Where(metadata => metadata.HasValue && criteria.IsMatch(metadata.Value) && !_saving.Contains(metadata.Value));

            return snapshots.Select(md => md.Value);    // guaranteed to be not null in previous constraint
        }

        private SnapshotMetadata? ExtractSnapshotMetadata(FileInfo fileInfo)
        {
            var match = FilenameRegex.Match(fileInfo.Name);
            if (match.Success)
            {
                var pid = match.Groups[1].Value;
                var seqNrString = match.Groups[2].Value;
                var timestampTicks = match.Groups[3].Value;

                long sequenceNr, ticks;
                if (long.TryParse(seqNrString, out sequenceNr) && long.TryParse(timestampTicks, out ticks))
                {
                    return new SnapshotMetadata(pid, sequenceNr, new DateTime(ticks));
                }
            }
            
            return null;
        }

        private FileInfo GetSnapshotFile(SnapshotMetadata metadata, string extension = "")
        {
            var filename = string.Format("snapshot-{0}-{1}-{2}${3}", metadata.PersistenceId, metadata.SequenceNr, metadata.Timestamp.Ticks, extension);
            return _snapshotDirectory.GetFiles(filename, SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
    }
}