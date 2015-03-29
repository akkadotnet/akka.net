using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;

namespace Akka.Persistence.Snapshot
{
    public class LocalSnapshotStore : SnapshotStore
    {
        #region settings class

        public class StoreSettings
        {
            /// <summary>
            /// Storage location of snapshot files;
            /// </summary>
            public readonly string Dir;

            /// <summary>
            /// Dispatcher for streaming snapshot IO.
            /// </summary>
            public readonly string StreamDispatcher;

            /// <summary>
            /// Number of attempts made to load a subsequent snapshots in case they were corrupted.
            /// </summary>
            public readonly int LoadAttempts;

            public StoreSettings(Config config)
            {
                Dir = config.GetString("dir");
                StreamDispatcher = config.GetString("stream-dispatcher");
                LoadAttempts = config.GetInt("load-attempts");
            }
        }

        #endregion

        private static readonly Regex FilenameRegex = new Regex(@"^snapshot-(.+)-(\d+)-(\d+)", RegexOptions.Compiled);

        private readonly DirectoryInfo _snapshotDirectory;
        private readonly ISet<SnapshotMetadata> _saving;
        private readonly MessageDispatcher _streamDispatcher;

        private readonly Akka.Serialization.Serialization _serialization;

        public LocalSnapshotStore()
        {
            var config = Context.System.Settings.Config.GetConfig("akka.persistence.snapshot-store.local");
            _settings = new StoreSettings(config);
            _snapshotDirectory = new DirectoryInfo(_settings.Dir);
            _streamDispatcher = Context.System.Dispatchers.Lookup(_settings.StreamDispatcher);
            _saving = new SortedSet<SnapshotMetadata>(SnapshotMetadata.TimestampComparer);
            _serialization = Context.System.Serialization;
        }

        private readonly StoreSettings _settings;
        public StoreSettings Settings { get { return _settings; } }

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
            var metas = GetSnapshotMetadata(persistenceId, criteria);
            return RunWithStreamDispatcher(() => Load(metas.Reverse().GetEnumerator(), Settings.LoadAttempts));
        }

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            _saving.Add(metadata);
            return RunWithStreamDispatcher(() =>
            {
                Save(metadata, snapshot);
                return new object();
            });
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

        private SelectedSnapshot Load(IEnumerator<SnapshotMetadata> metas, int dec)
        {
            if (dec > 0)
            {
                if (metas.MoveNext())
                {
                    var metadata = metas.Current;
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
                            return Load(metas, dec - 1);
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
            }

            // remove .tmp extension and rename
            var newFilePath = tempFile.FullName.Substring(0, tempFile.FullName.Length - 4);
            File.Move(tempFile.FullName, newFilePath);
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
                .EnumerateFiles("snapshot-" + persistenceId + "-*", SearchOption.TopDirectoryOnly)
                .Select(ExtractSnapshotMetadata)
                .Where(metadata => metadata != null && criteria.IsMatch(metadata) && !_saving.Contains(metadata)).ToList();

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
            var file = _snapshotDirectory.GetFiles(filename, SearchOption.TopDirectoryOnly).FirstOrDefault();
            return file ?? new FileInfo(Path.Combine(_snapshotDirectory.FullName, filename));
        }

        private Task<T> RunWithStreamDispatcher<T>(Func<T> fn)
        {
            var promise = new TaskCompletionSource<T>();

            _streamDispatcher.Schedule(() =>
            {
                try
                {
                    var result = fn();
                    promise.SetResult(result);
                }
                catch (Exception e)
                {
                    promise.SetException(e);
                }
            });

            return promise.Task;
        }
    }
}