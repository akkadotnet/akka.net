//-----------------------------------------------------------------------
// <copyright file="EventsByTagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using static Akka.Persistence.Query.Offset;

#nullable enable
namespace Akka.Persistence.TCK.Query
{
    public abstract class EventsByTagSpec : Akka.TestKit.Xunit2.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected IReadJournal? ReadJournal { get; set; }

        protected virtual bool SupportsTagsInEventEnvelope => false;
        
        protected EventsByTagSpec(Config? config = null, string? actorSystemName = null, ITestOutputHelper? output = null)
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
            if (ReadJournal is not IEventsByTagQuery queries)
                throw IsTypeException.ForMismatchedType(nameof(IEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

            var b = Sys.ActorOf(Query.TestActor.Props("b"));
            var d = Sys.ActorOf(Query.TestActor.Props("d"));

            b.Tell("a black car");
            ExpectMsg("a black car-done");

            var blackSrc = queries.EventsByTag("black", offset: NoOffset());
            var probe = blackSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2);
            ExpectEnvelope(probe, "b", 1L, "a black car", "black");
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            d.Tell("a black dog");
            ExpectMsg("a black dog-done");
            d.Tell("a black night");
            ExpectMsg("a black night-done");

            ExpectEnvelope(probe, "d", 1L, "a black dog", "black");
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(10);
            ExpectEnvelope(probe, "d", 2L, "a black night", "black");
            probe.Cancel();
        }

        [Fact]
        public virtual async Task ReadJournal_live_query_EventsByTag_should_find_events_from_offset_exclusive()
        {
            if (ReadJournal is not IEventsByTagQuery queries)
                throw IsTypeException.ForMismatchedType(nameof(IEventsByTagQuery), ReadJournal?.GetType().Name ?? "null");

            var a = Sys.ActorOf(Query.TestActor.Props("a"), "a");
            var b = Sys.ActorOf(Query.TestActor.Props("b"), "b");
            var c = Sys.ActorOf(Query.TestActor.Props("c"), "c");

            a.Tell("hello");
            await ExpectMsgAsync("hello-done");
            a.Tell("a green apple");
            await ExpectMsgAsync("a green apple-done");
            b.Tell("a black car");
            await ExpectMsgAsync("a black car-done");
            a.Tell("something else");
            await ExpectMsgAsync("something else-done");
            a.Tell("a green banana");
            await ExpectMsgAsync("a green banana-done");
            b.Tell("a green leaf");
            await ExpectMsgAsync("a green leaf-done");
            c.Tell("a green cucumber");
            await ExpectMsgAsync("a green cucumber-done");

            var greenSrc1 = queries.EventsByTag("green", offset: NoOffset());
            var probe1 = greenSrc1.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            await probe1.RequestAsync(2);
            await ExpectEnvelopeAsync(probe1, "a", 2L, "a green apple", "green");
            var offs = (await ExpectEnvelopeAsync(probe1, "a", 4L, "a green banana", "green")).Offset;
            await probe1.CancelAsync();

            var greenSrc2 = queries.EventsByTag("green", offset: offs);
            var probe2 = greenSrc2.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            await probe2.RequestAsync(10);
            await ExpectEnvelopeAsync(probe2, "b", 2L, "a green leaf", "green");
            await ExpectEnvelopeAsync(probe2, "c", 1L, "a green cucumber", "green");
            await probe2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            await probe2.CancelAsync();
        }
        
        private async Task<EventEnvelope> ExpectEnvelopeAsync(TestSubscriber.Probe<EventEnvelope> probe, string persistenceId, long sequenceNr, string @event, string tag)
        {
            var envelope = await probe.ExpectNextAsync<EventEnvelope>(_ => true);
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
