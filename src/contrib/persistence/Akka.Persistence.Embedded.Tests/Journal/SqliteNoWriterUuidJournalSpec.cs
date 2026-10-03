//-----------------------------------------------------------------------
// <copyright file="SqliteNoWriterUuidJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Threading.Tasks;
using Akka.Persistence.TCK.Journal;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Journal
{
    public class SqliteNoWriterUuidJournalSpec : JournalSpec
    {
        private readonly SqliteTestDb _db;

        public SqliteNoWriterUuidJournalSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private SqliteNoWriterUuidJournalSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(SqliteSpecConfig.Create(db, SqliteTestMode.NW), nameof(SqliteNoWriterUuidJournalSpec), output)
        {
            _db = db;
            SqliteSpecConfig.EnsureThreadPoolWarmed();
            Initialize();
        }

        // The row format stores the journal instance's writer uuid, not the persistent actor's,
        // exactly like Akka.Persistence.Sql does. The TCK check expects the actor's WriterGuid back.
        protected override bool SupportsSerialization => false;

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            _db.Dispose();
        }
    }
}
