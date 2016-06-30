//-----------------------------------------------------------------------
// <copyright file="AllPersistenceIdsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Query.Sql;
using Akka.Streams;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.TestKit
{
    public abstract class AllPersistenceIdsSpec : Akka.TestKit.Xunit2.TestKit
    {

        private readonly ActorMaterializer _materializer;
        private readonly SqlReadJournal _queries;

        protected AllPersistenceIdsSpec(Config config, ITestOutputHelper output) : base(config, output: output)
        {
            _materializer = Sys.Materializer();
            _queries = Sys.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);
        }

        [Fact]
        public void Sql_query_AllPersistenceIds_should_implement_standard_AllPersistenceIdsQuery()
        {
            (_queries is IAllPersistenceIdsQuery).Should().BeTrue();
        }

        [Fact]
        public void Sql_query_AllPersistenceIds_should_find_existing_persistence_ids()
        {
            Sys.ActorOf(TestKit.TestActor.Props("a")).Tell("a1");
            ExpectMsg("a1-done");
            Sys.ActorOf(TestKit.TestActor.Props("b")).Tell("b1");
            ExpectMsg("b1-done");
            Sys.ActorOf(TestKit.TestActor.Props("c")).Tell("c1");
            ExpectMsg("c1-done");

            var source = _queries.CurrentPersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), _materializer);
            probe.Within(TimeSpan.FromSeconds(10), () =>
                probe.Request(5)
                    .ExpectNextUnordered("a", "b", "c")
                    .ExpectComplete());
        }

        [Fact]
        public void Sql_query_AllPersistenceIds_should_find_new_persistence_ids()
        {
            Sql_query_AllPersistenceIds_should_find_existing_persistence_ids();
            // a, b, c created by previous step

            Sys.ActorOf(TestKit.TestActor.Props("d")).Tell("d1");
            ExpectMsg("d1-done");

            var source = _queries.AllPersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), _materializer);
            probe.Within(TimeSpan.FromSeconds(10), () =>
            {
                probe.Request(5).ExpectNextUnordered("a", "b", "c", "d");

                Sys.ActorOf(TestKit.TestActor.Props("e")).Tell("e1");
                probe.ExpectNext("e");

                var more = Enumerable.Range(1, 100).Select(i => "f" + i).ToArray();
                foreach (var x in more)
                    Sys.ActorOf(TestKit.TestActor.Props(x)).Tell(x);

                probe.Request(100);
                return probe.ExpectNextUnorderedN(more);
            });
        }

        protected override void Dispose(bool disposing)
        {
            _materializer.Dispose();
            base.Dispose(disposing);
        }
    }
}