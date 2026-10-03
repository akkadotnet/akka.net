-- Copied from Akka.Persistence.Sql 1.5.70 (tag 1.5.70, commit 0c111df3d): docs/ddl/default/sqlite/metadata.sql
-- The shape users get when they create the tables by hand from the Akka.Persistence.Sql docs.
-- Journal Metadata Table DDL
-- Generated for SQLite (table-mapping = default)
-- This table is used for delete-compatibility-mode

CREATE TABLE IF NOT EXISTS journal_metadata (
    persistence_id TEXT NOT NULL,
    sequence_number INTEGER NOT NULL,
    PRIMARY KEY (persistence_id, sequence_number)
);

