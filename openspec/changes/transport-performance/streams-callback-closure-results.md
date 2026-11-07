# Async callback closure allocation results

ConcurrentAsyncCallback<T>.OnAsyncInput declares its tracing lambda in the same method as the no-context return. The compiler allocates its display class before that return; actual Artery read/ack profiles observed 64 B closures through late trace windows.

Move the context-aware body into a private helper called only when ingressContext.HasValue. The no-context path dispatches the same cached handler; traced callbacks retain producer context, ingress Activity lifetime, handler, feedback promise and failure behavior. No public API, wire, scheduling, ownership or GC configuration change.

**Stack:** first of the two requested fixes, targeting `feature/serializer-writer-count-8811` pinned to #8811 head `c39216cee`. Frozen candidate `99f076fcf`. Restart/substream boxing is the following separate PR; literal caching/compression is excluded.

**Validation:** 15 focused async-callback/diagnostics tests passed, zero failed/skipped, including three new traced/untraced feedback, isolation and exception-cleanup controls. Existing prestart, ordering, detachment and failure coverage remains. Full local Streams suite: 1,903 passed, 33 inherited skips, zero failures (1,936 total). API approvals: all 35 passed. Baseline and candidate each passed the 15 focused controls. Slopwatch and code whitespace checks passed. Initial nullable diagnostics in optional test-output logging were fixed before the passing run; the failed zero-discovery build is retained.

**Benchmark plan:** reuse SourceActorRefBenchmarks with four Queue controls: int/reference payload × no-context/traced producer context. Every accepted offer, total count and sum is checked; traced controls verify producer parentage with bounded counters and no activity retention. Queue callbacks carry reference Offer commands for both payload arms, so these do not claim direct callback<T> value-boxing measurements. Identical harness SHA `995559e1638e2feb608c71f8a51daac28a25eb6203a73fe4ef6879fb03bbbb5c` on baseline/candidate. Generated BenchmarkDotNet reports are embedded below. Untraced controls save approximately 63–64 B per element; traced allocations remain unchanged. ShortRun timing confidence intervals are wide; these samples establish neither a speedup nor a fixed regression. The actual BDN child used concurrent workstation GC, one heap and adaptation mode 0.

**Transport measurement control:** sequential RemotePingPong only. Both revisions receive matching experimental adaptation-disabled GC settings; server-GC RPP requests eight heaps; both matched traces observed only eight heaps. Existing benchmark GC flavor is preserved and logged from actual BDN child setup. Temporary process allocation counters/config logging and a retained-array sanity probe remain outside production code. Previous default adaptive-GC totals were unstable; heap transitions are a hypothesis rather than a confirmed cause. Process-wide allocation observations and bounded site attribution are reported below; no throughput claim.

**Before: parent `c39216cee`**

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                           | Mean       | Error      | StdDev    | Req/sec      | Gen0   | Allocated |
|------------------------------------------------- |-----------:|-----------:|----------:|-------------:|-------:|----------:|
| Source_Queue_Ingress_Throughput                  |   887.1 ns | 1,900.6 ns | 104.18 ns | 1,127,213.70 | 0.0450 |     384 B |
| Source_Queue_Reference_Ingress_Throughput        |   855.5 ns |   774.1 ns |  42.43 ns | 1,168,861.00 | 0.0400 |     385 B |
| Source_Queue_Traced_Ingress_Throughput           | 1,311.8 ns | 1,385.7 ns |  75.96 ns |   762,332.12 | 0.0900 |     776 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1,600.7 ns | 9,465.6 ns | 518.84 ns |   624,721.61 | 0.0900 |     777 B |

**After: candidate `99f076fcf`**

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                           | Mean     | Error     | StdDev    | Req/sec    | Gen0   | Allocated |
|------------------------------------------------- |---------:|----------:|----------:|-----------:|-------:|----------:|
| Source_Queue_Ingress_Throughput                  | 1.304 μs | 5.4827 μs | 0.3005 μs | 767,153.61 | 0.0300 |     321 B |
| Source_Queue_Reference_Ingress_Throughput        | 1.012 μs | 0.2398 μs | 0.0131 μs | 987,893.08 | 0.0380 |     321 B |
| Source_Queue_Traced_Ingress_Throughput           | 1.392 μs | 0.2069 μs | 0.0113 μs | 718,602.19 | 0.0900 |     776 B |
| Source_Queue_Traced_Reference_Ingress_Throughput | 1.499 μs | 1.0109 μs | 0.0554 μs | 667,134.04 | 0.0900 |     777 B |

**Sequential RemotePingPong observations (ABBA)**

One client, 1,000,000 repeats, three repetitions per process, one inbound/outbound lane, window 50, 348 B V2 payload. Allocation snapshots cover the original priming/wait window, normalized by 2,000,000 configured ping+echo messages. Both revisions used `DOTNET_GCDynamicAdaptationMode=0 DOTNET_GCHeapCount=8`; startup configuration matched and retained-array probes each measured 8,388,800 B for an 8,388,608 B payload. This checks counter sanity, not universal precision. Raw counters were monotonic. These are process-wide observations; matched profiles confirm removal of callback-entry closure allocations, while the complete aggregate reduction remains unattributed.

| Run | Revision | Rep | Allocated bytes | B/configured message | Messages/s | Elapsed ms | Threads start/end |
|---|---|---:|---:|---:|---:|---:|---:|
| a1 | baseline | 1 | 6462799024 | 3231.400 | 221705 | 9021.99 | 28/47 |
| a1 | baseline | 2 | 6386623744 | 3193.312 | 187266 | 10680.21 | 47/55 |
| a1 | baseline | 3 | 6374459672 | 3187.230 | 188502 | 10610.15 | 55/53 |
| b1 | candidate | 1 | 6172533808 | 3086.267 | 253197 | 7899.57 | 28/42 |
| b1 | candidate | 2 | 6175711632 | 3087.856 | 381098 | 5248.74 | 42/42 |
| b1 | candidate | 3 | 6173445064 | 3086.723 | 377715 | 5295.06 | 42/42 |
| b2 | candidate | 1 | 6138664960 | 3069.332 | 204165 | 9796.26 | 28/55 |
| b2 | candidate | 2 | 6132871288 | 3066.436 | 247128 | 8093.61 | 55/55 |
| b2 | candidate | 3 | 6152184984 | 3076.092 | 292655 | 6834.92 | 55/55 |
| a2 | baseline | 1 | 6482844288 | 3241.422 | 255755 | 7820.48 | 28/47 |
| a2 | baseline | 2 | 6487222016 | 3243.611 | 377288 | 5301.30 | 47/47 |
| a2 | baseline | 3 | 6486563056 | 3243.282 | 372926 | 5363.57 | 47/55 |

All six allocation observations per revision: median 3,236.411 → 3,081.180 B/configured message (observed −155.231 B, −4.796%). Throughput medians 238,730 → 272,926 messages/s, with wide baseline 187,266–377,288 and candidate 204,165–381,098 ranges. No throughput improvement claim. Unrelated host activity was observed; these measurements are not isolated-host results.

# Matched callback-closure profiles

Sequential GC-verbose + SampleProfiler captures; profile timings are excluded from performance results. Both processes exited0. No additional captures are required for the bounded allocation-site claim.

| Check | Baseline | Candidate |
|---|---:|---:|
| EventsLost | 0 | 0 |
| Observed GC heap counts | [8] | [8] |
| Callback entry closure allocation ticks (namespace-validated TypeID) | 8783 | 0 |
| Ticks with namespace-qualified callback stack present | 4113 | 0 |
| Observed selected closure object sizes | {'64': 8783} | {} |

The baseline callback closure type is identified by namespace-qualified `Akka.Streams.Stage.GraphStageLogic.ConcurrentAsyncCallback<T>` allocation stacks, then linked by same-trace PID/CLR/TypeID to allocations whose caller was fully inlined. This includes all four closed generic variants without treating the compiler display-class ordinal as a stable identity. Candidate has no matching callback entry-closure allocations in this capture. TypeID values are not compared across processes.

## Allocation-counter/window check

| Variant/repetition | UTC window | Precise counter delta | Approximate AllocationTick bytes | Tick/counter ratio |
|---|---|---:|---:|---:|
| baseline/1 | 2026-10-15T12:15:55.8741985Z to 2026-10-15T12:16:17.2365625Z | 6,335,119,520 | 6,360,985,768 | 1.004083 |
| baseline/2 | 2026-10-15T12:16:17.3463799Z to 2026-10-15T12:16:33.4852084Z | 6,358,659,872 | 6,387,027,032 | 1.004461 |
| baseline/3 | 2026-10-15T12:16:33.6481368Z to 2026-10-15T12:16:51.3048604Z | 6,346,706,136 | 6,371,963,520 | 1.003980 |
| candidate/1 | 2026-10-15T12:18:06.5778549Z to 2026-10-15T12:18:16.6009956Z | 6,163,379,008 | 6,219,133,080 | 1.009046 |
| candidate/2 | 2026-10-15T12:18:16.7864359Z to 2026-10-15T12:18:26.7904362Z | 6,142,500,752 | 6,187,059,976 | 1.007254 |
| candidate/3 | 2026-10-15T12:18:26.9273069Z to 2026-10-15T12:18:37.2890958Z | 6,139,020,872 | 6,186,600,672 | 1.007750 |

GCGlobalHeapHistory reports only NumHeaps=8 in both captures, including all three raw UTC windows. Both logs request DOTNET_GCDynamicAdaptationMode=0 and DOTNET_GCHeapCount=8, and both retained-array sanity probes exceed their8MiB lower bound. Tick quantum accounting is approximately0.4–0.9% above the precise process counter totals in these windows. This supports the counter magnitudes under the fixed settings; it is not exact byte equality or proof of the cause of earlier unfixed-counter drift.

## Built IL and harness identity

Baseline `OnAsyncInput` has display-class `newobj` at IL_0000, before HasValue testing. Candidate `OnAsyncInput` has no `newobj`; only the context-present branch calls `OnAsyncInputWithContext`. The candidate helper retains both closure and delegate construction, preserving traced behavior. Retained snippets: baseline-OnAsyncInput.il, candidate-OnAsyncInput.il, candidate-OnAsyncInputWithContext.il; complete ILSpy output is also retained.

Both variants use one client,1M repeats,3 repetitions,window50,348-byte real V2 payload,one inbound/outbound lane and Artery.Tcp. Exact capture commands are in baseline-profile-command.txt and candidate-profile-command.txt. Original Program.cs is shared by both revisions; temporary instrumentation patch, instrumented source hashes and all harness-source manifests match byte-for-byte. Runtimeconfig and relevant executable/Streams binary hashes are retained and verified against the frozen build manifests.

## Limits

- Allocation ticks are sampled events, not exact object counts. Weighted bytes represent allocation quanta; they cannot assign the entire integrated allocation delta to this site.
- Some closure allocation callers are inlined;4113 baseline ticks have a qualified callback frame and the rest are linked by validated TypeID. Absence of an explicit callback frame is not itself a missing/unresolved stack. A separate exact stackless-closure-event total was not computed.
- Both profiles have zero lost events. Existing all-type analyzer CPU missing-stack counts are retained in profile-summary.json; those do not measure missing closure allocation stacks.
- No traced producer/listener workload was captured here. Runtime traced-feedback/exception tests and retained helper IL support traced preservation separately.
- Sampling, UTC boundary quantum spill and unrelated host activity remain limitations. Profile timing is excluded; no integrated throughput conclusion follows.
- Do not attribute the entire approximately155B process delta to this64B allocation site.

**Evidence retention:** commands, raw logs, generated BDN reports, TRX files, binary/source/runtimeconfig hashes, raw nettraces, analyzer sources and IL are archived with a verified SHA-256 manifest at `/home/petabridge/benchmark-results/akka.net/streams-callback-closure-2026-10-15`. These are local results; hosted CI remains separate.

Reproduce focused controls with `dotnet test src/core/Akka.Streams.Tests/Akka.Streams.Tests.csproj -c Release --filter "FullyQualifiedName~AsyncCallbackSpec|FullyQualifiedName~StreamsDiagnosticsSpec"`. Exact measurement commands and temporary instrumentation are retained in the evidence archive.
