//-----------------------------------------------------------------------
// <copyright file="SqliteLegacyJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.Legacy
{
    public class SqliteLegacyJournalSpec: Akka.TestKit.Xunit2.TestKit
    {
        private Dictionary<string, IActorRef> _actors = new();
        private readonly TestProbe _probe;

        public SqliteLegacyJournalSpec(ITestOutputHelper output)
            : base(CreateSpecConfig("Filename=file:TestDb.db"), nameof(SqliteLegacyJournalSpec), output)
        {
            SqlitePersistence.Get(Sys);
            _probe = CreateTestProbe();
        }
        
        private static Config CreateSpecConfig(string connectionString)
        {
            if(File.Exists("./TestDb.db"))
                File.Delete("./TestDb.db");
            File.Copy("./data/Sqlite.v1.3.0.db", "./TestDb.db");
            
            return ConfigurationFactory.ParseString($@"
akka.persistence {{
    publish-plugin-commands = on
    journal {{
        plugin = akka.persistence.journal.sqlite
        sqlite {{
            auto-initialize = on
            connection-string = ""{connectionString}""
        }}
    }}
    snapshot-store {{
        plugin = akka.persistence.snapshot-store.sqlite
        sqlite {{
            auto-initialize = on
            connection-string = ""{connectionString}""
        }}
    }}
}}").WithFallback(SqlitePersistence.DefaultConfiguration());
        }

        [Fact(DisplayName = "Legacy v1.3.0 and below data (null serializer_id) should be read correctly")]
        public void ReadLegacyDataTest()
        {
            var stateDict = new Dictionary<string, PersistedLegacyActor.CurrentState>();
            
            _actors["A"] = Sys.ActorOf(Props.Create(() => new PersistedLegacyActor("A", _probe)));
            _actors["B"] = Sys.ActorOf(Props.Create(() => new PersistedLegacyActor("B", _probe)));
            _actors["C"] = Sys.ActorOf(Props.Create(() => new PersistedLegacyActor("C", _probe)));

            _actors["A"].Tell(PersistedLegacyActor.GetState.Instance);
            _actors["B"].Tell(PersistedLegacyActor.GetState.Instance);
            _actors["C"].Tell(PersistedLegacyActor.GetState.Instance);
            
            for (var i = 0; i < 3; i++)
            {
                var state = _probe.ExpectMsg<PersistedLegacyActor.CurrentState>();
                stateDict[state.PersistenceId] = state;
            }

            stateDict.Keys.ToArray().Should().BeEquivalentTo("A", "B", "C");

            foreach (var state in stateDict.Values)
            {
                state.State.Should().BeOfType<PersistedLegacyActor.Persisted>();
                state.State.Payload.Should().Be(5);
                state.Events.Count.Should().Be(5);
                state.Events.Select(e => e.Payload).Should().BeEquivalentTo(6, 7, 8, 9, 10);
            }
        }

        // This method is not byte-rot, this documents how the legacy data are being generated. 
        private void Generate()
        {
            _actors["A"] = Sys.ActorOf(Props.Create(() => new PersistedLegacyActor("A", _probe)));
            _actors["B"] = Sys.ActorOf(Props.Create(() => new PersistedLegacyActor("B", _probe)));
            _actors["C"] = Sys.ActorOf(Props.Create(() => new PersistedLegacyActor("C", _probe)));
            
            foreach (var i in Enumerable.Range(1, 5))
            {
                _actors["A"].Tell(new PersistedLegacyActor.Persisted(i));
                _probe.ExpectMsg<PersistedLegacyActor.PersistAck>();
                _actors["B"].Tell(new PersistedLegacyActor.Persisted(i));
                _probe.ExpectMsg<PersistedLegacyActor.PersistAck>();
                _actors["C"].Tell(new PersistedLegacyActor.Persisted(i));
                _probe.ExpectMsg<PersistedLegacyActor.PersistAck>();
            }
            
            var a = _probe.ExpectMsg<PersistedLegacyActor.SaveSnapshotAck>();
            var b = _probe.ExpectMsg<PersistedLegacyActor.SaveSnapshotAck>();
            var c = _probe.ExpectMsg<PersistedLegacyActor.SaveSnapshotAck>();
            new [] { a.State.Payload, b.State.Payload, c.State.Payload }.Should().BeEquivalentTo(5, 5, 5);
            a.Events.Count.Should().Be(0);
            b.Events.Count.Should().Be(0);
            c.Events.Count.Should().Be(0);
            
            foreach (var i in Enumerable.Range(6, 5))
            {
                _actors["A"].Tell(new PersistedLegacyActor.Persisted(i));
                _probe.ExpectMsg<PersistedLegacyActor.PersistAck>();
                _actors["B"].Tell(new PersistedLegacyActor.Persisted(i));
                _probe.ExpectMsg<PersistedLegacyActor.PersistAck>();
                _actors["C"].Tell(new PersistedLegacyActor.Persisted(i));
                _probe.ExpectMsg<PersistedLegacyActor.PersistAck>();
            }
        }

    }
}
