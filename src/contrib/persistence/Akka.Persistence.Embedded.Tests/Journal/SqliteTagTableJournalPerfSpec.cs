//-----------------------------------------------------------------------
// <copyright file="SqliteTagTableJournalPerfSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Persistence.TestKit.Performance;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Journal
{
    public class SqliteTagTableJournalPerfSpec : JournalPerfSpec
    {
        private readonly SqliteTestDb _db;

        public SqliteTagTableJournalPerfSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private SqliteTagTableJournalPerfSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(SqliteSpecConfig.Create(db, SqliteTestMode.TT), nameof(SqliteTagTableJournalPerfSpec), output)
        {
            _db = db;
            // Every persisted event is a durable commit (several fsyncs in SQLite's default journal mode), so the
            // run time is set by the disk. 300 events keep a slow, busy CI disk (60 ms per commit) under ExpectDuration.
            EventsCount = 300;
            MeasurementIterations = 3;
            ExpectDuration = TimeSpan.FromSeconds(30);
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            _db.Dispose();
        }
    }
}
