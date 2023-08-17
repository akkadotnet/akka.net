//-----------------------------------------------------------------------
// <copyright file="SqliteSnapshotStoreSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Configuration;
using Akka.Persistence.TCK.Serialization;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sqlite.Tests.Serialization
{
    public class SqliteSnapshotStoreSerializationSpec : SnapshotStoreSerializationSpec
    {
        private static AtomicCounter Counter { get; } = new(0);

        public SqliteSnapshotStoreSerializationSpec(ITestOutputHelper output)
            : base(CreateSpecConfig("Filename=file:serialization-snapshot-" + Counter.IncrementAndGet() + ".db;Mode=Memory;Cache=Shared"), "SqliteSnapshotStoreSerializationSpec", output)
        {
        }

        private static Config CreateSpecConfig(string connectionString)
        {
            return ConfigurationFactory.ParseString(@"
                akka.persistence {
                    publish-plugin-commands = on
                    snapshot-store {
                        plugin = ""akka.persistence.snapshot-store.sqlite""
                        sqlite {
                            class = ""Akka.Persistence.Sqlite.Snapshot.SqliteSnapshotStore, Akka.Persistence.Sqlite""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            table-name = snapshot_store
                            auto-initialize = on
                            connection-string = """ + connectionString + @"""
                        }
                    }
                }
                under-test {
                    value = true
                }");
        }
        
        // This is a test for SnapshotStoreSerializationSpec, not SqliteSnapshotStoreSerializationSpec
        [Fact(DisplayName = "SnapshotStoreSerializationSpec ctor with ActorSystemSetup should combine all necessary configurations")]
        public void AllConfigShouldBePresentTest()
        {
            var config = Sys.Settings.Config;
        
            // Config inside original ActorSystemSetup should be preserved 
            config.GetBoolean("under-test.value").Should().BeTrue();
        
            // Config needed by SnapshotStoreSerializationSpec should be injected
            var serializers = config.GetConfig("akka.actor.serializers").AsEnumerable().ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            serializers.TryGetValue("my-snapshot", out var mySnapshot).Should().BeTrue();
            mySnapshot!.GetString().Should().Be("Akka.Persistence.TCK.Serialization.Test+MySnapshotSerializer, Akka.Persistence.TCK");
            serializers.TryGetValue("my-snapshot2", out var mySnapshot2).Should().BeTrue();
            mySnapshot2!.GetString().Should().Be("Akka.Persistence.TCK.Serialization.Test+MySnapshotSerializer2, Akka.Persistence.TCK");
        
            var bindings = config.GetConfig("akka.actor.serialization-bindings").AsEnumerable().ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            bindings.TryGetValue("Akka.Persistence.TCK.Serialization.Test+MySnapshot, Akka.Persistence.TCK", out var binding).Should().BeTrue();
            binding!.GetString().Should().Be("my-snapshot");
            bindings.TryGetValue("Akka.Persistence.TCK.Serialization.Test+MySnapshot2, Akka.Persistence.TCK", out var binding2).Should().BeTrue();
            binding2!.GetString().Should().Be("my-snapshot2");
        
            // Akka.Persistence default configuration should be injected
            config.HasPath("akka.persistence.max-concurrent-recoveries").Should().BeTrue();
            config.GetInt("akka.persistence.max-concurrent-recoveries").Should().Be(50);
        }
        
    }
}
