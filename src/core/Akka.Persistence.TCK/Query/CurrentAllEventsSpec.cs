using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Query
{
    public class CurrentAllEventsSpec : Akka.TestKit.Xunit2.TestKit
    {
        private static readonly Config InternalConfig = "akka.persistence.query.journal.sql.max-buffer-size = 3";

        protected ActorMaterializer Materializer { get; }

        protected IReadJournal ReadJournal { get; set; }

        protected CurrentAllEventsSpec(
            Config config = null,
            string actorSystemName = null,
            ITestOutputHelper output = null)
            : base(
                config == null ? InternalConfig : InternalConfig.WithFallback(config),
                actorSystemName, output)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void ReadJournal_should_implement_ICurrentAllEventsQuery()
        {
            Assert.IsAssignableFrom<ICurrentAllEventsQuery>(ReadJournal);
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentAllEvents_should_find_existing_events()
        {
            var queries = ReadJournal as ICurrentAllEventsQuery;
            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            var b = Sys.ActorOf(Query.TestActor.Props("b"));

            a.Tell("hello");
            ExpectMsg("hello-done");
            a.Tell("world");
            ExpectMsg("world-done");
            b.Tell("test");
            ExpectMsg("test-done");

            var eventSrc = queries.CurrentAllEvents(NoOffset.Instance);
            var probe = eventSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);

            probe.Request(2);
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 1L && p.Event.Equals("hello"));
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 2L && p.Event.Equals("world"));

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

            probe.Request(2);
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "b" && p.SequenceNr == 1L && p.Event.Equals("test"));
            probe.ExpectComplete();
        }

        [Fact]
        public virtual void ReadJournal_query_AllEvents_should_complete_when_no_events()
        {
            var queries = ReadJournal as ICurrentAllEventsQuery;

            var eventSrc = queries.CurrentAllEvents(NoOffset.Instance);
            var probe = eventSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2).ExpectComplete();
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentAllEvents_should_not_see_new_events_after_complete()
        {
            var queries = ReadJournal as ICurrentAllEventsQuery;
            ReadJournal_query_CurrentAllEvents_should_find_existing_events();

            var c = Sys.ActorOf(Query.TestActor.Props("c"));

            var eventSrc = queries.CurrentAllEvents(NoOffset.Instance);
            var probe = eventSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2);
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 1L && p.Event.Equals("hello"));
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 2L && p.Event.Equals("world"));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            c.Tell("a green cucumber");
            ExpectMsg("a green cucumber-done");

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(5);
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "b" && p.SequenceNr == 1L && p.Event.Equals("test"));
            probe.ExpectComplete(); // green cucumber not seen
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentAllEvents_should_find_events_from_offset_exclusive()
        {
            var queries = ReadJournal as ICurrentAllEventsQuery;

            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            var b = Sys.ActorOf(Query.TestActor.Props("b"));

            a.Tell("hello");
            ExpectMsg("hello-done");
            a.Tell("world");
            ExpectMsg("world-done");
            b.Tell("test");
            ExpectMsg("test-done");

            var eventSrc1 = queries.CurrentAllEvents(NoOffset.Instance);
            var probe1 = eventSrc1.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe1.Request(2);
            probe1.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 1L && p.Event.Equals("hello"));
            var offs = probe1.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 2L && p.Event.Equals("world")).Offset;
            probe1.Cancel();

            var eventSrc2 = queries.CurrentAllEvents(offs);
            var probe2 = eventSrc2.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe2.Request(10);
            // hello and world is not included, since exclusive offset
            probe2.ExpectNext<EventEnvelope>(p => p.PersistenceId == "b" && p.SequenceNr == 1L && p.Event.Equals("test"));
            probe2.Cancel();
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentAllEvents_should_see_all_150_events()
        {
            var queries = ReadJournal as ICurrentAllEventsQuery;
            var a = Sys.ActorOf(Query.TestActor.Props("a"));

            for (var i = 0; i < 150; ++i)
            {
                a.Tell("a green apple");
                ExpectMsg("a green apple-done");
            }

            var greenSrc = queries.CurrentAllEvents(NoOffset.Instance);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(150);
            for (var i = 0; i < 150; ++i)
            {
                var idx = i + 1;
                probe.ExpectNext<EventEnvelope>(p =>
                    p.PersistenceId == "a" && p.SequenceNr == idx && p.Event.Equals("a green apple"));
            }

            probe.ExpectComplete();
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        }
    }
}
