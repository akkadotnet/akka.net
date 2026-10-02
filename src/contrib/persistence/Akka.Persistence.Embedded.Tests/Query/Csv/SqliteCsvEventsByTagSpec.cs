//-----------------------------------------------------------------------
// <copyright file="SqliteCsvEventsByTagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Persistence.Embedded.Query;
using Akka.Persistence.Query;
using Akka.Persistence.TCK.Query;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Query.Csv
{
    public class SqliteCsvEventsByTagSpec : EventsByTagSpec
    {
        private readonly SqliteTestDb _db;

        public SqliteCsvEventsByTagSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private SqliteCsvEventsByTagSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(SqliteSpecConfig.Create(db, SqliteTestMode.CSV, $$"""
                    akka.persistence.journal.embedded {
                        event-adapters {
                          color-tagger = "{{typeof(ColorFruitTagger).FullName}}, {{typeof(ColorFruitTagger).Assembly.GetName().Name}}"
                        }
                        event-adapter-bindings = {
                          "System.String" = color-tagger
                        }
                    }
                    """), nameof(SqliteCsvEventsByTagSpec), output)
        {
            _db = db;
            SqliteSpecConfig.EnsureThreadPoolWarmed();
            Persistence.Instance.Get(Sys); // Initialize persistence immediately
            ReadJournal = Sys.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
        }

        protected override bool SupportsTagsInEventEnvelope => true;

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            _db.Dispose();
        }
    }
}
