# Untraced Streams stage-actor allocation results

Untraced stage-actor callbacks currently create a boxed (sender,message) tuple and an async-event carrier. Keep sender/message fields separate through internal dispatch: enqueue them inline for the same interpreter actor, or send one internal StageActorInput carrier across an actor boundary. Construct the tuple on the stack when invoking the existing receive delegate.

The public StageActor constructor and receive delegate remain compatible. The traced callback path keeps its existing activity adapter. Callback initialization precedes FunctionRef publication, and the same FIFO, quotas, flush-on-park, failure, completion and interpreter-context handling remain in use. The internal owner/shell references avoid eagerly allocating per-shell enqueue/dispatch delegates.

Allocation observations, phase1 a365a1e6a → phase2 4704dbe03:

- Native StageActor untraced: 104→96 B/item, an observed incremental 8 B/item reduction. The full baseline→phase1→phase2 sequence is 128→104→96. The incremental result does not equal the tuple's source-level size: mailbox segment allocation and producer/consumer timing remain potential contributors. The fixed source buffer controls only that buffer. Matched allocation profiles below separate sampled type/site observations from aggregate results.
- Native StageActor traced: 560→553 B/item; original baseline 553 B/item. This variation does not establish a traced-path improvement or regression.
- RestartFlow handoffs: 105 B/sequence item and 49 B/reference item in both revisions. Source.Queue controls remain unchanged within 1 B/item.
- Cold restart graph: 65707→65724 B/graph; simple graph: 29862→29868 B/graph. Full-stack differences from baseline are −152 and +16 B/graph respectively; these are observations for the measured graphs, not source-field-size estimates or teardown isolation.

Validation: final focused 104 passed; full Streams 1935 passed, 33 inherited skips, 0 failures; API approvals 35 passed without approval-file changes. Controls cover reference/sender identity, delivery order, mixed callbacks, concurrent producers, event limits, trace-context isolation, failure/stop and the public constructor/Become path. Initial and subfused PreStart ordering controls also pass. The final 4704 commit only expands the event-kind enum to Allman formatting; its tokens match the semantically tested predecessor. All 30 matched benchmark cases passed configuration/case/sample-count guards.

BenchmarkDotNet 0.15.8, .NET 10.0.12, Linux x64, concurrent workstation GC, adaptation 0, matching source/runtime settings. One launch, three warmups and three measured iterations; all results retain three raw measurements and Statistics.N=3. Hot cases perform 100000 logical operations per invocation with one invocation per iteration. Cold cases independently materialize and await 256 graphs per iteration. These short runs support allocation comparisons; wide timing intervals do not establish a throughput improvement. Sink completion does not establish that all interpreter/actor teardown finished before the next graph.

### RemotePingPong: StageActor tuple removal

**This study does not establish an integrated allocation benefit for phase 2.** The candidate median was 16.428 B/configured message higher (0.567%), with heavily overlapping ranges and process variation larger than this difference. Preserve this result alongside the controlled microbenchmark/site evidence; do not replace it with a theoretical net saving.

| Observation | Baseline | Candidate |
|---|---:|---:|
| Allocation median, B/configured message | 2897.703 | 2914.131 |
| Allocation range, B/configured message | 2895.749–2933.462 | 2886.801–2927.842 |
| Throughput median, messages/s | 216,191.5 | 246,324.5 |
| Throughput range, messages/s | 168,791–337,838 | 169,881–392,696 |

Actual revisions: baseline `a365a1e6abf39cb01e2d87a97fbdd7a1bfbb4c7e`, candidate `4704dbe03f00631d181d22918e34d7c775145b97`. Each median/range includes all six repetitions for its role across two processes. This study is independently attributed; it is not pooled with the other Streams study.

All 12 raw repetitions, in execution order:

| Block | Role | Variant | Rep | Allocated bytes | B/configured message | Messages/s | Elapsed ms |
|---|---|---|---:|---:|---:|---:|---:|
| a1 | baseline | phase1 | 1 | 5866923056 | 2933.462 | 255233 | 7836.45 |
| a1 | baseline | phase1 | 2 | 5866038104 | 2933.019 | 337838 | 5920.29 |
| a1 | baseline | phase1 | 3 | 5796260448 | 2898.130 | 209864 | 9530.65 |
| b1 | candidate | phase2 | 1 | 5773601088 | 2886.801 | 169881 | 11773.82 |
| b1 | candidate | phase2 | 2 | 5808156576 | 2904.078 | 249191 | 8026.73 |
| b1 | candidate | phase2 | 3 | 5801600160 | 2900.800 | 215355 | 9287.07 |
| b2 | candidate | phase2 | 1 | 5848366744 | 2924.183 | 243458 | 8215.22 |
| b2 | candidate | phase2 | 2 | 5853696928 | 2926.848 | 382117 | 5234.76 |
| b2 | candidate | phase2 | 3 | 5855684344 | 2927.842 | 392696 | 5093.67 |
| a2 | baseline | phase1 | 1 | 5794551888 | 2897.276 | 168791 | 11849.53 |
| a2 | baseline | phase1 | 2 | 5792826896 | 2896.413 | 208464 | 9594.23 |
| a2 | baseline | phase1 | 3 | 5791497528 | 2895.749 | 222519 | 8988.45 |

Each process used Artery TCP, one client, 1M requests, three repetitions, real V2 serialization (348-byte canonical payload), window 50, and one inbound/outbound lane. Startup reported server GC with eight heaps and dynamic adaptation disabled; the retained-array probe passed in every process. Raw cumulative allocation snapshots and UTC windows passed arithmetic/monotonicity checks. The probe is a sanity check; full heap-history/profile verification is separate.

The denominator is the existing configured two million ping+echo messages per repetition. Initial local primers and pending echo tails prevent an independently observed exact completed-message count. All repetitions are retained, with no exclusions, extra trials or concurrent RPP/benchmark processes.

Unrelated Netclaw tests were active on this shared host and are recorded in the process/load receipts. Throughput and elapsed-time observations cannot establish a causal speedup or slowdown. Phase 2 allocation ranges overlap substantially, and its small median increase must not be called a proven regression or a hidden improvement.

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


### final: SourceActorRefBenchmarks

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-ONRWCR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=3  LaunchCount=1
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3

```
| Method                                           | Mean       | Error        | StdDev    | Median     | Req/sec      | Gen0   | Allocated |
|------------------------------------------------- |-----------:|-------------:|----------:|-----------:|-------------:|-------:|----------:|
| Source_Queue_Ingress_Throughput                  | 1,010.5 ns |    355.33 ns |  19.48 ns | 1,011.2 ns |   989,651.67 | 0.0300 |     320 B |
| Source_Queue_Reference_Ingress_Throughput        | 1,223.5 ns |    320.96 ns |  17.59 ns | 1,229.3 ns |   817,345.36 | 0.0300 |     321 B |
| Source_Queue_Traced_Ingress_Throughput           | 1,853.0 ns | 12,389.52 ns | 679.11 ns | 1,522.5 ns |   539,657.33 | 0.0900 |     776 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 2,165.7 ns | 14,163.02 ns | 776.32 ns | 1,722.1 ns |   461,744.00 | 0.0900 |     777 B |
| StageActor_Reference_Ingress_Throughput          |   586.0 ns |     86.16 ns |   4.72 ns |   586.0 ns | 1,706,456.66 |      - |      96 B |
| StageActor_Traced_Reference_Ingress_Throughput   |   776.6 ns |  1,128.96 ns |  61.88 ns |   755.1 ns | 1,287,623.59 | 0.0600 |     553 B |


### final: StreamThroughputBenchmarks

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-ONRWCR : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=3  LaunchCount=1
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3

```
| Method                                   | Mean       | Error      | StdDev    | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |-----------:|-----------:|----------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 1,047.4 ns | 2,290.8 ns | 125.56 ns |   954,765.56 | 0.0100 |     105 B |
| RestartFlow_Reference_Handoff_Throughput |   796.5 ns |   259.0 ns |  14.20 ns | 1,255,507.36 |      - |      49 B |


### final: MaterializationBenchmarks

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
| Actor_materializer_run_restart_flow_graph  | 348.4 μs | 157.5 μs |  8.63 μs | 7.8125 |  64.18 KB |
| Actor_materializer_run_simple_linear_graph | 154.9 μs | 212.4 μs | 11.64 μs |      - |  29.17 KB |


### Allocation-profile attribution

The three matched captures use frozen baseline e1f188b45, phase1 a365a1e6a and phase2 4704dbe03. Each completed with zero lost events, three raw UTC/counter windows and eight heaps observed in every window. Binary/source/tool readbacks passed; profile timing is excluded from performance results. The final report/readback covers 58 preserved artifacts. Counts below are sampled allocation ticks, not object counts, operation counts or per-message rates. AllocationAmount64 weighted bytes are sampling-quantum estimates, not exact type allocation totals. Shared type identities do not establish target-site totals.

| Full-trace sample category / observed object size | Phase1 | Phase2 |
|---|---:|---:|
| StageActor caller tuple, 32 B: direct qualified stack | 454 | 0 |
| Private StageActorInput, 56 B: direct qualified Dispatch producer | 0 | 11 |
| Private StageActorInput, 56 B: other/inlined before identity linkage | 0 | 815 |
| Private StageActorInput, 56 B: validated private-type total | 0 | 826 |
| Shared AsyncInput, 56 B: direct lambda, local/mailbox branch ambiguous | 19 | 7 |
| Shared AsyncInput, 56 B: other/inlined/missing stack | 903 | 248 |
| Shared AsyncInput, 56 B: total selected type ticks | 922 | 255 |

The phase2 private carrier's 826 sampled ticks comprise 11 qualified producer ticks and 815 disjoint other/inlined type ticks; its per-window totals are 200/300/326. A nonzero same-trace PID/CLR/TypeID identity is validated against the qualified producer and the exact frozen constructor type. DispatchStageActorInput IL0042 constructs ActorGraphInterpreter/StageActorInput and IL0047 sends it. This supports private-type identity linkage, without assigning the linked samples to a lane, actor instance or producer callsite. Shortened type names alone were not accepted.

The source mechanism replaces the external boxed tuple 32 B plus shared AsyncInput 56 B with one private 56 B carrier, and keeps same-actor fields inline in the FIFO. Direct tuple samples are 454 in phase1 and zero in phase2; the original baseline has 541. These traces establish observed object sizes and sampled target-stack replacement. Zero samples does not prove allocation is impossible: public/tracing/legacy paths remain available. Shared AsyncInput totals 922→255 and direct lambda 19→7 retain local/mailbox ambiguity; no shared TypeID expansion was used.

Review this as a localized allocation removal with an observed 8 B/item incremental microbenchmark benefit. The independent unprofiled Artery study establishes no integrated allocation benefit: 2897.703→2914.131 B/configured message (+16.428, +0.567%) with overlapping ranges. The removed 32 B tuple, observed 8 B integrated microbenchmark delta and unprofiled transport median are different evidence and cannot be substituted for each other. Profiles do not explain the aggregate transport difference or establish throughput causation. Sparse RPP mailbox slot-array samples also do not explain a different BDN workload.


Measured-source pins and evidence: baseline e1f188b45c0afe571679681b6b30c0e4b60df519; phase1 a365a1e6abf39cb01e2d87a97fbdd7a1bfbb4c7e; phase2 4704dbe03f00631d181d22918e34d7c775145b97. Matching benchmark source/production/binary/runtimeconfig receipts and original generated child binaries are retained under /tmp/streams-events-corrected-evidence-20261016. Unprofiled RPP data and frozen binary pins are in /tmp/akka-remaining-perf-20261016/streams-events. Final matched profile report, raw traces/ETLX, all-type and CPU reports, selected raw stacks, IL, UTC windows, heap histories and 58-file readback receipt are in /tmp/akka-remaining-perf-20261016/streams-metadata-profiles. Original eager-delegate/dynamic-buffer experiment remains separately preserved under /tmp/streams-events-evidence-20261016 and is not mixed into these corrected comparisons. Local evidence is distinct from hosted CI, merge or release acceptance. No production GC settings, dependency, public API or wire format changed.
