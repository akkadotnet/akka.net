# Scheduler recovery and throughput, 2026-10-08

Automatic recovery and the remaining scheduler corrections were validated against the clarified behavior spec. The fixed stack removes the serialized occurrence-status reads from recurring processing. It uses atomic conditional writes inside existing chunk commits and the existing fetch timer for storage recovery.

An initial save that persists but reports Error recovers without client intervention. Recovery survives unrelated acknowledgements and further write/read failures. Repeated failed fetches are delayed even when another command refreshes an overdue overview. Model assertions retain recovery obligations after healing and account for persisted acknowledgements with lost replies, unobserved attempts, and delayed timeout detection.

Explicit recurring re-registration retains its overwrite behavior. First-attempt successors retain that behavior; retry successors are inserted only if absent and preserve existing state/budgets. Superseded predecessors are completed only while active. Custom providers must implement the additive conditional-mutation contract and advertise it with IConditionalReminderMutationStorage before processing a store containing recurring reminders. No storage schema or wire layout changes were introduced.

## Scheduler measurements

All times below are medians in seconds from one warmup and three measured runs. Each run verified the expected delivery count. The identical harness ran serially against a dedicated PostgreSQL 16 container. The fixed clock isolates throughput; these are shared-host short runs, not production-capacity or equivalence guarantees.

| Reminders | Scenario | dev | Original PR stack | Fixed | Fixed / dev |
|---:|---|---:|---:|---:|---:|
| 1,000 | one-off | 0.100 | 0.105 | 0.099 | 0.99x |
| 1,000 | due-recurring | 0.212 | 0.204 | 0.211 | 0.99x |
| 1,000 | early-recurring | 0.195 | 0.665 | 0.279 | 1.43x |
| 1,000 | recurring-retry | 0.227 | 0.589 | 0.184 | 0.81x |
| 5,000 | one-off | 0.556 | 0.542 | 0.594 | 1.07x |
| 5,000 | due-recurring | 1.009 | 0.906 | 0.961 | 0.95x |
| 5,000 | early-recurring | 1.010 | 2.718 | 1.100 | 1.09x |
| 5,000 | recurring-retry | 1.392 | 2.595 | 0.796 | 0.57x |
| 25,000 | one-off | 1.941 | 1.973 | 2.019 | 1.04x |
| 25,000 | due-recurring | 4.186 | 3.906 | 6.113 | 1.46x |
| 25,000 | early-recurring | 3.894 | 14.404 | 4.521 | 1.16x |
| 25,000 | recurring-retry | 4.259 | 11.863 | 3.676 | 0.86x |

The original stack performed one status read per reminder in the early-recurring and recurring-retry workloads. Every fixed run performed zero status reads. Fetch and commit call counts were unchanged in these measured workloads: for 25,000 reminders, 26 fetches and 250 chunk commits. Conditional writes may add SQL statements inside a commit; this is not a claim of identical SQL round-trip counts. A short conditional batch can require one final bounded fetch to reconcile the overview.

The initially slow 25,000 due-recurring fixed result is retained above. A separate reversed-order confirmation used one warmup and five measured runs per revision: fixed median 3.974 s (3.827–4.070), dev 3.889 s (3.830–4.038). The slowdown did not persist in that confirmation; its cause was not established. See due-recurring-25000-confirmation.csv.

At 25,000 early recurring reminders, the fixed stack is 68.6% faster than the original stack, with 16.1% overhead relative to dev in this run. This workload retains a batched conditional predecessor completion to enforce latest-only semantics. Retry throughput improved while preserving successors instead of rewriting them.

## Existing storage benchmark cross-check

The existing BenchmarkDotNet fetch-and-complete benchmark also completed all six cases on dev and fixed, serially against the same database. These values are means, unlike the scheduler medians above. Each case used one launch, one warmup, and three measured iterations.

| Reminders | Fetch batch size | dev mean (ms) | Fixed mean (ms) |
|---:|---:|---:|---:|
| 1,000 | 1,000 | 60.14 | 62.07 |
| 1,000 | 5,000 | 63.86 | 62.85 |
| 5,000 | 1,000 | 318.84 | 334.16 |
| 5,000 | 5,000 | 280.50 | 325.84 |
| 25,000 | 1,000 | 1,440.13 | 1,501.81 |
| 25,000 | 5,000 | 1,349.03 | 1,362.50 |

This benchmark bypasses the scheduler and its conditional recurring mutations. It checks the existing storage path, with wide error bars from the short shared-host run; it does not establish equivalence or explain scheduler differences. Full results, including dispersion, are in storage-dev.csv and storage-fixed.csv.

## Validation and boundaries

- The initial integrated run passed 316 tests. After adversarial checker corrections, 335 integrated tests passed, zero failed or skipped; REMINDERS_CSCHECK_SQL=1 enabled the generated model on all four providers.
- Twelve shared conditional-mutation cases passed across InMemory, SQLite, PostgreSQL, and SQL Server. InMemory/SQLite cases overlap with the integrated test run.
- Solution Release build passed with zero warnings and errors; Slopwatch found zero issues in changed C#.
- The full per-test-container PostgreSQL/SQL Server storage fixture suites were excluded from the integrated run. The changed SQL paths were checked with focused all-provider mutation tests and generated SQL model scenarios.
- Deadline/recovery correctness is established by virtual-clock regressions, not by the frozen-clock throughput timings. Consumer processing, acknowledgement throughput, and recovery latency are outside the timed harness.
- Model ambiguity bounds remain conservative because injected commit failures do not identify affected rows. The oracle may include unrelated batch commits as possible unobserved attempts.
- Adversarial review corrected lost-acknowledgement safety and batch attribution. Ack faults record submitted identities, never persisted rows or results. Known acceptance forbids redelivery; uncertain acceptance is confined to its occurrence and clears on another delivery or a successful nack. Positive and negative checker histories cover late NotFound, concurrent batches, buffered timing, and retry requirements after uncertainty resolves.

## Revisions and reproduction

Exact revisions, environment, harness hash, settings, and validation paths are recorded in benchmark-method.json and revisions.json. Runtime and benchmark source trees are identical between the fixed benchmark snapshot and the post-review implementation; later changes affect model checks and documentation.

Use the runner and instructions in src/Akka.Reminders.Benchmarks/README.md. Raw measured runs are in scheduler-results.jsonl; medians, ranges, and call counts are in scheduler-comparison.csv. Per-run console logs and original test TRX files remain in /tmp/akka-reminders-implementation-benchmarks and /tmp/akka-reminders-implementation-results.

The implementation was published through PRs #155, #157, #159 and #149 after adversarial review. The runtime fixes have reached dev; #149 carries the behavior model, final specification clarification and benchmark evidence. The local integration branch fix/reviewed-scheduler-recovery is retained for reproducibility.
