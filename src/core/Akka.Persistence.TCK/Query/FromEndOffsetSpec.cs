//-----------------------------------------------------------------------
// <copyright file="FromEndOffsetSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using Akka.Streams.Dsl;
using Xunit;
using Xunit.Sdk;
using static Akka.Persistence.Query.Offset;

namespace Akka.Persistence.TCK.Query
{
    /// <summary>
    /// Verifies that a read journal honors the <see cref="T:Akka.Persistence.Query.FromEnd"/> ("last N events") query offset, which
    /// begins a query at the Nth event from the end of history rather than from the beginning.
    /// <para>
    /// Only read journals that support <see cref="T:Akka.Persistence.Query.FromEnd"/> should inherit from this spec. Backends that
    /// cannot resolve a from-the-end position (e.g. those without a global ordering) should not opt in.
    /// </para>
    /// </summary>
    public abstract class FromEndOffsetSpec : XTestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected IReadJournal ReadJournal { get; set; }

        protected FromEndOffsetSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null)
            : base(config ?? Config.Empty, actorSystemName, output)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentEventsByTag_with_FromEnd_should_return_only_the_last_N_events()
        {
            if (ReadJournal is not ICurrentEventsByTagQuery queries)
                throw IsTypeException.ForMismatchedType(nameof(ICurrentEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            PersistGreenApples(a, 10);
            WaitForTagEvents(queries, "green", 10);

            var probe = queries.CurrentEventsByTag("green", FromEnd(3))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            // only the last three tagged events, in ascending order
            ExpectSeqNr(probe, "a", 8);
            ExpectSeqNr(probe, "a", 9);
            ExpectSeqNr(probe, "a", 10);
            probe.ExpectComplete();
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentEventsByTag_with_FromEnd_larger_than_total_should_return_all_events()
        {
            if (ReadJournal is not ICurrentEventsByTagQuery queries)
                throw IsTypeException.ForMismatchedType(nameof(ICurrentEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            PersistGreenApples(a, 5);
            WaitForTagEvents(queries, "green", 5);

            var probe = queries.CurrentEventsByTag("green", FromEnd(100))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            for (var i = 1; i <= 5; i++)
                ExpectSeqNr(probe, "a", i);
            probe.ExpectComplete();
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentAllEvents_with_FromEnd_should_return_only_the_last_N_events()
        {
            if (ReadJournal is not ICurrentAllEventsQuery queries)
                throw IsTypeException.ForMismatchedType(nameof(ICurrentAllEventsQuery), ReadJournal?.GetType().Name ?? "null");

            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            PersistGreenApples(a, 10);
            WaitForAllEvents(queries, 10);

            var probe = queries.CurrentAllEvents(FromEnd(2))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            ExpectSeqNr(probe, "a", 9);
            ExpectSeqNr(probe, "a", 10);
            probe.ExpectComplete();
        }

        [Fact]
        public virtual void ReadJournal_live_query_EventsByTag_with_FromEnd_should_return_last_N_then_new_events()
        {
            if (ReadJournal is not IEventsByTagQuery queries)
                throw IsTypeException.ForMismatchedType(nameof(IEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

            var a = Sys.ActorOf(Query.TestActor.Props("a"));
            PersistGreenApples(a, 5);

            if (ReadJournal is ICurrentEventsByTagQuery currentQueries)
                WaitForTagEvents(currentQueries, "green", 5);

            var probe = queries.EventsByTag("green", FromEnd(2))
                .RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(10);
            ExpectSeqNr(probe, "a", 4);
            ExpectSeqNr(probe, "a", 5);

            // a live query must continue to observe newly-persisted events past the initial window
            a.Tell("a green apple 6");
            ExpectMsg("a green apple 6-done");
            ExpectSeqNr(probe, "a", 6);
            probe.Cancel();
        }

        private void PersistGreenApples(IActorRef pa, int count)
        {
            for (var i = 1; i <= count; i++)
            {
                pa.Tell($"a green apple {i}");
                ExpectMsg($"a green apple {i}-done");
            }
        }

        private EventEnvelope ExpectSeqNr(TestSubscriber.Probe<EventEnvelope> probe, string persistenceId, long sequenceNr)
        {
            var envelope = probe.ExpectNext<EventEnvelope>(_ => true);
            Assert.Equal(persistenceId, envelope.PersistenceId);
            Assert.Equal(sequenceNr, envelope.SequenceNr);
            return envelope;
        }

        private void WaitForTagEvents(ICurrentEventsByTagQuery queries, string tag, int expectedCount)
        {
            AwaitConditionAsync(async () =>
            {
                var events = await queries.CurrentEventsByTag(tag, NoOffset())
                    .RunWith(Sink.Seq<EventEnvelope>(), Materializer);
                return events.Count >= expectedCount;
            }, max: TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }

        private void WaitForAllEvents(ICurrentAllEventsQuery queries, int expectedCount)
        {
            AwaitConditionAsync(async () =>
            {
                var events = await queries.CurrentAllEvents(NoOffset())
                    .RunWith(Sink.Seq<EventEnvelope>(), Materializer);
                return events.Count >= expectedCount;
            }, max: TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
    }
}
