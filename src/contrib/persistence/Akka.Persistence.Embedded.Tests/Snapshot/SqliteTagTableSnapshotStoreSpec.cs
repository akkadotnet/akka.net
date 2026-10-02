//-----------------------------------------------------------------------
// <copyright file="SqliteTagTableSnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Threading.Tasks;
using Akka.Persistence.TCK.Snapshot;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Snapshot
{
    public class SqliteTagTableSnapshotStoreSpec : SnapshotStoreSpec
    {
        private readonly SqliteTestDb _db;

        public SqliteTagTableSnapshotStoreSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private SqliteTagTableSnapshotStoreSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(SqliteSpecConfig.Create(db, SqliteTestMode.TT), nameof(SqliteTagTableSnapshotStoreSpec), output)
        {
            _db = db;
            SqliteSpecConfig.EnsureThreadPoolWarmed();
            Initialize();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            _db.Dispose();
        }
    }
}
