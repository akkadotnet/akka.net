# StageActor dispatch cost after the shell-dispatch simplification

**Decision: integration VERIFIED; performance acceptance NOT VERIFIED. Hold the merge.** The targeted StageActor workloads reduce allocation and median process CPU, but the RestartFlow reference-handoff control CPU is about 2.4% higher and RemotePingPong's primary CPU median is 5.12% higher. Process ranges overlap on this busy host, so neither increase is a proved causal regression. The results do not clear a CPU non-regression gate, and the higher observed wall throughput cannot substitute for that evidence. No speculative dispatch rewrite was made from these measurements.

The baseline contains #8826 and merged #8851 at production revision `b9793eab41fed15cf96e944ee44e8c0592cee076`. The candidate integrates that parent into #8827 through normal merge `47e60e200fc7e0491160ed0d0a38efe37ebeeab2`. The one shell target, direct owner initialization, ordinary public callback adapter, FIFO ordering and quota/flush behavior are preserved. The new public-adapter StageActor test also clears Activity.Current so tracing cannot hide the fallback being tested.

## Method and limits

BenchmarkDotNet 0.15.8; .NET 10.0.12 and SDK 10.0.103, ordinary tiering/PGO; workstation GC reporting HeapCount=1, adaptation 0. Identical benchmark sources in both variants, including one new local StageActor method and opt-in CPU accounting. BDN commits: baseline `012ddc7f77ae8a9e28dc46150329b3e5717d4901`, candidate `7af8e8a8e93044354ef7cd08c67ef669ebafc751`. Five fresh child processes per role/case, six warmups and twelve actual iterations per process; hot iterations perform 800,000 logical operations and cold iterations materialize 1,024 graphs. Throughput strategy, unroll 1, no outlier removal. Order baseline,candidate,candidate,baseline,baseline,candidate,candidate,baseline,baseline,candidate. All 90 case rows and 1,080 actual observations are retained. All means/ranges below use independent child processes; iterations are not independent trials.

Unrelated host builds/tests were permitted as recorded background after the original wall-time epoch was interrupted. CPU is summed process CPU between BDN WorkloadActualStart/Stop, including all process threads, workload GC and small counter/listener overhead. It avoids scheduling-wait delay but does not eliminate cache, memory or frequency contention, or scheduling-dependent batching and spinning. CPU and BDN wall time have different accounting boundaries; their difference is not a measured wait time. Tick unit 100 ns does not guarantee counter resolution. These are loaded-host observations, not a quiet-host non-inferiority certificate. Materialization cases await sink completion, not full actor teardown.

## CPU, allocation and wall-time summary

Each CPU and wall-time value is the median of five process means; brackets give the complete range of those five means. Allocation is the median reported BDN B/op. Summed CPU across worker threads can exceed elapsed wall time. CPU change is candidate/baseline-1. These summaries are generated from raw BDN and CPU windows, not BDN-generated Markdown tables.

| Case | CPU before ns/op [range] | CPU after ns/op [range] | CPU change | B/op before -> after | Wall before -> after ns/op |
|---|---:|---:|---:|---:|---:|
| Actor_materializer_run_restart_flow_graph | 370,850.67 [352,777.26, 392,912.84] | 369,331.87 [344,575.52, 412,595.46] | -0.41% | 64,788.00 -> 64,802.00 | 145,964.30 -> 141,545.21 |
| Actor_materializer_run_simple_linear_graph | 321,540.85 [311,716.88, 373,545.00] | 326,171.63 [313,753.74, 337,003.82] | +1.44% | 29,826.00 -> 29,837.00 | 127,047.85 -> 132,394.24 |
| Source_Queue_Reference_Ingress_Throughput | 6,703.12 [6,651.36, 6,805.22] | 6,715.26 [3,960.83, 6,960.55] | +0.18% | 321.00 -> 321.00 | 1,001.93 -> 997.54 |
| Source_Queue_Traced_Reference_Ingress_Throughput | 9,004.74 [8,851.18, 9,031.36] | 8,918.86 [4,664.15, 8,985.97] | -0.95% | 777.00 -> 777.00 | 1,459.89 -> 1,433.02 |
| StageActor_Local_Reference_Ingress_Throughput | 169.86 [154.52, 182.14] | 139.24 [138.01, 161.44] | -18.03% | 32.00 -> 0.00 | 125.70 -> 107.76 |
| StageActor_Reference_Ingress_Throughput | 609.45 [603.00, 623.88] | 544.53 [378.43, 565.64] | -10.65% | 113.00 -> 90.00 | 203.65 -> 183.10 |
| StageActor_Traced_Reference_Ingress_Throughput | 1,424.94 [1,402.33, 1,466.57] | 1,438.43 [1,253.63, 1,493.68] | +0.95% | 584.00 -> 584.00 | 806.15 -> 813.98 |
| RestartFlow_Reference_Handoff_Throughput | 664.46 [657.21, 678.77] | 680.71 [664.26, 686.37] | +2.45% | 49.00 -> 49.00 | 613.36 -> 623.04 |
| RestartFlow_Sequence_Handoff_Throughput | 793.24 [769.71, 797.13] | 798.81 [796.66, 810.24] | +0.70% | 105.00 -> 105.00 | 742.47 -> 747.65 |

## Sequential RemotePingPong

Ten sequential processes, five per role and three repetitions each. Artery.Tcp, one client, 1M requests/repetition, real V2 source-generated canonical 348 B payload, window 50, one inbound/outbound lane; server GC reporting eight configured heaps, adaptation 0. Frozen binaries are reused byte-for-byte from the same production sources before the CPU-only BDN helper was added (baseline 7406d821..., candidate 12012ada...). All 30 repetitions, exact cumulative counters and UTC windows remain; no exclusions. Denominator is configured 2M ping+echo messages; local primers and pending tails prevent an independently exact completed-message count. CPU covers both actor systems and all process threads, not StageActor alone.

Medians are medians of five process medians; brackets give all five process medians' range.

| Observation | Baseline | Candidate |
|---|---:|---:|
| Allocation B/configured message | 2,933.69 [2,926.56, 2,937.70] | 2,922.47 [2,917.30, 2,926.24] |
| Process CPU ns/configured message | 14,555.49 [13,241.01, 16,805.52] | 15,300.66 [13,613.47, 16,849.96] |
| Throughput configured messages/s | 319,847.00 [283,006.00, 399,841.00] | 345,424.00 [310,994.00, 386,848.00] |

## Dispatch evidence

Both real Artery allocation traces have zero lost events and eight heaps observed in all three windows. Baseline has 675 directly qualified sampled StageActor caller-tuple ticks; candidate has zero. Candidate has 38 directly qualified private StageActorInput producer ticks, with TCP AckCovered/StartFlush and HandlePipeRead stacks, plus 1,081 disjoint private-type-linked ticks (1,119 total, observed object size 56 B). These are sampled ticks, not object counts or per-message byte savings; shared AsyncInput samples retain local/mailbox ambiguity. Diagnostic timings are excluded.

Warm native code for the ordinary RestartFlow control shows the added event-kind branch and inline type/null checks. Cast helpers sit behind mismatch branches; they are not unconditional calls per callback. The ordinary callback entry calls the generic execution core directly; the separately emitted wrapper tail-jumps to it. A new normal wrapper call per callback was not established. Both FIFO slot layouts are 48 B. The candidate local StageActor route enqueues five references plus its event kind and invokes a tuple-specialized execution core without boxing the tuple. An additional shell reference per GraphInterpreter and a second callback slot per StageActor are fixed retained-state costs; callback construction also changes. The materialization allocation observations cover whole graphs and cannot be equated to an exact field-size estimate. These facts explain what changed, not its isolated cycle cost.

## Verification

Streams 1,939 passed, 33 existing skips, 0 failed; API 35 passed; final integration focused 20 passed. Slopwatch and diff checks passed; no approval files changed. Common benchmark additions compile without warnings, and the local control verifies interpreter context, sender and payload reference identity, exact order/count, demand and nonreentry in both variants. CPU windows must match each BDN case/PID, operations, sequence, cumulative monotonicity and completion count; a deliberately wrong operation count is rejected. Source and binary hashes, recursive RPP hashes and runner/configuration hashes are pinned and checked. Hosted CI on the newly pushed head remains separate.

## Preserved interrupted epoch

The prior epoch stopped after 27 case rows when repeated unrelated builds/tests interfered with wall time. Every row and its original scripts, receipts and binaries is retained separately at epoch1-interrupted.json and epoch1-frozen-bdn. Its timing values are not pooled with this epoch. Smoke checks and JIT/profile diagnostic timings are also excluded.

## Generated BenchmarkDotNet tables

The tables below are copied verbatim from every generated report-github.md for all ten role blocks. Their Mean/Error/StdDev are within-process BDN wall-time statistics, not uncertainty across the five independent processes and not process CPU. The summary above supplies the cross-process CPU comparison. No generated row is omitted.

### Block01 baseline: Akka.Benchmarks.Streams.SourceActorRefBenchmarks

| Method                                           | Mean       | Error    | StdDev   | Req/sec      | Gen0   | Gen1   | Gen2   | Allocated |
|------------------------------------------------- |-----------:|---------:|---------:|-------------:|-------:|-------:|-------:|----------:|
| Source_Queue_Reference_Ingress_Throughput        | 1,009.3 ns | 18.13 ns | 14.16 ns |   990,791.20 | 0.0375 |      - |      - |     321 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,459.9 ns | 11.01 ns |  8.59 ns |   684,983.69 | 0.0925 |      - |      - |     777 B |
| StageActor_Reference_Ingress_Throughput          |   209.8 ns | 12.71 ns |  9.92 ns | 4,766,417.61 | 0.0150 | 0.0100 | 0.0050 |     113 B |
| StageActor_Local_Reference_Ingress_Throughput    |   117.0 ns |  2.88 ns |  2.25 ns | 8,545,474.02 | 0.0038 |      - |      - |      32 B |
| StageActor_Traced_Reference_Ingress_Throughput   |   789.0 ns | 35.86 ns | 28.00 ns | 1,267,484.96 | 0.0750 | 0.0338 | 0.0100 |     584 B |

### Block01 baseline: Akka.Benchmarks.Streams.StreamThroughputBenchmarks

| Method                                   | Mean     | Error    | StdDev  | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|--------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 742.5 ns | 10.37 ns | 8.09 ns | 1,346,848.09 | 0.0125 |     105 B |
| RestartFlow_Reference_Handoff_Throughput | 606.7 ns |  9.88 ns | 7.71 ns | 1,648,180.59 | 0.0050 |      49 B |

### Block01 baseline: Akka.Benchmarks.Streams.MaterializationBenchmarks

| Method                                     | Mean     | Error    | StdDev   | Median   | Gen0   | Allocated |
|------------------------------------------- |---------:|---------:|---------:|---------:|-------:|----------:|
| Actor_materializer_run_restart_flow_graph  | 154.2 μs | 96.01 μs | 74.96 μs | 113.9 μs | 6.8359 |  63.19 KB |
| Actor_materializer_run_simple_linear_graph | 136.0 μs | 25.72 μs | 20.08 μs | 130.1 μs | 2.9297 |  29.13 KB |

### Block02 candidate: Akka.Benchmarks.Streams.SourceActorRefBenchmarks

| Method                                           | Mean       | Error    | StdDev   | Req/sec      | Gen0   | Gen1   | Gen2   | Allocated |
|------------------------------------------------- |-----------:|---------:|---------:|-------------:|-------:|-------:|-------:|----------:|
| Source_Queue_Reference_Ingress_Throughput        | 1,037.8 ns | 35.18 ns | 27.47 ns |   963,595.50 | 0.0375 |      - |      - |     321 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,447.8 ns | 22.38 ns | 17.47 ns |   690,700.19 | 0.0925 |      - |      - |     777 B |
| StageActor_Reference_Ingress_Throughput          |   189.9 ns | 10.15 ns |  7.92 ns | 5,266,606.47 | 0.0113 | 0.0100 | 0.0063 |      86 B |
| StageActor_Local_Reference_Ingress_Throughput    |   115.8 ns |  5.88 ns |  4.59 ns | 8,636,875.38 |      - |      - |      - |         - |
| StageActor_Traced_Reference_Ingress_Throughput   |   827.9 ns | 33.02 ns | 25.78 ns | 1,207,933.30 | 0.0763 | 0.0413 | 0.0125 |     584 B |

### Block02 candidate: Akka.Benchmarks.Streams.StreamThroughputBenchmarks

| Method                                   | Mean     | Error    | StdDev   | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|---------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 750.9 ns | 20.97 ns | 16.37 ns | 1,331,668.17 | 0.0125 |     105 B |
| RestartFlow_Reference_Handoff_Throughput | 617.9 ns |  8.48 ns |  6.62 ns | 1,618,434.37 | 0.0050 |      49 B |

### Block02 candidate: Akka.Benchmarks.Streams.MaterializationBenchmarks

| Method                                     | Mean     | Error    | StdDev   | Median   | Gen0   | Allocated |
|------------------------------------------- |---------:|---------:|---------:|---------:|-------:|----------:|
| Actor_materializer_run_restart_flow_graph  | 141.5 μs | 89.86 μs | 70.15 μs | 107.8 μs | 6.8359 |  63.24 KB |
| Actor_materializer_run_simple_linear_graph | 132.8 μs | 27.63 μs | 21.58 μs | 133.7 μs | 2.9297 |  29.18 KB |

### Block03 candidate: Akka.Benchmarks.Streams.SourceActorRefBenchmarks

| Method                                           | Mean       | Error    | StdDev   | Req/sec      | Gen0   | Gen1   | Gen2   | Allocated |
|------------------------------------------------- |-----------:|---------:|---------:|-------------:|-------:|-------:|-------:|----------:|
| Source_Queue_Reference_Ingress_Throughput        |   998.8 ns | 25.56 ns | 19.96 ns | 1,001,213.10 | 0.0375 |      - |      - |     321 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,447.5 ns | 38.25 ns | 29.87 ns |   690,829.77 | 0.0925 |      - |      - |     777 B |
| StageActor_Reference_Ingress_Throughput          |   183.1 ns |  6.99 ns |  5.46 ns | 5,461,597.14 | 0.0113 | 0.0100 | 0.0050 |      90 B |
| StageActor_Local_Reference_Ingress_Throughput    |   107.1 ns |  3.75 ns |  2.93 ns | 9,340,593.33 |      - |      - |      - |         - |
| StageActor_Traced_Reference_Ingress_Throughput   |   806.3 ns | 64.23 ns | 50.14 ns | 1,240,273.35 | 0.0763 | 0.0375 | 0.0113 |     584 B |

### Block03 candidate: Akka.Benchmarks.Streams.StreamThroughputBenchmarks

| Method                                   | Mean     | Error    | StdDev  | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|--------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 747.6 ns | 10.37 ns | 8.10 ns | 1,337,531.83 | 0.0125 |     105 B |
| RestartFlow_Reference_Handoff_Throughput | 631.4 ns |  7.00 ns | 5.46 ns | 1,583,857.45 | 0.0050 |      49 B |

### Block03 candidate: Akka.Benchmarks.Streams.MaterializationBenchmarks

| Method                                     | Mean     | Error    | StdDev   | Median   | Gen0   | Allocated |
|------------------------------------------- |---------:|---------:|---------:|---------:|-------:|----------:|
| Actor_materializer_run_restart_flow_graph  | 159.3 μs | 94.24 μs | 73.58 μs | 125.3 μs | 6.8359 |  63.29 KB |
| Actor_materializer_run_simple_linear_graph | 136.1 μs | 34.88 μs | 27.23 μs | 143.3 μs | 2.9297 |  29.14 KB |

### Block04 baseline: Akka.Benchmarks.Streams.SourceActorRefBenchmarks

| Method                                           | Mean       | Error    | StdDev   | Req/sec      | Gen0   | Gen1   | Gen2   | Allocated |
|------------------------------------------------- |-----------:|---------:|---------:|-------------:|-------:|-------:|-------:|----------:|
| Source_Queue_Reference_Ingress_Throughput        |   997.9 ns | 22.72 ns | 17.74 ns | 1,002,061.15 | 0.0375 |      - |      - |     321 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,460.0 ns | 13.44 ns | 10.49 ns |   684,914.36 | 0.0925 |      - |      - |     777 B |
| StageActor_Reference_Ingress_Throughput          |   200.4 ns | 12.30 ns |  9.60 ns | 4,988,956.76 | 0.0138 | 0.0100 | 0.0050 |     109 B |
| StageActor_Local_Reference_Ingress_Throughput    |   125.7 ns | 15.36 ns | 11.99 ns | 7,955,313.51 | 0.0038 |      - |      - |      32 B |
| StageActor_Traced_Reference_Ingress_Throughput   |   802.6 ns | 30.34 ns | 23.69 ns | 1,245,914.29 | 0.0763 | 0.0338 | 0.0113 |     584 B |

### Block04 baseline: Akka.Benchmarks.Streams.StreamThroughputBenchmarks

| Method                                   | Mean     | Error    | StdDev   | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|---------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 744.2 ns | 10.19 ns |  7.96 ns | 1,343,695.47 | 0.0125 |     105 B |
| RestartFlow_Reference_Handoff_Throughput | 626.1 ns | 36.53 ns | 28.52 ns | 1,597,312.73 | 0.0050 |      49 B |

### Block04 baseline: Akka.Benchmarks.Streams.MaterializationBenchmarks

| Method                                     | Mean     | Error    | StdDev   | Median   | Gen0   | Allocated |
|------------------------------------------- |---------:|---------:|---------:|---------:|-------:|----------:|
| Actor_materializer_run_restart_flow_graph  | 146.0 μs | 95.38 μs | 74.47 μs | 114.2 μs | 6.8359 |  63.27 KB |
| Actor_materializer_run_simple_linear_graph | 127.0 μs | 32.15 μs | 25.10 μs | 133.8 μs | 2.9297 |  29.13 KB |

### Block05 baseline: Akka.Benchmarks.Streams.SourceActorRefBenchmarks

| Method                                           | Mean       | Error    | StdDev   | Req/sec      | Gen0   | Gen1   | Gen2   | Allocated |
|------------------------------------------------- |-----------:|---------:|---------:|-------------:|-------:|-------:|-------:|----------:|
| Source_Queue_Reference_Ingress_Throughput        |   989.2 ns | 19.77 ns | 15.43 ns | 1,010,904.40 | 0.0375 |      - |      - |     321 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,472.3 ns | 14.40 ns | 11.24 ns |   679,216.48 | 0.0925 |      - |      - |     777 B |
| StageActor_Reference_Ingress_Throughput          |   201.8 ns | 20.65 ns | 16.12 ns | 4,956,312.60 | 0.0125 | 0.0113 | 0.0025 |     116 B |
| StageActor_Local_Reference_Ingress_Throughput    |   121.2 ns |  4.85 ns |  3.78 ns | 8,247,455.00 | 0.0038 |      - |      - |      32 B |
| StageActor_Traced_Reference_Ingress_Throughput   |   820.2 ns | 41.44 ns | 32.35 ns | 1,219,228.99 | 0.0763 | 0.0363 | 0.0113 |     584 B |

### Block05 baseline: Akka.Benchmarks.Streams.StreamThroughputBenchmarks

| Method                                   | Mean     | Error    | StdDev  | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|--------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 740.0 ns | 11.11 ns | 8.67 ns | 1,351,294.17 | 0.0125 |     105 B |
| RestartFlow_Reference_Handoff_Throughput | 611.7 ns | 10.13 ns | 7.91 ns | 1,634,753.25 | 0.0050 |      49 B |

### Block05 baseline: Akka.Benchmarks.Streams.MaterializationBenchmarks

| Method                                     | Mean     | Error    | StdDev   | Median   | Gen0   | Allocated |
|------------------------------------------- |---------:|---------:|---------:|---------:|-------:|----------:|
| Actor_materializer_run_restart_flow_graph  | 141.8 μs | 80.20 μs | 62.62 μs | 111.4 μs | 6.8359 |  63.27 KB |
| Actor_materializer_run_simple_linear_graph | 118.0 μs | 36.88 μs | 28.79 μs | 124.6 μs | 2.9297 |  28.88 KB |

### Block06 candidate: Akka.Benchmarks.Streams.SourceActorRefBenchmarks

| Method                                           | Mean       | Error    | StdDev   | Req/sec      | Gen0   | Gen1   | Gen2   | Allocated |
|------------------------------------------------- |-----------:|---------:|---------:|-------------:|-------:|-------:|-------:|----------:|
| Source_Queue_Reference_Ingress_Throughput        |   997.5 ns | 31.16 ns | 24.33 ns | 1,002,463.35 | 0.0375 |      - |      - |     321 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,433.0 ns | 15.09 ns | 11.78 ns |   697,824.97 | 0.0925 |      - |      - |     777 B |
| StageActor_Reference_Ingress_Throughput          |   179.6 ns |  6.99 ns |  5.46 ns | 5,566,680.86 | 0.0125 | 0.0113 | 0.0075 |      92 B |
| StageActor_Local_Reference_Ingress_Throughput    |   105.6 ns |  2.13 ns |  1.66 ns | 9,466,901.31 |      - |      - |      - |         - |
| StageActor_Traced_Reference_Ingress_Throughput   |   814.0 ns | 40.86 ns | 31.90 ns | 1,228,524.74 | 0.0750 | 0.0400 | 0.0100 |     584 B |

### Block06 candidate: Akka.Benchmarks.Streams.StreamThroughputBenchmarks

| Method                                   | Mean     | Error    | StdDev   | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|---------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 746.7 ns | 11.96 ns |  9.34 ns | 1,339,254.68 | 0.0125 |     105 B |
| RestartFlow_Reference_Handoff_Throughput | 623.0 ns | 13.45 ns | 10.50 ns | 1,605,038.06 | 0.0050 |      49 B |

### Block06 candidate: Akka.Benchmarks.Streams.MaterializationBenchmarks

| Method                                     | Mean     | Error    | StdDev   | Gen0   | Allocated |
|------------------------------------------- |---------:|---------:|---------:|-------:|----------:|
| Actor_materializer_run_restart_flow_graph  | 138.1 μs | 78.23 μs | 61.08 μs | 6.8359 |  63.28 KB |
| Actor_materializer_run_simple_linear_graph | 132.3 μs | 33.06 μs | 25.81 μs | 2.9297 |  29.08 KB |

### Block07 candidate: Akka.Benchmarks.Streams.SourceActorRefBenchmarks

| Method                                           | Mean       | Error    | StdDev   | Req/sec      | Gen0   | Gen1   | Gen2   | Allocated |
|------------------------------------------------- |-----------:|---------:|---------:|-------------:|-------:|-------:|-------:|----------:|
| Source_Queue_Reference_Ingress_Throughput        |   983.1 ns | 25.11 ns | 19.60 ns | 1,017,241.73 | 0.0375 |      - |      - |     321 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,429.3 ns | 30.43 ns | 23.76 ns |   699,631.53 | 0.0925 |      - |      - |     777 B |
| StageActor_Reference_Ingress_Throughput          |   182.8 ns | 10.74 ns |  8.39 ns | 5,471,664.76 | 0.0113 | 0.0100 | 0.0063 |      86 B |
| StageActor_Local_Reference_Ingress_Throughput    |   107.8 ns |  5.78 ns |  4.51 ns | 9,280,307.72 |      - |      - |      - |         - |
| StageActor_Traced_Reference_Ingress_Throughput   |   801.1 ns | 32.16 ns | 25.11 ns | 1,248,279.07 | 0.0763 | 0.0363 | 0.0113 |     584 B |

### Block07 candidate: Akka.Benchmarks.Streams.StreamThroughputBenchmarks

| Method                                   | Mean     | Error    | StdDev   | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|---------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 747.6 ns | 15.21 ns | 11.87 ns | 1,337,633.11 | 0.0125 |     105 B |
| RestartFlow_Reference_Handoff_Throughput | 613.8 ns | 18.37 ns | 14.35 ns | 1,629,145.66 | 0.0050 |      49 B |

### Block07 candidate: Akka.Benchmarks.Streams.MaterializationBenchmarks

| Method                                     | Mean     | Error    | StdDev   | Median   | Gen0   | Allocated |
|------------------------------------------- |---------:|---------:|---------:|---------:|-------:|----------:|
| Actor_materializer_run_restart_flow_graph  | 130.0 μs | 72.44 μs | 56.55 μs | 102.8 μs | 6.8359 |  63.28 KB |
| Actor_materializer_run_simple_linear_graph | 127.8 μs | 33.27 μs | 25.98 μs | 132.7 μs | 2.9297 |  29.14 KB |

### Block08 baseline: Akka.Benchmarks.Streams.SourceActorRefBenchmarks

| Method                                           | Mean       | Error    | StdDev   | Req/sec      | Gen0   | Gen1   | Gen2   | Allocated |
|------------------------------------------------- |-----------:|---------:|---------:|-------------:|-------:|-------:|-------:|----------:|
| Source_Queue_Reference_Ingress_Throughput        | 1,001.9 ns | 24.37 ns | 19.02 ns |   998,077.58 | 0.0375 |      - |      - |     321 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,451.6 ns | 18.48 ns | 14.43 ns |   688,878.94 | 0.0925 |      - |      - |     777 B |
| StageActor_Reference_Ingress_Throughput          |   203.6 ns | 11.32 ns |  8.84 ns | 4,910,458.13 | 0.0125 | 0.0088 | 0.0025 |     113 B |
| StageActor_Local_Reference_Ingress_Throughput    |   127.5 ns | 17.73 ns | 13.84 ns | 7,843,728.35 | 0.0038 |      - |      - |      32 B |
| StageActor_Traced_Reference_Ingress_Throughput   |   806.1 ns | 30.53 ns | 23.84 ns | 1,240,470.49 | 0.0738 | 0.0325 | 0.0088 |     584 B |

### Block08 baseline: Akka.Benchmarks.Streams.StreamThroughputBenchmarks

| Method                                   | Mean     | Error    | StdDev   | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|---------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 745.5 ns | 19.52 ns | 15.24 ns | 1,341,414.16 | 0.0125 |     105 B |
| RestartFlow_Reference_Handoff_Throughput | 613.4 ns | 16.31 ns | 12.74 ns | 1,630,360.02 | 0.0050 |      49 B |

### Block08 baseline: Akka.Benchmarks.Streams.MaterializationBenchmarks

| Method                                     | Mean     | Error    | StdDev   | Median   | Gen0   | Allocated |
|------------------------------------------- |---------:|---------:|---------:|---------:|-------:|----------:|
| Actor_materializer_run_restart_flow_graph  | 146.8 μs | 95.02 μs | 74.19 μs | 112.0 μs | 6.8359 |  63.27 KB |
| Actor_materializer_run_simple_linear_graph | 150.1 μs | 28.24 μs | 22.05 μs | 151.0 μs | 2.9297 |  29.19 KB |

### Block09 baseline: Akka.Benchmarks.Streams.SourceActorRefBenchmarks

| Method                                           | Mean       | Error     | StdDev   | Req/sec      | Gen0   | Gen1   | Gen2   | Allocated |
|------------------------------------------------- |-----------:|----------:|---------:|-------------:|-------:|-------:|-------:|----------:|
| Source_Queue_Reference_Ingress_Throughput        | 1,006.9 ns |  23.70 ns | 18.50 ns |   993,115.90 | 0.0375 |      - |      - |     321 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,436.8 ns |  18.59 ns | 14.51 ns |   695,971.67 | 0.0925 |      - |      - |     777 B |
| StageActor_Reference_Ingress_Throughput          |   205.7 ns |  12.76 ns |  9.96 ns | 4,861,942.52 | 0.0150 | 0.0100 | 0.0050 |     112 B |
| StageActor_Local_Reference_Ingress_Throughput    |   125.8 ns |   9.23 ns |  7.20 ns | 7,947,849.20 | 0.0038 |      - |      - |      32 B |
| StageActor_Traced_Reference_Ingress_Throughput   |   827.1 ns | 103.36 ns | 80.69 ns | 1,209,015.22 | 0.0763 | 0.0400 | 0.0113 |     584 B |

### Block09 baseline: Akka.Benchmarks.Streams.StreamThroughputBenchmarks

| Method                                   | Mean     | Error    | StdDev  | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|--------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 718.5 ns | 10.01 ns | 7.81 ns | 1,391,833.18 | 0.0125 |     105 B |
| RestartFlow_Reference_Handoff_Throughput | 613.6 ns |  8.22 ns | 6.42 ns | 1,629,853.28 | 0.0050 |      49 B |

### Block09 baseline: Akka.Benchmarks.Streams.MaterializationBenchmarks

| Method                                     | Mean     | Error    | StdDev   | Gen0   | Allocated |
|------------------------------------------- |---------:|---------:|---------:|-------:|----------:|
| Actor_materializer_run_restart_flow_graph  | 129.6 μs | 64.68 μs | 50.50 μs | 6.8359 |   63.3 KB |
| Actor_materializer_run_simple_linear_graph | 124.9 μs | 33.05 μs | 25.81 μs | 2.9297 |  29.14 KB |

### Block10 candidate: Akka.Benchmarks.Streams.SourceActorRefBenchmarks

| Method                                           | Mean       | Error     | StdDev    | Req/sec      | Gen0   | Gen1   | Gen2   | Allocated |
|------------------------------------------------- |-----------:|----------:|----------:|-------------:|-------:|-------:|-------:|----------:|
| Source_Queue_Reference_Ingress_Throughput        |   898.9 ns | 183.27 ns | 143.08 ns | 1,112,413.23 | 0.0375 |      - |      - |     321 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,331.1 ns |  83.52 ns |  65.21 ns |   751,235.08 | 0.0938 |      - |      - |     777 B |
| StageActor_Reference_Ingress_Throughput          |   228.0 ns |  19.32 ns |  15.08 ns | 4,386,192.34 | 0.0125 | 0.0100 | 0.0063 |      90 B |
| StageActor_Local_Reference_Ingress_Throughput    |   117.2 ns |  23.61 ns |  18.43 ns | 8,531,914.69 |      - |      - |      - |         - |
| StageActor_Traced_Reference_Ingress_Throughput   | 1,048.4 ns | 117.82 ns |  91.98 ns |   953,793.61 | 0.0763 | 0.0425 | 0.0113 |     584 B |

### Block10 candidate: Akka.Benchmarks.Streams.StreamThroughputBenchmarks

| Method                                   | Mean     | Error    | StdDev   | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|---------:|---------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 804.8 ns | 47.15 ns | 36.81 ns | 1,242,486.15 | 0.0125 |     105 B |
| RestartFlow_Reference_Handoff_Throughput | 626.9 ns | 18.82 ns | 14.69 ns | 1,595,069.68 | 0.0050 |      49 B |

### Block10 candidate: Akka.Benchmarks.Streams.MaterializationBenchmarks

| Method                                     | Mean     | Error     | StdDev   | Gen0   | Allocated |
|------------------------------------------- |---------:|----------:|---------:|-------:|----------:|
| Actor_materializer_run_restart_flow_graph  | 177.6 μs | 110.68 μs | 86.41 μs | 6.8359 |  63.32 KB |
| Actor_materializer_run_simple_linear_graph | 132.4 μs |  39.41 μs | 30.77 μs | 2.9297 |  29.07 KB |

## Reproduction

Build `src/benchmark/Akka.Benchmarks/Akka.Benchmarks.csproj` in Release, then run the compiled benchmark assembly with `AKKA_BENCHMARK_CPU_WINDOWS=1`, `DOTNET_GCDynamicAdaptationMode=0` and `DOTNET_GCHeapCount=8`. Hot filters: `*StreamThroughputBenchmarks.RestartFlow_*Handoff_Throughput`, `*SourceActorRefBenchmarks.Source_Queue_Reference_Ingress_Throughput`, `*SourceActorRefBenchmarks.Source_Queue_Traced_Reference_Ingress_Throughput`, `*SourceActorRefBenchmarks.StageActor_*Reference_Ingress_Throughput`. Use `--job Short --strategy Throughput --invocationCount 8 --unrollFactor 1 --warmupCount 6 --iterationCount 12 --launchCount 1 --outliers DontRemove --exporters fulljson csvmeasurements --keepFiles`. For cold controls use filter `*MaterializationBenchmarks.Actor_materializer_run_*` and invocationCount 1024. Repeat the balanced ten-block order; do not pool iterations as independent processes.

Run frozen RemotePingPong binaries sequentially with `run --artery --clients 1 --msgs 1000000 --times 3 --payload real --serializer v2 --inbound-lanes 1 --outbound-lanes 1`; cumulative process allocation and CPU counters bracket each repetition. Retain exact stdout, source/binary hashes, background-load telemetry and every observation. Never overlap RemotePingPong processes.

## RemotePingPong process medians in execution order

| Block | Role | Allocation B/configured message | CPU ns/configured message | Configured messages/s |
|---|---|---:|---:|---:|
| 1 | baseline | 2928.110 | 14555.492 | 283,006 |
| 2 | candidate | 2917.301 | 14527.859 | 310,994 |
| 3 | candidate | 2922.468 | 15300.656 | 345,424 |
| 4 | baseline | 2933.691 | 14549.219 | 319,847 |
| 5 | baseline | 2926.555 | 13241.010 | 303,031 |
| 6 | candidate | 2918.108 | 13613.474 | 338,410 |
| 7 | candidate | 2924.879 | 16849.964 | 375,305 |
| 8 | baseline | 2936.102 | 16805.519 | 379,651 |
| 9 | baseline | 2937.697 | 16515.691 | 399,841 |
| 10 | candidate | 2926.244 | 16801.274 | 386,848 |

The predeclared medians above show allocation 2933.691→2922.468 B/configured message (-0.383%), CPU 14555.492→15300.656 ns/configured message (+5.12%), and wall throughput 319847→345424 (+8.00%). Process CPU medians vary over time. As a post-hoc description, the five adjacent baseline/candidate pair differences are -0.19%, +5.16%, +2.81%, +0.26%, +1.73%; these are not a replacement estimator or a prespecified test. CPU covers all process threads and can change with scheduling, batching, spinning and GC. This study does not isolate an instruction-level dispatch penalty.

## All RemotePingPong repetitions

| Block | Role | Rep | Allocated bytes | B/configured message | CPU ns/configured message | Messages/s | Elapsed ms |
|---|---|---:|---:|---:|---:|---:|---:|
| 1 | baseline | 1 | 5856220296 | 2928.110 | 17283.113 | 228676 | 8746.63 |
| 1 | baseline | 2 | 5850174864 | 2925.087 | 13731.952 | 283006 | 7067.20 |
| 1 | baseline | 3 | 5867768144 | 2933.884 | 14555.492 | 352175 | 5679.11 |
| 2 | candidate | 1 | 5834602000 | 2917.301 | 17529.347 | 213470 | 9369.67 |
| 2 | candidate | 2 | 5833773384 | 2916.887 | 12878.683 | 310994 | 6431.82 |
| 2 | candidate | 3 | 5848008592 | 2924.004 | 14527.859 | 373553 | 5354.05 |
| 3 | candidate | 1 | 5844565032 | 2922.283 | 17945.995 | 248047 | 8063.84 |
| 3 | candidate | 2 | 5844935112 | 2922.468 | 15300.656 | 345424 | 5790.24 |
| 3 | candidate | 3 | 5848121816 | 2924.061 | 14929.180 | 371265 | 5387.83 |
| 4 | baseline | 1 | 5867381624 | 2933.691 | 16843.823 | 254130 | 7870.66 |
| 4 | baseline | 2 | 5869205752 | 2934.603 | 14549.219 | 362319 | 5520.03 |
| 4 | baseline | 3 | 5849704016 | 2924.852 | 13256.571 | 319847 | 6253.14 |
| 5 | baseline | 1 | 5853110568 | 2926.555 | 16307.672 | 225632 | 8864.59 |
| 5 | baseline | 2 | 5852062784 | 2926.031 | 12949.339 | 303031 | 6600.14 |
| 5 | baseline | 3 | 5857080800 | 2928.540 | 13241.010 | 341297 | 5860.93 |
| 6 | candidate | 1 | 5836215720 | 2918.108 | 16771.713 | 241139 | 8294.70 |
| 6 | candidate | 2 | 5836606104 | 2918.303 | 12880.522 | 349590 | 5721.23 |
| 6 | candidate | 3 | 5831459248 | 2915.730 | 13613.474 | 338410 | 5910.81 |
| 7 | candidate | 1 | 5840333888 | 2920.167 | 17523.456 | 230708 | 8669.56 |
| 7 | candidate | 2 | 5849758472 | 2924.879 | 15744.847 | 375305 | 5329.87 |
| 7 | candidate | 3 | 5852520328 | 2926.260 | 16849.964 | 392465 | 5096.33 |
| 8 | baseline | 1 | 5869780272 | 2934.890 | 20691.377 | 279018 | 7168.98 |
| 8 | baseline | 2 | 5874300024 | 2937.150 | 16805.519 | 379651 | 5268.87 |
| 8 | baseline | 3 | 5872203040 | 2936.102 | 16782.151 | 390016 | 5128.46 |
| 9 | baseline | 1 | 5875166064 | 2937.583 | 20594.859 | 274161 | 7295.49 |
| 9 | baseline | 2 | 5875393048 | 2937.697 | 15868.012 | 415801 | 4810.19 |
| 9 | baseline | 3 | 5875441512 | 2937.721 | 16515.691 | 399841 | 5002.46 |
| 10 | candidate | 1 | 5850856376 | 2925.428 | 21340.404 | 271408 | 7369.18 |
| 10 | candidate | 2 | 5852487880 | 2926.244 | 16535.380 | 398963 | 5013.74 |
| 10 | candidate | 3 | 5853377744 | 2926.689 | 16801.274 | 386848 | 5170.29 |

The final measurement audit rechecked all frozen source, binary and runner hashes and all 30 UTC windows are globally sequential. The RPP validation files' revision field denotes the current role source HEAD; it is not the binary build commit. The explicit provenance mapping is baseline build `7406d821093687a1f41c9f6573a54b883fcf171a` and candidate build `12012ada548cf7d6f9d9bc4e927c3a26d5da1687`; the later common BDN-only CPU helper is absent from RPP. Recursive hashes match all 105 original files in each role.
