//-----------------------------------------------------------------------
// <copyright file="SqliteCsvCurrentEventsByPersistenceIdSpec.cs" company="Akka.NET Project">
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
    public class SqliteCsvCurrentEventsByPersistenceIdSpec : CurrentEventsByPersistenceIdSpec
    {
        private readonly SqliteTestDb _db;

        public SqliteCsvCurrentEventsByPersistenceIdSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private SqliteCsvCurrentEventsByPersistenceIdSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(SqliteSpecConfig.Create(db, SqliteTestMode.CSV), nameof(SqliteCsvCurrentEventsByPersistenceIdSpec), output)
        {
            _db = db;
            SqliteSpecConfig.EnsureThreadPoolWarmed();
            Persistence.Instance.Get(Sys); // Initialize persistence immediately
            ReadJournal = Sys.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            _db.Dispose();
        }
    }
}
