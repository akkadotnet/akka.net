//-----------------------------------------------------------------------
// <copyright file="SqliteWriteJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Embedded.Internal;
using Akka.Persistence.Journal;

namespace Akka.Persistence.Embedded.Journal
{
    /// <summary>
    /// SQLite journal. Writes the same tables and rows as Akka.Persistence.Sql does on SQLite.
    /// One writer thread owns the write connection; recovery reads use a small pool of reader threads.
    /// </summary>
    public sealed class SqliteWriteJournal : AsyncWriteJournal, IWithUnboundedStash
    {
        private readonly JournalSettings _settings;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly string? _writerUuid;
        private readonly JournalSql _sql;
        private readonly RowCodec _codec;
        private readonly JournalWriter _writer;
        private readonly SqliteWorkerPool _readPool;
        private readonly Dictionary<string, Task> _writeInProgress = new();

        /// <summary>Plugin API: the stash. Set by the actor system.</summary>
        public IStash Stash { get; set; } = null!;

        /// <summary>Creates the journal. Called by Akka.Persistence with the plugin's config section.</summary>
        public SqliteWriteJournal(Config journalConfig)
        {
            var pluginPath = Self.Path.Name;
            _settings = JournalSettings.Create(journalConfig, pluginPath, Context.System.Settings.Config);
            foreach (var warning in _settings.Warnings)
                _log.Warning(warning);

            _writerUuid = _settings.UseWriterUuid ? Guid.NewGuid().ToString("N") : null;
            _sql = new JournalSql(_settings);
            _codec = new RowCodec((ExtendedActorSystem)Context.System, _settings);
            _writer = new JournalWriter(_settings, _sql, _log);
            _readPool = new SqliteWorkerPool(_settings.ConnectionString, _settings.ReadThreads, $"{pluginPath}-read", _log);
        }

        /// <summary>Test seam: the writer thread.</summary>
        internal JournalWriter Writer => _writer;

        protected override void PreStart()
        {
            base.PreStart();

            _writer.Start();
            _readPool.Start();

            var init = new InitWork(connection =>
            {
                var warnings = SqliteSchema.EnsureJournalSchema(connection, _settings, false, false);
                foreach (var warning in warnings)
                    _log.Warning(warning);
            });

            if (_writer.TryEnqueue(init) != EnqueueResult.Queued)
                throw new InvalidOperationException("Could not queue the journal initialization.");

            var self = Self;
            init.Completion.Task
                .ContinueWith(
                    t => t.IsFaulted
                        ? (object)new Status.Failure(t.Exception!.GetBaseException())
                        : Status.Success.Instance,
                    TaskContinuationOptions.ExecuteSynchronously)
                .PipeTo(self);

            // Same pattern as Akka.Persistence.Sql: hold every message until the tables are ready.
            BecomeStacked(Initializing);
        }

        protected override void PostStop()
        {
            _writer.Stop();
            _readPool.Dispose();
            base.PostStop();
        }

        private bool Initializing(object message)
        {
            switch (message)
            {
                case Status.Success:
                    UnbecomeStacked();
                    Stash.UnstashAll();
                    return true;

                case Status.Failure failure:
                    _log.Error(failure.Cause, "Failure during {0} initialization.", Self);
                    // the supervisor restarts the journal, so a later start can succeed
                    throw new ApplicationException("Failed to initialize SQLite journal.", failure.Cause);

                default:
                    Stash.Stash();
                    return true;
            }
        }

        protected override bool ReceivePluginInternal(object message)
        {
            switch (message)
            {
                case WriteFinished finished:
                    if (_writeInProgress.TryGetValue(finished.PersistenceId, out var pending) && ReferenceEquals(pending, finished.Future))
                        _writeInProgress.Remove(finished.PersistenceId);
                    return true;

                case EnsureInitialized:
                    Sender.Tell(Initialized.Instance);
                    return true;

                default:
                    return false;
            }
        }

        // ---- writes --------------------------------------------------------------------------------

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
        {
            var ticks = DateTime.UtcNow.Ticks;
            var writes = messages.ToList();
            if (writes.Count == 0)
                return Task.FromResult<IImmutableList<Exception>>(ImmutableList<Exception>.Empty);

            // null = success, the format AsyncWriteJournal expects
            var results = new Exception[writes.Count];
            var rows = new List<JournalRow>();

            for (var i = 0; i < writes.Count; i++)
            {
                var write = writes[i];
                if (write.Payload is not IImmutableList<IPersistentRepresentation> representations)
                {
                    results[i] = new ArgumentNullException(
                        $"{write.PersistenceId} received empty payload for sequenceNr range {write.LowestSequenceNr} - {write.HighestSequenceNr}");
                    continue;
                }

                try
                {
                    var writeRows = new List<JournalRow>(representations.Count);
                    foreach (var representation in representations)
                        writeRows.Add(_codec.Serialize(representation, ticks, _writerUuid));
                    rows.AddRange(writeRows);
                }
                catch (Exception e)
                {
                    results[i] = e;
                }
            }

            if (rows.Count == 0)
                return Task.FromResult<IImmutableList<Exception>>(results.ToImmutableList());

            var request = new WriteRequest(rows, cancellationToken);
            var enqueue = _writer.TryEnqueue(request);
            if (enqueue == EnqueueResult.Full)
            {
                request.Completion.TrySetException(new Exception(
                    $"Failed to enqueue journal row batch write, the queue buffer was full ({_settings.BufferSize} elements)"));
            }
            else if (enqueue == EnqueueResult.Closed)
            {
                request.Completion.TrySetException(new Exception("Failed to enqueue journal row batch write, the queue was closed."));
            }

            var task = Complete(request.Completion.Task, results);
            var persistenceId = writes[0].PersistenceId;
            _writeInProgress[persistenceId] = task;

            var self = Self;
            task.ContinueWith(
                t => self.Tell(new WriteFinished(persistenceId, t), ActorRefs.NoSender),
                TaskContinuationOptions.ExecuteSynchronously);

            return task;
        }

        private static async Task<IImmutableList<Exception>> Complete(Task written, Exception[] results)
        {
            await written.ConfigureAwait(false);
            return results.ToImmutableList();
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, CancellationToken cancellationToken)
        {
            var request = new DeleteRequest(persistenceId, toSequenceNr, cancellationToken);
            var enqueue = _writer.TryEnqueue(request);
            if (enqueue == EnqueueResult.Full)
            {
                request.Completion.TrySetException(new Exception(
                    $"Failed to enqueue journal delete, the queue buffer was full ({_settings.BufferSize} elements)"));
            }
            else if (enqueue == EnqueueResult.Closed)
            {
                request.Completion.TrySetException(new Exception("Failed to enqueue journal delete, the queue was closed."));
            }

            return request.Completion.Task;
        }

        // ---- reads ---------------------------------------------------------------------------------

        public override async Task ReplayMessagesAsync(
            IActorContext context,
            string persistenceId,
            long fromSequenceNr,
            long toSequenceNr,
            long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            var next = Math.Max(1L, fromSequenceNr);
            var remaining = max;
            var identifierColumn = _settings.Tables.Identifier;

            while (remaining > 0 && next <= toSequenceNr)
            {
                var take = (int)Math.Min(_settings.ReplayBatchSize, remaining);
                var from = next;
                var rows = await _readPool
                    .Run(connection => _sql.ReadReplayBatch(connection, persistenceId, from, toSequenceNr, take), CancellationToken.None)
                    .ConfigureAwait(false);

                // deserialize here, on the awaiting side, never on a reader thread
                foreach (var row in rows)
                    recoveryCallback(_codec.ToPersistent(row, identifierColumn));

                remaining -= rows.Count;
                if (rows.Count < take)
                    return;

                var lastSequenceNr = rows[^1].SequenceNr;
                if (lastSequenceNr >= toSequenceNr)
                    return;

                next = lastSequenceNr + 1;
            }
        }

        public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr, CancellationToken cancellationToken)
        {
            // This part runs synchronously on the actor thread, before the first await.
            _writeInProgress.TryGetValue(persistenceId, out var pending);
            if (pending is not null)
            {
                // we only care that it finished, not whether it worked: WhenAny completes without throwing the write's error
                await Task.WhenAny(pending).ConfigureAwait(false);
            }

            return await _readPool
                .Run(connection => _sql.ReadHighestSequenceNr(connection, persistenceId, fromSequenceNr), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
