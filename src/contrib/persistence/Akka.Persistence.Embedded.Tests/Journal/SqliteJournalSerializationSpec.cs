//-----------------------------------------------------------------------
// <copyright file="SqliteJournalSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Threading.Tasks;
using Akka.Persistence.TCK.Serialization;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Journal
{
    public class SqliteJournalSerializationSpec : JournalSerializationSpec
    {
        private readonly SqliteTestDb _db;

        public SqliteJournalSerializationSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private SqliteJournalSerializationSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(SqliteSpecConfig.Create(db), nameof(SqliteJournalSerializationSpec), output)
        {
            _db = db;
            SqliteSpecConfig.EnsureThreadPoolWarmed();
        }

        [Fact(Skip = "The Akka.Persistence.Sql row format has no event-manifest column (Akka.Persistence.Sql AkkaPersistenceDataConnectionFactory.cs:190-203).")]
        public override void Journal_should_serialize_Persistent_with_EventAdapter_manifest()
        {
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            _db.Dispose();
        }
    }
}
