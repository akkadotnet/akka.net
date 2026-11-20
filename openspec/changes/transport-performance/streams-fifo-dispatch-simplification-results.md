# Streams FIFO dispatch simplification: measured follow-up to #8826

Recommendation: keep the unboxed same-actor FIFO and prefer the single-target dispatch simplification for review. It removes redundant shell state and the ordinary bound delegate while preserving the public callback adapter. It also avoids the current PR's StageActor median penalty in the final comparison. This is a measured trade-off, not a claim of universal throughput neutrality: the final candidate's RemotePingPong median is 2.04% below current #8826, with overlapping process ranges.

## Source and behavior

Parent: `424de916d38f00c3b4b738921abb739368def656`; current #8826: `5640f8d724750ca8d1ce94648a176bcde878029d`; intermediate owner-only experiment: `1fbf8ec65320e56d1c6c55b32f786b7693d6cd8e`; final single-target candidate: `6871339f11bf7f0611b90ae41d711a6f229a42a8`.

Current #8826 stores both `Action<object> _enqueueToShortCircuit` and `ActorGraphInterpreter _shortCircuitOwner` in each shell. Normal initialization supplies both references. The candidate stores one reference target: the interpreter owner for ordinary materialization, or the supplied delegate for public `Init`. Resumes and local callbacks use the owner directly; the public adapter invokes its original delegate. The `object` field holds reference types, not message values. Normal callbacks still call `EnqueueAsyncToShortCircuit(AsyncInput)` without boxing. Mailbox and public-adapter boxing remain compatible.

One lazy `Queue<ShortCircuitEvent>` continues to preserve callback/control ordering. No queue pooling, split queues, custom ring buffer, new callback framework, public API, wire-format, dependency, or benchmark type is introduced. The 48-byte x64 FIFO entry remains a potential cold/burst storage cost; it was not redesigned without evidence. Initialization installs the target before stage initialization and PreStart. Actor-context routing, quotas, flush-on-park, completion, exceptions, tracing and feedback retain their existing execution paths.

## Final three-arm comparison

The final comparison uses fresh parent/current/target/target/current/parent blocks, two child launches per case per block, six warmups and twelve actuals per launch. Four fresh processes per role per case; 48 BDN rows, 1,152 workload actuals and 576 workload warmups across eight existing cases. Hot iterations contain four invocations of 100,000 logical operations; graph controls contain 1,024 invocations. Throughput strategy, DontRemove outliers, ordinary tiering/PGO, BDN 0.15.8, .NET 10.0.12 / SDK 10.0.103, concurrent workstation GC, one effective heap, adaptation 0. Harness and build settings match; the parent receives only existing current benchmark harness projections. Compiled production differences are confined to ActorGraphInterpreter.cs.

The timing summary below uses the median of four process means, rather than pooling iterations as independent trials. Memory rows summarize two generated BDN block observations. Graph operations await sink completion; pending actor teardown and scheduling remain part of the allocation/timing limits. Their totals do not establish a fixed saving per graph.

| Case | Parent ns/op | Current #8826 ns/op | Single-target candidate ns/op | Current vs parent time | Target vs current time |
|---|---:|---:|---:|---:|---:|
| Actor_materializer_run_restart_flow_graph | 142669.50 | 149892.29 | 141656.26 | +5.06% | -5.49% |
| Actor_materializer_run_simple_linear_graph | 124626.72 | 126657.38 | 125154.07 | +1.63% | -1.19% |
| Source_Queue_Reference_Ingress_Throughput | 1030.53 | 997.53 | 1024.04 | -3.20% | +2.66% |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1455.07 | 1465.34 | 1457.93 | +0.71% | -0.51% |
| StageActor_Reference_Ingress_Throughput | 213.74 | 293.28 | 208.37 | +37.21% | -28.95% |
| StageActor_Traced_Reference_Ingress_Throughput | 801.19 | 804.79 | 802.18 | +0.45% | -0.32% |
| RestartFlow_Reference_Handoff_Throughput | 700.32 | 625.62 | 621.05 | -10.67% | -0.73% |
| RestartFlow_Sequence_Handoff_Throughput | 802.95 | 771.37 | 734.14 | -3.93% | -4.83% |

| Case | Parent B/op | Current #8826 B/op | Single-target candidate B/op |
|---|---:|---:|---:|
| Actor_materializer_run_restart_flow_graph | 65147.00 | 64864.50 | 64711.00 |
| Actor_materializer_run_simple_linear_graph | 29918.50 | 29713.00 | 29846.00 |
| Source_Queue_Reference_Ingress_Throughput | 321.00 | 321.00 | 321.00 |
| Source_Queue_Traced_Reference_Ingress_Throughput | 777.00 | 777.00 | 777.00 |
| StageActor_Reference_Ingress_Throughput | 117.00 | 107.00 | 106.50 |
| StageActor_Traced_Reference_Ingress_Throughput | 584.00 | 584.00 | 584.00 |
| RestartFlow_Reference_Handoff_Throughput | 273.00 | 49.00 | 49.00 |
| RestartFlow_Sequence_Handoff_Throughput | 329.00 | 105.00 | 105.00 |

| Role | Processes | Allocation median of process medians, B/configured message | Throughput median of process medians, messages/s |
|---|---:|---:|---:|
| parent | 4 | 3050.733 | 391969.5 |
| current | 4 | 2938.886 | 405411.0 |
| target | 4 | 2937.949 | 397120.5 |

Current #8826's untraced StageActor median is 293.28 ns/item; the final candidate is 208.37 and the parent 213.74. The unchanged parent also exhibits the slow mode, so a particular cache-line or scheduler cause is not established. Preserve every process:

| Variant | Four StageActor process means, ns/item, in execution order |
|---|---|
| parent | 326.04, 208.96, 207.87, 218.52 |
| current | 278.60, 307.95, 375.26, 204.79 |
| target | 260.36, 208.04, 206.35, 208.70 |

The candidate retains RestartFlow's 329→105 and 273→49 B/item observations. Versus current #8826, the untraced queue median is 2.66% higher, and simple-graph allocation is 133 B/run higher (0.45%). Versus the parent, the final candidate's queue timing is 0.63% lower, simple-graph timing 0.42% higher, and restart-graph timing 0.71% lower. These bounded observations do not establish neutrality for all Streams graphs.

BenchmarkDotNet also reports short minimum iterations for the untraced StageActor control (candidate minima 71.762 and 75.244 ms versus its 100 ms recommendation) and multimodal distributions in some cases. The existing configuration reports a duplicate GitHub Markdown exporter; case/sample guards and generated files pass validation. All warnings and detected outliers are retained, with no sample removal. These are additional limits on timing conclusions.

## Integrated RemotePingPong

All twelve processes and all 36 repetitions are retained. Four processes per role, three repetitions each, one client, 1M requests, window 50, real V2 payload (348 serialized bytes), one inbound and one outbound lane. Processes run sequentially and separately from BDN, builds/tests and native diagnostics. Startup reports server GC, eight heaps, adaptation disabled; retained-array sanity probes and precise cumulative allocation/window checks pass. Startup configuration/probe checks are not a full heap-history trace.

Final allocation median of process medians is 2937.949 B/configured message, versus parent 3050.733 (112.784 B lower; 3.697%). Throughput medians are parent 391,969.5, current 405,411 and target 397,120.5 messages/s. The target is 2.04% below current and 1.31% above parent. Process-median ranges overlap: parent 377,715–401,526, current 395,962–411,185, target 380,301–405,598. No agreed numeric non-inferiority margin or causal transport speedup/slowdown is asserted.

| Process | Variant | Allocation median, B/configured message | Throughput median, messages/s |
|---|---|---:|---:|
| rpp-01-parent-transport | parent | 3051.704 | 401526 |
| rpp-02-current-transport | current | 2939.559 | 411185 |
| rpp-03-target-transport | target | 2937.641 | 401930 |
| rpp-04-target-transport | target | 2938.645 | 392311 |
| rpp-05-current-transport | current | 2938.764 | 408164 |
| rpp-06-parent-transport | parent | 3049.985 | 377715 |
| rpp-07-parent-transport | parent | 3050.789 | 393314 |
| rpp-08-current-transport | current | 2938.558 | 402658 |
| rpp-09-target-transport | target | 2937.985 | 405598 |
| rpp-10-target-transport | target | 2937.912 | 380301 |
| rpp-11-current-transport | current | 2939.008 | 395962 |
| rpp-12-parent-transport | parent | 3050.677 | 390625 |

The configured denominator is two million ping+echo messages per repetition; primers and pending echo tails prevent an independently observed exact completed count. Counter arithmetic, positive values and chronological non-overlap pass. Shared-host process/CPU/pressure telemetry is retained; it does not prove universal absence of unrelated work. No repetition is excluded or rerun based on its timing.

## Native code and shell layout

Captured warmed Tier1 `GraphInterpreterShell.Receive` inlines the extracted shell helper and directly calls the interpreter with the same argument-loading sequence. An extra helper call/struct copy is not established as the StageActor timing cause. The final `TryInit` IL has no bound Action constructor; normal local enqueue remains typed, while the two existing boxing branches remain at mailbox/public-adapter boundaries. No speculative `in` or inlining attributes were added.

On the measured x64 runtime, the final candidate removes the extra eight-byte reference and restores the parent's measured offsets: Self 96 and resume flag 120, versus current Self 104 / resume flag 128. These offsets are diagnostic observations, not correctness dependencies or public layout contracts. Cache-line contention is a hypothesis; hardware contention counters were not captured. The paired uninitialized-object sizing probe reports an eight-byte reduction with the same small residual measurement overhead, so it is not used as an exact absolute object-allocation measurement.

Native capture follows the [runtime's documented method filters](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/jit/viewing-jit-dumps.md). Native-diagnostic timings are excluded from performance comparisons.

## Verification and provenance

* Final implementation source: full Streams 1,930 passed, 33 existing skips, zero failures; focused compatibility/ordering/feedback/tracing controls 19 passed; API approvals 35 passed without approval-file changes.
* New quota control covers mailbox and local resume paths. Throwing only at the owner local-resume call fails limit=1000 while limit=1 passes; restored source passes. A separate new public-adapter test proves supplied-callback delivery remains queued and usable.
* Full-suite binaries were built from identical implementation/test source bytes before the local source commit; benchmark and API binaries were built after commit. Source/binary receipts preserve that distinction. Documentation commits do not change the measured source.
* Slopwatch reports zero new issues. Raw commands, source/build/runtime/binary hashes, generated Markdown/full JSON/measurement CSV, TRX results, native captures, layout-tool source and audit receipt are retained under the study directory.

The earlier parent/current/owner-only study is retained independently and published in #8826; it is not pooled with this final comparison. It also reproduced the 224 B/item saving, while showing process-sensitive StageActor timings. A preparation guard initially observed the test app exit before VSTest published its TRX file; the wait guard was corrected and the successful unchanged test result retained. No performance run was affected or selectively repeated.

Generated BDN tables are embedded directly in the PR descriptions. Local results, hosted CI, review, merge and release remain separate; no merge is performed by this follow-up.
