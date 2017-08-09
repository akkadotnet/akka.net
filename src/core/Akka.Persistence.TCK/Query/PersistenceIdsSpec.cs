//-----------------------------------------------------------------------
// <copyright file="PersistenceIdsSpec.cs" company="Akka.NET Project">
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
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Query
{
    public abstract class PersistenceIdsSpec : Akka.TestKit.Xunit2.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected IReadJournal ReadJournal { get; set; }

        protected PersistenceIdsSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null) 
            : base(config, actorSystemName, output)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void ReadJournal_should_implement_IAllPersistenceIdsQuery()
        {
            Assert.IsAssignableFrom<IPersistenceIdsQuery>(ReadJournal);
        }

        [Fact]
        public virtual void ReadJournal_AllPersistenceIds_should_find_new_events()
        {
            var queries = ReadJournal.AsInstanceOf<IPersistenceIdsQuery>();

            Setup("e", 1);
            Setup("f", 1);

            var source = queries.PersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);

            probe.Within(TimeSpan.FromSeconds(10), () => probe.Request(5).ExpectNextUnordered("e", "f"));

            Setup("g", 1);
            probe.ExpectNext("g", TimeSpan.FromSeconds(10));
        }

        [Fact]
        public virtual void ReadJournal_AllPersistenceIds_should_find_new_events_after_demand_request()
        {
            var queries = ReadJournal.AsInstanceOf<IPersistenceIdsQuery>();

            Setup("h", 1);
            Setup("i", 1);

            var source = queries.PersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);

            probe.Within(TimeSpan.FromSeconds(10), () =>
            {
                probe.Request(1).ExpectNext();
                return probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            });

            Setup("j", 1);
            probe.Within(TimeSpan.FromSeconds(10), () =>
            {
                probe.Request(5).ExpectNext();
                return probe.ExpectNext();
            });
        }

        [Fact]
        public virtual void ReadJournal_AllPersistenceIds_should_only_deliver_what_requested_if_there_is_more_in_the_buffer()
        {
            var queries = ReadJournal.AsInstanceOf<IPersistenceIdsQuery>();

            Setup("k", 1);
            Setup("l", 1);
            Setup("m", 1);
            Setup("n", 1);
            Setup("o", 1);

            var source = queries.PersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);

            probe.Within(TimeSpan.FromSeconds(10), () =>
            {
                probe.Request(2);
                probe.ExpectNext();
                probe.ExpectNext();
                probe.ExpectNoMsg(TimeSpan.FromMilliseconds(1000));

                probe.Request(2);
                probe.ExpectNext();
                probe.ExpectNext();
                probe.ExpectNoMsg(TimeSpan.FromMilliseconds(1000));

                return probe;
            });
        }

        [Fact]
        public virtual void ReadJournal_AllPersistenceIds_should_deliver_persistenceId_only_once_if_there_are_multiple_events()
        {
            var queries = ReadJournal.AsInstanceOf<IPersistenceIdsQuery>();

            Setup("p", 1000);

            var source = queries.PersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);

            probe.Within(TimeSpan.FromSeconds(10), () =>
            {
                return probe.Request(10)
                    .ExpectNext("p")
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(1000));
            });

            Setup("q", 1000);

            probe.Within(TimeSpan.FromSeconds(10), () =>
            {
                return probe.Request(10)
                    .ExpectNext("q")
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(1000));
            });
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
