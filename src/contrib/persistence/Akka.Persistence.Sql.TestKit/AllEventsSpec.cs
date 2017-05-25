//-----------------------------------------------------------------------
// <copyright file="AllEventsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Query.Sql;
using Akka.Streams;
using Akka.Streams.TestKit;
using System;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.TestKit
{
    public abstract class AllEventsSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer _materializer;

        protected AllEventsSpec(Config config, ITestOutputHelper output) : base(config, output: output)
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public void Sql_query_CurrentEvents_should_find_existing_events()
        {
            var queries = Sys.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);
            var a = SetupEmpty("a");
            var b = SetupEmpty("b");

            a.Tell("A1");
            ExpectMsg("A1-done");
            a.Tell("A2");
            ExpectMsg("A2-done");
            b.Tell("B1");
            ExpectMsg("B1-done");
            a.Tell("A3");
            ExpectMsg("A3-done");
            b.Tell("B2");
            ExpectMsg("B2-done");

            var src = queries.CurrentEvents();
            var probe = src.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(2)
                .ExpectNext(new EventEnvelope(1, "a", 1, "A1"))
                .ExpectNext(new EventEnvelope(2, "a", 2, "A2"));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            probe.Request(1)
                .ExpectNext(new EventEnvelope(3, "b", 1, "B1"))
                .ExpectComplete();
            probe.Request(5)
                .ExpectNext(new EventEnvelope(4, "a", 3, "A3"))
                .ExpectNext(new EventEnvelope(5, "b", 2, "B2"))
                .ExpectComplete();
        }


        [Fact]
        public void Sql_query_CurrentEvents_should_find_existing_events_by_offset_range()
        {
            var queries = Sys.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);

            Sql_query_CurrentEvents_should_find_existing_events();

            var src = queries.CurrentEvents(fromOffset: 2, toOffset: 4);
            var probe = src.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(10)
                .ExpectNext(new EventEnvelope(2, "a", 2, "A2"))
                .ExpectNext(new EventEnvelope(3, "b", 1, "B1"))
                .ExpectNext(new EventEnvelope(4, "a", 3, "A3"))
                .ExpectComplete();
        }

        [Fact]
        public void Sql_live_query_AllEvents_should_find_new_events()
        {
            var queries = Sys.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);
            var a = SetupEmpty("a");
            var b = SetupEmpty("b");

            a.Tell("A1");
            ExpectMsg("A1-done");
            a.Tell("A2");
            ExpectMsg("A2-done");
            b.Tell("B1");
            ExpectMsg("B1-done");

            var src = queries.AllEvents();
            var probe = src.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(4)
                .ExpectNext(new EventEnvelope(1, "a", 1, "A1"))
                .ExpectNext(new EventEnvelope(2, "a", 2, "A2"))
                .ExpectNext(new EventEnvelope(3, "b", 1, "B1"));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

            b.Tell("B2");
            ExpectMsg("B2-done");
            a.Tell("A3");
            ExpectMsg("A3-done");

            probe.ExpectNext(new EventEnvelope(4, "b", 2, "B2"));

            probe.Request(2)
                .ExpectNext(new EventEnvelope(5, "a", 3, "A3"));
        }

        [Fact]
        public void Sql_live_query_AllEvents_should_find_new_events_by_offset_range()
        {
            var queries = Sys.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);
            var c = SetupEmpty("c");

            Sql_live_query_AllEvents_should_find_new_events();

            var src = queries.AllEvents(fromOffset: 2, toOffset: 6);
            var probe = src.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(10)
                .ExpectNext(new EventEnvelope(2, "a", 2, "A2"))
                .ExpectNext(new EventEnvelope(3, "b", 1, "B1"))
                .ExpectNext(new EventEnvelope(4, "b", 2, "B2"))
                .ExpectNext(new EventEnvelope(5, "a", 3, "A3"));

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

            c.Tell("C1");
            ExpectMsg("C1-done");
            c.Tell("C2");
            ExpectMsg("C1-done");

            probe
                .ExpectNext(new EventEnvelope(5, "c", 1, "C1"))
                .ExpectComplete(); // upperbound passed - we can complete now
        }

        private IActorRef SetupEmpty(string persistenceId) => Sys.ActorOf(TestKit.TestActor.Props(persistenceId));

        protected override void Dispose(bool disposing)
        {
            _materializer.Dispose();
            base.Dispose(disposing);
        }
    }
}