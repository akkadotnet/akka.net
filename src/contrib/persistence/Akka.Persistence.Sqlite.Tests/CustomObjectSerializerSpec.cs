//-----------------------------------------------------------------------
// <copyright file="CustomObjectSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
        private static readonly string ConnectionString;
        private static readonly Config Config;
        static CustomObjectSerializerSpec()
        {
            var filename = $"AkkaSqlite-{Guid.NewGuid()}.db";
            File.Copy("./data/Sqlite.CustomObject.db", $"{filename}.db");
            
            ConnectionString = $"DataSource={filename}.db";
            Config = ConfigurationFactory.ParseString($@"
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
            connection-string = ""{ConnectionString}""
            auto-initialize = on
        }}
    }}
    snapshot-store {{
        plugin = ""akka.persistence.snapshot-store.sqlite""
        sqlite {{
            connection-string = ""{ConnectionString}""
            auto-initialize = on
        }}
    }}
}}").WithFallback(SqlitePersistence.DefaultConfiguration());
        }

        public CustomObjectSerializerSpec(ITestOutputHelper helper) 
            : base(Config, nameof(CustomObjectSerializerSpec), helper)
        {
        }

        [Fact(DisplayName = "Persistence.Sql should use custom serializer for object type")]
        public async Task CustomSerializerTest()
        {
            var probe = CreateTestProbe();
            
            // Sanity check to see that the system should serialize object type using MySerializer
            var serializer = Sys.Serialization.FindSerializerForType(typeof(Persisted));
            serializer.Should().BeOfType<MySerializer>();
            
            var actor = Sys.ActorOf(Props.Create(() => new PersistedActor("a", probe)));
            probe.ExpectMsg("recovered");
            actor.Tell(new Persisted("a"), probe);
            probe.ExpectMsg(new Persisted("a"));

            // Read the database directly, make sure that we're using the correct object type serializer
            var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            const string sql = "SELECT ej.serializer_id FROM event_journal ej WHERE ej.persistence_id = 'a'";
            await using var cmd = new SqliteCommand(sql, conn);
            var record = await cmd.ExecuteReaderAsync();
            await record.ReadAsync();
            
            // In the bug this fails, the serializer id is JSON id instead of MySerializer id
            record[0].Should().Be(9999);
        }

        [Fact(DisplayName = "Persistence.Sql should be able to read legacy data")]
        public void LegacyDataTest()
        {
            var probe = CreateTestProbe();
            var actor = Sys.ActorOf(Props.Create(() => new PersistedActor("old", probe)));
            probe.ExpectMsg(new Persisted("old"));
            probe.ExpectMsg("recovered");
        }
        
        public Task InitializeAsync()
        {
            if(File.Exists("AkkaSqlite.db"))
                File.Delete("AkkaSqlite.db");
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }

    internal sealed class Persisted: IEquatable<Persisted>
    {
        public Persisted(string payload)
        {
            Payload = payload;
        }

        public string Payload { get; }

        public bool Equals(Persisted other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Payload == other.Payload;
        }

        public override bool Equals(object obj)
        {
            return ReferenceEquals(this, obj) || obj is Persisted other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Payload != null ? Payload.GetHashCode() : 0);
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
        private readonly IActorRef _probe;
        
        public PersistedActor(string persistenceId, IActorRef probe)
        {
            PersistenceId = persistenceId;
            _probe = probe;
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
            switch (message)
            {
                case Persisted msg:
                    _probe.Tell(msg);
                    break;
                case RecoveryCompleted _:
                    _probe.Tell("recovered");
                    break;
            }
        }
    }    
}
