//-----------------------------------------------------------------------
// <copyright file="ExpectedSchemas.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Generic;

namespace Akka.Persistence.Embedded.Tests.Schema
{
    /// <summary>
    /// sqlite_master rows (type, name, tbl_name, sql) that Akka.Persistence.Sql 1.5.70 produces on SQLite,
    /// ordered by type, name. Copied by hand from the capture run (schema-A..E.txt); no generator produces this file.
    /// </summary>
    internal static class ExpectedSchemas
    {
        public static IReadOnlyList<(string Type, string Name, string TableName, string? Sql)> For(SqliteTestMode mode)
            => mode switch
            {
                SqliteTestMode.TT => ModeTT,
                SqliteTestMode.CSV => ModeCSV,
                SqliteTestMode.BOTH => ModeBOTH,
                SqliteTestMode.DC => ModeDC,
                SqliteTestMode.NW => ModeNW,
                _ => throw new System.ArgumentOutOfRangeException(nameof(mode))
            };

        private static readonly (string Type, string Name, string TableName, string? Sql)[] ModeTT =
        [
            ("index", "journal_created_idx", "journal", "CREATE INDEX journal_created_idx ON journal (created)"),
            ("index", "journal_sequence_number_idx", "journal", "CREATE INDEX journal_sequence_number_idx ON journal (sequence_number)"),
            ("index", "journal_uq", "journal", "CREATE UNIQUE INDEX journal_uq ON journal (persistence_id, sequence_number)"),
            ("index", "snapshot_created_idx", "snapshot", "CREATE INDEX snapshot_created_idx ON snapshot (created)"),
            ("index", "snapshot_sequence_number_idx", "snapshot", "CREATE INDEX snapshot_sequence_number_idx ON snapshot (sequence_number)"),
            ("index", "sqlite_autoindex_snapshot_1", "snapshot", null),
            ("index", "sqlite_autoindex_tags_1", "tags", null),
            ("index", "tags_persistence_id_sequence_nr_idx", "tags", "CREATE INDEX tags_persistence_id_sequence_nr_idx ON tags (persistence_id, sequence_nr)"),
            ("index", "tags_tag_idx", "tags", "CREATE INDEX tags_tag_idx ON tags (tag)"),
            ("table", "journal", "journal", "CREATE TABLE [journal]\n(\n\t[ordering]        INTEGER        NOT NULL PRIMARY KEY AUTOINCREMENT,\n\t[created]         BigInt         NOT NULL,\n\t[deleted]         Bit            NOT NULL,\n\t[persistence_id]  NVarChar(255)  NOT NULL,\n\t[sequence_number] BigInt         NOT NULL,\n\t[message]         VarBinary      NOT NULL,\n\t[manifest]        NVarChar(500)      NULL,\n\t[identifier]      INTEGER            NULL,\n\t[writer_uuid]     NVarChar(128)      NULL\n)"),
            ("table", "snapshot", "snapshot", "CREATE TABLE [snapshot]\n(\n\t[persistence_id]  NVarChar(255) NOT NULL,\n\t[sequence_number] BigInt        NOT NULL,\n\t[created]         BigInt        NOT NULL,\n\t[snapshot]        VarBinary         NULL,\n\t[manifest]        NVarChar(500)     NULL,\n\t[serializer_id]   INTEGER           NULL,\n\n\tCONSTRAINT [PK_snapshot] PRIMARY KEY ([persistence_id], [sequence_number])\n)"),
            ("table", "sqlite_sequence", "sqlite_sequence", "CREATE TABLE sqlite_sequence(name,seq)"),
            ("table", "tags", "tags", "CREATE TABLE [tags]\n(\n\t[ordering_id]    INTEGER       NOT NULL,\n\t[tag]            NVarChar(64)  NOT NULL,\n\t[sequence_nr]    INTEGER       NOT NULL,\n\t[persistence_id] NVarChar(255) NOT NULL,\n\n\tCONSTRAINT [PK_tags] PRIMARY KEY ([ordering_id], [tag])\n)"),
        ];

        private static readonly (string Type, string Name, string TableName, string? Sql)[] ModeCSV =
        [
            ("index", "journal_created_idx", "journal", "CREATE INDEX journal_created_idx ON journal (created)"),
            ("index", "journal_sequence_number_idx", "journal", "CREATE INDEX journal_sequence_number_idx ON journal (sequence_number)"),
            ("index", "journal_uq", "journal", "CREATE UNIQUE INDEX journal_uq ON journal (persistence_id, sequence_number)"),
            ("index", "snapshot_created_idx", "snapshot", "CREATE INDEX snapshot_created_idx ON snapshot (created)"),
            ("index", "snapshot_sequence_number_idx", "snapshot", "CREATE INDEX snapshot_sequence_number_idx ON snapshot (sequence_number)"),
            ("index", "sqlite_autoindex_snapshot_1", "snapshot", null),
            ("table", "journal", "journal", "CREATE TABLE [journal]\n(\n\t[ordering]        INTEGER        NOT NULL PRIMARY KEY AUTOINCREMENT,\n\t[created]         BigInt         NOT NULL,\n\t[deleted]         Bit            NOT NULL,\n\t[persistence_id]  NVarChar(255)  NOT NULL,\n\t[sequence_number] BigInt         NOT NULL,\n\t[message]         VarBinary      NOT NULL,\n\t[tags]            NVarChar(100)      NULL,\n\t[manifest]        NVarChar(500)      NULL,\n\t[identifier]      INTEGER            NULL,\n\t[writer_uuid]     NVarChar(128)      NULL\n)"),
            ("table", "snapshot", "snapshot", "CREATE TABLE [snapshot]\n(\n\t[persistence_id]  NVarChar(255) NOT NULL,\n\t[sequence_number] BigInt        NOT NULL,\n\t[created]         BigInt        NOT NULL,\n\t[snapshot]        VarBinary         NULL,\n\t[manifest]        NVarChar(500)     NULL,\n\t[serializer_id]   INTEGER           NULL,\n\n\tCONSTRAINT [PK_snapshot] PRIMARY KEY ([persistence_id], [sequence_number])\n)"),
            ("table", "sqlite_sequence", "sqlite_sequence", "CREATE TABLE sqlite_sequence(name,seq)"),
        ];

        private static readonly (string Type, string Name, string TableName, string? Sql)[] ModeBOTH =
        [
            ("index", "journal_created_idx", "journal", "CREATE INDEX journal_created_idx ON journal (created)"),
            ("index", "journal_sequence_number_idx", "journal", "CREATE INDEX journal_sequence_number_idx ON journal (sequence_number)"),
            ("index", "journal_uq", "journal", "CREATE UNIQUE INDEX journal_uq ON journal (persistence_id, sequence_number)"),
            ("index", "snapshot_created_idx", "snapshot", "CREATE INDEX snapshot_created_idx ON snapshot (created)"),
            ("index", "snapshot_sequence_number_idx", "snapshot", "CREATE INDEX snapshot_sequence_number_idx ON snapshot (sequence_number)"),
            ("index", "sqlite_autoindex_snapshot_1", "snapshot", null),
            ("index", "sqlite_autoindex_tags_1", "tags", null),
            ("index", "tags_persistence_id_sequence_nr_idx", "tags", "CREATE INDEX tags_persistence_id_sequence_nr_idx ON tags (persistence_id, sequence_nr)"),
            ("index", "tags_tag_idx", "tags", "CREATE INDEX tags_tag_idx ON tags (tag)"),
            ("table", "journal", "journal", "CREATE TABLE [journal]\n(\n\t[ordering]        INTEGER        NOT NULL PRIMARY KEY AUTOINCREMENT,\n\t[created]         BigInt         NOT NULL,\n\t[deleted]         Bit            NOT NULL,\n\t[persistence_id]  NVarChar(255)  NOT NULL,\n\t[sequence_number] BigInt         NOT NULL,\n\t[message]         VarBinary      NOT NULL,\n\t[tags]            NVarChar(100)      NULL,\n\t[manifest]        NVarChar(500)      NULL,\n\t[identifier]      INTEGER            NULL,\n\t[writer_uuid]     NVarChar(128)      NULL\n)"),
            ("table", "snapshot", "snapshot", "CREATE TABLE [snapshot]\n(\n\t[persistence_id]  NVarChar(255) NOT NULL,\n\t[sequence_number] BigInt        NOT NULL,\n\t[created]         BigInt        NOT NULL,\n\t[snapshot]        VarBinary         NULL,\n\t[manifest]        NVarChar(500)     NULL,\n\t[serializer_id]   INTEGER           NULL,\n\n\tCONSTRAINT [PK_snapshot] PRIMARY KEY ([persistence_id], [sequence_number])\n)"),
            ("table", "sqlite_sequence", "sqlite_sequence", "CREATE TABLE sqlite_sequence(name,seq)"),
            ("table", "tags", "tags", "CREATE TABLE [tags]\n(\n\t[ordering_id]    INTEGER       NOT NULL,\n\t[tag]            NVarChar(64)  NOT NULL,\n\t[sequence_nr]    INTEGER       NOT NULL,\n\t[persistence_id] NVarChar(255) NOT NULL,\n\n\tCONSTRAINT [PK_tags] PRIMARY KEY ([ordering_id], [tag])\n)"),
        ];

        private static readonly (string Type, string Name, string TableName, string? Sql)[] ModeDC =
        [
            ("index", "journal_created_idx", "journal", "CREATE INDEX journal_created_idx ON journal (created)"),
            ("index", "journal_sequence_number_idx", "journal", "CREATE INDEX journal_sequence_number_idx ON journal (sequence_number)"),
            ("index", "journal_uq", "journal", "CREATE UNIQUE INDEX journal_uq ON journal (persistence_id, sequence_number)"),
            ("index", "snapshot_created_idx", "snapshot", "CREATE INDEX snapshot_created_idx ON snapshot (created)"),
            ("index", "snapshot_sequence_number_idx", "snapshot", "CREATE INDEX snapshot_sequence_number_idx ON snapshot (sequence_number)"),
            ("index", "sqlite_autoindex_journal_metadata_1", "journal_metadata", null),
            ("index", "sqlite_autoindex_snapshot_1", "snapshot", null),
            ("index", "sqlite_autoindex_tags_1", "tags", null),
            ("index", "tags_persistence_id_sequence_nr_idx", "tags", "CREATE INDEX tags_persistence_id_sequence_nr_idx ON tags (persistence_id, sequence_nr)"),
            ("index", "tags_tag_idx", "tags", "CREATE INDEX tags_tag_idx ON tags (tag)"),
            ("table", "journal", "journal", "CREATE TABLE [journal]\n(\n\t[ordering]        INTEGER        NOT NULL PRIMARY KEY AUTOINCREMENT,\n\t[created]         BigInt         NOT NULL,\n\t[deleted]         Bit            NOT NULL,\n\t[persistence_id]  NVarChar(255)  NOT NULL,\n\t[sequence_number] BigInt         NOT NULL,\n\t[message]         VarBinary      NOT NULL,\n\t[manifest]        NVarChar(500)      NULL,\n\t[identifier]      INTEGER            NULL,\n\t[writer_uuid]     NVarChar(128)      NULL\n)"),
            ("table", "journal_metadata", "journal_metadata", "CREATE TABLE [journal_metadata]\n(\n\t[persistence_id]  NVarChar(255) NOT NULL,\n\t[sequence_number] BigInt        NOT NULL,\n\n\tCONSTRAINT [PK_journal_metadata] PRIMARY KEY ([sequence_number], [persistence_id])\n)"),
            ("table", "snapshot", "snapshot", "CREATE TABLE [snapshot]\n(\n\t[persistence_id]  NVarChar(255) NOT NULL,\n\t[sequence_number] BigInt        NOT NULL,\n\t[created]         BigInt        NOT NULL,\n\t[snapshot]        VarBinary         NULL,\n\t[manifest]        NVarChar(500)     NULL,\n\t[serializer_id]   INTEGER           NULL,\n\n\tCONSTRAINT [PK_snapshot] PRIMARY KEY ([persistence_id], [sequence_number])\n)"),
            ("table", "sqlite_sequence", "sqlite_sequence", "CREATE TABLE sqlite_sequence(name,seq)"),
            ("table", "tags", "tags", "CREATE TABLE [tags]\n(\n\t[ordering_id]    INTEGER       NOT NULL,\n\t[tag]            NVarChar(64)  NOT NULL,\n\t[sequence_nr]    INTEGER       NOT NULL,\n\t[persistence_id] NVarChar(255) NOT NULL,\n\n\tCONSTRAINT [PK_tags] PRIMARY KEY ([ordering_id], [tag])\n)"),
        ];

        private static readonly (string Type, string Name, string TableName, string? Sql)[] ModeNW =
        [
            ("index", "journal_created_idx", "journal", "CREATE INDEX journal_created_idx ON journal (created)"),
            ("index", "journal_sequence_number_idx", "journal", "CREATE INDEX journal_sequence_number_idx ON journal (sequence_number)"),
            ("index", "journal_uq", "journal", "CREATE UNIQUE INDEX journal_uq ON journal (persistence_id, sequence_number)"),
            ("index", "snapshot_created_idx", "snapshot", "CREATE INDEX snapshot_created_idx ON snapshot (created)"),
            ("index", "snapshot_sequence_number_idx", "snapshot", "CREATE INDEX snapshot_sequence_number_idx ON snapshot (sequence_number)"),
            ("index", "sqlite_autoindex_snapshot_1", "snapshot", null),
            ("index", "sqlite_autoindex_tags_1", "tags", null),
            ("index", "tags_persistence_id_sequence_nr_idx", "tags", "CREATE INDEX tags_persistence_id_sequence_nr_idx ON tags (persistence_id, sequence_nr)"),
            ("index", "tags_tag_idx", "tags", "CREATE INDEX tags_tag_idx ON tags (tag)"),
            ("table", "journal", "journal", "CREATE TABLE [journal]\n(\n\t[ordering]        INTEGER        NOT NULL PRIMARY KEY AUTOINCREMENT,\n\t[created]         BigInt         NOT NULL,\n\t[deleted]         Bit            NOT NULL,\n\t[persistence_id]  NVarChar(255)  NOT NULL,\n\t[sequence_number] BigInt         NOT NULL,\n\t[message]         VarBinary      NOT NULL,\n\t[manifest]        NVarChar(500)      NULL,\n\t[identifier]      INTEGER            NULL\n)"),
            ("table", "snapshot", "snapshot", "CREATE TABLE [snapshot]\n(\n\t[persistence_id]  NVarChar(255) NOT NULL,\n\t[sequence_number] BigInt        NOT NULL,\n\t[created]         BigInt        NOT NULL,\n\t[snapshot]        VarBinary         NULL,\n\t[manifest]        NVarChar(500)     NULL,\n\t[serializer_id]   INTEGER           NULL,\n\n\tCONSTRAINT [PK_snapshot] PRIMARY KEY ([persistence_id], [sequence_number])\n)"),
            ("table", "sqlite_sequence", "sqlite_sequence", "CREATE TABLE sqlite_sequence(name,seq)"),
            ("table", "tags", "tags", "CREATE TABLE [tags]\n(\n\t[ordering_id]    INTEGER       NOT NULL,\n\t[tag]            NVarChar(64)  NOT NULL,\n\t[sequence_nr]    INTEGER       NOT NULL,\n\t[persistence_id] NVarChar(255) NOT NULL,\n\n\tCONSTRAINT [PK_tags] PRIMARY KEY ([ordering_id], [tag])\n)"),
        ];

    }
}
