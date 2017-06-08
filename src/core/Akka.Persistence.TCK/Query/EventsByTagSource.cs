//-----------------------------------------------------------------------
// <copyright file="EventsByTagSource.cs" company="Akka.NET Project">
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
    public abstract class EventsByTagSource : Akka.TestKit.Xunit2.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected IReadJournal ReadJournal { get; set; }

        protected EventsByTagSource(Config config) : base(config)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void ReadJournal_should_implement_IEventsByTagQuery()
        {
            Assert.IsAssignableFrom<IEventsByTagQuery>(ReadJournal);
        }

        [Fact]
        public void ReadJournal_live_query_EventsByTag_should_find_new_events()
        {
            var queries = ReadJournal as IEventsByTagQuery;

            var b = Sys.ActorOf(Query.TestActor.Props("b"));
            var d = Sys.ActorOf(Query.TestActor.Props("d"));

            b.Tell("a black car");
            ExpectMsg("a black car-done");

            var blackSrc = queries.EventsByTag("black", offset: 0L);
            var probe = blackSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2)
                .ExpectNext(new EventEnvelope(0L, "b", 1L, "a black car"))
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            d.Tell("a black dog");
            ExpectMsg("a black dog-done");
            d.Tell("a black night");
            ExpectMsg("a black night-done");

            probe
                .ExpectNext(new EventEnvelope(1L, "d", 1L, "a black dog"))
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe
                .Request(10)
                .ExpectNext(new EventEnvelope(2L, "d", 2L, "a black night"));
        }

        [Fact]
        public void ReadJournal_live_query_EventsByTag_should_find_events_from_offset()
        {
            var queries = ReadJournal as IEventsByTagQuery;

            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            var b = Sys.ActorOf(Query.TestActor.Props("b"));
            var c = Sys.ActorOf(Query.TestActor.Props("c"));

            a.Tell("a green apple");
            ExpectMsg("a green apple-done");
            a.Tell("a green banana");
            ExpectMsg("a green banana-done");
            b.Tell("a black car");
            ExpectMsg("a black car-done");
            b.Tell("a green leaf");
            ExpectMsg("a green leaf-done");
            c.Tell("a green cucumber");
            ExpectMsg("a green cucumber-done");

            var greenSrc = queries.EventsByTag("green", offset: 2L);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);

            probe
                .Request(10)
                // note that banana is not included, since exclusive offset
                .ExpectNext(new EventEnvelope(2L, "b", 2L, "a green leaf"))
                .ExpectNext(new EventEnvelope(3L, "c", 1L, "a green cucumber"))
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }
    }
}
