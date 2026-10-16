# TCP read callback and async-wrapper experiment

Baseline: #8813 head `0879aceea3044dd2bf03958bd4ef0d5d0e05f0b1`.
Candidate: `b097007d16998e1c73d7b2a6d3bb3d6077ec4f6d`.

`RequestPipeRead` previously allocated a capturing local async Task wrapper around an async ValueTask read operation. The candidate uses one static async Task operation for the read loop, copy, and mailbox delivery. It retains copy-before-AdvanceTo, empty-read retry, EOF, cancellation and failure handling. The copy keeps received bytes valid for asynchronous actor consumers. No public API, protocol, GC or ownership contract changes.

## Correctness

Both revisions passed all **72 TCP module tests**, zero failures/skips. Candidate passed **35 API approvals**, zero failures/skips. Four added socket/pipe-backed read controls distinguish buffered and genuinely pending reads, preserve prior received bytes across a subsequent read, and exercise pipe failure and empty EOF. Initial EOF fixture failures came from the fake transport not completing its read-pump task; the corrected fixture passes on both revisions. Original failure evidence is retained. Slopwatch and whitespace checks passed.

## BenchmarkDotNet

The existing `TcpOperationsBenchmarks.ClientServerCommunication` covers existing 10/100-byte payloads and one client. Both revisions use an identical benchmark harness (SHA256 `c8a5353701dda687637870d3ad7b3a1512fb2a95fb9fca34e5260ef3162005ff`) that verifies every payload byte and exactly 1,000,000 sent/received echoes, correctly distributes remainders, and propagates corruption/count/trailing-partial errors through the benchmark Ask. Connection actor capture is outside the Props factory. Iteration setup is excluded; communication and awaited actor cleanup are included and amortized over one million roundtrips.

BenchmarkDotNet remains 0.15.8. The initial generated runner rejected Task-returning IterationCleanup (CS0407), executing zero cases; its logs and NA report are retained and excluded from results. Corrected cleanup is awaited in the benchmark finally. Both runs executed exactly two cases under the existing Monitoring job with one launch, three warmups and three measured iterations. Actual child GC: concurrent Server GC, eight heaps, adaptation disabled; matching experimental environment on both revisions.

Allocation falls **310→273 B/roundtrip** for 10-byte payloads and **536→496 B/roundtrip** for 100-byte payloads. Timing means **3.815→3.759 µs** and **4.026→4.044 µs** have wide intervals; these directional runs establish no speedup. Every configured echo and payload check passed.

### Baseline generated report

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  LongRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=LongRun  Concurrent=True  Server=True  
InvocationCount=1  IterationCount=3  LaunchCount=1  
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3  

```
| Method                    | MessageLength | ClientsCount | Mean     | Error     | StdDev    | Req/sec    | Gen0   | Allocated |
|-------------------------- |-------------- |------------- |---------:|----------:|----------:|-----------:|-------:|----------:|
| **ClientServerCommunication** | **10**            | **1**            | **3.815 μs** | **0.5924 μs** | **0.0325 μs** | **262,117.06** | **0.0030** |     **310 B** |
| **ClientServerCommunication** | **100**           | **1**            | **4.026 μs** | **1.9094 μs** | **0.1047 μs** | **248,394.03** | **0.0060** |     **536 B** |

### Candidate generated report

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  LongRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=LongRun  Concurrent=True  Server=True  
InvocationCount=1  IterationCount=3  LaunchCount=1  
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3  

```
| Method                    | MessageLength | ClientsCount | Mean     | Error    | StdDev    | Req/sec    | Gen0   | Allocated |
|-------------------------- |-------------- |------------- |---------:|---------:|----------:|-----------:|-------:|----------:|
| **ClientServerCommunication** | **10**            | **1**            | **3.759 μs** | **1.821 μs** | **0.0998 μs** | **266,054.64** | **0.0030** |     **273 B** |
| **ClientServerCommunication** | **100**           | **1**            | **4.044 μs** | **1.539 μs** | **0.0844 μs** | **247,307.96** | **0.0060** |     **496 B** |

## Actual Artery RemotePingPong

Sequential ABBA processes, three repetitions per process: one client, one million configured roundtrips per repetition, real V2 serializer payload (348 bytes), window 50, one inbound/outbound lane. Fixed adaptation-disabled GC policy, eight server heaps, identical instrumentation and runtime configurations. Every process passed the 8×1 MiB allocation-counter probe (8,388,800 bytes), reported expected startup GC settings and monotonic cumulative snapshots, with no warnings.

Median allocation is **3056.302→3042.011 B/configured message**, an observed reduction of **14.291 B (0.468%)**. Baseline range is 3055.126–3060.299 B; candidate range is 3018.163–3047.099 B. Median throughput is 321,964→240,760.5 messages/s with broad ranges. These observations do not establish a causal timing regression or benefit: unrelated host test activity overlapped, and process/coalescing variation remains. All observations are retained without exclusions or extra trials.

The denominator is configured ping+echo messages; initial local primers and pending echo tails prevent an exact independently counted delivery denominator. Allocation totals cover the entire process inside explicit UTC windows, not just the optimized call site. Do not attribute the entire observed change to read wrappers. Original Program.cs files were restored exactly after instrumented builds.

| Run | Revision | Rep | Allocated bytes | B/configured message | Messages/s | Elapsed ms | Threads start/end |
|---|---|---:|---:|---:|---:|---:|---:|
| a1 | baseline | 1 | 6113124336 | 3056.562 | 222693 | 8981.34 | 28/47 |
| a1 | baseline | 2 | 6115445592 | 3057.723 | 333668 | 5994.62 | 47/47 |
| a1 | baseline | 3 | 6112084288 | 3056.042 | 323363 | 6185.84 | 47/55 |
| b1 | candidate | 1 | 6076310152 | 3038.155 | 220095 | 9087.63 | 28/42 |
| b1 | candidate | 2 | 6036325704 | 3018.163 | 205445 | 9735.12 | 42/42 |
| b1 | candidate | 3 | 6045172320 | 3022.586 | 230894 | 8662.91 | 42/39 |
| b2 | candidate | 1 | 6091733592 | 3045.867 | 250627 | 7980.39 | 28/47 |
| b2 | candidate | 2 | 6092579136 | 3046.290 | 345483 | 5789.27 | 47/47 |
| b2 | candidate | 3 | 6094197136 | 3047.099 | 352051 | 5681.06 | 47/47 |
| a2 | baseline | 1 | 6110251744 | 3055.126 | 233837 | 8553.87 | 28/51 |
| a2 | baseline | 2 | 6110590968 | 3055.295 | 320565 | 6239.92 | 51/51 |
| a2 | baseline | 3 | 6120598696 | 3060.299 | 339905 | 5884.05 | 51/51 |

## Profile attribution

Matched sequential actual Artery RemotePingPong captures completed successfully. Both have **0 lost events**, with **8 GC heaps observed in every raw allocation window**. Profile timings are excluded from performance results.

Baseline `0879aceea3044dd2bf03958bd4ef0d5d0e05f0b1`; candidate `b097007d16998e1c73d7b2a6d3bb3d6077ec4f6d`. Identical retained instrumentation patches; binary manifests read back successfully. Fixed adaptation disabled / heap count 8, one client, one lane, window 50, real V2/348-byte payload, three repetitions of 1M ping-pong exchanges.

| Target allocation | Baseline window-sampled ticks | Candidate window-sampled ticks | Observed object size |
|---|---:|---:|---:|
| RequestPipeRead capture | 439 (143/133/163) | no matching site observed | 40 B |
| Outer AwaitPipeReadAsync box | 461 (149/172/140) | no matching site observed | 120 B |
| Inner ReadPipeChunkAsync box | 716 (250/213/253) | replaced | 160 B |
| ReadPipeAndSendAsync box | absent in baseline source | 715 (250/230/235) | 160 B |

The capture identity has 20 direct RequestPipeRead stack samples and 419 additional samples linked by the same trace PID/CLR/nonzero TypeID after that namespace-qualified validation. The async boxes have closed type names and method stacks identifying their exact source functions. Compiler display-class ordinal alone is not used. All target sampled ticks happen to lie inside the three retained UTC windows.

| Variant | Tick/counter ratio, window 1 | Window2 | Window3 |
|---|---:|---:|---:|
| baseline | 1.011989 | 1.010227 | 1.009803 |
| candidate | 1.011252 | 1.008543 | 1.009557 |

The source refactor removes the capture and one outer await/state-machine layer while retaining copy-before-AdvanceTo, stable actor identity, cancellation filtering and EOF/error mailbox delivery. Observed sizes establish the removed allocation sites; they do not establish a fixed number of bytes per configured message. Pending reads and synchronous buffered reads allocate differently, and TCP can batch multiple messages into one read.

AllocationTick counts are sampled ticks, not object counts. Weighted bytes are allocation-quantum estimates, not exact type allocation totals. UTC windows bracket precise process-counter reads and need not line up with tick quanta. CPU samples and absent/inlined methods do not establish the causal time saving. No all-TCP-closure elimination or aggregate-allocation attribution is claimed.

Raw captures, ETLX, generic allocation/CPU reports, final window analyses, exact commands, source/tool provenance and binary readback logs are retained beside this summary. Intermediate analyzer reports preserve an initial category overlap corrected in the **-final** window reports; use those final JSON files for attribution.

## Decision and evidence

Retain the production refactor: both revisions pass the same TCP controls, isolated TCP BenchmarkDotNet reports lower allocation, and actual Artery profiles independently confirm removal of the targeted capture/outer-wrapper sites. The smaller and variable aggregate Artery observation remains qualified; no causal throughput gain or regression is established. The receive copy and ordinary asynchronous consumer contract remain intact.

Raw evidence is retained at `/tmp/akka-remaining-perf-20261016/tcp-read`, including the failed initial runner, corrected generated reports, every ABBA observation, raw nettraces, exact type/stack identities, UTC windows, GC history and hash readbacks. Durable archive: `/home/petabridge/benchmark-results/akka.net/tcp-read-wrapper-2026-10-16`. All 468 evidence files (405,155,480 bytes) were copied and SHA256 read back successfully; `archive-receipt.json` records each relative path/hash/size. Local checks are complete; hosted CI is separate and was still pending at publication.
