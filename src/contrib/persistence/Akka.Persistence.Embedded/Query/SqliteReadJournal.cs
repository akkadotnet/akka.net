//-----------------------------------------------------------------------
// <copyright file="SqliteReadJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Pattern;
using Akka.Persistence.Embedded.Internal;
using Akka.Persistence.Embedded.Query.Internal;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Util;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Embedded.Query
{
    /// <summary>
    /// Read journal over the SQLite journal tables. Returns the same events, offsets and sequence numbers
    /// as the Akka.Persistence.Sql read journal does for the same database file.
    /// </summary>
    public sealed class SqliteReadJournal :
        IPersistenceIdsQuery,
        ICurrentPersistenceIdsQuery,
        IEventsByPersistenceIdQuery,
        ICurrentEventsByPersistenceIdQuery,
        IEventsByTagQuery,
        ICurrentEventsByTagQuery,
        IAllEventsQuery,
        ICurrentAllEventsQuery
    {
        /// <summary>Config path of the read journal plugin.</summary>
        public const string Identifier = "akka.persistence.query.journal.embedded";

        private readonly ExtendedActorSystem _system;
        private readonly QuerySettings _settings;
        private readonly string _pluginPath;
        private readonly ILoggingAdapter _log;
        private readonly QuerySql _sql;
        private readonly RowCodec _codec;
        private readonly EventAdapters _adapters;
        private readonly IActorRef _journal;
        private readonly SqliteWorkerPool _pool;
        private readonly SemaphoreSlim _throttle;
        private readonly object _initLock = new();
        private Task? _initialization;

        internal string PluginPathForTests => _pluginPath;

        /// <summary>The reference configuration of the plugin. Core adds it to the system config on first use.</summary>
        public static Config DefaultConfiguration() => SqlitePersistence.DefaultConfiguration;

        internal SqliteReadJournal(ExtendedActorSystem system, Config config, string pluginPath)
        {
            _system = system;
            _pluginPath = pluginPath;
            _log = Logging.GetLogger(system, pluginPath);
            _settings = QuerySettings.Create(config, _pluginPath, system.Settings.Config);

            _sql = new QuerySql(_settings);
            _codec = new RowCodec(system);

            var persistence = Persistence.Instance.Apply(system);
            _adapters = persistence.AdaptersFor(_settings.WritePluginId);
            _journal = persistence.JournalFor(_settings.WritePluginId);

            _pool = new SqliteWorkerPool(_settings.Journal.ConnectionString, _settings.QueryThreads, $"{_pluginPath}-query", _log);
            _pool.Start();
            _throttle = new SemaphoreSlim(QuerySettings.MaxConcurrentQueries, QuerySettings.MaxConcurrentQueries);
            system.RegisterOnTermination(() => _pool.Dispose());
        }

        // ---- plumbing ------------------------------------------------------------------------------

        /// <summary>
        /// The first successful handshake is cached and shared by every query, also by queries that start while it is
        /// still running. A failed one (timeout, missing column) is dropped so the next query tries again.
        /// </summary>
        private Task EnsureInitializedAsync()
        {
            var current = Volatile.Read(ref _initialization);
            if (current is not null && !current.IsFaulted && !current.IsCanceled)
                return current;

            lock (_initLock)
            {
                if (_initialization is null || _initialization.IsFaulted || _initialization.IsCanceled)
                    _initialization = InitializeAsync();
                return _initialization;
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                await _journal.Ask<Initialized>(EnsureInitialized.Instance, QuerySettings.WritePluginInitTimeout).ConfigureAwait(false);
            }
            catch (Exception e) when (e is AskTimeoutException or TimeoutException)
            {
                throw new TimeoutException(
                    $"[{_pluginPath}] write plugin [{_settings.WritePluginPath}] did not finish initializing within {QuerySettings.WritePluginInitTimeout}.", e);
            }

            // The read journal never creates tables, but it checks the ones it reads.
            await _pool.Run(
                connection =>
                {
                    SqliteSchema.VerifyJournalSchema(connection, _settings.Journal);
                    return true;
                },
                CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>Runs one SQL round trip on a query thread, after the write plugin is ready and the throttle allows it.</summary>
        private async Task<T> RunAsync<T>(Func<SqliteConnection, T> work)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);

            if (!await _throttle.WaitAsync(QuerySettings.ThrottleTimeout).ConfigureAwait(false))
            {
                throw new TimeoutException(
                    $"[{_pluginPath}] could not start a query within {QuerySettings.ThrottleTimeout}: {QuerySettings.MaxConcurrentQueries} queries are running or waiting.");
            }

            try
            {
                return await _pool.Run(work, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _throttle.Release();
            }
        }

        private Task Delay()
            => FutureTimeoutSupport.After(_settings.RefreshInterval, _system.Scheduler, static () => Task.FromResult(true));

        private static Option<(TState, IReadOnlyList<TElem>)> Next<TState, TElem>(TState state, IReadOnlyList<TElem> items)
            => Option<(TState, IReadOnlyList<TElem>)>.Create((state, items));

        private IReadOnlyList<EventEnvelope> ToEnvelopes(List<RawJournalRow> rows)
        {
            var envelopes = new List<EventEnvelope>(rows.Count);
            foreach (var row in rows)
            {
                var persistent = _codec.ToPersistent(row);
                var tags = RowCodec.SplitTags(row.TagList);
                envelopes.AddRange(RowCodec.ToEnvelopes(persistent, row.Ordering, tags, _adapters));
            }

            return envelopes;
        }

        // ---- persistence ids -----------------------------------------------------------------------

        private sealed record IdsState(string? After, bool Done);

        private sealed class LiveIdsState
        {
            public HashSet<string> Seen { get; } = new();

            public bool Wait { get; set; }
        }

        /// <inheritdoc />
        public Source<string, NotUsed> CurrentPersistenceIds()
            => Source.UnfoldAsync<IdsState, IReadOnlyList<string>>(new IdsState(null, false), CurrentIdsStep)
                .Flatten()
                .Named("CurrentPersistenceIds");

        private async Task<Option<(IdsState, IReadOnlyList<string>)>> CurrentIdsStep(IdsState state)
        {
            if (state.Done)
                return Option<(IdsState, IReadOnlyList<string>)>.None;

            var take = _settings.MaxBufferSize;
            var after = state.After;
            var ids = await RunAsync(connection => _sql.ReadPersistenceIdsPage(connection, after, take)).ConfigureAwait(false);
            if (ids.Count == 0)
                return Option<(IdsState, IReadOnlyList<string>)>.None;

            return Next(new IdsState(ids[^1], ids.Count < take), ids);
        }

        /// <inheritdoc />
        public Source<string, NotUsed> PersistenceIds()
            => Source.UnfoldAsync<LiveIdsState, IReadOnlyList<string>>(new LiveIdsState(), LiveIdsStep)
                .Flatten()
                .Named("AllPersistenceIds");

        private async Task<Option<(LiveIdsState, IReadOnlyList<string>)>> LiveIdsStep(LiveIdsState state)
        {
            if (state.Wait)
                await Delay().ConfigureAwait(false);
            state.Wait = true;

            var take = _settings.MaxBufferSize;
            var fresh = new List<string>();
            string? after = null;
            while (true)
            {
                var page = await RunAsync(connection => _sql.ReadPersistenceIdsPage(connection, after, take)).ConfigureAwait(false);
                foreach (var id in page)
                {
                    if (state.Seen.Add(id))
                        fresh.Add(id);
                }

                if (page.Count < take)
                    break;

                after = page[^1];
            }

            return Next(state, fresh);
        }

        // ---- by persistence id ---------------------------------------------------------------------

        private sealed record PidState(long Next, bool Wait, bool Done);

        /// <inheritdoc />
        public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(string persistenceId, long fromSequenceNr, long toSequenceNr)
            => ByPersistenceId(persistenceId, fromSequenceNr, toSequenceNr, live: false)
                .Named("CurrentEventsByPersistenceId-" + persistenceId);

        /// <inheritdoc />
        public Source<EventEnvelope, NotUsed> EventsByPersistenceId(string persistenceId, long fromSequenceNr, long toSequenceNr)
            => ByPersistenceId(persistenceId, fromSequenceNr, toSequenceNr, live: true)
                .Named("EventsByPersistenceId-" + persistenceId);

        private Source<EventEnvelope, NotUsed> ByPersistenceId(string persistenceId, long from, long to, bool live)
            => Source.UnfoldAsync<PidState, IReadOnlyList<EventEnvelope>>(
                    new PidState(Math.Max(1L, from), false, false),
                    state => ByPersistenceIdStep(persistenceId, to, live, state))
                .Flatten();

        private async Task<Option<(PidState, IReadOnlyList<EventEnvelope>)>> ByPersistenceIdStep(string persistenceId, long to, bool live, PidState state)
        {
            if (state.Done || state.Next > to)
                return Option<(PidState, IReadOnlyList<EventEnvelope>)>.None;

            if (live && state.Wait)
                await Delay().ConfigureAwait(false);

            var take = _settings.MaxBufferSize;
            var next = state.Next;
            var rows = await RunAsync(connection => _sql.ReadByPersistenceId(connection, persistenceId, next, to, take)).ConfigureAwait(false);

            if (rows.Count == 0)
            {
                return live
                    ? Next(state with { Wait = true }, Array.Empty<EventEnvelope>())
                    : Option<(PidState, IReadOnlyList<EventEnvelope>)>.None;
            }

            var envelopes = ToEnvelopes(rows);
            var last = rows[^1].SequenceNr;
            var reachedEnd = last >= to;
            var done = live ? reachedEnd : reachedEnd || rows.Count < take;
            return Next(new PidState(last + 1, live && rows.Count < take, done), envelopes);
        }

        // ---- by tag and all events -----------------------------------------------------------------

        private sealed record OffsetState(Offset Initial, bool Started, long Next, long Max, bool Wait, bool Done);

        /// <inheritdoc />
        public Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag, Offset offset)
            => ByOrdering(tag, offset, live: false).Named($"CurrentEventsByTag-{tag}");

        /// <inheritdoc />
        public Source<EventEnvelope, NotUsed> EventsByTag(string tag, Offset offset)
            => ByOrdering(tag, offset, live: true).Named($"EventsByTag-{tag}");

        /// <inheritdoc />
        public Source<EventEnvelope, NotUsed> CurrentAllEvents(Offset offset)
            => ByOrdering(null, offset, live: false).Named("CurrentAllEvents");

        /// <inheritdoc />
        public Source<EventEnvelope, NotUsed> AllEvents(Offset offset)
            => ByOrdering(null, offset, live: true).Named("AllEvents");

        private Source<EventEnvelope, NotUsed> ByOrdering(string? tag, Offset offset, bool live)
            => Source.UnfoldAsync<OffsetState, IReadOnlyList<EventEnvelope>>(
                    new OffsetState(offset, false, 0L, 0L, false, false),
                    state => ByOrderingStep(tag, live, state))
                .Flatten();

        private async Task<long> ResolveStartAsync(string? tag, Offset? offset)
        {
            switch (offset)
            {
                case Sequence sequence:
                    return sequence.Value;
                case FromEnd fromEnd:
                    {
                        var count = fromEnd.Count;
                        var found = await RunAsync(connection => _sql.ReadFromEndOrdering(connection, tag, count)).ConfigureAwait(false);
                        return found is null ? 0L : found.Value - 1;
                    }
                default:
                    // NoOffset, null and every other offset type start at the beginning
                    return 0L;
            }
        }

        private async Task<Option<(OffsetState, IReadOnlyList<EventEnvelope>)>> ByOrderingStep(string? tag, bool live, OffsetState state)
        {
            if (state.Done)
                return Option<(OffsetState, IReadOnlyList<EventEnvelope>)>.None;

            var next = state.Started ? state.Next : await ResolveStartAsync(tag, state.Initial).ConfigureAwait(false);
            if (live && state.Wait)
                await Delay().ConfigureAwait(false);

            var take = _settings.MaxBufferSize;
            long max;
            List<RawJournalRow> rows;
            var from = next;
            if (!live && state.Started)
            {
                // a current query stops at the max it saw when it started
                max = state.Max;
                rows = await RunAsync(connection => _sql.ReadOrdered(connection, null, tag, from, state.Max, take)).ConfigureAwait(false);
            }
            else
            {
                // MAX(ordering) and the batch come from one read transaction: no committed row can hide below it
                var batch = await RunAsync(connection => _sql.ReadMaxAndBatch(connection, tag, from, take)).ConfigureAwait(false);
                max = batch.MaxOrdering;
                rows = batch.Rows;
            }

            if (!live)
            {
                if (rows.Count == 0)
                    return Option<(OffsetState, IReadOnlyList<EventEnvelope>)>.None;

                var lastOrdering = rows[^1].Ordering;
                var done = rows.Count < take || lastOrdering >= max;
                return Next(state with { Started = true, Next = lastOrdering, Max = max, Done = done }, ToEnvelopes(rows));
            }

            if (rows.Count == 0)
                return Next(state with { Started = true, Next = Math.Max(next, max), Max = max, Wait = true }, Array.Empty<EventEnvelope>());

            return Next(
                state with { Started = true, Next = rows[^1].Ordering, Max = max, Wait = rows.Count < take },
                ToEnvelopes(rows));
        }
    }
}
