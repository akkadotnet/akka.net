# Restart substream value-payload allocation results

SubSource<T>.PushSubstream currently wraps value elements in public OnNext(object), allocating both the message and a boxed payload on each handoff. Keep value elements in a private TypedOnNext message and push its T directly into the interpreter. Reference payloads retain the existing OnNext path. The public actor protocol, wire format, status transitions, cancellation and tracing remain unchanged. Legacy SubSink egress boxing remains outside this change.

**Stack:** follows #8812, targeting upstream `feature/streams-callback-closure-8812` pinned to final parent `7c2ba6cb485605f46404188ee3ffd2be31725571`. Frozen candidate `6c46763380e30877fbfd833f7bc6e089abf08d64`. Artery literal caching/compression is excluded.

**Local verification:** baseline and candidate each pass all 63 focused SubSource, Restart, AsyncCallback and StreamsDiagnostics cases, zero failures/skips. Sixteen new SubSource cases cover default/nullable/segmented values, reference identity, early/queued completion and failure, cancellation cause, tracing and forced restart. API approvals pass all 35 cases. Full Streams suite: 1,919 passed, 33 inherited skips, zero failures (1,952 total). All six existing Artery recovery/buffer-ownership checks passed with zero skips. These cover returned-buffer poisoning, initially failed connection recovery and four batching/disposal cases; no single case combines forced restart with exhaustive disposal accounting. Initial missing-import build and four fixture subscription failures occurred on both revisions, were corrected identically, and are retained as evidence.

**Benchmark controls:** reuse StreamThroughputBenchmarks with two RestartFlow cases: a prebuilt 128-byte ReadOnlySequence<byte> and reusable byte[] reference. Each validates every byte, 100,000 deliveries and the aggregate total. One changed SubSource ingress and one legacy SubSink egress per element; no restart induced. Identical benchmark source SHA256 `bfceb672ca453d211feedb72d323b5ae4f72d8e7e208cc6a16221c81d72669f4`. Generated before/after BenchmarkDotNet reports are embedded below. Sequence allocation falls 353 → 329 B/element (24 B per changed handoff); reference allocation stays 273 B. Matched profiles below verify actual carrier sizes and removal of the targeted box site. Actual BDN child used concurrent workstation GC, one heap and adaptation mode 0. Heavy unrelated host load was recorded, and ShortRun timing intervals are wide; no throughput improvement claim.

**Transport verification:** sequential RemotePingPong ABBA with the same adaptation-disabled GC control as #8812; separate matched allocation profiles for site attribution and remaining boxing. Unprofiled measurement and matched profiles completed; no simultaneous RPP or competing benchmark/test workloads. These local results and hosted CI will be reported separately.

**Before: parent `7c2ba6cb4`**

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                   | Mean     | Error      | StdDev    | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|-----------:|----------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 936.9 ns | 2,222.2 ns | 121.81 ns | 1,067,379.49 | 0.0420 |     353 B |
| RestartFlow_Reference_Handoff_Throughput | 758.7 ns |   816.5 ns |  44.76 ns | 1,317,982.05 | 0.0314 |     273 B |

**After: candidate `6c4676338`**

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                   | Mean     | Error      | StdDev   | Req/sec      | Gen0   | Allocated |
|----------------------------------------- |---------:|-----------:|---------:|-------------:|-------:|----------:|
| RestartFlow_Sequence_Handoff_Throughput  | 854.8 ns | 1,008.7 ns | 55.29 ns | 1,169,808.49 | 0.0383 |     329 B |
| RestartFlow_Reference_Handoff_Throughput | 766.8 ns |   770.5 ns | 42.23 ns | 1,304,055.43 | 0.0317 |     273 B |

**Sequential RemotePingPong observations (ABBA)**

One client, 1,000,000 repeats, three repetitions per process, one inbound/outbound lane, window 50, 348 B real V2 payload. Baseline parent `7c2ba6cb4`, candidate `6c4676338`; fresh builds use identical temporary instrumentation SHA `6ed841f8f9ca09fd0be5051bad0f17ebe64b76e96aff5f9d93e1a25c3295bb58`, restored afterward. Actual ServerGC runtimeconfig SHA `49dcbda731c01b2aa372bf17e085fef222498703fc3981d795064c587e1def70`. Both processes use experimental `DOTNET_GCDynamicAdaptationMode=0 DOTNET_GCHeapCount=8`; startup configuration matched, allocation snapshots were monotonic, and all retained-array probes measured 8,388,800 B against an 8,388,608 B payload lower bound. The probe is a sanity check, not universal counter-precision proof. No production GC settings change.

Allocation covers the original priming/wait window, normalized by 2,000,000 configured ping+echo messages. Initial local primers and a pending echo tail mean this is not an independent exact delivery count. All repetitions are retained below.

| Run | Revision | Rep | Allocated bytes | B/configured message | Messages/s | Elapsed ms | Threads start/end |
|---|---|---:|---:|---:|---:|---:|---:|
| a1 | baseline | 1 | 6087939400 | 3043.970 | 147038 | 13602.89 | 28/40 |
| a1 | baseline | 2 | 6069553808 | 3034.777 | 172533 | 11592.08 | 40/48 |
| a1 | baseline | 3 | 6081287176 | 3040.644 | 177810 | 11248.75 | 48/46 |
| b1 | candidate | 1 | 6019005544 | 3009.503 | 121015 | 16527.57 | 28/40 |
| b1 | candidate | 2 | 6025942456 | 3012.971 | 162023 | 12344.70 | 40/40 |
| b1 | candidate | 3 | 6061415864 | 3030.708 | 209842 | 9531.88 | 40/40 |
| b2 | candidate | 1 | 6065580424 | 3032.790 | 162417 | 12314.39 | 28/39 |
| b2 | candidate | 2 | 6063242360 | 3031.621 | 206356 | 9692.07 | 39/39 |
| b2 | candidate | 3 | 6047963184 | 3023.982 | 189001 | 10582.25 | 39/37 |
| a2 | baseline | 1 | 6156372432 | 3078.186 | 225276 | 8878.32 | 28/40 |
| a2 | baseline | 2 | 6156186136 | 3078.093 | 310270 | 6446.40 | 40/40 |
| a2 | baseline | 3 | 6155120784 | 3077.560 | 276358 | 7237.13 | 40/48 |

All-six allocation medians: 3,060.765 → 3,027.345 B/configured message (observed −33.420 B, −1.092%). Baseline range 3,034.777–3,078.186; candidate 3,009.503–3,032.790. Throughput medians 201,543 → 175,709 messages/s; these timing observations decreased. Severe unrelated host load was recorded (including Netclaw mutation/integration tests); timing and batching variation prevent a causal throughput conclusion. The full process allocation difference is not assigned entirely to the one removed box. Separate profile timings are excluded.

# Matched SubSource typed-carrier profiles

Sequential baseline and candidate GC-verbose + SampleProfiler captures both exited0. Profile timings are excluded from performance results. The fixed comparison uses DOTNET_GCDynamicAdaptationMode=0 and DOTNET_GCHeapCount=8 in both processes.

| Observed sampled allocation | Baseline | Candidate |
|---|---:|---:|
| Sequence boxes, all selected sites | 2472 | 32 |
| Direct SubSource.PushSubstream sequence boxes | 51 | 0 |
| Source-mapped Restart.CreateSubOutlet sequence boxes | 2374 | 0 |
| Other/remaining sequence boxes | 47 | 32 |
| Public OnNext messages at selected source stacks | 1404 ticks,24B objects | 0 observed |
| Private TypedOnNext<ReadOnlySequence<byte>> | 0 observed | 2377 ticks,40B objects |
| EventsLost | 0 | 0 |
| Observed GC heap counts | 8 | 8 |

Observed old objects are24B public OnNext plus40B boxed sequence. The new private sequence carrier is40B. The source allocation pattern therefore replaces64B with40B per eligible handoff; the isolated BDN allocation result353→329B/operation independently confirms a24B net reduction. Reference control273→273B is unchanged. Sampled tick counts do not count every handoff and are not used to calculate that size delta.

The new private carrier is validated using namespace-qualified SubSource.PushSubstream and source-mapped Restart.CreateSubOutlet stacks, closed ReadOnlySequence type evidence, and same-trace PID/CLR/nonzero TypeID. No sequence-box or public-OnNext TypeID is expanded into a single-site attribution. Here all2377 carrier ticks already have direct or source-mapped producer stacks.

Remaining candidate sequence boxes are32 observed40B objects in TcpConnectionStage.TcpStreamLogic.<.ctor>b__13_2 stacks, sampled around0.788–3.822 seconds. Baseline remaining47 samples have the same TCP stack family. No SubSink.Logic.OnPush sequence-box stack was observed in either capture; this does not establish that SubSink never boxes or predict its steady per-message rate. Remaining boxes are preserved in JSON rather than credited as removed.

## Allocation-counter windows

| Variant/repetition | UTC window | Precise counter delta | Approximate tick bytes | Ratio |
|---|---|---:|---:|---:|
| baseline/1 | 2026-10-15T12:48:43.0198937Z to 2026-10-15T12:48:57.1369329Z | 6,054,505,152 | 6,087,463,736 | 1.005444 |
| baseline/2 | 2026-10-15T12:48:57.2951541Z to 2026-10-15T12:49:08.2385866Z | 6,093,144,160 | 6,120,582,792 | 1.004503 |
| baseline/3 | 2026-10-15T12:49:08.3178691Z to 2026-10-15T12:49:19.1441662Z | 6,094,214,968 | 6,123,308,408 | 1.004774 |
| candidate/1 | 2026-10-15T12:49:41.9192051Z to 2026-10-15T12:49:56.0658502Z | 6,042,293,544 | 6,078,377,032 | 1.005972 |
| candidate/2 | 2026-10-15T12:49:56.2289053Z to 2026-10-15T12:50:07.0975578Z | 6,042,640,248 | 6,075,415,856 | 1.005424 |
| candidate/3 | 2026-10-15T12:50:07.1738815Z to 2026-10-15T12:50:18.483336Z | 6,046,287,592 | 6,078,268,984 | 1.005289 |

Each capture has three raw UTC windows and monotonic before/after cumulative counters. All GCGlobalHeapHistory observations report8heaps, including each window. Approximate allocation-quantum totals are about0.45–0.60% above the precise counters. Both startup retained-array sanity probes pass their8MiB lower bound. This supports current counter magnitudes, not exact byte equality or a causal explanation for earlier dynamic-heap counter drift.

## Built IL and matched scope

Retained baseline-PushSubstream.il shows unconditional box !T followed by public OnNext allocation. Candidate-PushSubstream.il shows the IsValueType branch constructing private TypedOnNext<T> directly without boxing; the reference branch retains box !T/public OnNext (boxing a reference-type T is a runtime no-op). Complete ILSpy output is retained alongside the snippets.

Baseline revision: 7c2ba6cb485605f46404188ee3ffd2be31725571. Candidate revision: 6c46763380e30877fbfd833f7bc6e089abf08d64. Both use Artery.Tcp, real V2 serializer987001,348-byte canonical payload,eight readings,one client,1M repeats,three repetitions,window50,one inbound/outbound lane. Exact commands are in baseline-profile-command.txt and candidate-profile-command.txt. Temporary instrumentation patches, instrumented source hashes and all harness-source manifests are byte-identical. Frozen RemotePingPong.dll and Akka.Streams.dll hashes match their build manifests.

## Interpretation limits

- Allocation ticks are sampled, not exact object counts; weighted bytes are allocation-quantum attribution, not exact type/site bytes. No tick-rate normalization proves a transport-per-message saving.
- Selected sequence/message allocation stacks have no empty/missing stacks in these captures; unresolved-frame tick counts are separately retained in JSON. JIT-inlined source mappings remain distinct from direct PushSubstream frames.
- Heap counts are observed at GC events, and UTC boundaries may include quantum spill or incidental process allocations. Profile timing is excluded.
- The unprofiled12-repetition median3060.765→3027.345B/configured ping+echo, observed−33.420B/−1.092%, is a process total. It is not entirely attributable to the24B isolated handoff saving and does not establish a throughput gain.
- Public/reference OnNext behavior and remaining SubSink/other boxing are separate boundaries. This change does not eliminate all ReadOnlySequence boxing.

**Evidence retention:** generated reports, all raw repetitions/counters/windows, host snapshots, build/test logs and TRX, source/runtimeconfig/binary hashes, raw traces, analyzer sources and IL are archived with a verified SHA-256 manifest at `/home/petabridge/benchmark-results/akka.net/streams-substream-boxing-2026-10-15`. These are local results; hosted CI remains separate.

Reproduce focused coverage with `dotnet test src/core/Akka.Streams.Tests/Akka.Streams.Tests.csproj -c Release --filter "FullyQualifiedName~SubSourceSpec|FullyQualifiedName~RestartSpec|FullyQualifiedName~StreamsDiagnosticsSpec|FullyQualifiedName~AsyncCallbackSpec"`. Exact sequential BDN and temporary RemotePingPong measurement/profile commands are retained in the evidence archive.
