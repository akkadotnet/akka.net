// //-----------------------------------------------------------------------
// // <copyright file="SqliteJournalSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Data.Common;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Persistence.TCK.Journal;
using Akka.Util.Internal;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Custom.Tests
{
    public class SqliteJournalSpec : JournalSpec
    {
        public SqliteJournalSpec(ITestOutputHelper output)
            : base(CreateSpecConfig("Filename=file:memdb-journal-" + Guid.NewGuid() + ".db"), nameof(SqliteJournalSpec), output)
        {
            SqlitePersistence.Get(Sys);

            Initialize();
        }

        protected override bool SupportsSerialization => false;

        private static Config CreateSpecConfig(string connectionString)
        {
            return ConfigurationFactory.ParseString(@"
                akka.persistence {
                    publish-plugin-commands = on
                    journal {
                        plugin = ""akka.persistence.journal.custom-sqlite""
                        custom-sqlite {
                            class = ""Akka.Persistence.Custom.Journal.SqliteJournal, Akka.Persistence.Custom""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            auto-initialize = on
                            connection-string = """ + connectionString + @"""
                        }
                    }
                }");
        }
    }
}