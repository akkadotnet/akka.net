-- Copied from Akka.Persistence.Sql 1.5.70 (tag 1.5.70, commit 0c111df3d): docs/ddl/default/sqlite/journal-tags.sql
-- The shape users get when they create the tables by hand from the Akka.Persistence.Sql docs.
-- Journal Tags Table DDL
-- Generated for SQLite (table-mapping = default)
-- This table stores tags in normalized form (TagMode.TagTable)

CREATE TABLE IF NOT EXISTS tags (
    ordering_id INTEGER NOT NULL,
    tag TEXT NOT NULL,
    sequence_nr INTEGER NOT NULL,
    persistence_id TEXT NOT NULL,
    PRIMARY KEY (ordering_id, tag)
);

CREATE INDEX IF NOT EXISTS tags_persistence_id_sequence_nr_idx ON tags (persistence_id, sequence_nr);
CREATE INDEX IF NOT EXISTS tags_tag_idx ON tags (tag);

