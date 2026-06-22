//-----------------------------------------------------------------------
// <copyright file="FromEndOffsetSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
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
    /// Individual tests are <c>virtual</c> so a backend can override or skip the dimensions it does not implement.
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
            await WaitForTagEventsAsync(queries, "green", GreenEvents.Length);

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
            await WaitForTagEventsAsync(queries, "green", GreenEvents.Length);

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

        #endregion

        #region by-tag, live

        [Fact]
        public virtual async Task ReadJournal_live_query_EventsByTag_with_FromEnd_should_return_last_N_then_new_events()
        {
            var queries = RequireQuery<IEventsByTagQuery>();
            var (_, _, c) = await PersistInterleavedFixtureAsync();

            // stabilize the from-end window before materializing, so the initial batch is exactly the last N
            if (ReadJournal is ICurrentEventsByTagQuery currentQueries)
                await WaitForTagEventsAsync(currentQueries, "green", GreenEvents.Length);

            var probe = queries.EventsByTag("green", FromEnd(2))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            await ExpectEnvelopeAsync(probe, "b", 2);
            await ExpectEnvelopeAsync(probe, "c", 2);

            // a live query must continue to observe newly-persisted matching events past the initial window
            await PersistAsync(c, "a green pear");
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
            await WaitForAllEventsAsync(queries, AllEvents.Length);

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
            await WaitForAllEventsAsync(queries, AllEvents.Length);

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
            var (a, _, _) = await PersistInterleavedFixtureAsync();

            if (ReadJournal is ICurrentAllEventsQuery currentQueries)
                await WaitForAllEventsAsync(currentQueries, AllEvents.Length);

            var probe = queries.AllEvents(FromEnd(2))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            await ExpectEnvelopeAsync(probe, "a", 3);
            await ExpectEnvelopeAsync(probe, "c", 2);

            // a live all-events query must keep emitting newly-persisted events (tagged or not) past the window
            await PersistAsync(a, "brand new event");
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
            await WaitForAllEventsAsync(allQueries, AllEvents.Length);

            var greenLastTwo = await tagQueries.CurrentEventsByTag("green", FromEnd(2))
                .RunWith(Sink.Seq<EventEnvelope>(), Materializer);
            var allLastTwo = await allQueries.CurrentAllEvents(FromEnd(2))
                .RunWith(Sink.Seq<EventEnvelope>(), Materializer);

            // "last 2 green" is resolved against the per-tag count → the last two *tagged* events...
            Assert.Equal(
                new[] { ("b", 2L), ("c", 2L) },
                greenLastTwo.Select(e => (e.PersistenceId, e.SequenceNr)).ToArray());

            // ...whereas "last 2 of everything" is resolved against the total count → includes the untagged a-3.
            Assert.Equal(
                new[] { ("a", 3L), ("c", 2L) },
                allLastTwo.Select(e => (e.PersistenceId, e.SequenceNr)).ToArray());

            // a backend that resolved the by-tag window against the global count would return the same window for both
            Assert.NotEqual(
                allLastTwo.Select(e => (e.PersistenceId, e.SequenceNr)).ToArray(),
                greenLastTwo.Select(e => (e.PersistenceId, e.SequenceNr)).ToArray());
        }

        #endregion

        #region fixture

        /// <summary>
        /// The events of <see cref="PersistInterleavedFixtureAsync"/> in global (ordering) order, as
        /// (persistenceId, sequenceNr) pairs. Three writers are interleaved and several events are untagged.
        /// </summary>
        protected static readonly (string PersistenceId, long SequenceNr)[] AllEvents =
        {
            ("a", 1), // "a green apple"    [green]
            ("b", 1), // "a black car"      [black]
            ("a", 2), // "just plain text"  [untagged]
            ("c", 1), // "a green banana"   [green]
            ("b", 2), // "a green leaf"     [green]
            ("a", 3), // "more plain text"  [untagged]
            ("c", 2), // "a green cucumber" [green]
        };

        /// <summary>
        /// The subset of <see cref="AllEvents"/> tagged "green", in global order.
        /// </summary>
        protected static readonly (string PersistenceId, long SequenceNr)[] GreenEvents =
        {
            ("a", 1),
            ("c", 1),
            ("b", 2),
            ("c", 2),
        };

        /// <summary>
        /// Persists an interleaved, multi-persistence-id stream that mixes "green"-tagged and untagged events, awaiting
        /// each write's acknowledgement so the resulting global ordering is deterministic. Returns the three writers so
        /// live tests can append further events.
        /// </summary>
        private async Task<(IActorRef a, IActorRef b, IActorRef c)> PersistInterleavedFixtureAsync()
        {
            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            var b = Sys.ActorOf(Query.TestActor.Props("b"));
            var c = Sys.ActorOf(Query.TestActor.Props("c"));

            await PersistAsync(a, "a green apple");
            await PersistAsync(b, "a black car");
            await PersistAsync(a, "just plain text");
            await PersistAsync(c, "a green banana");
            await PersistAsync(b, "a green leaf");
            await PersistAsync(a, "more plain text");
            await PersistAsync(c, "a green cucumber");

            return (a, b, c);
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

        private async Task WaitForTagEventsAsync(ICurrentEventsByTagQuery queries, string tag, int expectedCount)
        {
            await AwaitConditionAsync(async () =>
            {
                var events = await queries.CurrentEventsByTag(tag, NoOffset())
                    .RunWith(Sink.Seq<EventEnvelope>(), Materializer);
                return events.Count >= expectedCount;
            }, max: TimeSpan.FromSeconds(10));
        }

        private async Task WaitForAllEventsAsync(ICurrentAllEventsQuery queries, int expectedCount)
        {
            await AwaitConditionAsync(async () =>
            {
                var events = await queries.CurrentAllEvents(NoOffset())
                    .RunWith(Sink.Seq<EventEnvelope>(), Materializer);
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
