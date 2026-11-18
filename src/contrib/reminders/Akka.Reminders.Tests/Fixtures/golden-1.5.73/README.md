# Golden reminders databases written by 1.5.73

`golden-local.db` and `golden-cluster.db` are SQLite files written by the released
`Aaron.Akka.Reminders` / `Aaron.Akka.Reminders.Sqlite` **1.5.73** packages (Akka.NET 1.5.73).
`GoldenDatabaseSpecs` copies each file to a temp path and checks that the current build reads
every row back with the same values. The test is read-only compatibility: 1.6 must read what
1.5 wrote. Reading 1.6 data with 1.5 is out of scope.

| File | Written by | Why |
| --- | --- | --- |
| `golden-local.db` | local-provider system | 1.5 stored `string`/`int`/`long` and Protobuf with the JSON serializer (id 1) |
| `golden-cluster.db` | cluster-provider system | 1.5 stored them with `primitive` (17) and `proto` (2) |

Each file holds 353 rows: every payload kind (JSON POCO, `string`, `int`, `long`, `byte[]`, `bool`,
`double`, a consumer-style `SerializerWithStringManifest` with `rp-v1`/`rp-v2` manifests, a type-manifest
`Serializer`, a no-manifest `Serializer`, two Protobuf well-known types), one-off and recurring
reminders, delivery windows and deadlines, and all six statuses (Pending, AwaitingAck, Delivered,
Cancelled, Expired, Failed) with attempt counts and failure reasons. Live rows are due in 2099 so the
file does not go stale. Three overdue rows in `golden-live` exist so the scheduler has something to
deliver. All data is synthetic.

## Where the expectations live

`Shared/GoldenRows.cs` is the single list of expected rows. The generator writes it through the 1.5.73
API and the test compares the database to it. `Shared/GoldenPayloads.cs` holds the payload types and
serializers. The test project and the generator both compile these two files. A change to either
file that is not followed by a regenerate makes the test fail. Stored serializer ids and manifests are
hard-coded in the test, so the test records what 1.5.73 wrote.

## Do not regenerate casually

These files are evidence of what a released version wrote. Regenerating replaces that evidence with
whatever the generator produces today. Do it only to add coverage, never to make a failing test pass.
If the test fails, the 1.6 read path has a bug: fix that, not the file. Output is deterministic, so a
regenerate with unchanged inputs gives byte-identical files.

## Regenerate

The generator in `generator/` is not part of `Akka.Reminders.slnx` and not built by CI. It has its own
`Directory.Build.props` and `Directory.Packages.props`, so it ignores the repo's central package
versions and uses pinned 1.5.73 packages. Its assembly name is `Akka.Reminders.Tests` on purpose:
stored manifests such as `Akka.Reminders.Tests.Golden.TypedPayload, Akka.Reminders.Tests` embed it.

```bash
cd src/Akka.Reminders.Tests/Fixtures/golden-1.5.73/generator
dotnet run -c Release -- ..
```

The generator reads each file back with 1.5.73 before it finishes, runs `VACUUM`, and fails if a
`-wal`, `-shm` or `-journal` file is left behind.
