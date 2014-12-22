using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Dispatch;
using Akka.Event;

namespace Akka.Persistence.Snapshot
{
    public class LocalSnapshotStore : SnapshotStore
    {
        private static readonly Regex FilenameRegex = new Regex(@"^snapshot-(.+)-(\d+)-(\d+)", RegexOptions.Compiled);

        private readonly DirectoryInfo _snapshotDirectory;
        private readonly ISet<SnapshotMetadata> _saving;
        private readonly MessageDispatcher _streamDispatcher;

        private readonly Akka.Serialization.Serialization _serialization;

        public LocalSnapshotStore()
        {
            var config = Context.System.Settings.Config.GetConfig("akka.persistence.snapshot-store.local");
            _snapshotDirectory = new DirectoryInfo(config.GetString("dir"));
            _streamDispatcher = Context.System.Dispatchers.Lookup(config.GetString("stream-dispatcher"));
            _saving = new SortedSet<SnapshotMetadata>(SnapshotMetadata.TimestampComparer);
            _serialization = Context.System.Serialization;
        }

        private LoggingAdapter _log;
        public LoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

        protected override void PreStart()
        {
            if (!_snapshotDirectory.Exists)
            {
                _snapshotDirectory.Create();
            }

            base.PreStart();
        }

        protected override Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            // Heurisitics: take 3 latest snapshots
            //TODO: convert 3 to configurable value
            var metas = GetSnapshotMetadata(persistenceId, criteria);
            return Task.FromResult(Load(metas, 3));
        }

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            _saving.Add(metadata);
            Save(metadata, snapshot);
            return Task.Delay(0);
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
            foreach (var metadata in GetSnapshotMetadata(persistenceId, criteria))
            {
                Delete(metadata);
            }
        }

        private SelectedSnapshot Load(IEnumerable<SnapshotMetadata> metas, int dec)
        {
            if (dec > 0)
            {
                var metadata = metas.LastOrDefault();
                if (metadata != null)
                {
                    using (var stream = ReadableStream(metadata))
                    {
                        try
                        {
                            var snapshot = Deserialize(stream);

                            return new SelectedSnapshot(metadata, snapshot.Data);
                        }
                        catch (Exception exc)
                        {
                            Log.Error(exc, "Error loading a snapshot [{0}]", exc.Message);
                            return Load(metas.Skip(1), dec - 1);
                        }
                    }
                }
            }

            return null;
        }

        private Serialization.Snapshot Deserialize(Stream stream)
        {
            var buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            var snapshotType = typeof(Serialization.Snapshot);
            var serializer = _serialization.FindSerializerForType(snapshotType);
            var snapshot = (Serialization.Snapshot)serializer.FromBinary(buffer, snapshotType);
            return snapshot;
        }

        private Stream ReadableStream(SnapshotMetadata metadata)
        {
            var snapshotFile = GetSnapshotFile(metadata);
            return snapshotFile.OpenRead();
        }

        private void Save(SnapshotMetadata metadata, object payload)
        {
            var tempFile = GetSnapshotFile(metadata, ".tmp");
            using (var stream = tempFile.OpenWrite())
            {
                var snapshot = new Serialization.Snapshot(payload);
                Serialize(snapshot, stream);
                tempFile.MoveTo(tempFile.Name.Replace(".tmp", string.Empty));
            }
        }

        private void Serialize(Serialization.Snapshot snapshot, Stream stream)
        {
            var serializer = _serialization.FindSerializerFor(snapshot);
            var bytes = serializer.ToBinary(snapshot);
            stream.Write(bytes, 0, bytes.Length);
        }

        private IEnumerable<SnapshotMetadata> GetSnapshotMetadata(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var snapshots = _snapshotDirectory
                .EnumerateFiles("snapshot-" + persistenceId, SearchOption.TopDirectoryOnly)
                .Select(ExtractSnapshotMetadata)
                .Where(metadata => metadata != null && criteria.IsMatch(metadata) && !_saving.Contains(metadata));

            return snapshots;    // guaranteed to be not null in previous constraint
        }

        private SnapshotMetadata ExtractSnapshotMetadata(FileInfo fileInfo)
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
            var filename = string.Format("snapshot-{0}-{1}-{2}{3}", metadata.PersistenceId, metadata.SequenceNr, metadata.Timestamp.Ticks, extension);
            return _snapshotDirectory.GetFiles(filename, SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
    }
}