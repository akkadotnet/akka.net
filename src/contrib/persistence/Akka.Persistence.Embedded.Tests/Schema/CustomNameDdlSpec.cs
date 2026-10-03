//-----------------------------------------------------------------------
// <copyright file="CustomNameDdlSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Configuration;
using Akka.Persistence.Embedded.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Schema
{
    /// <summary>
    /// The expected text was captured by hand from linq2db 5.4.1.9 (the CreateTable that Akka.Persistence.Sql 1.5.70 uses) run on tables
    /// of the same shapes with these custom names. The test does not run linq2db. CustomNamesSpecs runs the plugin end to end with custom names.
    /// </summary>
    public class CustomNameDdlSpec
    {
        private const string Hocon = """
            akka.persistence.journal.embedded {
                connection-string = "Data Source=/tmp/never-opened.db"
                tag-write-mode = Both
                default {
                    journal {
                        table-name = evt
                        columns {
                            ordering = o, created = created_at_ticks, deleted = is_deleted_flag, persistence-id = pid
                            sequence-number = seq, message = payload_blob, tags = tgs, manifest = man, identifier = ident
                            writer-uuid = writer_uuid_long_name_x
                        }
                    }
                    metadata {
                        table-name = md
                        columns { persistence-id = p, sequence-number = sequence_number_longer_name }
                    }
                    tag {
                        table-name = tg
                        columns { ordering-id = oid, tag-value = tg_value_column, persistence-id = p, sequence-nr = sq }
                    }
                }
            }
            akka.persistence.snapshot-store.embedded {
                connection-string = "Data Source=/tmp/never-opened.db"
                default.snapshot {
                    table-name = snp
                    columns { persistence-id = p, sequence-number = s, created = c, snapshot = payload_snapshot_bytes, manifest = m, serializerId = sid }
                }
            }
            """;

        private static Config Full => ConfigurationFactory.ParseString(Hocon).WithFallback(SqlitePersistence.DefaultConfiguration);

        private static JournalSettings Journal => JournalSettings.Create(Full.GetConfig(SqlitePersistence.JournalPluginId), SqlitePersistence.JournalPluginId, Full);

        [Fact(DisplayName = "Should_pad_journal_columns_like_linq2db_When_names_are_custom")]
        public void Should_pad_journal_columns_like_linq2db_When_names_are_custom()
        {
            var table = SqliteSchema.JournalDdl(Journal);

            table.Should().StartWith(
                "CREATE TABLE IF NOT EXISTS [evt]\n(\n" +
                "\t[o]                       INTEGER        NOT NULL PRIMARY KEY AUTOINCREMENT,\n" +
                "\t[created_at_ticks]        BigInt         NOT NULL,\n" +
                "\t[is_deleted_flag]         Bit            NOT NULL,\n" +
                "\t[pid]                     NVarChar(255)  NOT NULL,\n" +
                "\t[seq]                     BigInt         NOT NULL,\n" +
                "\t[payload_blob]            VarBinary      NOT NULL,\n" +
                "\t[tgs]                     NVarChar(100)      NULL,\n" +
                "\t[man]                     NVarChar(500)      NULL,\n" +
                "\t[ident]                   INTEGER            NULL,\n" +
                "\t[writer_uuid_long_name_x] NVarChar(128)      NULL\n" +
                ")\n;\r\n");
            table.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS evt_uq ON evt (pid, seq);\r\n");
            table.Should().Contain("CREATE INDEX IF NOT EXISTS evt_created_at_ticks_idx ON evt (created_at_ticks);\r\n");
            table.Should().EndWith("CREATE INDEX IF NOT EXISTS evt_seq_idx ON evt (seq);");
        }

        [Fact(DisplayName = "Should_pad_tag_metadata_and_snapshot_columns_like_linq2db_When_names_are_custom")]
        public void Should_pad_tag_metadata_and_snapshot_columns_like_linq2db_When_names_are_custom()
        {
            SqliteSchema.TagTableDdl(Journal).Should().StartWith(
                "CREATE TABLE IF NOT EXISTS [tg]\n(\n" +
                "\t[oid]             INTEGER       NOT NULL,\n" +
                "\t[tg_value_column] NVarChar(64)  NOT NULL,\n" +
                "\t[sq]              INTEGER       NOT NULL,\n" +
                "\t[p]               NVarChar(255) NOT NULL,\n" +
                "\n" +
                "\tCONSTRAINT [PK_tg] PRIMARY KEY ([oid], [tg_value_column])\n" +
                ")\n;\r\n");

            SqliteSchema.MetadataDdl(Journal).Should().Be(
                "CREATE TABLE IF NOT EXISTS [md]\n(\n" +
                "\t[p]                           NVarChar(255) NOT NULL,\n" +
                "\t[sequence_number_longer_name] BigInt        NOT NULL,\n" +
                "\n" +
                "\tCONSTRAINT [PK_md] PRIMARY KEY ([sequence_number_longer_name], [p])\n" +
                ")");

            var snapshot = SnapshotSettings.Create(
                Full.GetConfig(SqlitePersistence.SnapshotStorePluginId), SqlitePersistence.SnapshotStorePluginId, Full);
            SqliteSchema.SnapshotDdl(snapshot).Should().StartWith(
                "CREATE TABLE IF NOT EXISTS [snp]\n(\n" +
                "\t[p]                      NVarChar(255) NOT NULL,\n" +
                "\t[s]                      BigInt        NOT NULL,\n" +
                "\t[c]                      BigInt        NOT NULL,\n" +
                "\t[payload_snapshot_bytes] VarBinary         NULL,\n" +
                "\t[m]                      NVarChar(500)     NULL,\n" +
                "\t[sid]                    INTEGER           NULL,\n" +
                "\n" +
                "\tCONSTRAINT [PK_snp] PRIMARY KEY ([p], [s])\n" +
                ")\n;\r\n");
        }
    }
}
