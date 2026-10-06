# Akka.Persistence.Sql compatibility fixture

`aps-1.5.70.db` is a SQLite file that Akka.Persistence.Sql 1.5.70 wrote with its default SQLite settings:
tag-write-mode `TagTable`, no `delete-compatibility-mode`, default table names (`journal`, `tags`, `snapshot`),
`auto-initialize` on. `ApsCompatSpec` copies it, starts Akka.Persistence.Embedded on the copy, and checks that
Embedded reads it, leaves its tables alone, and keeps writing to it.

## Contents

| persistence id | what it holds |
|----------------|---------------|
| `alpha` | events 1-8 (strings `alpha-1`..`alpha-8`), a snapshot at sequence 6, then `DeleteMessagesTo(3)`: rows 1-2 are gone and row 3 is the tombstone (`deleted = 1`) |
| `beta` | events 1-6; events 2 and 4 are `CompatItem` records, the rest are strings; no snapshot, no delete |
| `gamma` | events 1-5, then `DeleteMessagesTo(5)`: only the tombstone (row 5) is left |

- Strings use the built-in JSON serializer (id 1, empty manifest).
- `CompatItem(string Name, int Qty)` uses a small serializer with fixed id 9001 and manifest `I`. The spec defines
  the same serializer (`CompatItemSerializer`).
- A write event adapter tags events: an odd trailing digit gets `odd`, `-3` and `-6` also get `blue`, records get
  `item`. The tombstone rows keep their `tags` rows.
- Journal `ordering` values are 3-8 (alpha), 9-14 (beta) and 19 (gamma). 13 journal rows, 13 tag rows, 1 snapshot
  row. The file is a single 56 KB `.db`: no `-wal` or `-shm`, journal mode `delete`.
- `created` and `writer_uuid` change on every regeneration, so the spec asserts on neither.

## Versions

Akka 1.5.70, Akka.Hosting 1.5.70, Akka.Persistence.Sql 1.5.70, Akka.Persistence.Sql.Hosting 1.5.70,
Microsoft.Data.Sqlite 8.0.14 (SQLitePCLRaw 2.1.6), linq2db 5.4.1.9. NuGet also pulled Akka.Persistence 1.5.70-beta1 and
Akka.Persistence.Hosting 1.5.67 through Akka.Persistence.Sql. Built and run on .NET 8.

## Regenerate

`Program.cs.txt` and `ApsCompatGenerator.csproj.txt` are the generator, renamed so the test project does not compile
them. Put them in an empty folder, drop the `.txt` suffixes, and run:

```bash
dotnet run -c Release -- generate ./out      # writes ./out/aps-1.5.70.db
```

Replace `aps-1.5.70.db` with the new file. If the contents change, update the literals in `ApsCompatSpec`.

## Reverse check (done once, not in CI)

`dotnet run -c Release -- verify <copy-of-a-db>` opens a file with Akka.Persistence.Sql 1.5.70 and prints what it
reads: recovery of each persistence id (with and without the snapshot), `CurrentEventsByTag`, `CurrentAllEvents`,
`CurrentEventsByPersistenceId` and `CurrentPersistenceIds`. It writes to the file, so give it a copy.

Verified on 2026-10-06: Akka.Persistence.Sql 1.5.70 reads a copy of the database after `ApsCompatSpec`'s last test
wrote to it with Embedded. The file held Embedded's own tombstones, with an empty message: `alpha` at sequence 5
(rows 6-10 live) and `delta` at sequence 4 (every event deleted). Akka.Persistence.Sql recovered `alpha` 6-10,
`beta` 1-7 (with the snapshot Embedded saved at 7), `gamma` 6 and `delta` with nothing and highest sequence number 4.
`CurrentEventsByTag` (`odd`, `blue`, `item`), `CurrentAllEvents` and `CurrentPersistenceIds` (`alpha`, `beta`,
`gamma`) matched the literals in the spec, offsets included.
