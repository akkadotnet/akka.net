-- Copied from Akka.Persistence.Sql 1.5.70 (tag 1.5.70, commit 0c111df3d): docs/ddl/default/sqlite/snapshot.sql
-- The shape users get when they create the tables by hand from the Akka.Persistence.Sql docs.
-- Snapshot Table DDL
-- Generated for SQLite (table-mapping = default)
-- This table stores actor state snapshots

CREATE TABLE IF NOT EXISTS snapshot (
    persistence_id TEXT NOT NULL,
    sequence_number INTEGER NOT NULL,
    created INTEGER NOT NULL,
    snapshot BLOB,
    manifest TEXT,
    serializer_id INTEGER,
    PRIMARY KEY (persistence_id, sequence_number)
);

CREATE INDEX IF NOT EXISTS snapshot_sequence_number_idx ON snapshot (sequence_number);
CREATE INDEX IF NOT EXISTS snapshot_created_idx ON snapshot (created);

