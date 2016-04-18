//-----------------------------------------------------------------------
// <copyright file="EventsByPersistenceIdSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Query.Sqlite.Tests
{
    public class EventsByPersistenceIdSpec : TestKit.Xunit2.TestKit
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(100);
        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.sqlite""
            akka.persistence.journal.sqlite {{
                class = ""Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                table-name = event_journal
                auto-initialize = on
                connection-string = ""FullUri=file:memdb-journal-{id}.db?mode=memory&cache=shared;""
                refresh-interval = 1s
            }}
            akka.test.single-expect-default = 10s")
            .WithFallback(SqliteReadJournal.DefaultConfiguration());

        private readonly ActorMaterializer _materializer;
        private readonly SqliteReadJournal _queries;

        public EventsByPersistenceIdSpec(ITestOutputHelper output) : base(Config(Counter.GetAndIncrement()), output: output)
        {
            _materializer = Sys.Materializer();
            _queries = Sys.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
        }

        [Fact]
        public void Sqlite_query_EventsByPersistenceId_should_implement_standard_EventsByTagQuery()
        {
            (_queries is IEventsByTagQuery).Should().BeTrue();
        }

        [Fact]
        public void Sqlite_query_EventsByPersistenceId_should_find_existing_events()
        {
            var pref = Setup("a");

            var src = _queries.CurrentEventsByPersistenceId("a", 0, long.MaxValue);
            var probe = src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer)
                .Request(2)
                .ExpectNext("a-1", "a-2")
                .ExpectNoMsg(TimeSpan.FromMilliseconds(500)) as TestSubscriber.Probe<object>;
            probe.Request(2)
                .ExpectNext("a-3")
                .ExpectComplete();
        }

        [Fact]
        public void Sqlite_query_EventsByPersistenceId_should_find_existing_events_up_to_a_sequence_number()
        {
            var pref = Setup("b");
            var src = _queries.CurrentEventsByPersistenceId("b", 0L, 2L);
            var probe = src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer)
                .Request(5)
                .ExpectNext("b-1", "b-2")
                .ExpectComplete();
        }

        [Fact]
        public void Sqlite_query_EventsByPersistenceId_should_not_see_new_events_after_demand_request()
        {
            var pref = Setup("f");
            var src = _queries.CurrentEventsByPersistenceId("f", 0L, long.MaxValue);
            var probe = src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer)
                .Request(2)
                .ExpectNext("f-1", "f-2")
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100)) as TestSubscriber.Probe<object>;

            pref.Tell("f-4");
            ExpectMsg("f-4-done");

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(5)
                .ExpectNext("f-3")
                .ExpectComplete(); // f-4 not seen
        }

        [Fact]
        public void Sqlite_query_EventsByPersistenceId_should_return_empty_stream_for_cleaned_journal_from_0_to_MaxLong()
        {
            var pref = Setup("g1");

            pref.Tell(new Tests.TestActor.DeleteCommand(3));
            ExpectMsg("3-deleted");

            var src = _queries.CurrentEventsByPersistenceId("g1", 0, long.MaxValue);
            src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer).Request(1).ExpectComplete();
        }

        [Fact]
        public void Sqlite_query_EventsByPersistenceId_should_return_empty_stream_for_cleaned_journal_from_0_to_0()
        {
            var pref = Setup("g2");

            pref.Tell(new Tests.TestActor.DeleteCommand(3));
            ExpectMsg("3-deleted");

            var src = _queries.CurrentEventsByPersistenceId("g2", 0, 0);
            src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer).Request(1).ExpectComplete();
        }

        [Fact]
        public void Sqlite_query_EventsByPersistenceId_should_return_remaining_values_after_partial_journal_cleanup()
        {
            var pref = Setup("h");

            pref.Tell(new Tests.TestActor.DeleteCommand(2));
            ExpectMsg("2-deleted");

            var src = _queries.CurrentEventsByPersistenceId("h", 0, long.MaxValue);
            src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer).Request(1).ExpectNext("h-3").ExpectComplete();
        }

        [Fact]
        public void Sqlite_query_EventsByPersistenceId_should_return_empty_stream_for_empty_journal()
        {
            var pref = Setup("i");

            var src = _queries.CurrentEventsByPersistenceId("i", 0L, long.MaxValue);
            src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer).Request(1).ExpectComplete();
        }

        [Fact]
        public void Sqlite_query_EventsByPersistenceId_should_return_empty_stream_for_journal_from_0_to_0()
        {
            var pref = Setup("k1");

            var src = _queries.CurrentEventsByPersistenceId("k1", 0, 0);
            src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer).Request(1).ExpectComplete();
        }

        [Fact]
        public void Sqlite_query_EventsByPersistenceId_should_return_empty_stream_for_empty_journal_from_0_to_0()
        {
            var pref = SetupEmpty("k2");

            var src = _queries.CurrentEventsByPersistenceId("k2", 0, 0);
            src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer).Request(1).ExpectComplete();
        }

        [Fact]
        public void Sqlite_query_EventsByPersistenceId_should_return_empty_stream_for_journal_from_SequenceNr_greater_than_HighestSequenceNr()
        {
            var pref = Setup("l");

            var src = _queries.CurrentEventsByPersistenceId("l", 4, 3);
            src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer).Request(1).ExpectComplete();

        }

        [Fact]
        public void Sqlite_live_query_EventsByPersistenceId_should_find_new_events()
        {
            var pref = Setup("c");

            var src = _queries.EventsByPersistenceId("c", 0, long.MaxValue);
            var probe = src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer)
                .Request(5)
                .ExpectNext("c-1", "c-2", "c-3");

            pref.Tell("c-4");
            ExpectMsg("c-4-done");

            probe.ExpectNext("c-4");
        }

        [Fact]
        public void Sqlite_live_query_EventsByPersistenceId_should_find_new_events_up_to_SequenceNr()
        {
            var pref = Setup("d");

            var src = _queries.EventsByPersistenceId("d", 0, 4);
            var probe = src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer)
                .Request(5)
                .ExpectNext("d-1", "d-2", "d-3");

            pref.Tell("d-4");
            ExpectMsg("d-4-done");

            probe.ExpectNext("d-4").ExpectComplete();
        }

        [Fact]
        public void Sqlite_live_query_EventsByPersistenceId_should_find_new_events_after_demand_request()
        {
            var pref = Setup("e");

            var src = _queries.EventsByPersistenceId("e", 0, long.MaxValue);
            var probe = src.Map(x => x.Event).RunWith(this.SinkProbe<object>(), _materializer)
                .Request(2)
                .ExpectNext("e-1", "e-2")
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100)) as TestSubscriber.Probe<object>;

            pref.Tell("e-4");
            ExpectMsg("e-4-done");

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(5)
                .ExpectNext("e-3")
                .ExpectNext("e-4");
        }

        private IActorRef Setup(string persistenceId)
        {
            var pref = SetupEmpty(persistenceId);
            pref.Tell(persistenceId + "-1");
            pref.Tell(persistenceId + "-2");
            pref.Tell(persistenceId + "-3");
            ExpectMsg(persistenceId + "-1-done");
            ExpectMsg(persistenceId + "-2-done");
            ExpectMsg(persistenceId + "-3-done");
            return pref;
        }

        private IActorRef SetupEmpty(string persistenceId)
        {
            return Sys.ActorOf(Tests.TestActor.Props(persistenceId));
        }

        protected override void Dispose(bool disposing)
        {
            _materializer.Dispose();
            base.Dispose(disposing);
        }
    }
}