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
    public class AllEventsSpec : Akka.TestKit.Xunit2.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected IReadJournal ReadJournal { get; set; }

        protected AllEventsSpec(
            Config config = null, 
            string actorSystemName = null, 
            ITestOutputHelper output = null)
            : base(config ?? Config.Empty, actorSystemName, output)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void ReadJournal_should_implement_IAllEventsQuery()
        {
            Assert.IsAssignableFrom<IAllEventsQuery>(ReadJournal);
        }

        [Fact]
        public virtual void ReadJournal_query_AllEvents_should_find_new_events()
        {
            var queries = ReadJournal as IAllEventsQuery;
            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            var b = Sys.ActorOf(Query.TestActor.Props("b"));

            a.Tell("hello");
            ExpectMsg("hello-done");

            var eventSrc = queries.AllEvents(NoOffset.Instance);
            var probe = eventSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2);
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 1L && p.Event.Equals("hello"));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            a.Tell("world");
            ExpectMsg("world-done");
            b.Tell("test");
            ExpectMsg("test-done");

            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 2L && p.Event.Equals("world"));

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            probe.Request(10);
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "b" && p.SequenceNr == 1L && p.Event.Equals("test"));
            probe.Cancel();
        }


        [Fact]
        public virtual void ReadJournal_query_AllEvents_should_find_events_from_offset_exclusive()
        {
            var queries = ReadJournal as IAllEventsQuery;

            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            var b = Sys.ActorOf(Query.TestActor.Props("b"));
            var c = Sys.ActorOf(Query.TestActor.Props("c"));

            a.Tell("keep");
            ExpectMsg("keep-done");
            a.Tell("calm");
            ExpectMsg("calm-done");
            b.Tell("and");
            ExpectMsg("and-done");
            a.Tell("keep");
            ExpectMsg("keep-done");
            a.Tell("streaming");
            ExpectMsg("streaming-done");

            var eventSrc1 = queries.AllEvents(NoOffset.Instance);
            var probe1 = eventSrc1.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe1.Request(4);
            probe1.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 1L && p.Event.Equals("keep"));
            probe1.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 2L && p.Event.Equals("calm"));
            probe1.ExpectNext<EventEnvelope>(p => p.PersistenceId == "b" && p.SequenceNr == 1L && p.Event.Equals("and"));
            var offs = probe1.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 3L && p.Event.Equals("keep")).Offset;
            probe1.Cancel();

            var eventSrc2 = queries.AllEvents(offs);
            var probe2 = eventSrc2.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe2.Request(10);

            b.Tell("new");
            ExpectMsg("new-done");
            c.Tell("events");
            ExpectMsg("events-done");

            // everything before "streaming" are not included, since exclusive offset
            probe2.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 4L && p.Event.Equals("streaming"));
            probe2.ExpectNext<EventEnvelope>(p => p.PersistenceId == "b" && p.SequenceNr == 2L && p.Event.Equals("new"));
            probe2.ExpectNext<EventEnvelope>(p => p.PersistenceId == "c" && p.SequenceNr == 1L && p.Event.Equals("events"));
            probe2.Cancel();
        }
    }
}
