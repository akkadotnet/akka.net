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
using System.Threading;
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
using Microsoft.Data.Sqlite;
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

        /// <summary>Csv tag matching ignores ASCII case, so "red" also finds the event tagged "Red" (ordering 6).</summary>
        protected virtual long LastRedOrdering => 5L;

        [Fact(DisplayName = "Should_resolve_FromEnd_like_Sql")]
        public async Task Should_resolve_FromEnd_like_Sql()
        {
            await PersistCaptureWorkloadAsync();

            // Akka.Persistence.Sql 1.5.70 capture: FromEnd(1) on "red" starts at ordering 5,
            // CurrentAllEvents(FromEnd(2)) returns the events at orderings 7 and 10.
            var red = await RunAsync(ReadJournal.CurrentEventsByTag("red", new FromEnd(1)));
            red.Select(e => ((Sequence)e.Offset).Value).Should().Equal(LastRedOrdering);

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

    public class TagTableQueryBehaviorSpec : CaptureWorkloadQuerySpecBase
    {
        public TagTableQueryBehaviorSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private TagTableQueryBehaviorSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(
                db,
                SqliteSpecConfig.Create(db, SqliteTestMode.TT, $$"""
                    akka.persistence.query.journal.embedded {
                        max-buffer-size = 3
                        max-concurrent-queries = 1
                        query-throttle-timeout = 500ms
                    }
                    akka.persistence.journal.embedded {
                        event-adapters.doubler = "{{typeof(DoublingReadAdapter).AssemblyQualifiedName}}"
                        event-adapter-bindings."{{typeof(ObjectManifestEvent).AssemblyQualifiedName}}" = doubler
                    }
                    """),
                nameof(TagTableQueryBehaviorSpec),
                output)
        {
        }

        [Fact(DisplayName = "Should_match_TagTable_tags_exactly")]
        public async Task Should_match_TagTable_tags_exactly()
        {
            await WriteOneAsync("exact", 1, new TestEvent("a"), "red");
            await WriteOneAsync("exact", 2, new TestEvent("b"), "Red");

            (await RunAsync(ReadJournal.CurrentEventsByTag("red", NoOffset.Instance))).Select(e => e.SequenceNr).Should().Equal(1L);
            (await RunAsync(ReadJournal.CurrentEventsByTag("RED", NoOffset.Instance))).Should().BeEmpty();
        }

        [Fact(DisplayName = "Should_treat_percent_and_underscore_in_tag_literally_When_TagTable")]
        public async Task Should_treat_percent_and_underscore_in_tag_literally_When_TagTable()
        {
            await WriteOneAsync("like", 1, new TestEvent("a"), "a%b_c");

            (await RunAsync(ReadJournal.CurrentEventsByTag("a%b_c", NoOffset.Instance))).Should().HaveCount(1);
            (await RunAsync(ReadJournal.CurrentEventsByTag("a%", NoOffset.Instance))).Should().BeEmpty();
            (await RunAsync(ReadJournal.CurrentEventsByTag("a_b_c", NoOffset.Instance))).Should().BeEmpty();
            (await RunAsync(ReadJournal.CurrentEventsByTag("A%B_C", NoOffset.Instance))).Should().BeEmpty();
        }

        [Fact(DisplayName = "Should_return_tags_containing_semicolon_intact_When_TagTable")]
        public async Task Should_return_tags_containing_semicolon_intact_When_TagTable()
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

        [Fact(DisplayName = "Should_fail_with_timeout_When_query_throttle_is_exceeded")]
        public async Task Should_fail_with_timeout_When_query_throttle_is_exceeded()
        {
            await WriteOneAsync("throttle", 1, new TestEvent("x"));
            await RunAsync(ReadJournal.CurrentEventsByPersistenceId("throttle", 0, long.MaxValue)); // warm: query init done

            using var other = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db.FilePath, Pooling = false }.ConnectionString);
            other.Open();
            using (var lockCommand = other.CreateCommand())
            {
                lockCommand.CommandText = "BEGIN EXCLUSIVE";
                lockCommand.ExecuteNonQuery();
            }

            // the only permit goes to a query that waits for the database lock
            var holding = ReadJournal.CurrentPersistenceIds().RunWith(Sink.Seq<string>(), Mat);
            SpinWait.SpinUntil(() => ReadJournal.AvailablePermitsForTests == 0, Timeout).Should().BeTrue();
            var starved = ReadJournal.CurrentPersistenceIds().RunWith(Sink.Seq<string>(), Mat);

            var thrown = await Record.ExceptionAsync(() => starved.WaitAsync(Timeout));
            var failure = thrown is AggregateException aggregate ? aggregate.Flatten().InnerException : thrown;
            failure.Should().BeOfType<TimeoutException>();
            failure!.Message.Should().Contain("could not start a query within");

            using (var release = other.CreateCommand())
            {
                release.CommandText = "ROLLBACK";
                release.ExecuteNonQuery();
            }

            (await holding.WaitAsync(Timeout)).Should().Equal("throttle");
        }

        [Fact(DisplayName = "Should_not_stall_When_live_query_crosses_physically_deleted_rows")]
        public async Task Should_not_stall_When_live_query_crosses_physically_deleted_rows()
        {
            await WriteOneAsync("holes", 1, new TestEvent("a"));
            await WriteOneAsync("holes", 2, new TestEvent("b"));
            await WriteOneAsync("holes", 3, new TestEvent("c"));
            await DeleteToAsync("holes", 3); // physically removes orderings 1 and 2, tombstone at 3

            var probe = ReadJournal.AllEvents(NoOffset.Instance).RunWith(this.SinkProbe<EventEnvelope>(), Mat);
            probe.Request(5);
            await WriteOneAsync("after-holes", 1, new TestEvent("d"));

            var next = probe.ExpectNext(Timeout);
            next.PersistenceId.Should().Be("after-holes");
            ((Sequence)next.Offset).Value.Should().Be(4L);
            probe.Cancel();
        }
    }

    public class CsvQueryBehaviorSpec : CaptureWorkloadQuerySpecBase
    {
        public CsvQueryBehaviorSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private CsvQueryBehaviorSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db, SqliteTestMode.CSV), nameof(CsvQueryBehaviorSpec), output)
        {
        }

        protected override long LastRedOrdering => 6L;

        [Fact(DisplayName = "Should_match_Csv_tags_ignoring_ascii_case_like_Sql")]
        public async Task Should_match_Csv_tags_ignoring_ascii_case_like_Sql()
        {
            await WriteOneAsync("case", 1, new TestEvent("a"), "red");
            await WriteOneAsync("case", 2, new TestEvent("b"), "Red");
            await WriteOneAsync("case", 3, new TestEvent("c"), "blue");

            foreach (var tag in new[] { "red", "Red", "RED" })
            {
                (await RunAsync(ReadJournal.CurrentEventsByTag(tag, NoOffset.Instance))).Select(e => e.SequenceNr)
                    .Should().Equal(1L, 2L);
            }
        }

        [Fact(DisplayName = "Should_treat_percent_and_underscore_in_tag_literally_When_Csv")]
        public async Task Should_treat_percent_and_underscore_in_tag_literally_When_Csv()
        {
            await WriteOneAsync("like", 1, new TestEvent("a"), "a%b_c");
            await WriteOneAsync("like", 2, new TestEvent("b"), "x~y");

            (await RunAsync(ReadJournal.CurrentEventsByTag("a%b_c", NoOffset.Instance))).Should().HaveCount(1);
            (await RunAsync(ReadJournal.CurrentEventsByTag("a%", NoOffset.Instance))).Should().BeEmpty();
            (await RunAsync(ReadJournal.CurrentEventsByTag("a_b_c", NoOffset.Instance))).Should().BeEmpty();
            // LIKE is ASCII case-insensitive, so the upper-case spelling still matches (same as Akka.Persistence.Sql)
            (await RunAsync(ReadJournal.CurrentEventsByTag("A%B_C", NoOffset.Instance))).Should().HaveCount(1);
            // "~" is the escape character of the LIKE pattern and is escaped itself
            (await RunAsync(ReadJournal.CurrentEventsByTag("x~y", NoOffset.Instance))).Should().HaveCount(1);
            (await RunAsync(ReadJournal.CurrentEventsByTag("x", NoOffset.Instance))).Should().BeEmpty();
        }
    }

    public class BothQueryBehaviorSpec : CaptureWorkloadQuerySpecBase
    {
        public BothQueryBehaviorSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private BothQueryBehaviorSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db, SqliteTestMode.BOTH), nameof(BothQueryBehaviorSpec), output)
        {
        }
    }

    public class GapDetectionQueryBehaviorSpec : QueryBehaviorSpecBase
    {
        public GapDetectionQueryBehaviorSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private GapDetectionQueryBehaviorSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(
                db,
                SqliteSpecConfig.Create(db, SqliteTestMode.TT, """
                    akka.persistence.query.journal.embedded.journal-sequence-retrieval {
                        enabled = on
                        query-delay = 500ms
                        max-tries = 3
                    }
                    """),
                nameof(GapDetectionQueryBehaviorSpec),
                output)
        {
        }

        [Fact(DisplayName = "Should_hold_back_at_gap_When_gap_detection_is_on")]
        public async Task Should_hold_back_at_gap_When_gap_detection_is_on()
        {
            await WriteOneAsync("gap", 1, new TestEvent("a"));
            await WriteOneAsync("gap", 2, new TestEvent("b"));

            var probe = ReadJournal.AllEvents(NoOffset.Instance).RunWith(this.SinkProbe<EventEnvelope>(), Mat);
            probe.Request(10);
            probe.ExpectNext(Timeout).SequenceNr.Should().Be(1L);
            probe.ExpectNext(Timeout).SequenceNr.Should().Be(2L);

            // a row at ordering 5 with 3 and 4 missing: the tracker holds reads back at 2 until it gives up on them
            Db.Execute(
                "INSERT INTO journal (ordering, created, deleted, persistence_id, sequence_number, message, manifest, identifier, writer_uuid) " +
                "SELECT 5, created, deleted, 'gap-later', 1, message, manifest, identifier, writer_uuid FROM journal WHERE ordering = 2");

            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(700));
            var late = probe.ExpectNext(TimeSpan.FromSeconds(30));
            late.PersistenceId.Should().Be("gap-later");
            probe.Cancel();
        }
    }
}
