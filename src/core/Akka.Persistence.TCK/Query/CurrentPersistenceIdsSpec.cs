//-----------------------------------------------------------------------
// <copyright file="CurrentPersistenceIdsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Query
{
    public abstract class CurrentPersistenceIdsSpec : Akka.TestKit.Xunit2.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected IReadJournal ReadJournal { get; set; }

        protected CurrentPersistenceIdsSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null)
            : base(config ?? Config.Empty, actorSystemName, output)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void ReadJournal_should_implement_ICurrentEventsByPersistenceIdQuery()
        {
            Assert.IsAssignableFrom<ICurrentPersistenceIdsQuery>(ReadJournal);
        }

        [Fact]
        public virtual void ReadJournal_CurrentPersistenceIds_should_find_existing_events()
        {
            var queries = ReadJournal.AsInstanceOf<ICurrentPersistenceIdsQuery>();

            Setup("a", 1);
            Setup("b", 1);
            Setup("c", 1);

            var source = queries.CurrentPersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);
            probe.Within(TimeSpan.FromSeconds(10), () =>
                probe.Request(4)
                    .ExpectNextUnordered("a", "b", "c")
                    .ExpectComplete());
        }

        [Fact]
        public virtual void ReadJournal_CurrentPersistenceIds_should_deliver_persistenceId_only_once()
        {
            var queries = ReadJournal.AsInstanceOf<ICurrentPersistenceIdsQuery>();

            Setup("d", 100);

            var source = queries.CurrentPersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);
            probe.Within(TimeSpan.FromSeconds(10), () =>
                probe.Request(10)
                    .ExpectNext("d")
                    .ExpectComplete());
        }

        [Fact]
        public virtual void ReadJournal_query_CurrentPersistenceIds_should_not_see_new_events_after_complete()
        {
            var queries = ReadJournal.AsInstanceOf<ICurrentPersistenceIdsQuery>();

            Setup("a", 1);
            Setup("b", 1);
            Setup("c", 1);

            var greenSrc = queries.CurrentPersistenceIds();
            var probe = greenSrc.RunWith(this.SinkProbe<string>(), Materializer);
            var set = new List<string> { "a", "b", "c" };
            probe.Request(2)
                .ExpectNextWithinSet(set)
                .ExpectNextWithinSet(set)
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            Setup("d", 1);

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(5)
                .ExpectNextWithinSet(set)
                .ExpectComplete();
        }

        private IActorRef Setup(string persistenceId, int n)
        {
            var pref = Sys.ActorOf(Query.TestActor.Props(persistenceId));
            for (int i = 1; i <= n; i++)
            {
                pref.Tell($"{persistenceId}-{i}");
                ExpectMsg($"{persistenceId}-{i}-done");
            }

            return pref;
        }

        protected override void Dispose(bool disposing)
        {
            Materializer.Dispose();
            base.Dispose(disposing);
        }
    }
}
