//-----------------------------------------------------------------------
// <copyright file="JournalWriter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Persistence.Embedded.Internal;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Embedded.Journal
{
    /// <summary>Work for the writer thread.</summary>
    internal abstract class WriteWork
    {
    }

    /// <summary>Runs once on the writer thread before anything else: creates and verifies the schema.</summary>
    internal sealed class InitWork : WriteWork
    {
        public InitWork(Action<SqliteConnection> action)
        {
            Action = action;
        }

        public Action<SqliteConnection> Action { get; }

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>The rows of one <c>WriteMessagesAsync</c> call, in write order.</summary>
    internal sealed class WriteRequest : WriteWork
    {
        public WriteRequest(IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken)
        {
            Rows = rows;
            CancellationToken = cancellationToken;
        }

        public IReadOnlyList<JournalRow> Rows { get; }

        public CancellationToken CancellationToken { get; }

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>A <c>DeleteMessagesTo</c> call. Never batched with writes.</summary>
    internal sealed class DeleteRequest : WriteWork
    {
        public DeleteRequest(string persistenceId, long toSequenceNr, CancellationToken cancellationToken)
        {
            PersistenceId = persistenceId;
            ToSequenceNr = toSequenceNr;
            CancellationToken = cancellationToken;
        }

        public string PersistenceId { get; }

        public long ToSequenceNr { get; }

        public CancellationToken CancellationToken { get; }

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal enum EnqueueResult
    {
        Queued,
        Full,
        Closed
    }

    /// <summary>
    /// The one thread that writes to the database. Owns one connection, groups queued write requests into a
    /// single <c>BEGIN IMMEDIATE</c> transaction, and runs deletes alone.
    /// </summary>
    internal sealed class JournalWriter
    {
        private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(5);

        private readonly JournalSettings _settings;
        private readonly JournalSql _sql;
        private readonly ILoggingAdapter _log;
        private readonly BlockingCollection<WriteWork> _queue;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly ConnectionHolder _holder;
        private readonly Thread _thread;
        private int _fullRejections;
        private SqliteCommand? _insert;
        private SqliteCommand? _insertReturningId;
        private SqliteCommand? _insertTag;

        public JournalWriter(JournalSettings settings, JournalSql sql, ILoggingAdapter log)
        {
            _settings = settings;
            _sql = sql;
            _log = log;
            _queue = new BlockingCollection<WriteWork>(settings.BufferSize);
            _holder = new ConnectionHolder(ConnectionHolder.Prepare(settings.ConnectionString, log), log);
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = $"{settings.PluginPath}-writer"
            };
        }

        internal string ConnectionString => _settings.ConnectionString;

        internal bool ShutdownRequested => _shutdown.IsCancellationRequested;

        internal int QueuedForTests => _queue.Count;

        internal int ConnectionResetsForTests => _holder.ResetCount;

        internal string HolderConnectionStringForTests => _holder.ConnectionString;

        internal int FullRejectionsForTests => Volatile.Read(ref _fullRejections);

        /// <summary>Test seam: runs on the writer thread after it took the first item of a round, before it collects the batch.</summary>
        internal Action? BeforeBatchForTests { get; set; }

        /// <summary>Test seam: runs on the writer thread after a write transaction committed, with request and row counts.</summary>
        internal Action<int, int>? AfterCommitForTests { get; set; }

        public void Start() => _thread.Start();

        public EnqueueResult TryEnqueue(WriteWork work)
        {
            try
            {
                if (_queue.TryAdd(work))
                    return EnqueueResult.Queued;

                Interlocked.Increment(ref _fullRejections);
                return EnqueueResult.Full;
            }
            catch (InvalidOperationException)
            {
                return EnqueueResult.Closed;
            }
        }

        /// <summary>Stops the thread after the transaction it is in. Queued requests fail.</summary>
        public void Stop()
        {
            // the queue is never disposed, so CompleteAdding cannot throw
            _queue.CompleteAdding();
            _shutdown.Cancel();
            if (_thread.ThreadState != System.Threading.ThreadState.Unstarted && !_thread.Join(JoinTimeout))
                _log.Warning("[{0}] writer thread did not stop within 5s.", _settings.PluginPath);
        }

        private void Loop()
        {
            WriteWork? carry = null;
            try
            {
                while (!_shutdown.IsCancellationRequested)
                {
                    WriteWork first;
                    if (carry is not null)
                    {
                        first = carry;
                        carry = null;
                    }
                    else
                    {
                        try
                        {
                            first = _queue.Take(_shutdown.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (InvalidOperationException)
                        {
                            break;
                        }
                    }

                    try
                    {
                        BeforeBatchForTests?.Invoke();

                        switch (first)
                        {
                            case InitWork init:
                                RunInit(init);
                                break;
                            case DeleteRequest delete:
                                RunDelete(delete);
                                break;
                            case WriteRequest write:
                                carry = RunWriteBatch(write);
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        // The run methods complete their own requests. This only guards the thread itself.
                        _log.Error(e, "[{0}] writer thread hit an unexpected error.", _settings.PluginPath);
                        Fail(first, e);
                    }
                }
            }
            finally
            {
                var stopped = new Exception("SQLite journal stopped before this write ran.");
                if (carry is not null)
                    Fail(carry, stopped);
                while (_queue.TryTake(out var left))
                    Fail(left, stopped);

                ResetCommands();
                _holder.Dispose();
            }
        }

        private static void Fail(WriteWork work, Exception exception)
        {
            switch (work)
            {
                case InitWork init:
                    init.Completion.TrySetException(exception);
                    break;
                case WriteRequest write:
                    write.Completion.TrySetException(exception);
                    break;
                case DeleteRequest delete:
                    delete.Completion.TrySetException(exception);
                    break;
            }
        }

        // ---- init ----------------------------------------------------------------------------------

        private void RunInit(InitWork work)
        {
            try
            {
                work.Action(_holder.Get());
                work.Completion.TrySetResult();
            }
            catch (Exception e)
            {
                work.Completion.TrySetException(e);
                if (ConnectionHolder.ShouldReset(e))
                    ResetConnection();
            }
        }

        // ---- writes --------------------------------------------------------------------------------

        private WriteWork? RunWriteBatch(WriteRequest first)
        {
            var batch = new List<WriteRequest> { first };
            var rowCount = first.Rows.Count;
            WriteWork? carry = null;

            while (_queue.TryTake(out var next))
            {
                if (next is WriteRequest write && rowCount + write.Rows.Count <= _settings.BatchSize)
                {
                    batch.Add(write);
                    rowCount += write.Rows.Count;
                }
                else
                {
                    carry = next;
                    break;
                }
            }

            batch.RemoveAll(request =>
            {
                if (!request.CancellationToken.IsCancellationRequested)
                    return false;

                request.Completion.TrySetCanceled(request.CancellationToken);
                rowCount -= request.Rows.Count;
                return true;
            });

            if (batch.Count == 0)
                return carry;

            SqliteTransaction? transaction = null;
            try
            {
                var connection = _holder.Get();
                transaction = connection.BeginTransaction(deferred: false);
                PrepareCommands(connection, transaction);

                foreach (var request in batch)
                {
                    foreach (var row in request.Rows)
                        InsertRow(row);
                }

                transaction.Commit();
                transaction.Dispose();
                transaction = null;

                AfterCommitForTests?.Invoke(batch.Count, rowCount);
                foreach (var request in batch)
                    request.Completion.TrySetResult();
            }
            catch (Exception e)
            {
                if (transaction is not null)
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception rollbackError)
                    {
                        // the write error below is what the callers get; the rollback failure is only logged
                        _log.Warning(rollbackError, "[{0}] rollback failed after a write error.", _settings.PluginPath);
                    }

                    transaction.Dispose();
                }

                foreach (var request in batch)
                    request.Completion.TrySetException(e);

                if (ConnectionHolder.ShouldReset(e))
                    ResetConnection();
            }

            return carry;
        }

        private void PrepareCommands(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (_insert is null)
            {
                _insert = CreateInsert(connection, _sql.Insert);
                _insertReturningId = CreateInsert(connection, _sql.InsertReturningId);

                var tag = connection.CreateCommand();
                tag.CommandText = _sql.InsertTag;
                tag.Parameters.Add("@ordering_id", SqliteType.Integer);
                tag.Parameters.Add("@tag", SqliteType.Text);
                tag.Parameters.Add("@sequence_nr", SqliteType.Integer);
                tag.Parameters.Add("@persistence_id", SqliteType.Text);
                _insertTag = tag;
            }

            _insert.Transaction = transaction;
            _insertReturningId!.Transaction = transaction;
            _insertTag!.Transaction = transaction;

            // Prepared once per batch inside the transaction: a missing table fails the batch, not the thread.
            // Prepare is a no-op for a command that is already prepared.
            _insert.Prepare();
            _insertReturningId.Prepare();
            _insertTag.Prepare();
        }

        private SqliteCommand CreateInsert(SqliteConnection connection, string sql)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add("@created", SqliteType.Integer);
            command.Parameters.Add("@deleted", SqliteType.Integer);
            command.Parameters.Add("@persistence_id", SqliteType.Text);
            command.Parameters.Add("@sequence_number", SqliteType.Integer);
            command.Parameters.Add("@message", SqliteType.Blob);
            command.Parameters.Add("@manifest", SqliteType.Text);
            command.Parameters.Add("@identifier", SqliteType.Integer);
            command.Parameters.Add("@writer_uuid", SqliteType.Text);
            return command;
        }

        private void InsertRow(JournalRow row)
        {
            var returnsId = row.Tags.Length > 0;
            var command = returnsId ? _insertReturningId! : _insert!;

            var p = command.Parameters;
            p["@created"].Value = row.Created;
            p["@deleted"].Value = 0L;
            p["@persistence_id"].Value = row.PersistenceId;
            p["@sequence_number"].Value = row.SequenceNr;
            p["@message"].Value = row.Message;
            p["@manifest"].Value = row.Manifest;
            p["@identifier"].Value = (long)row.Identifier;
            p["@writer_uuid"].Value = (object?)row.WriterUuid ?? DBNull.Value;

            if (!returnsId)
            {
                command.ExecuteNonQuery();
                return;
            }

            var ordering = Convert.ToInt64(command.ExecuteScalar());
            var tagParameters = _insertTag!.Parameters;
            foreach (var tag in row.Tags)
            {
                tagParameters["@ordering_id"].Value = ordering;
                tagParameters["@tag"].Value = tag;
                tagParameters["@sequence_nr"].Value = row.SequenceNr;
                tagParameters["@persistence_id"].Value = row.PersistenceId;
                _insertTag.ExecuteNonQuery();
            }
        }

        // ---- deletes -------------------------------------------------------------------------------

        private void RunDelete(DeleteRequest request)
        {
            if (request.CancellationToken.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(request.CancellationToken);
                return;
            }

            try
            {
                var connection = _holder.Get();
                using var transaction = connection.BeginTransaction(deferred: false);

                long? marker;
                using (var select = Command(connection, transaction, _sql.DeleteSelectHighest))
                {
                    select.Parameters.Add("@persistence_id", SqliteType.Text).Value = request.PersistenceId;
                    select.Parameters.Add("@to", SqliteType.Integer).Value = request.ToSequenceNr;
                    var found = select.ExecuteScalar();
                    marker = found is null or DBNull ? null : Convert.ToInt64(found);
                }

                if (marker is not null)
                {
                    ExecuteWithMarker(connection, transaction, _sql.DeleteTombstone, request.PersistenceId, marker.Value);
                    ExecuteWithMarker(connection, transaction, _sql.DeletePhysical, request.PersistenceId, marker.Value);
                    ExecuteWithMarker(connection, transaction, _sql.DeleteTags, request.PersistenceId, marker.Value);
                }

                transaction.Commit();
                request.Completion.TrySetResult();
            }
            catch (Exception e)
            {
                request.Completion.TrySetException(e);
                if (ConnectionHolder.ShouldReset(e))
                    ResetConnection();
            }
        }

        private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return command;
        }

        private static void ExecuteWithMarker(SqliteConnection connection, SqliteTransaction transaction, string sql, string persistenceId, long marker)
        {
            using var command = Command(connection, transaction, sql);
            command.Parameters.Add("@persistence_id", SqliteType.Text).Value = persistenceId;
            command.Parameters.Add("@marker", SqliteType.Integer).Value = marker;
            command.ExecuteNonQuery();
        }

        // ---- connection ----------------------------------------------------------------------------

        private void ResetCommands()
        {
            _insert?.Dispose();
            _insertReturningId?.Dispose();
            _insertTag?.Dispose();
            _insert = null;
            _insertReturningId = null;
            _insertTag = null;
        }

        private void ResetConnection()
        {
            ResetCommands();
            _holder.Reset();
        }
    }
}
