//-----------------------------------------------------------------------
// <copyright file="SqliteWorkerPool.cs" company="Akka.NET Project">
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
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Embedded.Internal
{
    /// <summary>
    /// One long-lived <see cref="SqliteConnection"/> that a worker thread owns. Opens on first use and
    /// reopens after an error that may have left the connection unusable.
    /// </summary>
    /// <remarks>
    /// The connection string is rewritten with <c>Pooling=False</c>. A thread keeps its connection for its whole life,
    /// so pooling buys nothing, and with it Microsoft.Data.Sqlite can hand a reset thread the very handle that just
    /// failed. Without the pool, <see cref="Reset"/> really closes the handle (which also releases the database file at
    /// shutdown) and the next <see cref="Get"/> opens a new one.
    /// </remarks>
    internal sealed class ConnectionHolder : IDisposable
    {
        private const int SqliteBusy = 5;
        private const int SqliteLocked = 6;
        private const int SqliteConstraint = 19;

        private readonly string _connectionString;
        private readonly ILoggingAdapter _log;
        private SqliteConnection? _connection;
        private int _resetCount;

        public ConnectionHolder(string connectionString, ILoggingAdapter log)
        {
            _log = log;
            _connectionString = new SqliteConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
        }

        /// <summary>The connection string the holder opens, with pooling off.</summary>
        public string ConnectionString => _connectionString;

        /// <summary>How many times the connection was dropped after an error. For tests.</summary>
        public int ResetCount => Volatile.Read(ref _resetCount);

        public bool IsOpen => _connection is not null;

        public SqliteConnection Get()
        {
            if (_connection is not null)
                return _connection;

            var connection = new SqliteConnection(_connectionString);
            try
            {
                connection.Open();
            }
            catch
            {
                connection.Dispose();
                throw;
            }

            _connection = connection;
            return connection;
        }

        /// <summary>Drops the connection so the next <see cref="Get"/> opens a new one.</summary>
        public void Reset()
        {
            var connection = _connection;
            _connection = null;
            if (connection is null)
                return;

            Interlocked.Increment(ref _resetCount);

            try
            {
                connection.Dispose();
            }
            catch (Exception e) when (e is SqliteException or InvalidOperationException)
            {
                // The connection is already broken and is being thrown away; the next Get opens a new one.
                _log.Debug(e, "Closing a broken SQLite connection failed. It is dropped anyway.");
            }
        }

        /// <summary>
        /// A <see cref="SqliteException"/> other than busy, locked or a constraint violation may leave the
        /// connection unusable.
        /// </summary>
        public static bool ShouldReset(Exception exception)
            => exception is SqliteException { SqliteErrorCode: not (SqliteBusy or SqliteLocked or SqliteConstraint) };

        public void Dispose() => Reset();
    }

    /// <summary>
    /// A fixed set of dedicated threads, each with its own connection. Microsoft.Data.Sqlite's async methods
    /// run synchronously, so all SQL runs here and callers await a <see cref="Task"/>.
    /// </summary>
    internal sealed class SqliteWorkerPool : IDisposable
    {
        private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(5);

        private readonly string _connectionString;
        private readonly int _threadCount;
        private readonly string _threadNamePrefix;
        private readonly ILoggingAdapter _log;
        private readonly BlockingCollection<WorkItem> _queue = new();
        private readonly CancellationTokenSource _shutdown = new();
        private readonly List<Thread> _threads = new();
        private int _started;
        private int _disposed;

        public SqliteWorkerPool(string connectionString, int threadCount, string threadNamePrefix, ILoggingAdapter log)
        {
            _connectionString = connectionString;
            _threadCount = threadCount;
            _threadNamePrefix = threadNamePrefix;
            _log = log;
        }

        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
                return;

            for (var i = 0; i < _threadCount; i++)
            {
                var thread = new Thread(ThreadLoop)
                {
                    IsBackground = true,
                    Name = _threadCount == 1 ? _threadNamePrefix : $"{_threadNamePrefix}-{i}"
                };
                _threads.Add(thread);
                thread.Start();
            }
        }

        /// <summary>Runs <paramref name="work"/> on a pool thread with that thread's connection.</summary>
        public Task<T> Run<T>(Func<SqliteConnection, T> work, CancellationToken cancellationToken)
        {
            var item = new WorkItem<T>(work, cancellationToken);
            try
            {
                if (!_queue.TryAdd(item))
                    item.Fail(new InvalidOperationException("The SQLite worker queue rejected the item."));
            }
            catch (InvalidOperationException)
            {
                item.Fail(new OperationCanceledException("The SQLite worker pool is stopped."));
            }

            return item.Task;
        }

        private void ThreadLoop()
        {
            using var holder = new ConnectionHolder(_connectionString, _log);
            try
            {
                foreach (var item in _queue.GetConsumingEnumerable(_shutdown.Token))
                {
                    if (item.IsCancelled)
                    {
                        item.Cancel();
                        continue;
                    }

                    try
                    {
                        item.Execute(holder.Get());
                    }
                    catch (Exception e)
                    {
                        // item.Execute already faulted its own task; this catches a failure to open.
                        item.Fail(e);
                        if (ConnectionHolder.ShouldReset(e))
                            holder.Reset();
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                _log.Debug("[{0}] worker thread stopping.", Thread.CurrentThread.Name);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            _queue.CompleteAdding();
            _shutdown.Cancel();
            foreach (var thread in _threads)
            {
                if (!thread.Join(JoinTimeout))
                    _log.Warning("[{0}] thread did not stop within 5s.", thread.Name);
            }

            while (_queue.TryTake(out var left))
                left.Fail(new OperationCanceledException("The SQLite worker pool stopped before this item ran."));

        }

        private abstract class WorkItem
        {
            public abstract bool IsCancelled { get; }
            public abstract void Cancel();
            public abstract void Fail(Exception exception);

            /// <summary>Runs the work and completes the task. Reports a failure to the caller too, by rethrowing connection errors.</summary>
            public abstract void Execute(SqliteConnection connection);
        }

        private sealed class WorkItem<T> : WorkItem
        {
            private readonly Func<SqliteConnection, T> _work;
            private readonly CancellationToken _token;
            private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public WorkItem(Func<SqliteConnection, T> work, CancellationToken token)
            {
                _work = work;
                _token = token;
            }

            public Task<T> Task => _tcs.Task;

            public override bool IsCancelled => _token.IsCancellationRequested;

            public override void Cancel() => _tcs.TrySetCanceled(_token);

            public override void Fail(Exception exception) => _tcs.TrySetException(exception);

            public override void Execute(SqliteConnection connection)
            {
                try
                {
                    _tcs.TrySetResult(_work(connection));
                }
                catch (Exception e)
                {
                    _tcs.TrySetException(e);
                    // let the thread decide whether the connection is still usable
                    if (ConnectionHolder.ShouldReset(e))
                        throw;
                }
            }
        }
    }
}
