-- Copied from Akka.Persistence.Sql 1.5.70 (tag 1.5.70, commit 0c111df3d): docs/ddl/default/sqlite/journal.sql
-- The shape users get when they create the tables by hand from the Akka.Persistence.Sql docs.
-- Journal Table DDL
-- Generated for SQLite (table-mapping = default)
-- This table stores all persisted events

CREATE TABLE IF NOT EXISTS journal (
    ordering INTEGER PRIMARY KEY AUTOINCREMENT,
    deleted INTEGER NOT NULL DEFAULT 0,
    persistence_id TEXT NOT NULL,
    sequence_number INTEGER NOT NULL,
    created INTEGER NOT NULL,
    tags TEXT,
    message BLOB NOT NULL,
    identifier INTEGER,
    manifest TEXT,
    writer_uuid TEXT,
    UNIQUE (persistence_id, sequence_number)
);

CREATE INDEX IF NOT EXISTS journal_ordering_idx ON journal (ordering);
CREATE INDEX IF NOT EXISTS journal_created_idx ON journal (created);
CREATE INDEX IF NOT EXISTS journal_persistence_id_idx ON journal (persistence_id);

