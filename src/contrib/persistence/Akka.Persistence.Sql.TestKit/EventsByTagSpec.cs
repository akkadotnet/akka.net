//-----------------------------------------------------------------------
// <copyright file="EventsByTagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Persistence.Query.Sql;
using Akka.Streams;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.TestKit
{
    public abstract class EventsByTagSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer _materializer;
        private readonly SqlReadJournal _queries;

        protected EventsByTagSpec(Config config, ITestOutputHelper output) : base(config, output: output)
        {
            _materializer = Sys.Materializer();
            _queries = Sys.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);
        }

        [Fact]
        public void Sql_query_EventsByTag_should_implement_standard_EventsByTagQuery()
        {
            (_queries is IEventsByTagQuery).Should().BeTrue();
        }

        [Fact]
        public void Sql_query_EventsByTag_should_find_existing_events()
        {
            var a = SetupEmpty("a");
            var b = SetupEmpty("b");

            a.Tell("hello");
            ExpectMsg("hello-done");
            a.Tell("a green apple");    
            ExpectMsg("a green apple-done");
            b.Tell("a black car");
            ExpectMsg("a black car-done");
            a.Tell("a green banana");
            ExpectMsg("a green banana-done");
            b.Tell("a green leaf");
            ExpectMsg("a green leaf-done");

            var greenSrc = _queries.CurrentEventsByTag("green", offset: 0);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(2)
                .ExpectNext(new EventEnvelope(1, "a", 2, "a green apple"))
                .ExpectNext(new EventEnvelope(2, "a", 3, "a green banana"));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            probe.Request(2)
                .ExpectNext(new EventEnvelope(3, "b", 2, "a green leaf"))
                .ExpectComplete();

            var blackSrc = _queries.CurrentEventsByTag("black", offset: 0);
            probe = blackSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(5)
                .ExpectNext(new EventEnvelope(1, "b", 1, "a black car"))
                .ExpectComplete();
        }

        [Fact]
        public void Sql_query_EventsByTag_should_not_see_new_events_after_demand_request()
        {
            Sql_query_EventsByTag_should_find_existing_events();

            var c = SetupEmpty("c");
            var greenSrc = _queries.CurrentEventsByTag("green", offset: 0);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(2)
                .ExpectNext(new EventEnvelope(1, "a", 2, "a green apple"))
                .ExpectNext(new EventEnvelope(2, "a", 3, "a green banana"))
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            c.Tell("a green cucumber");
            ExpectMsg("a green cucumber-done");

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(5)
                .ExpectNext(new EventEnvelope(3, "b", 2, "a green leaf"))
                .ExpectComplete();
        }

        [Fact]
        public void Sql_query_EventsByTag_should_find_events_from_offset()
        {
            Sql_query_EventsByTag_should_not_see_new_events_after_demand_request();

            var greenSrc = _queries.CurrentEventsByTag("green", offset: 2);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(10)
                .ExpectNext(new EventEnvelope(2, "a", 3, "a green banana"))
                .ExpectNext(new EventEnvelope(3, "b", 2, "a green leaf"))
                .ExpectNext(new EventEnvelope(4, "c", 1, "a green cucumber"))
                .ExpectComplete();
        }

        [Fact]
        public void Sql_live_query_EventsByTag_should_find_new_events()
        {
            Sql_query_EventsByTag_should_find_events_from_offset();

            var d = SetupEmpty("d");

            var blackSrc = _queries.EventsByTag("black", offset: 0);
            var probe = blackSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(2)
                .ExpectNext(new EventEnvelope(1, "b", 1, "a black car"))
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            d.Tell("a black dog");
            ExpectMsg("a black dog-done");
            d.Tell("a black night");
            ExpectMsg("a black night-done");

            probe.ExpectNext(new EventEnvelope(2, "d", 1, "a black dog"))
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(10)
                .ExpectNext(new EventEnvelope(3, "d", 2, "a black night"));
        }

        [Fact]
        public void Sql_live_query_EventsByTag_should_find_events_from_offset()
        {
            Sql_live_query_EventsByTag_should_find_new_events();

            var greenSrc = _queries.EventsByTag("green", offset: 2L);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(10)
                .ExpectNext(new EventEnvelope(2L, "a", 3L, "a green banana"))
                .ExpectNext(new EventEnvelope(3L, "b", 2L, "a green leaf"))
                .ExpectNext(new EventEnvelope(4L, "c", 1L, "a green cucumber"))
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        private IActorRef SetupEmpty(string persistenceId)
        {
            return Sys.ActorOf(TestKit.TestActor.Props(persistenceId));
        }

        protected override void Dispose(bool disposing)
        {
            _materializer.Dispose();
            base.Dispose(disposing);
        }
    }

    public class ColorTagger : IWriteEventAdapter
    {
        public static readonly IImmutableSet<string> Colors = ImmutableHashSet.CreateRange(new[] { "green", "black", "blue" });
        public string Manifest(object evt) => string.Empty;

        public object ToJournal(object evt)
        {
            var s = evt as string;
            if (s != null)
            {
                var tags = Colors.Aggregate(ImmutableHashSet<string>.Empty, (acc, color) => s.Contains(color) ? acc.Add(color) : acc);
                return tags.IsEmpty
                    ? evt
                    : new Tagged(evt, tags);
            }
            else return evt;
        }
    }
}