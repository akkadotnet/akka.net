//-----------------------------------------------------------------------
// <copyright file="SqliteSnapshotStoreSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Threading.Tasks;
using Akka.Persistence.TCK.Serialization;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Snapshot
{
    public class SqliteSnapshotStoreSerializationSpec : SnapshotStoreSerializationSpec
    {
        private readonly SqliteTestDb _db;

        public SqliteSnapshotStoreSerializationSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private SqliteSnapshotStoreSerializationSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(SqliteSpecConfig.Create(db), nameof(SqliteSnapshotStoreSerializationSpec), output)
        {
            _db = db;
            SqliteSpecConfig.EnsureThreadPoolWarmed();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            _db.Dispose();
        }
    }
}
