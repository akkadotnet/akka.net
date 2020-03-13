//-----------------------------------------------------------------------
// <copyright file="CurrentEventsByTagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using Xunit;
using Xunit.Abstractions;
using static Akka.Persistence.Query.Offset;

namespace Akka.Persistence.TCK.Query
{
    public abstract class CurrentEventsByTagSpec : Akka.TestKit.Xunit2.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected IReadJournal ReadJournal { get; set; }

        protected CurrentEventsByTagSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null)
            : base(config ?? Config.Empty, actorSystemName, output)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void ReadJournal_should_implement_ICurrentEventsByTagQuery()
        {
            Assert.IsAssignableFrom<ICurrentEventsByTagQuery>(ReadJournal);
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentEventsByTag_should_find_existing_events()
        {
            var queries = ReadJournal as ICurrentEventsByTagQuery;
            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            var b = Sys.ActorOf(Query.TestActor.Props("b"));

            a.Tell("hello");
            ExpectMsg("hello-done");
            a.Tell("a green apple");
            ExpectMsg("a green apple-done");
            b.Tell("a black car");
            ExpectMsg("a black car-done");
            a.Tell("something else");
            ExpectMsg("something else-done");
            a.Tell("a green banana");
            ExpectMsg("a green banana-done");
            b.Tell("a green leaf");
            ExpectMsg("a green leaf-done");

            var greenSrc = queries.CurrentEventsByTag("green", offset: NoOffset());
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2);
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 2L && p.Event.Equals("a green apple"));
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 4L && p.Event.Equals("a green banana"));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            probe.Request(2);
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "b" && p.SequenceNr == 2L && p.Event.Equals("a green leaf"));
            probe.ExpectComplete();

            var blackSrc = queries.CurrentEventsByTag("black", offset: NoOffset());
            var probe2 = blackSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe2.Request(5);
            probe2.ExpectNext<EventEnvelope>(p => p.PersistenceId == "b" && p.SequenceNr == 1L && p.Event.Equals("a black car"));
            probe2.ExpectComplete();

            var appleSrc = queries.CurrentEventsByTag("apple", offset: NoOffset());
            var probe3 = appleSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe3.Request(5);
            probe3.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 2L && p.Event.Equals("a green apple"));
            probe3.ExpectComplete();
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentEventsByTag_should_complete_when_no_events()
        {
            var queries = ReadJournal as ICurrentEventsByTagQuery;
            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            var b = Sys.ActorOf(Query.TestActor.Props("b"));

            a.Tell("a green apple");
            ExpectMsg("a green apple-done");
            b.Tell("a black car");
            ExpectMsg("a black car-done");

            var greenSrc = queries.CurrentEventsByTag("pink", offset: NoOffset());
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2).ExpectComplete();
        }

        // Unit test failed because of time-out but passed when the delays were raised to 300
        [Fact]
        public virtual void ReadJournal_query_CurrentEventsByTag_should_not_see_new_events_after_complete()
        {
            var queries = ReadJournal as ICurrentEventsByTagQuery;
            ReadJournal_query_CurrentEventsByTag_should_find_existing_events();

            var c = Sys.ActorOf(Query.TestActor.Props("c"));

            var greenSrc = queries.CurrentEventsByTag("green", offset: NoOffset());
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2);
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 2L && p.Event.Equals("a green apple"));
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 4L && p.Event.Equals("a green banana"));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            c.Tell("a green cucumber");
            ExpectMsg("a green cucumber-done");

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(5);
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "b" && p.SequenceNr == 2L && p.Event.Equals("a green leaf"));
            probe.ExpectComplete(); // green cucumber not seen
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentEventsByTag_should_find_events_from_offset_exclusive()
        {
            var queries = ReadJournal as ICurrentEventsByTagQuery;

            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            var b = Sys.ActorOf(Query.TestActor.Props("b"));
            var c = Sys.ActorOf(Query.TestActor.Props("c"));

            a.Tell("hello");
            ExpectMsg("hello-done");
            a.Tell("a green apple");
            ExpectMsg("a green apple-done");
            b.Tell("a black car");
            ExpectMsg("a black car-done");
            a.Tell("something else");
            ExpectMsg("something else-done");
            a.Tell("a green banana");
            ExpectMsg("a green banana-done");
            b.Tell("a green leaf");
            ExpectMsg("a green leaf-done");
            c.Tell("a green cucumber");
            ExpectMsg("a green cucumber-done");

            var greenSrc1 = queries.CurrentEventsByTag("green", offset: NoOffset());
            var probe1 = greenSrc1.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe1.Request(2);
            probe1.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 2L && p.Event.Equals("a green apple"));
            var offs = probe1.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 4L && p.Event.Equals("a green banana")).Offset;
            probe1.Cancel();

            var greenSrc = queries.CurrentEventsByTag("green", offset: offs);
            var probe2 = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe2.Request(10);
            // note that banana is not included, since exclusive offset
            probe2.ExpectNext<EventEnvelope>(p => p.PersistenceId == "b" && p.SequenceNr == 2L && p.Event.Equals("a green leaf"));
            probe2.Cancel();
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentEventsByTag_should_see_all_150_events()
        {
            var queries = ReadJournal as ICurrentEventsByTagQuery;
            var a = Sys.ActorOf(Query.TestActor.Props("a"));

            for (int i = 0; i < 150; ++i) 
            {
                a.Tell("a green apple");
                ExpectMsg("a green apple-done");
            }

            var greenSrc = queries.CurrentEventsByTag("green", offset: NoOffset());
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(150);
            for (int i = 0; i < 150; ++i)
            {
                probe.ExpectNext<EventEnvelope>(p => 
                    p.PersistenceId == "a" && p.SequenceNr == (i + 1) && p.Event.Equals("a green apple"));
            }

            probe.ExpectComplete();
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        }
    }
}
