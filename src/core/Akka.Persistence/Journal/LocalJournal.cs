using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;

namespace Akka.Persistence.Journal
{
    public class LocalJournal : AsyncWriteJournal
    {
        private const string TempFileExtension = ".tmp";
        private const string BackupFileExtension = ".bak";

        private readonly MessageDispatcher _streamDispatcher;
        private readonly DirectoryInfo _dir;

        private readonly Akka.Serialization.Serialization _serialization;
        private readonly ILoggingAdapter _log;

        public LocalJournal(Config journalConfig)
        {
            _streamDispatcher = Context.System.Dispatchers.Lookup(journalConfig.GetString("stream-dispatcher"));
            _dir = new DirectoryInfo(journalConfig.GetString("dir"));
            _serialization = Context.System.Serialization;
            _log = Context.GetLogger();
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            return RunWithStreamDispatcher(() =>
            {
                var files = GetJournalFiles(persistenceId);
                int count = 0;

                foreach (var file in files)
                {
                    var parts = file.Name.Split('-');
                    var sequence = Convert.ToInt64(parts[parts.Length - 1].TrimStart('0'));
                    if (sequence >= fromSequenceNr && sequence <= toSequenceNr)
                    {
                        var journal = Load(file);
                        if (!journal.IsDeleted)
                        {
                            recoveryCallback(journal);
                        }

                        count++;
                    }

                    if (count >= max) break;
                }

                return new object();
            });
        }

        public IPersistentRepresentation Load(FileInfo file)
        {
            return WithInputStream(file, stream =>
            {
                var buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);

                var serializer = _serialization.FindSerializerForType(typeof(IPersistentRepresentation));
                return serializer.FromBinary<IPersistentRepresentation>(buffer);
            });
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            return RunWithStreamDispatcher(() =>
            {
                var files = GetJournalFiles(persistenceId);
                if (files.Length == 0) return 0;

                var lastFile = files[files.Length - 1];
                return Load(lastFile).SequenceNr;
            });
        }

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            return RunWithStreamDispatcher(() =>
            {
                WriteMessages(messages);
                return (IImmutableList<Exception>)null;
            });
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            return RunWithStreamDispatcher(() =>
            {
                var files = GetJournalFiles(persistenceId);

                var toDelete = new List<FileInfo>();

                foreach (var file in files)
                {
                    // skip tmp files
                    if (file.Name.EndsWith(TempFileExtension)) continue;
                    if (file.Name.EndsWith(BackupFileExtension)) continue;

                    var parts = file.Name.Split('-');
                    var trimmedSequence = parts[parts.Length - 1].TrimStart('0');
                    var sequence = Convert.ToInt64(trimmedSequence);
                    if (sequence <= toSequenceNr)
                    {
                        toDelete.Add(file);
                    }
                }

                if (toDelete.Count > 0)
                {
                    // delete all files except the last one
                    FileInfo last = toDelete[toDelete.Count - 1];
                    toDelete.RemoveAt(toDelete.Count - 1);
                    foreach (var file in toDelete)
                    {
                        try
                        {
                            file.Delete();
                        }
                        catch (Exception ex)
                        {
                            _log.Warning("Failed to delete file, ignoring: " + ex);
                        }
                    }

                    // read the last file and set is deleted to to true
                    var journal = Load(last);
                    var updated = journal.Update(journal.SequenceNr, journal.PersistenceId, true, journal.Sender, journal.WriterGuid);

                    // dotnet standard 1.6 doesn't have support for replace, so we must delete and then resave
                    last.Delete();
                    Save(updated);
                }

                return new object();
            });
        }

        private FileInfo[] GetJournalFiles(string persistenceId)
        {
            var prefix = $"{Uri.EscapeDataString(persistenceId)}-*";
            return GetJournalDir().GetFiles(prefix, SearchOption.TopDirectoryOnly);
        }

        private void WriteMessages(IEnumerable<AtomicWrite> messages)
        {
            foreach (var w in messages)
            {
                foreach (var p in (IEnumerable<IPersistentRepresentation>)w.Payload)
                {
                    Save(p);
                }
            }
        }

        private void Save(IPersistentRepresentation payload)
        {
            var serializer = _serialization.FindSerializerForType(typeof(IPersistentRepresentation));
            var binary = serializer.ToBinary(payload);

            var tempFile = WithOutputStream(payload, stream => { stream.Write(binary, 0, binary.Length); });
            tempFile.MoveTo(GetJournalFileForWrite(payload.PersistenceId, payload.SequenceNr).FullName);
        }

        protected FileInfo WithOutputStream(IPersistentRepresentation payload, Action<Stream> p)
        {
            var tmpFile = GetJournalFileForWrite(payload.PersistenceId, payload.SequenceNr, TempFileExtension);
            WithStream(new BufferedStream(new FileStream(tmpFile.FullName, FileMode.Create)), stream =>
            {
                p(stream);
                stream.Flush();
                return new object();
            });
            return tmpFile;
        }

        private T WithInputStream<T>(FileInfo file, Func<Stream, T> p)
        {
            return WithStream(new BufferedStream(new FileStream(file.FullName, FileMode.Open)), p);
        }

        private T WithStream<T>(Stream stream, Func<Stream, T> p)
        {
            try
            {
                var result = p(stream);
                return result;
            }
            finally
            {
                stream.Dispose();
            }
        }

        private DirectoryInfo GetJournalDir()
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

        private FileInfo GetJournalFileForWrite(string persistenceId, long sequenceNumber, string extension = "")
        {
            var sequencePadded = sequenceNumber.ToString().PadLeft(6, '0');
            var filename = $"{Uri.EscapeDataString(persistenceId)}-{sequencePadded}{extension}";
            return new FileInfo(Path.Combine(GetJournalDir().FullName, filename));
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