//-----------------------------------------------------------------------
// <copyright file="LocalSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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

        private readonly int _maxLoadAttempts;
        private readonly MessageDispatcher _streamDispatcher;
        private readonly DirectoryInfo _dir;
        private readonly ISet<SnapshotMetadata> _saving;

        private readonly Akka.Serialization.Serialization _serialization;

        public LocalSnapshotStore()
        {
            var config = Context.System.Settings.Config.GetConfig("akka.persistence.snapshot-store.local");
            _maxLoadAttempts = config.GetInt("max-load-attempts");

            _streamDispatcher = Context.System.Dispatchers.Lookup(config.GetString("stream-dispatcher"));
            _dir = new DirectoryInfo(config.GetString("dir"));

            _serialization = Context.System.Serialization;
            _saving = new SortedSet<SnapshotMetadata>(SnapshotMetadata.Comparer); // saving in progress
            _log = Context.GetLogger();
        }

        private ILoggingAdapter _log;

        protected override Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            //
            // Heuristics:
            //
            // Select youngest `maxLoadAttempts` snapshots that match upper bound.
            // This may help in situations where saving of a snapshot could not be completed because of a VM crash.
            // Hence, an attempt to load that snapshot will fail but loading an older snapshot may succeed.
            //
            var metadata = GetSnapshotMetadata(persistenceId, criteria).Reverse().Take(_maxLoadAttempts);
            return RunWithStreamDispatcher(() => Load(metadata.GetEnumerator()));
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

        protected override Task DeleteAsync(SnapshotMetadata metadata)
        {
            _saving.Remove(metadata);
            return RunWithStreamDispatcher(() =>
            {
                // multiple snapshot files here mean that there were multiple snapshots for this seqNr, we delete all of them
                // usually snapshot-stores would keep one snapshot per sequenceNr however here in the file-based one we timestamp
                // snapshots and allow multiple to be kept around (for the same seqNr) if desired
                foreach (var file in GetSnapshotFiles(metadata))
                {
                    file.Delete();
                }
                return new object();
            });
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            foreach (var metadata in GetSnapshotMetadata(persistenceId, criteria))
            {
                await DeleteAsync(metadata);
            }
        }

        protected override bool ReceivePluginInternal(object message)
        {
            if (message is SaveSnapshotSuccess)
            {
                _saving.Remove(((SaveSnapshotSuccess) message).Metadata);
            }
            else if (message is SaveSnapshotFailure)
            {
                // ignore
            }
            else if (message is DeleteSnapshotsSuccess)
            {
                // ignore
            }
            else if (message is DeleteSnapshotsFailure)
            {
                // ignore
            }
            else
            {
                return false;
            }
            return true;
        }

        private IEnumerable<FileInfo> GetSnapshotFiles(SnapshotMetadata metadata)
        {
            return GetSnapshotDir()
                .GetFiles("*", SearchOption.TopDirectoryOnly)
                .Where(f => SnapshotSequenceNrFilenameFilter(f, metadata));
        }

        private SelectedSnapshot Load(IEnumerator<SnapshotMetadata> metadata)
        {
            if (metadata.MoveNext())
            {
                var md = metadata.Current;
                try
                {
                    return WithInputStream(md, stream =>
                    {
                        var snapshot = Deserialize(stream);

                        return new SelectedSnapshot(md, snapshot.Data);
                    });
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error loading snapshot [{0}]", md);
                    return Load(metadata);
                }
            }

            return null;
        }

        protected virtual void Save(SnapshotMetadata metadata, object snapshot)
        {
            var tempFile = WithOutputStream(metadata, stream =>
            {
                Serialize(stream, new Serialization.Snapshot(snapshot));
            });
            tempFile.MoveTo(GetSnapshotFileForWrite(metadata).FullName);
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

        protected void Serialize(Stream stream, Serialization.Snapshot snapshot)
        {
            var serializer = _serialization.FindSerializerFor(snapshot);
            var bytes = serializer.ToBinary(snapshot);
            stream.Write(bytes, 0, bytes.Length);
        }

        protected FileInfo WithOutputStream(SnapshotMetadata metadata, Action<Stream> p)
        {
            var tmpFile = GetSnapshotFileForWrite(metadata, ".tmp");
            WithStream(new BufferedStream(new FileStream(tmpFile.FullName, FileMode.Create)), stream =>
            {
                p(stream);
                stream.Flush();
                return new object();
            });
            return tmpFile;
        }

        private T WithInputStream<T>(SnapshotMetadata metadata, Func<Stream, T> p)
        {
            return
                WithStream(
                    new BufferedStream(new FileStream(GetSnapshotFileForWrite(metadata).FullName, FileMode.Open)), p);
        }

        private T WithStream<T>(Stream stream, Func<Stream, T> p)
        {
            try
            {
                var result = p(stream);
                stream.Dispose();
                return result;
            }
            finally
            {
                stream.Dispose();
            }
        }

        // only by PersistenceId and SequenceNr, timestamp is informational - accommodates for older files
        private FileInfo GetSnapshotFileForWrite(SnapshotMetadata metadata, string extension = "")
        {
            var filename = string.Format("snapshot-{0}-{1}-{2}{3}", Uri.EscapeDataString(metadata.PersistenceId),
                metadata.SequenceNr, metadata.Timestamp.Ticks, extension);
            return new FileInfo(Path.Combine(GetSnapshotDir().FullName, filename));
        }

        private IEnumerable<SnapshotMetadata> GetSnapshotMetadata(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var snapshots = GetSnapshotDir()
                .EnumerateFiles("snapshot-" + Uri.EscapeDataString(persistenceId) + "-*", SearchOption.TopDirectoryOnly)
                .Select(ExtractSnapshotMetadata)
                .Where(metadata => metadata != null && criteria.IsMatch(metadata) && !_saving.Contains(metadata)).ToList();

            return snapshots;
        }

        private SnapshotMetadata ExtractSnapshotMetadata(FileInfo fileInfo)
        {
            var match = FilenameRegex.Match(fileInfo.Name);
            if (match.Success)
            {
                var pid = Uri.UnescapeDataString(match.Groups[1].Value);
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

        protected override void PreStart()
        {
            GetSnapshotDir();
            base.PreStart();
        }

        private DirectoryInfo GetSnapshotDir()
        {
            if (!_dir.Exists || (_dir.Attributes & FileAttributes.Directory) == 0)
            {
                // try to create the directory, on failure double check if someone else beat us to it
                Exception exception;
                try
                {
                    _dir.Create();
                    exception = null;
                }
                catch (Exception e)
                {
                    exception = e;
                }
                finally
                {
                    _dir.Refresh();
                }
                if (exception != null || ((_dir.Attributes & FileAttributes.Directory) == 0))
                {
                    throw new IOException("Failed to create snapshot directory " + _dir.FullName, exception);
                }
            }
            return _dir;
        }

        private static bool SnapshotSequenceNrFilenameFilter(FileInfo fileInfo, SnapshotMetadata metadata)
        {
            var match = FilenameRegex.Match(fileInfo.Name);
            if (match.Success)
            {
                var pid = Uri.UnescapeDataString(match.Groups[1].Value);
                var seqNrString = match.Groups[2].Value;
                var timestampTicks = match.Groups[3].Value;

                try
                {
                    return pid.Equals(metadata.PersistenceId) &&
                           long.Parse(seqNrString) == metadata.SequenceNr &&
                           (metadata.Timestamp == SnapshotMetadata.TimestampNotSpecified ||
                            long.Parse(timestampTicks) == metadata.Timestamp.Ticks);
                }
                catch
                {
                    return false;
                }
            }
            return false;
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

