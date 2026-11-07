# Typed Streams slots: measurements and adversarial review

Measured 2026-10-14 for [PR #8807](https://github.com/akkadotnet/akka.net/pull/8807).

## Judgment

Typed slots provide a real Artery allocation improvement and broader value-type Streams savings. They are not the only way to address Artery boxing. Keep the PR as a draft while reviewing its permanent compatibility machinery and completing the remaining same-revision checks. No confirmed correctness/API defect was identified in the bounded independent review; this is not a universal compatibility approval.

The actual Artery graph now supplies evidence that the synthetic forwarding benchmark alone could not. A smaller existing-box forwarding helper or an Artery-private reference carrier remains an alternative, not an implemented comparison. The current branch does not redesign actor/substream messages, ownership, scheduling, public contracts or wire data.

## Revisions and measurement scope

- Baseline interpreter: `a50f67d65eec468c279ba86baa67f1751d44675b`.
- Candidate production: `041f39dc66ae08735ba6c26898e2ce6cd422a905`.
- Production Artery and RemotePingPong sources match between revisions; baseline includes the earlier encoder fixes and sequence benchmark prerequisite #8806.
- BenchmarkDotNet 0.15.8, .NET 10.0.12, SDK 10.0.103; Ubuntu 24.04 x64 VM, eight visible cores. Host activity and scheduling vary.
- Reuse existing SequenceBoxingBenchmarks, StreamThroughputBenchmarks, AsyncBoundaryBenchmarks and MaterializationBenchmarks. No new general control/materialization class.
- Materialization returns its completion Task on both revisions. It measures graph setup, one element, sink completion and scheduling, not only synchronous materialization or fully drained actor termination.
- Generated BenchmarkDotNet tables are embedded directly in the PR description. Raw tables, logs, hashes, commands, traces and analysis are retained locally under `/home/petabridge/benchmark-results/akka.net/typed-stream-slots-2026-10-14`.

## Allocation and reference controls

All 19 Short cases completed on both revisions. One operation is one element except materialization, which is per completed graph.

| Existing workload | Baseline B/element | Candidate B/element |
| --- | ---: | ---: |
| Sequence, 0 forwarding stages | 41 | 1 |
| Sequence, 1 forwarding stage | 81 | 1 |
| Sequence, 4 forwarding stages | 201 | 1 |
| Sequence, 8 forwarding stages | 361 | 1 |
| Allocated reference carrier, all stage counts | 41 | 41 |
| Integer linear / deep / merge | 73 / 145 / 49 | 1 / 1 / 1 |
| Integer batch | 201 | 153 |

The residual 1 B includes amortized graph overhead; this is not an allocation-free transport claim. The one-stage reference timing outlier from Short did not reproduce in the paired Medium controls. All four reference-carrier means were lower on the candidate in that pair; this does not prove that every reference graph is faster.

## Actual Artery RemotePingPong

Identical temporary process-wide `GC.GetTotalAllocatedBytes(precise: true)` instrumentation brackets priming through completion-task settlement. ActorSystem construction and teardown are outside the window. Both loopback systems and all managed threads are included. Temporary source changes were restored exactly; instrumented binaries and patch are retained.

All six unprofiled invocations were sequential. Real source-generated V2 payload: 348 serialized bytes, window 50, default outbound queue. One-lane ABBA uses one client, 1,000,000 repeats/client and three repetitions/invocation. Four-lane baseline/candidate uses ten clients, 250,000 repeats/client and three repetitions/invocation. Inbound/outbound lane counts are equal within each configuration.

| Configuration | Baseline median B/configured message | Candidate median | Reduction |
| --- | ---: | ---: | ---: |
| One lane, six reps/revision, ABBA | 3613.892 | 3314.373 | 299.519 B / 8.29% |
| Four lanes, three reps/revision, one pair | 3377.177 | 3050.352 | 326.825 B / 9.68% |

One-lane allocation ranges: 3613.360–3615.022 versus 3313.237–3316.214 B. Four-lane ranges: 3376.764–3380.761 versus 3040.838–3057.683 B. All invocations exited zero without drop/resend warnings. Process priority elevation was denied, so runs used normal priority.

One-lane throughput medians 354,270 versus 348,551 configured messages/s overlap substantially; no speedup is established. Four-lane medians 585,412 versus 702,248 are directional observations from one pair, not a confidence claim.

The denominator is configured ping-plus-echo traffic, not an exactly drained delivery count. The inherited harness counts local primers and can settle with up to window−1 echo chains/client pending; maximum denominator discrepancy is about 0.0049% at one million repeats or 0.0196% at 250,000. Readiness is Identify, not real-payload warmup. Lazy initialization, control traffic, heartbeats and background allocations may remain in the window. Each repetition recreates both systems. These are comparative process totals, not exact transport-only or boxing-only bytes.

## Allocation stack profiles

Four separate GC/managed-thread profiles each use 2,000,000 configured ping-plus-echo messages. One lane: one client × 1,000,000 repeats. Four lanes: ten clients × 100,000 repeats. All profile processes ran sequentially, separately from unprofiled timing. Every converted trace reports zero lost events.

| Exact sampled type: ReadOnlySequence<byte>, 40 B | Baseline ticks | Candidate ticks |
| --- | ---: | ---: |
| One lane | 6,644 | 801 |
| Four lanes | 6,053 | 231 |

Baseline sequence allocation stacks at encoding, shared/unique kill switches, termination watching, concat, delay cancellation and recovery disappear on the candidate. Remaining boxes chiefly cross restart/substream object handoffs; TCP also samples a smaller early allocation site. MergeHub's 48 B element wrapper is separate and remains allocated.

Ticks and AllocationAmount64 weights are sampled observations, not exact object totals or exact bytes attributable to a type. Inbound sequences are TCP chunks; multi-lane outbound sequences may contain batches. Multiplying a static stage count by 40 does not establish 280 B per application message. The process-wide measurement and resolved stacks are complementary evidence.

## Materialization: structural cost is real; fixed timing regression is unproven

| Unprofiled Medium order | Baseline mean / SD | Candidate mean / SD | Baseline / candidate allocation |
| --- | ---: | ---: | ---: |
| Baseline then candidate | 24.66 / 0.60 μs | 26.24 / 1.06 μs | 28.48 / 28.56 KB |
| Candidate then baseline | 31.04 / 3.80 μs | 25.76 / 0.95 μs | 28.53 / 28.52 KB |

The first pair's +1.58 μs/+6.4% did not reproduce with reversed order. Neither a fixed slowdown nor speedup is established. The earlier claim of a confirmed materialization timing cost was too strong; both pairs remain visible in the PR.

Separate profiles reuse the same existing materialization benchmark in process. BenchmarkDotNet EngineEventSource IDs 15/16 select five complete measured windows and 163,840 operations on each revision, including worker threads; zero lost events, clipped windows, marker mismatches or unclosed windows. Profiling timings are not substituted for the unprofiled tables.

Observed object sizes: Connection 168 → 176 B; ElementSlot<int> 32 B. Holders allocate per eligible connection; additional stage-port lookups and factory/type checks run during materialization. Standard shapes cache their inlet/outlet arrays, so the new lookups do not allocate port arrays. Removed element boxes offset some cost. Aggregate graph allocation differences vary with scheduling and the completion boundary; they do not isolate the exact holder/factory cost.

Graph materialization/fusing dominates identifiable application stacks. GraphAssembly.Materialize appears in 444 versus 385 managed samples; that is stack frequency, not CPU-time attribution. Factory methods can inline, and absence of a separate frame cannot prove negligible cost. Repetitive allocation-tick sampling aliases strongly, so weighted type totals must not partition the materialization delta. The profiles do not attribute the earlier 1.58 μs to factories, GC or scheduling specifically.

Initial profiling attempts completed workloads but stalled during Markdown export: dotnet --version inherited the profiler's suspended diagnostic endpoint. The minidump established that mechanism. Initial failures remain retained. Baseline's final export recovered by stopping only the owned SDK metadata child after measured work; candidate attached to an already running process and completed normally. The workload interval filter excludes export and startup. For future runs, attach to the running benchmark to avoid inherited suspension.

## Additional boxing opportunities, ranked by evidence

1. Generated IReadOnlyList<Reading> enumeration: 40 B List enumerator boxes occur in WriteRealBenchmarkMessage throughout all four actual profiles. SizeOf/SizeHint boxes are also sampled earlier. Both generated methods enumerate through the interface. Prefer concrete List/array fast paths with the current interface fallback; blanket indexing can change mutation/version behavior and make custom/tree-backed lists slower. Two emitted loops do not prove a steady 80 B saving per encode, and no fix is implemented here.
2. Restart/substream payloads: SubSource.PushSubstream creates OnNext(object), and these sequence boxes persist on the actual ordinary path. A typed message must count its larger size against the removed box; removing one 40 B box does not imply a 40 B net saving. Preserve delayed initialization, restart, cancellation, tracing and ownership.
3. TCP null guard: TcpStages.cs passes a successfully grabbed nonnullable sequence to RequireNonNullElement(object). Actual IL contains a box and profiles sample it early (roughly 0.66–2.62 seconds of the one-lane trace, 0.60–4.32 seconds of the four-lane trace). Removing this specific redundant guard is smaller than changing the null-check API. Cold/tiering causality and steady per-message savings remain unproven.
4. Singleton actor-boundary forwarding: an existing box in object[] can be unboxed and boxed again during singleton flush. Reusing that box is a narrower option, but its frequency on current Artery needs evidence before assigning payoff.
5. Generic async callbacks: value tuples/booleans pass through object callbacks; AsyncInput is also boxed. Combining or typing these envelopes needs a measured net object-size benefit and strong lifecycle/tracing checks. The profiles also contain reference callback closures; they are allocations rather than boxing.

## Verification checkpoint

At candidate production revision: 109 focused tests passed, zero skipped/failed, including 22 typed-slot cases; independently reviewed. All 19 Short benchmark cases and both paired Medium/reversed confirmations completed. Slopwatch and whitespace checks passed.

The preceding implementation passed the full Streams suite (1,899 passed, 33 pre-existing skips); that is not a same-revision full-suite result for the object-path refinement. Current-revision full local suite, API approvals, dependent remoting checks and local native execution remain incomplete while the necessity review proceeds. Hosted AOT canaries and CodeQL reported success at 041f39; other hosted jobs were still running at the review checkpoint. No merge authority or readiness is implied for #8807.

## Reproduction

Use the same awaited materialization harness adjustment on the unchanged baseline. Existing benchmark commands:

```bash
dotnet src/benchmark/Akka.Benchmarks/bin/Release/net10.0/Akka.Benchmarks.dll --filter '*SequenceBoxingBenchmarks*' '*StreamThroughputBenchmarks*' '*AsyncBoundaryBenchmarks*' '*MaterializationBenchmarks*' --job Short --buildTimeout 300 --artifacts /path/to/short

dotnet src/benchmark/Akka.Benchmarks/bin/Release/net10.0/Akka.Benchmarks.dll --filter '*SequenceBoxingBenchmarks.ReferenceFrames*' '*MaterializationBenchmarks*' --job Medium --buildTimeout 300 --artifacts /path/to/medium
```

For unprofiled Artery, apply the retained identical allocation-window patch and build each revision separately. Never overlap runs:

```bash
dotnet /path/to/revision/RemotePingPong.dll run --artery --clients 1 --msgs 1000000 --times 3 --payload real --serializer v2 --inbound-lanes 1 --outbound-lanes 1
```

For allocation attribution, collect gc-verbose plus Microsoft-DotNETCore-SampleProfiler in separate runs; exclude their instrumented timings from performance claims. Retained scripts show exact lane, client, message and marker settings. No additional fix or benchmark class is included in this review round.
