//-----------------------------------------------------------------------
// <copyright file="FromEndOffsetSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Xunit;
using Xunit.Sdk;
using static Akka.Persistence.Query.Offset;

#nullable enable
namespace Akka.Persistence.TCK.Query
{
    /// <summary>
    /// Verifies that a read journal honors the <see cref="T:Akka.Persistence.Query.FromEnd"/> ("last N events") query
    /// offset, which begins a query at the Nth event from the end of history rather than from the beginning.
    /// <para>
    /// This is the cross-backend contract for <see cref="T:Akka.Persistence.Query.FromEnd"/>. It exercises the full
    /// matrix of <c>{by-tag, all-events} × {current, live}</c> against an <b>interleaved, multi-persistence-id</b>
    /// fixture that mixes tagged and untagged events. The interleaving is deliberate: a single-persistence-id fixture
    /// cannot distinguish a backend that resolves the from-end position correctly (per-tag count for by-tag queries,
    /// total count for all-events queries) from one that naively resolves both against a single global ordinal,
    /// because the two coincide only when there is one writer and every event carries the tag. The
    /// <c>...should_count_per_tag_...</c> test pins exactly that distinction.
    /// </para>
    /// <para>
    /// Only read journals that support <see cref="T:Akka.Persistence.Query.FromEnd"/> should inherit from this spec.
    /// Backends that cannot resolve a from-the-end position (e.g. those without a global ordering) should not opt in.
    /// A backend must also implement the <c>current</c> counterpart of every query it supports here, since the spec
    /// uses the current queries to confirm writes are visible before resolving a from-end window. Individual tests are
    /// <c>virtual</c> so a backend can override or skip the dimensions it does not implement.
    /// </para>
    /// </summary>
    public abstract class FromEndOffsetSpec : XTestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected IReadJournal? ReadJournal { get; set; }

        protected FromEndOffsetSpec(Config? config = null, string? actorSystemName = null, ITestOutputHelper? output = null)
            : base(config ?? Config.Empty, actorSystemName, output)
        {
            Materializer = Sys.Materializer();
        }

        #region by-tag, current

        [Fact]
        public virtual async Task ReadJournal_query_CurrentEventsByTag_with_FromEnd_should_return_only_the_last_N_events()
        {
            var queries = RequireQuery<ICurrentEventsByTagQuery>();
            await PersistInterleavedFixtureAsync();
            await WaitForVisibleAsync(() => queries.CurrentEventsByTag("green", NoOffset()), GreenEvents.Length);

            var probe = queries.CurrentEventsByTag("green", FromEnd(2))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            // the last two green events, across persistence ids, in ascending order
            await ExpectEnvelopeAsync(probe, "b", 2);
            await ExpectEnvelopeAsync(probe, "c", 2);
            await probe.ExpectCompleteAsync();
        }

        [Fact]
        public virtual async Task ReadJournal_query_CurrentEventsByTag_with_FromEnd_larger_than_total_should_return_all_events()
        {
            var queries = RequireQuery<ICurrentEventsByTagQuery>();
            await PersistInterleavedFixtureAsync();
            await WaitForVisibleAsync(() => queries.CurrentEventsByTag("green", NoOffset()), GreenEvents.Length);

            var probe = queries.CurrentEventsByTag("green", FromEnd(100))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            foreach (var (pid, seqNr) in GreenEvents)
                await ExpectEnvelopeAsync(probe, pid, seqNr);
            await probe.ExpectCompleteAsync();
        }

        [Fact]
        public virtual async Task ReadJournal_query_CurrentEventsByTag_with_FromEnd_and_no_matching_events_should_complete_empty()
        {
            var queries = RequireQuery<ICurrentEventsByTagQuery>();
            // the fixture contains no "blue" events, so a from-end query for that tag must complete with nothing
            await PersistInterleavedFixtureAsync();

            var probe = queries.CurrentEventsByTag("blue", FromEnd(3))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            await probe.ExpectCompleteAsync();
        }

        [Fact]
        public virtual async Task ReadJournal_query_CurrentEventsByTag_with_FromEnd_should_resolve_a_deep_window_in_a_long_history()
        {
            var queries = RequireQuery<ICurrentEventsByTagQuery>();
            // a long single-writer history so the from-end resolution must skip far into the stream, exercising the
            // "start = count - N" arithmetic at depth and a long forward replay (not just the small interleaved fixture)
            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            const int total = 25;
            for (var i = 1; i <= total; i++)
                await PersistAsync(a, $"a green apple {i}");
            await WaitForVisibleAsync(() => queries.CurrentEventsByTag("green", NoOffset()), total);

            var probe = queries.CurrentEventsByTag("green", FromEnd(4))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(20);
            for (var seqNr = total - 4 + 1; seqNr <= total; seqNr++)
                await ExpectEnvelopeAsync(probe, "a", seqNr);
            await probe.ExpectCompleteAsync();
        }

        #endregion

        #region by-tag, live

        [Fact]
        public virtual async Task ReadJournal_live_query_EventsByTag_with_FromEnd_should_return_last_N_then_new_events()
        {
            var queries = RequireQuery<IEventsByTagQuery>();
            // the current counterpart is required to deterministically stabilize the from-end window: the window is
            // resolved once at materialization, so all fixture writes must be visible to the read side beforehand
            var currentQueries = RequireQuery<ICurrentEventsByTagQuery>();
            var actors = await PersistInterleavedFixtureAsync();
            await WaitForVisibleAsync(() => currentQueries.CurrentEventsByTag("green", NoOffset()), GreenEvents.Length);

            var probe = queries.EventsByTag("green", FromEnd(2))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            await ExpectEnvelopeAsync(probe, "b", 2);
            await ExpectEnvelopeAsync(probe, "c", 2);

            // a live query must continue to observe newly-persisted matching events past the initial window
            await PersistAsync(actors["c"], "a green pear");
            await ExpectEnvelopeAsync(probe, "c", 3);
            probe.Cancel();
        }

        #endregion

        #region all-events, current

        [Fact]
        public virtual async Task ReadJournal_query_CurrentAllEvents_with_FromEnd_should_return_only_the_last_N_events()
        {
            var queries = RequireQuery<ICurrentAllEventsQuery>();
            await PersistInterleavedFixtureAsync();
            await WaitForVisibleAsync(() => queries.CurrentAllEvents(NoOffset()), AllEvents.Length);

            var probe = queries.CurrentAllEvents(FromEnd(2))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            // the last two events of the entire journal include the untagged "more plain text" (a-3)
            await ExpectEnvelopeAsync(probe, "a", 3);
            await ExpectEnvelopeAsync(probe, "c", 2);
            await probe.ExpectCompleteAsync();
        }

        [Fact]
        public virtual async Task ReadJournal_query_CurrentAllEvents_with_FromEnd_larger_than_total_should_return_all_events()
        {
            var queries = RequireQuery<ICurrentAllEventsQuery>();
            await PersistInterleavedFixtureAsync();
            await WaitForVisibleAsync(() => queries.CurrentAllEvents(NoOffset()), AllEvents.Length);

            var probe = queries.CurrentAllEvents(FromEnd(100))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(20);
            foreach (var (pid, seqNr) in AllEvents)
                await ExpectEnvelopeAsync(probe, pid, seqNr);
            await probe.ExpectCompleteAsync();
        }

        [Fact]
        public virtual async Task ReadJournal_query_CurrentAllEvents_with_FromEnd_on_empty_journal_should_complete_empty()
        {
            var queries = RequireQuery<ICurrentAllEventsQuery>();
            // nothing persisted: a from-end query against an empty journal must complete, not hang
            var probe = queries.CurrentAllEvents(FromEnd(5))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            await probe.ExpectCompleteAsync();
        }

        #endregion

        #region all-events, live

        [Fact]
        public virtual async Task ReadJournal_live_query_AllEvents_with_FromEnd_should_return_last_N_then_new_events()
        {
            var queries = RequireQuery<IAllEventsQuery>();
            // see the by-tag live test: the current counterpart stabilizes the from-end window before materialization
            var currentQueries = RequireQuery<ICurrentAllEventsQuery>();
            var actors = await PersistInterleavedFixtureAsync();
            await WaitForVisibleAsync(() => currentQueries.CurrentAllEvents(NoOffset()), AllEvents.Length);

            var probe = queries.AllEvents(FromEnd(2))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            await ExpectEnvelopeAsync(probe, "a", 3);
            await ExpectEnvelopeAsync(probe, "c", 2);

            // a live all-events query must keep emitting newly-persisted events (tagged or not) past the window
            await PersistAsync(actors["a"], "brand new event");
            await ExpectEnvelopeAsync(probe, "a", 4);
            probe.Cancel();
        }

        #endregion

        #region cross-query contract

        [Fact]
        public virtual async Task ReadJournal_FromEnd_should_count_per_tag_for_by_tag_and_all_events_for_all_events()
        {
            var tagQueries = RequireQuery<ICurrentEventsByTagQuery>();
            var allQueries = RequireQuery<ICurrentAllEventsQuery>();
            await PersistInterleavedFixtureAsync();
            // stabilize BOTH read paths: the all-events index and the (separately maintained) green tag index can
            // settle independently, so waiting only for all-events would let the green window resolve against a
            // partial tag set on backends whose tag projection lags the global ordering
            await WaitForVisibleAsync(() => allQueries.CurrentAllEvents(NoOffset()), AllEvents.Length);
            await WaitForVisibleAsync(() => tagQueries.CurrentEventsByTag("green", NoOffset()), GreenEvents.Length);

            var greenLastTwo = await tagQueries.CurrentEventsByTag("green", FromEnd(2))
                .RunWith(Sink.Seq<EventEnvelope>(), Materializer);
            var allLastTwo = await allQueries.CurrentAllEvents(FromEnd(2))
                .RunWith(Sink.Seq<EventEnvelope>(), Materializer);

            var green = greenLastTwo.Select(e => (e.PersistenceId, e.SequenceNr)).ToArray();
            var all = allLastTwo.Select(e => (e.PersistenceId, e.SequenceNr)).ToArray();

            // "last 2 green" is resolved against the per-tag count → the last two *tagged* events...
            Assert.Equal(new[] { ("b", 2L), ("c", 2L) }, green);
            // ...whereas "last 2 of everything" is resolved against the total count → includes the untagged a-3.
            Assert.Equal(new[] { ("a", 3L), ("c", 2L) }, all);
            // a backend that resolved the by-tag window against the global count would return the same window for both
            Assert.NotEqual(all, green);
        }

        #endregion

        #region fixture

        /// <summary>
        /// The single source of truth for the interleaved fixture: (persistenceId, event) pairs in persist order.
        /// Three writers (a/b/c) are interleaved and several events are untagged. Tagging is by substring via
        /// <c>ColorFruitTagger</c>, so any event whose text contains "green" is part of the green tag set.
        /// </summary>
        private static readonly (string PersistenceId, string Event)[] Fixture =
        {
            ("a", "a green apple"),
            ("b", "a black car"),
            ("a", "just plain text"),
            ("c", "a green banana"),
            ("b", "a green leaf"),
            ("a", "more plain text"),
            ("c", "a green cucumber"),
        };

        /// <summary>
        /// <see cref="Fixture"/> enriched with the per-persistence-id sequence number each write produces.
        /// </summary>
        private static readonly (string PersistenceId, long SequenceNr, string Event)[] EnrichedFixture = EnrichFixture();

        /// <summary>
        /// Every fixture event in global (ordering) order, as (persistenceId, sequenceNr) pairs.
        /// </summary>
        protected static readonly (string PersistenceId, long SequenceNr)[] AllEvents =
            EnrichedFixture.Select(e => (e.PersistenceId, e.SequenceNr)).ToArray();

        /// <summary>
        /// The subset of <see cref="AllEvents"/> tagged "green", in global order — derived from the fixture text so it
        /// can never drift from the writes.
        /// </summary>
        protected static readonly (string PersistenceId, long SequenceNr)[] GreenEvents =
            EnrichedFixture.Where(e => e.Event.Contains("green")).Select(e => (e.PersistenceId, e.SequenceNr)).ToArray();

        private static (string PersistenceId, long SequenceNr, string Event)[] EnrichFixture()
        {
            var seqByPid = new Dictionary<string, long>();
            var enriched = new List<(string, long, string)>(Fixture.Length);
            foreach (var (pid, evt) in Fixture)
            {
                var next = (seqByPid.TryGetValue(pid, out var s) ? s : 0) + 1;
                seqByPid[pid] = next;
                enriched.Add((pid, next, evt));
            }

            return enriched.ToArray();
        }

        /// <summary>
        /// Persists the interleaved fixture, awaiting each write's acknowledgement so the resulting global ordering is
        /// deterministic. Returns the writers keyed by persistence id so live tests can append further events.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, IActorRef>> PersistInterleavedFixtureAsync()
        {
            var actors = new Dictionary<string, IActorRef>();
            foreach (var (pid, evt) in Fixture)
            {
                if (!actors.TryGetValue(pid, out var actor))
                {
                    actor = Sys.ActorOf(Query.TestActor.Props(pid));
                    actors[pid] = actor;
                }

                await PersistAsync(actor, evt);
            }

            return actors;
        }

        private async Task PersistAsync(IActorRef pa, string evt)
        {
            pa.Tell(evt);
            await ExpectMsgAsync($"{evt}-done");
        }

        private async Task<EventEnvelope> ExpectEnvelopeAsync(
            TestSubscriber.Probe<EventEnvelope> probe, string persistenceId, long sequenceNr)
        {
            var envelope = await probe.ExpectNextAsync<EventEnvelope>(_ => true);
            Assert.Equal(persistenceId, envelope.PersistenceId);
            Assert.Equal(sequenceNr, envelope.SequenceNr);
            return envelope;
        }

        /// <summary>
        /// Polls the supplied current-query until at least <paramref name="expectedCount"/> events are visible, so a
        /// subsequent from-end query resolves its window against a settled read model.
        /// </summary>
        private async Task WaitForVisibleAsync(Func<Source<EventEnvelope, NotUsed>> query, int expectedCount)
        {
            await AwaitConditionAsync(async () =>
            {
                var events = await query().RunWith(Sink.Seq<EventEnvelope>(), Materializer);
                return events.Count >= expectedCount;
            }, max: TimeSpan.FromSeconds(10));
        }

        private T RequireQuery<T>() where T : class, IReadJournal
        {
            if (ReadJournal is not T queries)
                throw IsTypeException.ForMismatchedType(typeof(T).Name, ReadJournal?.GetType().Name ?? "null");
            return queries;
        }

        #endregion
    }
}
