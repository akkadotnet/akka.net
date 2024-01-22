﻿//-----------------------------------------------------------------------
// <copyright file="CurrentEventsByTagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using static Akka.Persistence.Query.Offset;

namespace Akka.Persistence.TCK.Query
{
    public abstract class CurrentEventsByTagSpec : Akka.TestKit.Xunit2.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected IReadJournal ReadJournal { get; set; }

        protected virtual bool SupportsTagsInEventEnvelope => false;
        
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
            if (ReadJournal is not ICurrentEventsByTagQuery queries)
                throw IsTypeException.ForMismatchedType(nameof(ICurrentEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

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
            ExpectEnvelope(probe, "a", 2L, "a green apple", "green");
            ExpectEnvelope(probe, "a", 4L, "a green banana", "green");
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            probe.Request(2);
            ExpectEnvelope(probe, "b", 2L, "a green leaf", "green");
            probe.ExpectComplete();

            var blackSrc = queries.CurrentEventsByTag("black", offset: NoOffset());
            var probe2 = blackSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe2.Request(5);
            ExpectEnvelope(probe2, "b", 1L, "a black car", "black");
            probe2.ExpectComplete();

            var appleSrc = queries.CurrentEventsByTag("apple", offset: NoOffset());
            var probe3 = appleSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe3.Request(5);
            ExpectEnvelope(probe3, "a", 2L, "a green apple", "apple");
            probe3.ExpectComplete();
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentEventsByTag_should_complete_when_no_events()
        {
            if (ReadJournal is not ICurrentEventsByTagQuery queries)
                throw IsTypeException.ForMismatchedType(nameof(ICurrentEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

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
            if (ReadJournal is not ICurrentEventsByTagQuery queries)
                throw IsTypeException.ForMismatchedType(nameof(ICurrentEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

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

            var c = Sys.ActorOf(Query.TestActor.Props("c"));

            var greenSrc = queries.CurrentEventsByTag("green", offset: NoOffset());
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2);
            ExpectEnvelope(probe, "a", 2L, "a green apple", "green");
            ExpectEnvelope(probe, "a", 4L, "a green banana", "green");
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            c.Tell("a green cucumber");
            ExpectMsg("a green cucumber-done");

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(5);
            ExpectEnvelope(probe, "b", 2L, "a green leaf", "green");
            probe.ExpectComplete(); // green cucumber not seen
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentEventsByTag_should_find_events_from_offset_exclusive()
        {
            if (ReadJournal is not ICurrentEventsByTagQuery queries)
                throw IsTypeException.ForMismatchedType(nameof(ICurrentEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

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
            ExpectEnvelope(probe1, "a", 2L, "a green apple", "green");
            var offs = ExpectEnvelope(probe1, "a", 4L, "a green banana", "green").Offset;
            probe1.Cancel();

            var greenSrc = queries.CurrentEventsByTag("green", offset: offs);
            var probe2 = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe2.Request(10);
            // note that banana is not included, since exclusive offset
            ExpectEnvelope(probe2, "b", 2L, "a green leaf", "green");
            probe2.Cancel();
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentEventsByTag_should_see_all_150_events()
        {
            if (ReadJournal is not ICurrentEventsByTagQuery queries)
                throw IsTypeException.ForMismatchedType(nameof(ICurrentEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

            var a = Sys.ActorOf(Query.TestActor.Props("a"));

            foreach (var _ in Enumerable.Range(1, 150))
            {
                a.Tell("a green apple");
                ExpectMsg("a green apple-done");
            }

            var greenSrc = queries.CurrentEventsByTag("green", offset: NoOffset());
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(150);
            foreach (var i in Enumerable.Range(1, 150))
            {
                ExpectEnvelope(probe, "a", i, "a green apple", "green");
            }

            probe.ExpectComplete();
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        }
        
        [Fact]
        public virtual void ReadJournal_query_CurrentEventsByTag_should_include_timestamp_in_EventEnvelope()
        {
            if (ReadJournal is not ICurrentEventsByTagQuery queries)
                throw IsTypeException.ForMismatchedType(nameof(ICurrentEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

            var a = Sys.ActorOf(Query.TestActor.Props("testTimestamp"));
            
            a.Tell("a green apple");
            ExpectMsg("a green apple-done");
            a.Tell("a black car");
            ExpectMsg("a black car-done");
            a.Tell("a green banana");
            ExpectMsg("a green banana-done");

            var greenSrc = queries.CurrentEventsByTag("green", offset: NoOffset());
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2);
            probe.ExpectNext().Timestamp.Should().BeGreaterThan(0);
            probe.ExpectNext().Timestamp.Should().BeGreaterThan(0);
            probe.Cancel();
        }

        private EventEnvelope ExpectEnvelope(TestSubscriber.Probe<EventEnvelope> probe, string persistenceId, long sequenceNr, string @event, string tag)
        {
            var envelope = probe.ExpectNext<EventEnvelope>(_ => true);
            envelope.PersistenceId.Should().Be(persistenceId);
            envelope.SequenceNr.Should().Be(sequenceNr);
            envelope.Event.Should().Be(@event);
            if (SupportsTagsInEventEnvelope)
            {
                envelope.Tags.Should().NotBeNull();
                envelope.Tags.Should().Contain(tag);
            }
            return envelope;
        }
    }
}
