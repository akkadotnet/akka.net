//-----------------------------------------------------------------------
// <copyright file="QueryBehaviorSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Embedded.Journal;
using Akka.Persistence.Embedded.Query;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Query
{
    /// <summary>Doubles every event on the way out of the journal, to prove adapters run on queries.</summary>
    public sealed class DoublingReadAdapter : IReadEventAdapter
    {
        public IEventSequence FromJournal(object evt, string manifest) => EventSequence.Create(evt, evt);
    }

    public abstract class QueryBehaviorSpecBase : EmbeddedSpec
    {
        protected QueryBehaviorSpecBase(SqliteTestDb db, Config config, string name, ITestOutputHelper output)
            : base(db, config, name, output)
        {
            Mat = Sys.Materializer();
        }

        protected ActorMaterializer Mat { get; }

        protected SqliteReadJournal ReadJournal => Sys.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);

        protected async Task<IReadOnlyList<EventEnvelope>> RunAsync(Source<EventEnvelope, NotUsed> source)
            => await source.RunWith(Sink.Seq<EventEnvelope>(), Mat).WaitAsync(Timeout);

        protected static string Tags(EventEnvelope e) => string.Join(",", e.Tags.OrderBy(t => t, StringComparer.Ordinal));

        protected async Task WriteOneAsync(string pid, long seq, object payload, params string[] tags)
        {
            var body = tags.Length == 0 ? payload : new Tagged(payload, tags);
            (await WriteAsync(Write(Evt(pid, seq, body)))).Succeeded.Should().BeTrue();
        }

        /// <summary>
        /// The workload of the Akka.Persistence.Sql capture run, so offsets can be compared with its output:
        /// orderings 1..10 with p1 (1-2) and p2 (1-2) deleted afterwards.
        /// </summary>
        protected async Task PersistCaptureWorkloadAsync()
        {
            await WriteOneAsync("p1", 1, new TestEvent("evt-1-untagged"));
            await WriteOneAsync("p3", 1, new TestEvent("custom"));
            (await WriteAsync(Write(
                Evt("p1", 2, new Tagged(new TestEvent("evt-2-red-blue"), new[] { "red", "blue" })),
                Evt("p1", 3, new TestEvent("evt-3-untagged")),
                Evt("p1", 4, new Tagged(new TestEvent("evt-4-red"), new[] { "red" }))))).Succeeded.Should().BeTrue();
            await WriteOneAsync("p1", 5, new TestEvent("evt-5-Red"), "Red");
            await WriteOneAsync("p1", 6, new TestEvent("evt-6-a%b_c"), "a%b_c");
            await WriteOneAsync("p2", 1, new TestEvent("p2-evt-1-red"), "red");
            await WriteOneAsync("p2", 2, new TestEvent("p2-evt-2-red"), "red");
            await DeleteToAsync("p1", 2);
            await DeleteToAsync("p2", 100);
            await WriteOneAsync("p2", 3, new TestEvent("p2-evt-after-delete"));
        }
    }

    /// <summary>Queries over the workload of the Akka.Persistence.Sql capture run. Needs a fresh database per test.</summary>
    public abstract class CaptureWorkloadQuerySpecBase : QueryBehaviorSpecBase
    {
        protected CaptureWorkloadQuerySpecBase(SqliteTestDb db, Config config, string name, ITestOutputHelper output)
            : base(db, config, name, output)
        {
        }

        [Fact(DisplayName = "Should_resolve_FromEnd_like_Sql")]
        public async Task Should_resolve_FromEnd_like_Sql()
        {
            await PersistCaptureWorkloadAsync();

            // Akka.Persistence.Sql 1.5.70 capture: FromEnd(1) on "red" starts at ordering 5,
            // CurrentAllEvents(FromEnd(2)) returns the events at orderings 7 and 10.
            var red = await RunAsync(ReadJournal.CurrentEventsByTag("red", new FromEnd(1)));
            red.Select(e => ((Sequence)e.Offset).Value).Should().Equal(5L);

            var all = await RunAsync(ReadJournal.CurrentAllEvents(new FromEnd(2)));
            all.Select(e => ((Sequence)e.Offset).Value).Should().Equal(7L, 10L);

            var wide = await RunAsync(ReadJournal.CurrentAllEvents(new FromEnd(100)));
            wide.Select(e => ((Sequence)e.Offset).Value).Should().Equal(2L, 4L, 5L, 6L, 7L, 10L);
        }

        [Fact(DisplayName = "Should_return_the_same_events_and_offsets_as_Sql_When_querying_the_capture_workload")]
        public async Task Should_return_the_same_events_and_offsets_as_Sql_When_querying_the_capture_workload()
        {
            await PersistCaptureWorkloadAsync();

            var ids = await ReadJournal.CurrentPersistenceIds().RunWith(Sink.Seq<string>(), Mat).WaitAsync(Timeout);
            ids.Should().Equal("p1", "p2", "p3");

            var all = await RunAsync(ReadJournal.CurrentAllEvents(NoOffset.Instance));
            all.Select(e => (((Sequence)e.Offset).Value, e.PersistenceId, e.SequenceNr))
                .Should().Equal((2L, "p3", 1L), (4L, "p1", 3L), (5L, "p1", 4L), (6L, "p1", 5L), (7L, "p1", 6L), (10L, "p2", 3L));
            all.Select(Tags).Should().Equal("", "", "red", "Red", "a%b_c", "");

            var afterFive = await RunAsync(ReadJournal.CurrentAllEvents(Offset.Sequence(5)));
            afterFive.Select(e => ((Sequence)e.Offset).Value).Should().Equal(6L, 7L, 10L);

            var byPid = await RunAsync(ReadJournal.CurrentEventsByPersistenceId("p1", 3, 4));
            byPid.Select(e => e.SequenceNr).Should().Equal(3L, 4L);
            byPid.Select(e => ((Sequence)e.Offset).Value).Should().Equal(4L, 5L);
        }
    }

    public class QueryBehaviorSpec : CaptureWorkloadQuerySpecBase
    {
        public QueryBehaviorSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private QueryBehaviorSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(
                db,
                SqliteSpecConfig.Create(db, $$"""
                    akka.persistence.query.journal.embedded {
                        max-buffer-size = 3
                    }
                    akka.persistence.journal.embedded {
                        event-adapters.doubler = "{{typeof(DoublingReadAdapter).AssemblyQualifiedName}}"
                        event-adapter-bindings."{{typeof(ObjectManifestEvent).AssemblyQualifiedName}}" = doubler
                    }
                    """),
                nameof(QueryBehaviorSpec),
                output)
        {
        }

        [Fact(DisplayName = "Should_match_tags_exactly")]
        public async Task Should_match_tags_exactly()
        {
            await WriteOneAsync("exact", 1, new TestEvent("a"), "red");
            await WriteOneAsync("exact", 2, new TestEvent("b"), "Red");

            (await RunAsync(ReadJournal.CurrentEventsByTag("red", NoOffset.Instance))).Select(e => e.SequenceNr).Should().Equal(1L);
            (await RunAsync(ReadJournal.CurrentEventsByTag("RED", NoOffset.Instance))).Should().BeEmpty();
        }

        [Fact(DisplayName = "Should_treat_percent_and_underscore_in_tag_literally_When_querying_by_tag")]
        public async Task Should_treat_percent_and_underscore_in_tag_literally_When_querying_by_tag()
        {
            await WriteOneAsync("like", 1, new TestEvent("a"), "a%b_c");

            (await RunAsync(ReadJournal.CurrentEventsByTag("a%b_c", NoOffset.Instance))).Should().HaveCount(1);
            (await RunAsync(ReadJournal.CurrentEventsByTag("a%", NoOffset.Instance))).Should().BeEmpty();
            (await RunAsync(ReadJournal.CurrentEventsByTag("a_b_c", NoOffset.Instance))).Should().BeEmpty();
            (await RunAsync(ReadJournal.CurrentEventsByTag("A%B_C", NoOffset.Instance))).Should().BeEmpty();
        }

        [Fact(DisplayName = "Should_return_tags_containing_semicolon_intact_When_querying_by_tag")]
        public async Task Should_return_tags_containing_semicolon_intact_When_querying_by_tag()
        {
            await WriteOneAsync("semi", 1, new TestEvent("a"), "a;b", "plain");

            var found = await RunAsync(ReadJournal.CurrentEventsByTag("a;b", NoOffset.Instance));

            found.Should().HaveCount(1);
            found[0].Tags.Should().BeEquivalentTo("a;b", "plain");
        }

        [Fact(DisplayName = "Should_page_persistence_ids_When_more_than_max_buffer_size")]
        public async Task Should_page_persistence_ids_When_more_than_max_buffer_size()
        {
            var pids = Enumerable.Range(1, 8).Select(i => $"page-{i}").ToArray();
            foreach (var pid in pids)
                await WriteOneAsync(pid, 1, new TestEvent("x"));

            // max-buffer-size is 3, so this takes three round trips
            var ids = await ReadJournal.CurrentPersistenceIds().RunWith(Sink.Seq<string>(), Mat).WaitAsync(Timeout);

            ids.Should().Equal(pids.OrderBy(p => p, StringComparer.Ordinal));
        }

        [Fact(DisplayName = "Should_apply_event_adapters_When_querying")]
        public async Task Should_apply_event_adapters_When_querying()
        {
            await WriteOneAsync("adapted", 1, new ObjectManifestEvent("twice"), "t");

            var found = await RunAsync(ReadJournal.CurrentEventsByPersistenceId("adapted", 0, long.MaxValue));

            // the adapter turns one stored event into two envelopes that share offset, sequence number and tags
            found.Should().HaveCount(2);
            found.Select(e => e.Event).Should().AllBeEquivalentTo(new ObjectManifestEvent("twice"));
            found.Select(e => ((Sequence)e.Offset).Value).Distinct().Should().HaveCount(1);
            found.Select(e => e.SequenceNr).Distinct().Should().Equal(1L);
            found.SelectMany(e => e.Tags).Should().Equal("t", "t");
        }

        [Fact(DisplayName = "Should_wait_for_journal_initialization_When_query_starts_first")]
        public async Task Should_wait_for_journal_initialization_When_query_starts_first()
        {
            // nothing has touched the journal yet: the query starts it and waits for its tables
            var ids = await ReadJournal.CurrentPersistenceIds().RunWith(Sink.Seq<string>(), Mat).WaitAsync(Timeout);

            ids.Should().BeEmpty();
            Db.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'journal'").Should().HaveCount(1);
        }

        [Fact(DisplayName = "Should_not_stall_When_live_query_crosses_physically_deleted_rows")]
        public async Task Should_not_stall_When_live_query_crosses_physically_deleted_rows()
        {
            await WriteOneAsync("holes", 1, new TestEvent("a"));
            await WriteOneAsync("holes", 2, new TestEvent("b"));
            await WriteOneAsync("holes", 3, new TestEvent("c"));
            await DeleteToAsync("holes", 3); // physically removes orderings 1 and 2, tombstone at 3

            var probe = ReadJournal.AllEvents(NoOffset.Instance).RunWith(this.SinkProbe<EventEnvelope>(), Mat);
            await probe.RequestAsync(5);
            await WriteOneAsync("after-holes", 1, new TestEvent("d"));

            // APS's tracker stalls for query-delay x max-tries (10 s by default) at a hole, so a short wait fails a stall
            var next = await probe.ExpectNextAsync(TimeSpan.FromSeconds(2));
            next.PersistenceId.Should().Be("after-holes");
            ((Sequence)next.Offset).Value.Should().Be(4L);
            probe.Cancel();
        }

        [Fact(DisplayName = "Should_not_return_the_tombstone_event_When_querying_by_tag")]
        public async Task Should_not_return_the_tombstone_event_When_querying_by_tag()
        {
            await WriteOneAsync("tomb-tag", 1, new TestEvent("a"), "tomb");
            await WriteOneAsync("tomb-tag", 2, new TestEvent("b"), "tomb");
            await WriteOneAsync("tomb-tag", 3, new TestEvent("c"), "tomb");

            await DeleteToAsync("tomb-tag", 3);

            // the tombstone (sequence number 3) keeps its tag row, so only the deleted flag keeps it out of the results
            Db.Query("SELECT sequence_nr FROM tags WHERE persistence_id = 'tomb-tag'").Select(r => r[0]).Should().Equal(3L);
            Db.Query("SELECT deleted FROM journal WHERE persistence_id = 'tomb-tag'").Select(r => r[0]).Should().Equal(1L);

            (await RunAsync(ReadJournal.CurrentEventsByTag("tomb", NoOffset.Instance))).Should().BeEmpty();
            (await RunAsync(ReadJournal.CurrentEventsByTag("tomb", new FromEnd(1)))).Should().BeEmpty();
            (await RunAsync(ReadJournal.CurrentAllEvents(NoOffset.Instance))).Should().BeEmpty();
            (await RunAsync(ReadJournal.CurrentEventsByPersistenceId("tomb-tag", 0, long.MaxValue))).Should().BeEmpty();

            // the live query skips it too, and still sees what comes after
            var probe = ReadJournal.EventsByTag("tomb", NoOffset.Instance).RunWith(this.SinkProbe<EventEnvelope>(), Mat);
            await probe.RequestAsync(5);
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
            await WriteOneAsync("tomb-tag", 4, new TestEvent("d"), "tomb");
            (await probe.ExpectNextAsync(Timeout)).SequenceNr.Should().Be(4L);
            probe.Cancel();

            (await RunAsync(ReadJournal.CurrentEventsByTag("tomb", NoOffset.Instance))).Select(e => e.SequenceNr).Should().Equal(4L);
        }
    }

    /// <summary>Two read journals in one system, with the same write plugin and table names.</summary>
    public class TwoReadJournalsSpec : QueryBehaviorSpecBase
    {
        private const string SecondId = "akka.persistence.query.journal.embedded-2";

        public TwoReadJournalsSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private TwoReadJournalsSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(
                db,
                SqliteSpecConfig.Create(db, """
                    akka.persistence.query.journal.embedded-2 {
                        class = "Akka.Persistence.Embedded.Query.SqliteReadJournalProvider, Akka.Persistence.Embedded"
                        write-plugin = "akka.persistence.journal.embedded"
                        refresh-interval = 100ms
                        max-buffer-size = 1
                    }
                    """),
                nameof(TwoReadJournalsSpec),
                output)
        {
        }

        [Fact(DisplayName = "Should_serve_both_read_journals_When_they_share_a_write_plugin")]
        public async Task Should_serve_both_read_journals_When_they_share_a_write_plugin()
        {
            await WriteOneAsync("two", 1, new TestEvent("a"));
            await WriteOneAsync("two", 2, new TestEvent("b"));

            var second = Sys.ReadJournalFor<SqliteReadJournal>(SecondId);
            var first = ReadJournal;

            first.Should().NotBeSameAs(second);
            foreach (var journal in new[] { first, second })
            {
                // the second section pages one row at a time, the first 500: same events either way
                var events = await journal.CurrentEventsByPersistenceId("two", 0, long.MaxValue).RunWith(Sink.Seq<EventEnvelope>(), Mat).WaitAsync(Timeout);
                events.Select(e => e.SequenceNr).Should().Equal(1L, 2L);
            }
        }
    }
}
