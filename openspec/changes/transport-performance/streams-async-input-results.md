# Same-actor Streams async-input allocation results

Same-actor async stream callbacks currently pass the public AsyncInput struct through an object queue, allocating a box for each handoff. Store their fields inline in a private tagged FIFO entry instead, using an internal interpreter-owner reference to enqueue them. Control events share that FIFO; actor-boundary and public Init adapter behavior retain their existing paths.

The interpreter still applies its existing event quota, scheduling, completion, failure, tracing and feedback logic. No public API or wire format changes. The larger FIFO entry can cost more when its backing array grows; the added cold materialization controls cover two concrete graphs, rather than proving every graph or burst is neutral.

Allocation observations, baseline e1f188b45 → phase1 a365a1e6a:

- RestartFlow sequence handoff: 329→105 B/item; reference handoff: 273→49 B/item, both 224 B/item lower.
- Source.Queue external and traced controls: unchanged within 1 B/item.
- Cold restart graph: 65876→65707 B/graph; simple graph: 29852→29862 B/graph.
- Native StageActor control: 128→104 B/item untraced and 553→560 B/item traced. The untraced 24 B difference does not establish an external-event carrier saving: this PR retains that public actor-boundary representation. Mailbox allocation can vary with producer/consumer timing, even though the source buffer is now preallocated identically. This control difference does not identify a specific allocation site.

Validation: phase1 focused 88 passed; final stacked implementation full Streams 1935 passed, 33 inherited skips, 0 failures; final API approvals 35 passed without approval-file changes. Initial/subfused PreStart ordering, FIFO/nonreentry, event limits, detached callbacks, feedback, failures, tracing and cross-interpreter delivery have focused controls. All 30 matched benchmark cases passed configuration/case/sample-count guards. The final full-suite/API result validates the combined stack; it is not a standalone full-suite run at this intermediate commit.

BenchmarkDotNet 0.15.8, .NET 10.0.12, Linux x64, concurrent workstation GC, adaptation 0, matching source/runtime settings. One launch, three warmups and three measured iterations; all results retain three raw measurements and Statistics.N=3. Hot cases perform 100000 logical operations per invocation with one invocation per iteration. Cold cases independently materialize and await 256 graphs per iteration. These short runs support allocation comparisons; wide timing intervals do not establish a throughput improvement. Sink completion does not establish that all interpreter/actor teardown finished before the next graph.

### RemotePingPong: Typed Streams FIFO

The unprofiled ABBA study observed an allocation median reduction of 113.354 B/configured message (3.718%). This is an integrated observation; allocation-stack attribution is a separate matched-profile check.

| Observation | Baseline | Candidate |
|---|---:|---:|
| Allocation median, B/configured message | 3048.414 | 2935.060 |
| Allocation range, B/configured message | 3047.071–3049.784 | 2907.636–2937.203 |
| Throughput median, messages/s | 371,570.0 | 281,382.0 |
| Throughput range, messages/s | 261,097–399,601 | 236,603–390,702 |

Actual revisions: baseline `e1f188b45c0afe571679681b6b30c0e4b60df519`, candidate `a365a1e6abf39cb01e2d87a97fbdd7a1bfbb4c7e`. Each median/range includes all six repetitions for its role across two processes. This study is independently attributed; it is not pooled with the other Streams study.

All 12 raw repetitions, in execution order:

| Block | Role | Variant | Rep | Allocated bytes | B/configured message | Messages/s | Elapsed ms |
|---|---|---|---:|---:|---:|---:|---:|
| a1 | baseline | tcp | 1 | 6094141640 | 3047.071 | 261097 | 7660.86 |
| a1 | baseline | tcp | 2 | 6095063456 | 3047.532 | 380229 | 5260.51 |
| a1 | baseline | tcp | 3 | 6096733920 | 3048.367 | 362911 | 5511.65 |
| b1 | candidate | phase1 | 1 | 5871247080 | 2935.624 | 258766 | 7729.15 |
| b1 | candidate | phase1 | 2 | 5850830760 | 2925.415 | 303998 | 6579.84 |
| b1 | candidate | phase1 | 3 | 5815271752 | 2907.636 | 236603 | 8453.84 |
| b2 | candidate | phase1 | 1 | 5868993712 | 2934.497 | 250032 | 7999.72 |
| b2 | candidate | phase1 | 2 | 5872708944 | 2936.354 | 390702 | 5119.82 |
| b2 | candidate | phase1 | 3 | 5874406616 | 2937.203 | 358231 | 5583.39 |
| a2 | baseline | tcp | 1 | 6096921168 | 3048.461 | 264726 | 7555.87 |
| a2 | baseline | tcp | 2 | 6098060064 | 3049.030 | 399601 | 5005.37 |
| a2 | baseline | tcp | 3 | 6099568920 | 3049.784 | 381680 | 5240.82 |

Each process used Artery TCP, one client, 1M requests, three repetitions, real V2 serialization (348-byte canonical payload), window 50, and one inbound/outbound lane. Startup reported server GC with eight heaps and dynamic adaptation disabled; the retained-array probe passed in every process. Raw cumulative allocation snapshots and UTC windows passed arithmetic/monotonicity checks. The probe is a sanity check; full heap-history/profile verification is separate.

The denominator is the existing configured two million ping+echo messages per repetition. Initial local primers and pending echo tails prevent an independently observed exact completed-message count. All repetitions are retained, with no exclusions, extra trials or concurrent RPP/benchmark processes.

Unrelated Netclaw tests were active on this shared host and are recorded in the process/load receipts. Throughput and elapsed-time observations cannot establish a causal speedup or slowdown.

### baseline: SourceActorRefBenchmarks

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-ONRWCR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=3  LaunchCount=1
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3

```
| Method                                           | Mean       | Error       | StdDev      | Median     | Req/sec      | Gen0   | Gen1   | Gen2   | Allocated |
|------------------------------------------------- |-----------:|------------:|------------:|-----------:|-------------:|-------:|-------:|-------:|----------:|
| Source_Queue_Ingress_Throughput                  | 1,278.5 ns |  8,260.9 ns |   452.81 ns | 1,037.8 ns |   782,145.69 | 0.0300 |      - |      - |     320 B |
| Source_Queue_Reference_Ingress_Throughput        | 1,476.4 ns | 12,479.6 ns |   684.05 ns | 1,087.0 ns |   677,309.54 | 0.0300 |      - |      - |     321 B |
| Source_Queue_Traced_Ingress_Throughput           | 2,237.6 ns | 18,214.1 ns |   998.38 ns | 1,677.1 ns |   446,908.99 | 0.0900 |      - |      - |     776 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,943.1 ns | 15,384.7 ns |   843.29 ns | 1,479.2 ns |   514,631.40 | 0.0900 |      - |      - |     777 B |
| StageActor_Reference_Ingress_Throughput          |   803.5 ns |    842.6 ns |    46.18 ns |   810.0 ns | 1,244,551.67 | 0.0200 | 0.0100 | 0.0100 |     128 B |
| StageActor_Traced_Reference_Ingress_Throughput   | 2,265.9 ns | 26,524.9 ns | 1,453.92 ns | 2,373.4 ns |   441,334.72 | 0.0600 | 0.0100 |      - |     553 B |


### baseline: StreamThroughputBenchmarks

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-ONRWCR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=3  LaunchCount=1
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3

```
| Method                                   | Mean     | Error      | StdDev    | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|-----------:|----------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 898.9 ns | 1,491.7 ns |  81.76 ns | 1,112,479.07 | 0.0300 |     329 B |
| RestartFlow_Reference_Handoff_Throughput | 838.2 ns | 2,098.4 ns | 115.02 ns | 1,193,015.13 | 0.0300 |     273 B |


### baseline: MaterializationBenchmarks

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-ZJWTBY : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

InvocationCount=256  IterationCount=3  LaunchCount=1
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3

```
| Method                                     | Mean     | Error    | StdDev   | Gen0   | Allocated |
|------------------------------------------- |---------:|---------:|---------:|-------:|----------:|
| Actor_materializer_run_restart_flow_graph  | 372.0 μs | 494.9 μs | 27.13 μs | 7.8125 |  64.33 KB |
| Actor_materializer_run_simple_linear_graph | 159.1 μs | 264.5 μs | 14.50 μs |      - |  29.15 KB |


### phase1: SourceActorRefBenchmarks

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-ONRWCR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=3  LaunchCount=1
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3

```
| Method                                           | Mean       | Error       | StdDev    | Req/sec      | Gen0   | Gen1   | Allocated |
|------------------------------------------------- |-----------:|------------:|----------:|-------------:|-------:|-------:|----------:|
| Source_Queue_Ingress_Throughput                  |   705.7 ns |  3,343.3 ns | 183.26 ns | 1,417,072.81 | 0.0300 |      - |     320 B |
| Source_Queue_Reference_Ingress_Throughput        | 1,461.4 ns | 10,730.5 ns | 588.18 ns |   684,253.83 | 0.0300 |      - |     321 B |
| Source_Queue_Traced_Ingress_Throughput           | 2,367.8 ns | 15,539.4 ns | 851.76 ns |   422,335.02 | 0.0900 |      - |     777 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,578.3 ns |    956.4 ns |  52.42 ns |   633,579.00 | 0.0900 |      - |     777 B |
| StageActor_Reference_Ingress_Throughput          |   982.0 ns |  4,745.9 ns | 260.14 ns | 1,018,377.28 | 0.0100 |      - |     104 B |
| StageActor_Traced_Reference_Ingress_Throughput   | 1,532.9 ns | 11,170.6 ns | 612.30 ns |   652,357.82 | 0.0600 | 0.0300 |     560 B |


### phase1: StreamThroughputBenchmarks

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-ONRWCR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=3  LaunchCount=1
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3

```
| Method                                   | Mean     | Error      | StdDev   | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|-----------:|---------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 942.7 ns | 1,238.5 ns | 67.89 ns | 1,060,777.19 | 0.0100 |     105 B |
| RestartFlow_Reference_Handoff_Throughput | 708.1 ns |   241.8 ns | 13.25 ns | 1,412,176.14 |      - |      49 B |


### phase1: MaterializationBenchmarks

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-ZJWTBY : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

InvocationCount=256  IterationCount=3  LaunchCount=1
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3

```
| Method                                     | Mean     | Error    | StdDev   | Gen0   | Allocated |
|------------------------------------------- |---------:|---------:|---------:|-------:|----------:|
| Actor_materializer_run_restart_flow_graph  | 407.4 μs | 225.4 μs | 12.36 μs | 7.8125 |  64.17 KB |
| Actor_materializer_run_simple_linear_graph | 176.5 μs | 503.5 μs | 27.60 μs |      - |  29.16 KB |


### Allocation-profile attribution

The three matched captures use frozen baseline e1f188b45, phase1 a365a1e6a and phase2 4704dbe03. Each completed with zero lost events, three raw UTC/counter windows and eight heaps observed in every window. Binary/source/tool readbacks passed; profile timing is excluded from performance results. The final report/readback covers 58 preserved artifacts. Counts below are sampled allocation ticks, not object counts, operation counts or per-message rates. AllocationAmount64 weighted bytes are sampling-quantum estimates, not exact type allocation totals. Shared type identities do not establish target-site totals.

| Full-trace sample category / observed object size | Baseline | Phase1 |
|---|---:|---:|
| Shared AsyncInput, 56 B: direct GetInterpreter lambda, local/mailbox branch ambiguous | 153 | 19 |
| Shared AsyncInput, 56 B: other/inlined/missing stack | 7749 | 903 |
| Shared AsyncInput, 56 B: total selected type ticks | 7902 | 922 |
| StageActor caller tuple, 32 B: direct qualified stack | 541 | 454 |

The source and frozen IL show the same-actor path enqueuing the public AsyncInput value into private inline storage while preserving boxing at the actor/public adapter boundary. The sampled shared-type decline is consistent with that mechanism but is not an exact count of eliminated local boxes. Direct lambda samples all occur in the first window; later callers are often inlined. The visible lambda contains both local and mailbox/fallback routes, so these samples cannot distinguish those branches. One phase1 other sample lies outside the measured windows. No shared AsyncInput TypeID expansion was used.

The profile comparison does not explain the full 113.354 B/configured-message unprofiled allocation observation, prove a fixed saving for each Artery message or establish CPU causation. StageActor tuple allocations remain in phase1; their sampled frequency is not a rate estimate. The larger inline FIFO entry can increase cold or burst backing-array costs; the two materialization controls above are observations for those graphs, with asynchronous teardown limits.


Measured-source pins and evidence: baseline e1f188b45c0afe571679681b6b30c0e4b60df519; phase1 a365a1e6abf39cb01e2d87a97fbdd7a1bfbb4c7e; phase2 4704dbe03f00631d181d22918e34d7c775145b97. Matching benchmark source/production/binary/runtimeconfig receipts and original generated child binaries are retained under /tmp/streams-events-corrected-evidence-20261016. Unprofiled RPP data and frozen binary pins are in /tmp/akka-remaining-perf-20261016/streams-events. Final matched profile report, raw traces/ETLX, all-type and CPU reports, selected raw stacks, IL, UTC windows, heap histories and 58-file readback receipt are in /tmp/akka-remaining-perf-20261016/streams-metadata-profiles. Original eager-delegate/dynamic-buffer experiment remains separately preserved under /tmp/streams-events-evidence-20261016 and is not mixed into these corrected comparisons. Local evidence is distinct from hosted CI, merge or release acceptance. No production GC settings, dependency, public API or wire format changed.
