# Scheduler throughput comparison

The scheduler harness measures real PostgreSQL reads, batched mutation commits, and envelope sends.
It uses a counting resolver instead of a consumer and freezes the clock during each run. The timed
region begins with a fetch message and ends at a following scheduler mailbox query. Initialization,
population, reset, and shutdown are outside the timer. Separate virtual-clock tests check deadlines
and recovery; this benchmark does not measure consumer acknowledgement throughput or recovery latency.

Use a dedicated disposable database. Each run truncates `reminders.scheduled_reminders`.

```bash
docker run -d --name reminders-benchmark -p 127.0.0.1:55432:5432 \
  -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=reminders_bench postgres:16-alpine
export REMINDERS_BENCHMARK_CONNECTION_STRING='Host=localhost;Port=55432;Database=reminders_bench;Username=postgres;Password=postgres'
dotnet build src/Akka.Reminders.Benchmarks -c Release
dotnet src/Akka.Reminders.Benchmarks/bin/Release/net9.0/Akka.Reminders.Benchmarks.dll \
  --scheduler-throughput early-recurring 5000 3
```

Scenarios are `one-off`, `due-recurring`, `early-recurring` (due two seconds ahead), and
`recurring-retry` (attempt count one, with its successor already present). Settings are a fetch batch
of 1,000, commit chunks of 100, five seconds of slippage, a one-hour recurring interval, and a two-hour
ack timeout. One warmup precedes the requested measured runs. Output includes elapsed milliseconds,
delivery counts, fetches, commits, overview reads, and occurrence status reads.

For a revision comparison, copy the same `SchedulerThroughput.cs`, `Program.cs`, and
`SqlReminderBenchmarkBase.cs` into three isolated worktrees and build each in Release. Keep the
database and harness identical; run the comparisons serially without concurrent builds or tests.

```bash
python3 src/Akka.Reminders.Benchmarks/compare-scheduler.py \
  --dev /path/to/dev --unfixed /path/to/unfixed-pr-stack \
  --fixed /path/to/fixed-stack --output /path/to/results
```

The script checks every expected delivery, runs all four scenarios at 1,000, 5,000, and 25,000
reminders, and writes raw JSON lines, per-run logs, revision identifiers, and a CSV of medians and
minimum/maximum times. Shared-host short runs establish observed differences, not production capacity
or statistical equivalence. Remove the dedicated container when finished:

```bash
docker rm -f reminders-benchmark
```
