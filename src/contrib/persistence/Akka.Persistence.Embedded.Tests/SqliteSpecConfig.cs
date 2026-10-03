//-----------------------------------------------------------------------
// <copyright file="SqliteSpecConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading;
using Akka.Configuration;

namespace Akka.Persistence.Embedded.Tests
{
    /// <summary>The five modes the TCK matrix runs in.</summary>
    public enum SqliteTestMode
    {
        /// <summary>TagTable.</summary>
        TT,

        /// <summary>Csv.</summary>
        CSV,

        /// <summary>Both (reads resolve to TagTable).</summary>
        BOTH,

        /// <summary>TagTable plus delete-compatibility-mode.</summary>
        DC,

        /// <summary>TagTable without the writer_uuid column.</summary>
        NW
    }

    public static class SqliteSpecConfig
    {
        /// <summary>Raises the thread pool minimum so recovery does not wait on thread pool growth in CI.</summary>
        public static void EnsureThreadPoolWarmed()
        {
            ThreadPool.GetMinThreads(out var worker, out var io);
            ThreadPool.SetMinThreads(Math.Max(worker, Environment.ProcessorCount * 2), io);
        }

        public static Config Create(SqliteTestDb db, SqliteTestMode mode, string extra = "")
            => Create(
                db,
                mode switch
                {
                    SqliteTestMode.CSV => TagWriteMode.Csv,
                    SqliteTestMode.BOTH => TagWriteMode.Both,
                    _ => TagWriteMode.TagTable
                },
                deleteCompat: mode == SqliteTestMode.DC,
                writerUuid: mode != SqliteTestMode.NW,
                extra: extra);

        public static Config Create(SqliteTestDb db, TagWriteMode mode, bool deleteCompat = false, bool writerUuid = true, string extra = "")
            => ConfigurationFactory.ParseString(
                    $$"""
                    akka.loglevel = INFO
                    akka.persistence.journal.plugin = "akka.persistence.journal.embedded"
                    akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.embedded"
                    akka.persistence.journal.embedded {
                        connection-string = "{{db.HoconConnectionString}}"
                        tag-write-mode = {{mode}}
                        delete-compatibility-mode = {{(deleteCompat ? "true" : "false")}}
                        default.journal.use-writer-uuid-column = {{(writerUuid ? "true" : "false")}}
                    }
                    akka.persistence.snapshot-store.embedded {
                        connection-string = "{{db.HoconConnectionString}}"
                    }
                    akka.persistence.query.journal.embedded {
                        refresh-interval = 100ms
                    }
                    {{extra}}
                    """)
                .WithFallback(SqlitePersistence.DefaultConfiguration);
    }
}
