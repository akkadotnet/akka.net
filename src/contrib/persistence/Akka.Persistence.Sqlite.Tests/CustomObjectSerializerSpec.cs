// -----------------------------------------------------------------------
//  <copyright file="CustomObjectSerializerSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests
{
    public class CustomObjectSerializerSpec : Akka.TestKit.Xunit2.TestKit, IAsyncLifetime
    {
        private static readonly Config Config = ConfigurationFactory.ParseString($@"
akka.actor {{
    serializers {{
        mySerializer = ""{typeof(MySerializer).AssemblyQualifiedName}""
    }}
    serialization-bindings {{
        ""System.Object"" = mySerializer
    }}
}}

akka.persistence {{
    journal {{
        plugin = ""akka.persistence.journal.sqlite""
        sqlite {{
            connection-string = ""DataSource=AkkaJournal.db""
            auto-initialize = on
        }}
    }}
    snapshot-store {{
        plugin = ""akka.persistence.snapshot-store.sqlite""
        sqlite {{
            connection-string = ""DataSource=AkkaSnapshot.db""
            auto-initialize = on
        }}
    }}
}}").WithFallback(SqlitePersistence.DefaultConfiguration());

        public CustomObjectSerializerSpec(ITestOutputHelper helper) 
            : base(Config, nameof(CustomObjectSerializerSpec), helper)
        {
        }

        [Fact(DisplayName = "Persistence.Sql should use custom serializer for object type")]
        public async Task CustomSerializerTest()
        {
            var probe = CreateTestProbe();
            
            var actor = Sys.ActorOf(Props.Create(() => new PersistedActor("a")));
            actor.Tell("a", probe);
            probe.ExpectMsg("a");

            var conn = new SqliteConnection("DataSource=AkkaJournal.db");
            conn.Open();
            const string sql = "SELECT ej.serializer_id FROM event_journal ej WHERE ej.persistence_id = 'a'";
            await using var cmd = new SqliteCommand(sql, conn);
            var record = await cmd.ExecuteReaderAsync();
            await record.ReadAsync();
            record[0].Should().Be(9999);
        }

        public Task InitializeAsync()
        {
            if(File.Exists("AkkaJournal.db"))
                File.Delete("AkkaJournal.db");
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }
    
    internal class MySerializer : Serializer
    {
        public MySerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override bool IncludeManifest { get { return true; } }
        public override int Identifier { get { return 9999; } }

        public override byte[] ToBinary(object obj)
        {
            return Encoding.UTF8.GetBytes(obj.ToString());
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }
    
    internal sealed class PersistedActor : UntypedPersistentActor
    {
        public PersistedActor(string persistenceId)
        {
            PersistenceId = persistenceId;
        }
    
        public override string PersistenceId { get; }

        protected override void OnCommand(object message)
        {
            var sender = Sender;
            Persist(message, _ =>
            {
                sender.Tell(message);
            });
        }

        protected override void OnRecover(object message)
        {
        }
    }    
}