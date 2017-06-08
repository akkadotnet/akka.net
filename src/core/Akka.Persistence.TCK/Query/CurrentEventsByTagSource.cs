//-----------------------------------------------------------------------
// <copyright file="CurrentEventsByTagSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using Xunit;

namespace Akka.Persistence.TCK.Query
{
    public abstract class CurrentEventsByTagSource : Akka.TestKit.Xunit2.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected IReadJournal ReadJournal { get; set; }

        protected CurrentEventsByTagSource(Config config) : base(config)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void ReadJournal_should_implement_ICurrentEventsByTagQuery()
        {
            Assert.IsAssignableFrom<ICurrentEventsByTagQuery>(ReadJournal);
        }

        [Fact]
        public void ReadJournal_query_CurrentEventsByTag_should_find_existing_events()
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

            var greenSrc = queries.CurrentEventsByTag("green", offset: 0L);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2)
                .ExpectNext(new EventEnvelope(0L, "a", 2L, "a green apple"))
                .ExpectNext(new EventEnvelope(1L, "a", 4L, "a green banana"));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            probe.Request(2)
                .ExpectNext(new EventEnvelope(2L, "b", 2L, "a green leaf"))
                .ExpectComplete();

            var blackSrc = queries.CurrentEventsByTag("black", offset: 0L);
            probe = blackSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(5)
                .ExpectNext(new EventEnvelope(0L, "b", 1L, "a black car"))
                .ExpectComplete();

            var appleSrc = queries.CurrentEventsByTag("apple", offset: 0L);
            probe = appleSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(5)
                .ExpectNext(new EventEnvelope(0L, "a", 2L, "a green apple"))
                .ExpectComplete();
        }

        [Fact]
        public void ReadJournal_query_CurrentEventsByTag_should_complete_when_no_events()
        {
            var queries = ReadJournal as ICurrentEventsByTagQuery;
            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            var b = Sys.ActorOf(Query.TestActor.Props("b"));

            a.Tell("a green apple");
            ExpectMsg("a green apple-done");
            b.Tell("a black car");
            ExpectMsg("a black car-done");

            var greenSrc = queries.CurrentEventsByTag("pink", offset: 0L);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2).ExpectComplete();
        }

        [Fact(Skip = "Not done in original implementation")]
        public void ReadJournal_query_CurrentEventsByTag_should_not_see_new_events_after_complete()
        {
            var queries = ReadJournal as ICurrentEventsByTagQuery;
            ReadJournal_query_CurrentEventsByTag_should_find_existing_events();

            var c = Sys.ActorOf(Query.TestActor.Props("c"));

            var greenSrc = queries.CurrentEventsByTag("green", offset: 0L);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2)
                .ExpectNext(new EventEnvelope(0L, "a", 2L, "a green apple"))
                .ExpectNext(new EventEnvelope(1L, "a", 4L, "a green banana"))
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            c.Tell("a green cucumber");
            ExpectMsg("a green cucumber-done");

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(5)
                .ExpectNext(new EventEnvelope(2L, "b", 2L, "a green leaf"))
                .ExpectComplete(); // green cucumber not seen
        }

        [Fact]
        public void ReadJournal_query_CurrentEventsByTag_should_find_events_from_offset()
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

            var greenSrc = queries.CurrentEventsByTag("green", offset: 2L);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);

            probe.Request(10)
                .ExpectNext(new EventEnvelope(2L, "b", 2L, "a green leaf"))
                .ExpectNext(new EventEnvelope(3L, "c", 1L, "a green cucumber"))
                .ExpectComplete();
        }
    }
}
