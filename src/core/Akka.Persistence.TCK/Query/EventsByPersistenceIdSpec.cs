//-----------------------------------------------------------------------
// <copyright file="EventsByPersistenceIdSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Query
{
    public abstract class EventsByPersistenceIdSpec : Akka.TestKit.Xunit2.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected IReadJournal ReadJournal { get; set; }

        protected EventsByPersistenceIdSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null)
            : base(config ?? Config.Empty, actorSystemName, output)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void ReadJournal_should_implement_IEventsByPersistenceIdQuery()
        {
            Assert.IsAssignableFrom<IEventsByPersistenceIdQuery>(ReadJournal);
        }

        [Fact]
        public void ReadJournal_live_query_EventsByPersistenceId_should_find_new_events()
        {
            var queries = ReadJournal.AsInstanceOf<IEventsByPersistenceIdQuery>();
            var pref = Setup("c");

            var src = queries.EventsByPersistenceId("c", 0, long.MaxValue);
            var probe = src.Select(x => x.Event).RunWith(this.SinkProbe<object>(), Materializer)
                .Request(5)
                .ExpectNext("c-1", "c-2", "c-3");

            pref.Tell("c-4");
            ExpectMsg("c-4-done");

            probe.ExpectNext("c-4");
        }

        [Fact]
        public void ReadJournal_live_query_EventsByPersistenceId_should_find_new_events_up_to_SequenceNr()
        {
            var queries = ReadJournal.AsInstanceOf<IEventsByPersistenceIdQuery>();
            var pref = Setup("d");

            var src = queries.EventsByPersistenceId("d", 0, 4);
            var probe = src.Select(x => x.Event).RunWith(this.SinkProbe<object>(), Materializer)
                .Request(5)
                .ExpectNext("d-1", "d-2", "d-3");

            pref.Tell("d-4");
            ExpectMsg("d-4-done");

            probe.ExpectNext("d-4").ExpectComplete();
        }

        [Fact]
        public void ReadJournal_live_query_EventsByPersistenceId_should_find_new_events_after_demand_request()
        {
            var queries = ReadJournal.AsInstanceOf<IEventsByPersistenceIdQuery>();
            var pref = Setup("e");

            var src = queries.EventsByPersistenceId("e", 0, long.MaxValue);
            var probe = src.Select(x => x.Event).RunWith(this.SinkProbe<object>(), Materializer)
                .Request(2)
                .ExpectNext("e-1", "e-2")
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100)) as TestSubscriber.Probe<object>;

            pref.Tell("e-4");
            ExpectMsg("e-4-done");

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(5)
                .ExpectNext("e-3")
                .ExpectNext("e-4");
        }

        private IActorRef Setup(string persistenceId)
        {
            var pref = SetupEmpty(persistenceId);

            pref.Tell(persistenceId + "-1");
            pref.Tell(persistenceId + "-2");
            pref.Tell(persistenceId + "-3");

            ExpectMsg(persistenceId + "-1-done");
            ExpectMsg(persistenceId + "-2-done");
            ExpectMsg(persistenceId + "-3-done");
            return pref;
        }

        private IActorRef SetupEmpty(string persistenceId)
        {
            return Sys.ActorOf(Query.TestActor.Props(persistenceId));
        }

        protected override void Dispose(bool disposing)
        {
            Materializer.Dispose();
            base.Dispose(disposing);
        }
    }
}
