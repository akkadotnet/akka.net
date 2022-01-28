// //-----------------------------------------------------------------------
// // <copyright file="SqliteJournalSerializationSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.TCK.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Custom.Tests
{
    public class SqliteJournalSerializationSpec : JournalSerializationSpec
    {
        public SqliteJournalSerializationSpec(ITestOutputHelper output) 
            : base(CreateSpecConfig("Filename=file:memdb-journal-" + Guid.NewGuid() + ".db"), nameof(SqliteJournalSerializationSpec), output)
        {
        }
        
        private static Config CreateSpecConfig(string connectionString)
        {
            return ConfigurationFactory.ParseString(@"
                akka.persistence {
                    publish-plugin-commands = on
                    journal {
                        plugin = ""akka.persistence.journal.custom-sqlite""
                        custom-sqlite {
                            event-adapters {
                                custom-adapter = ""Akka.Persistence.TCK.Serialization.TestJournal+MyWriteAdapter, Akka.Persistence.TCK""
                            }
                            event-adapter-bindings = {
                                ""Akka.Persistence.TCK.Serialization.TestJournal+MyPayload3, Akka.Persistence.TCK"" = custom-adapter
                            }
                            class = ""Akka.Persistence.Custom.Journal.SqliteJournal, Akka.Persistence.Custom""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            auto-initialize = on
                            connection-string = """ + connectionString + @"""
                        }
                    }
                }");
        }
        

        [Fact(Skip = "SQLite plugin does not support EventAdapter.Manifest")]
        public override void Journal_should_serialize_Persistent_with_EventAdapter_manifest()
        {
        }
    }
}