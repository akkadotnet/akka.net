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
using Akka.Streams;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Query.Sqlite.Tests
{
    public class AllPersistenceIdsSpec : TestKit.Xunit2.TestKit
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(1);
        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.sqlite""
            akka.persistence.journal.sqlite {{
                class = ""Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                table-name = event_journal
                auto-initialize = on
                connection-string = ""FullUri=file:memdb-journal-{id}.db?mode=memory&cache=shared;""
            }}
            akka.test.single-expect-default = 10s")
            .WithFallback(SqliteReadJournal.DefaultConfiguration());

        private readonly ActorMaterializer _materializer;
        private readonly SqliteReadJournal _queries;

        public AllPersistenceIdsSpec(ITestOutputHelper output) : base(Config(Counter.GetAndIncrement()), output: output)
        {
            _materializer = Sys.Materializer();
            _queries = Sys.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
        }

        [Fact]
        public void Sqlite_query_AllPersistenceIds_should_implement_standard_AllPersistenceIdsQuery()
        {
            (_queries is IAllPersistenceIdsQuery).Should().BeTrue();
        }

        [Fact]
        public void Sqlite_query_AllPersistenceIds_should_find_existing_persistence_ids()
        {
            Sys.ActorOf(Tests.TestActor.Props("a")).Tell("a1");
            ExpectMsg("a1-done");
            Sys.ActorOf(Tests.TestActor.Props("b")).Tell("b1");
            ExpectMsg("b1-done");
            Sys.ActorOf(Tests.TestActor.Props("c")).Tell("c1");
            ExpectMsg("c1-done");

            var source = _queries.CurrentPersistenceIds;
            var probe = source.RunWith(this.SinkProbe<string>(), _materializer);
            probe.Within(TimeSpan.FromSeconds(10), () =>
                probe.Request(5)
                    .ExpectNextUnordered("a", "b", "c")
                    .ExpectComplete());
        }

        [Fact]
        public void Sqlite_query_AllPersistenceIds_should_find_new_persistence_ids()
        {
            Sqlite_query_AllPersistenceIds_should_find_existing_persistence_ids();
            // a, b, c created by previous step

            Sys.ActorOf(Tests.TestActor.Props("d")).Tell("d1");
            ExpectMsg("d1-done");

            var source = _queries.AllPersistenceIds;
            var probe = source.RunWith(this.SinkProbe<string>(), _materializer);
            probe.Within(TimeSpan.FromSeconds(10), () =>
            {
                probe.Request(5).ExpectNextUnordered("a", "b", "c", "d");

                Sys.ActorOf(Tests.TestActor.Props("e")).Tell("e1");
                probe.ExpectNext("e");

                var more = Enumerable.Range(1, 100).Select(i => "f" + i).ToArray();
                foreach (var x in more)
                    Sys.ActorOf(Tests.TestActor.Props(x)).Tell(x);

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