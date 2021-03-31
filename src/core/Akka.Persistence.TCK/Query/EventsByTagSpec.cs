//-----------------------------------------------------------------------
// <copyright file="EventsByTagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Persistence.Query.Offset;

namespace Akka.Persistence.TCK.Query
{
    public abstract class EventsByTagSpec : Akka.TestKit.Xunit2.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected IReadJournal ReadJournal { get; set; }

        protected EventsByTagSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null)
            : base(config ?? Config.Empty, actorSystemName, output)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void ReadJournal_should_implement_IEventsByTagQuery()
        {
            Assert.IsAssignableFrom<IEventsByTagQuery>(ReadJournal);
        }

        [Fact]
        public virtual void ReadJournal_live_query_EventsByTag_should_find_new_events()
        {
            var queries = ReadJournal as IEventsByTagQuery;

            var b = Sys.ActorOf(Query.TestActor.Props("b"));
            var d = Sys.ActorOf(Query.TestActor.Props("d"));

            b.Tell("a black car");
            ExpectMsg("a black car-done");

            var blackSrc = queries.EventsByTag("black", offset: NoOffset());
            var probe = blackSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2);
            ExpectEnvelope(probe, "b", 1L, "a black car");
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            d.Tell("a black dog");
            ExpectMsg("a black dog-done");
            d.Tell("a black night");
            ExpectMsg("a black night-done");

            ExpectEnvelope(probe, "d", 1L, "a black dog");
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(10);
            ExpectEnvelope(probe, "d", 2L, "a black night");
            probe.Cancel();
        }

        [Fact]
        public virtual void ReadJournal_live_query_EventsByTag_should_find_events_from_offset_exclusive()
        {
            var queries = ReadJournal as IEventsByTagQuery;

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

            var greenSrc1 = queries.EventsByTag("green", offset: NoOffset());
            var probe1 = greenSrc1.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe1.Request(2);
            ExpectEnvelope(probe1, "a", 2L, "a green apple");
            var offs = ExpectEnvelope(probe1, "a", 4L, "a green banana").Offset;
            probe1.Cancel();

            var greenSrc2 = queries.EventsByTag("green", offset: offs);
            var probe2 = greenSrc2.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe2.Request(10);
            ExpectEnvelope(probe2, "b", 2L, "a green leaf");
            ExpectEnvelope(probe2, "c", 1L, "a green cucumber");
            probe2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe2.Cancel();
        }

        private EventEnvelope ExpectEnvelope(TestSubscriber.Probe<EventEnvelope> probe, string persistenceId, long sequenceNr, string @event)
        {
            var envelope = probe.ExpectNext<EventEnvelope>(e => true);
            envelope.PersistenceId.Should().Be(persistenceId);
            envelope.SequenceNr.Should().Be(sequenceNr);
            envelope.Event.Should().Be(@event);
            return envelope;
        }
    }
}
